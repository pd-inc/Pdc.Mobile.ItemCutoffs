using Pdc.EventPro.Domain.Entities;

namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// Publishes a sales deadline's announcement to the news feed.
/// </summary>
public interface IAnnouncementPublisher
{
    /// <summary>
    /// Writes the post and, when the deadline asks for it, enqueues the push.
    /// </summary>
    /// <returns>The post id that was written.</returns>
    Task<string> Publish(EventItemCutoffGroup group, bool dryRun);
}
