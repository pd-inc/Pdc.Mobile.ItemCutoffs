using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;
using System.Text.Json;

namespace Pdc.Mobile.ItemCutoffs.Repositories;

///////////////////////////////////////////////
/// NEWS FEED PUSH ENQUEUER
///////////////////////////////////////////////

/// <summary>
/// Publishes the push message for an announcement post.
///
/// THE PAYLOAD IS A COPY OF Pdc.Mobile's NewsFeedNotificationService AND MUST MATCH
/// IT. This makes the function the SECOND producer of news_feed_post on the shared
/// notifications queue; Pdc.Mobile.Notifications consumes both and cannot tell them
/// apart. A drifted key here would be a push that arrives but deep-links nowhere,
/// which nobody notices until an attendee taps it.
///
/// The Notifications Lambda forwards only `feature` and `requestId` from `data` and
/// injects eventUuid itself, so the remaining keys are there to match the existing
/// message rather than because they are read.
/// </summary>
public class NewsFeedPushEnqueuer : INewsFeedPushEnqueuer
{
    private const string FeatureKeyQueued = "##_ITEMCUTOFF_PUSH_QUEUED";

    private const string MessageTypeNewsFeedPost = "news_feed_post";
    private const string AppFeatureNewsFeed = "newsFeed";

    private readonly ILogger logger;
    private readonly IAmazonSQS sqsClient;
    private readonly string sqsQueueUrl;

    public NewsFeedPushEnqueuer(
        ILogger logger,
        IAmazonSQS sqsClient,
        string sqsQueueUrl)
    {
        this.logger = logger;
        this.sqsClient = sqsClient;
        this.sqsQueueUrl = sqsQueueUrl;
    }

    /// <summary>
    /// Enqueues the fan-out message.
    /// </summary>
    public async Task Enqueue(string eventUuid, NewsFeedPostDocument post)
    {
        var payload = new
        {
            messageType = MessageTypeNewsFeedPost,
            eventUuid,
            title = post.Title,
            body = post.Body,
            data = new Dictionary<string, string>
            {
                ["feature"] = AppFeatureNewsFeed,
                ["postId"] = post.Id,
                ["postType"] = post.Type,
                ["postPriority"] = post.Priority,
                ["authorName"] = post.AuthorName
            },
            createdAt = post.CreatedAt
        };

        var response = await sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = sqsQueueUrl,
            MessageBody = JsonSerializer.Serialize(payload)
        });

        logger.LogInformation("{FeatureKey} Push queued for announcement post {PostId}, EventUuid {EventUuid}, MessageId {MessageId}",
            FeatureKeyQueued, post.Id, eventUuid, response.MessageId);
    }
}
