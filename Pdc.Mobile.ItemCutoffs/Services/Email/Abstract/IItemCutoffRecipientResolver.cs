using Pdc.Mobile.ItemCutoffs.Models;

namespace Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

/// <summary>
/// Resolves who receives an event's sales deadline emails.
/// </summary>
public interface IItemCutoffRecipientResolver
{
    /// <summary>
    /// The event's role holders, deduplicated by email address.
    ///
    /// Resolved fresh on every send, never cached, so a staff change the day
    /// before is picked up with no action.
    /// </summary>
    List<ItemCutoffRecipient> Resolve(string eventUuid);
}
