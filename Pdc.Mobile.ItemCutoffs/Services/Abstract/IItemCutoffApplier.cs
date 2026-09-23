using Pdc.EventPro.Domain.Entities;

namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// Closes the items of one sales deadline.
/// </summary>
public interface IItemCutoffApplier
{
    /// <summary>
    /// Closes every product row whose (type, internal code) is in the group.
    /// </summary>
    /// <returns>The number of product rows changed.</returns>
    Task<int> Apply(EventItemCutoffGroup group, bool dryRun);
}
