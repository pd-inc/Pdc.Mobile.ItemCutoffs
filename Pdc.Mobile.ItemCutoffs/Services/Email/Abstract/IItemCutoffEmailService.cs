using Pdc.EventPro.Domain.Entities;

namespace Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

/// <summary>
/// Sends the two sales deadline emails.
///
/// Neither throws: an email is a notification about work that has already been
/// done, so a mail failure must never turn a completed cutoff into a failed one
/// or stop the rest of a tick.
/// </summary>
public interface IItemCutoffEmailService
{
    /// <summary>
    /// Tells the registration team a deadline has closed and that they must clear
    /// the point of sale cache.
    /// </summary>
    Task SendCutoffCompleted(EventItemCutoffGroup deadline, int itemsClosed, bool dryRun);

    /// <summary>
    /// Sends the day's close-times digest for an event.
    /// </summary>
    /// <returns>True when a message was actually sent.</returns>
    Task<bool> SendDigest(
        EventItemCutoffSettings settings,
        DateTime localDate,
        DateTime eventToday,
        IReadOnlyList<EventItemCutoffGroup> deadlines,
        bool dryRun);
}
