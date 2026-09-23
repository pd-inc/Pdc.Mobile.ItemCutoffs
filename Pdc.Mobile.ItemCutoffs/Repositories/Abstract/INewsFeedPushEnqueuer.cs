using Pdc.Mobile.ItemCutoffs.Models;

namespace Pdc.Mobile.ItemCutoffs.Repositories.Abstract;

/// <summary>
/// Enqueues the push notification for an announcement post.
/// </summary>
public interface INewsFeedPushEnqueuer
{
    /// <summary>
    /// Publishes a news_feed_post message for the Notifications Lambda to fan out.
    /// Throws on failure so the caller can decide what that means.
    /// </summary>
    Task Enqueue(string eventUuid, NewsFeedPostDocument post);
}
