using Pdc.Mobile.ItemCutoffs.Models;

namespace Pdc.Mobile.ItemCutoffs.Repositories.Abstract;

/// <summary>
/// Writes a news feed post to the Realtime Database.
/// </summary>
public interface IFirebaseNewsFeedPostRepository
{
    /// <summary>
    /// Creates the post at events/{eventUuid}/news_feed/posts/{post.Id}.
    /// Throws on failure, so the caller records a retryable outcome.
    /// </summary>
    Task WritePost(string eventUuid, NewsFeedPostDocument post);
}
