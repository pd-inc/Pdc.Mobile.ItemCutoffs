using Microsoft.Extensions.Logging;
using Pdc.EventPro.Domain.Entities;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using System.Globalization;
using System.Text.Json;

namespace Pdc.Mobile.ItemCutoffs.Services;

///////////////////////////////////////////////
/// ANNOUNCEMENT PUBLISHER
///////////////////////////////////////////////

/// <summary>
/// Posts a sales deadline's announcement.
///
/// The copy is NOT written here. It was generated, previewed and stored when the
/// admin saved the deadline, and is posted verbatim. That is deliberate: an admin
/// has already seen exactly what will go out, and a model being unreachable at
/// 12:30 on an event Saturday cannot lose the post.
///
/// Order is load bearing. The post is written FIRST and the push enqueued second.
/// A push that arrives before the post exists deep-links to nothing, which is worse
/// than a post that is briefly silent - so a failed enqueue is logged and swallowed
/// rather than retried, since retrying would duplicate the post.
/// </summary>
public class AnnouncementPublisher : IAnnouncementPublisher
{
    private const string PostIdPrefix = "post-";
    private const string PostType = "general";
    private const string PostPriority = "high";
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly JsonSerializerOptions MediaSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger logger;
    private readonly IFirebaseNewsFeedPostRepository postRepository;
    private readonly INewsFeedPushEnqueuer pushEnqueuer;

    public AnnouncementPublisher(
        ILogger logger,
        IFirebaseNewsFeedPostRepository postRepository,
        INewsFeedPushEnqueuer pushEnqueuer)
    {
        this.logger = logger;
        this.postRepository = postRepository;
        this.pushEnqueuer = pushEnqueuer;
    }

    /// <summary>
    /// Writes the post and enqueues the push.
    /// </summary>
    public async Task<string> Publish(EventItemCutoffGroup group, bool dryRun)
    {
        var post = BuildPost(group);

        if (dryRun)
        {
            logger.LogInformation("{FeatureKey} Would post announcement {PostId} for sales deadline {GroupId} to EventUuid {EventUuid} (push {PushState})",
                LogProperties.DryRun, post.Id, group.Id, group.EventUuid, group.PostNotify ? "on" : "off");

            return post.Id;
        }

        await postRepository.WritePost(group.EventUuid, post);

        logger.LogInformation("{FeatureKey} Announcement {PostId} posted for sales deadline {GroupId} ({GroupName}) to EventUuid {EventUuid}",
            LogProperties.PostSent, post.Id, group.Id, group.Name, group.EventUuid);

        if (group.PostNotify)
        {
            await EnqueuePushSafe(group, post);
        }

        return post.Id;
    }

    ///////////////////////////////////////////////
    /// POST CONSTRUCTION
    ///////////////////////////////////////////////

    /// <summary>
    /// Builds the post document from the stored deadline.
    /// </summary>
    private NewsFeedPostDocument BuildPost(EventItemCutoffGroup group)
    {
        var utcNow = DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture);

        return new NewsFeedPostDocument
        {
            Id = $"{PostIdPrefix}{Guid.NewGuid():N}",
            Title = group.PostTitle ?? string.Empty,
            Body = group.PostBody ?? string.Empty,
            Type = PostType,
            Priority = PostPriority,

            // The admin who set the deadline up, captured at save time, so the byline
            // is a person rather than the system.
            AuthorName = group.CreatedByName ?? string.Empty,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            IsDeleted = false,
            DeletedAt = null,
            Media = DeserializeMedia(group)
        };
    }

    /// <summary>
    /// Reads the media the REST API already validated and stamped.
    ///
    /// A parse failure costs the attachments, not the post: the words carry the
    /// deadline, and an announcement with no image still does its job.
    /// </summary>
    private List<NewsFeedMediaItemDocument>? DeserializeMedia(EventItemCutoffGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.PostMediaJson))
        {
            return null;
        }

        try
        {
            var media = JsonSerializer.Deserialize<List<NewsFeedMediaItemDocument>>(group.PostMediaJson, MediaSerializerOptions);

            return media is { Count: > 0 } ? media : null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "{FeatureKey} Stored announcement media for sales deadline {GroupId} could not be read; posting without it",
                LogProperties.PostFailed, group.Id);

            return null;
        }
    }

    ///////////////////////////////////////////////
    /// PUSH
    ///////////////////////////////////////////////

    /// <summary>
    /// Enqueues the push without letting a queue failure undo a successful post.
    ///
    /// Swallowed rather than retried on purpose: the post is already live, so a
    /// retry of the whole announcement would write a second one. A missing push is
    /// visible in the feed; a duplicated post is visible to every attendee.
    /// </summary>
    private async Task EnqueuePushSafe(EventItemCutoffGroup group, NewsFeedPostDocument post)
    {
        try
        {
            await pushEnqueuer.Enqueue(group.EventUuid, post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{FeatureKey} Announcement {PostId} was posted for sales deadline {GroupId} but its push could not be queued",
                LogProperties.PostFailed, post.Id, group.Id);
        }
    }
}
