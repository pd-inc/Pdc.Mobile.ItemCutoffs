using Pdc.Mobile.ItemCutoffs.Models;

namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// Runs one sales deadline tick.
/// </summary>
public interface IItemCutoffExecutorService
{
    Task<ItemCutoffRunSummary> Execute(ItemCutoffTrigger trigger);
}
