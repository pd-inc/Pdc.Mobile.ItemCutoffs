using Microsoft.Extensions.Logging;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services;

///////////////////////////////////////////////
/// ITEM CUTOFF APPLIER
///////////////////////////////////////////////

/// <summary>
/// Closes the items of one sales deadline.
///
/// The deadline stores INTERNAL CODES, not product ids, so one selected entry
/// expands here to every price point sharing that code. That expansion is the whole
/// point of the feature: an admin closes "Novice J&amp;J" once and both the early-bird
/// and standard rows go.
///
/// Three flags are set on each row, and each does a different job:
///
///   Display    = false - gone from the online store
///   AdminOnly  = true  - gone from the point of sale for non-admin staff, while an
///                        admin can still sell it at the desk if they must
///   SalesClosed= true  - no automated visibility job may ever turn it back on
///
/// The prior values of all three are recorded BEFORE each row is touched, so the
/// audit trail is truthful even when a group fails part way through.
/// </summary>
public class ItemCutoffApplier : IItemCutoffApplier
{
    private readonly ILogger logger;
    private readonly IItemRepository itemRepository;
    private readonly IUpdateEventItemRepository updateItemRepository;
    private readonly IEventItemCutoffGroupRepository cutoffRepository;

    public ItemCutoffApplier(
        ILogger logger,
        IItemRepository itemRepository,
        IUpdateEventItemRepository updateItemRepository,
        IEventItemCutoffGroupRepository cutoffRepository)
    {
        this.logger = logger;
        this.itemRepository = itemRepository;
        this.updateItemRepository = updateItemRepository;
        this.cutoffRepository = cutoffRepository;
    }

    /// <summary>
    /// Closes every product row the group names.
    /// </summary>
    public Task<int> Apply(EventItemCutoffGroup group, bool dryRun)
    {
        var targets = ResolveTargets(group);

        if (targets.Count == 0)
        {
            logger.LogWarning("{FeatureKey} Sales deadline {GroupId} matched no product rows; its item codes may have been removed from the event",
                LogProperties.CutoffApplyFailed, group.Id);

            return Task.FromResult(0);
        }

        if (dryRun)
        {
            logger.LogInformation("{FeatureKey} Would close {ItemCount} product row(s) for sales deadline {GroupId} ({GroupName})",
                LogProperties.DryRun, targets.Count, group.Id, group.Name);

            return Task.FromResult(targets.Count);
        }

        var closed = 0;

        foreach (var item in targets)
        {
            // Recorded first: a failure after this leaves a truthful record of what
            // was already changed, which is what makes the audit usable.
            cutoffRepository.InsertItemResult(new EventItemCutoffGroupItemResult
            {
                GroupId = group.Id,
                ProductType = item.Type,
                ItemId = item.Id,
                InternalCode = item.InternalCode,
                ItemName = item.Name,
                PriorDisplay = item.Display,
                PriorAdminOnly = item.AdminOnly,
                PriorSalesClosed = item.SalesClosed
            });

            // Order matters on a live store. SalesClosed goes first so that if the
            // process dies mid-item, the visibility jobs already know not to reopen
            // it; Display last so the item is never visible-but-unprotected.
            updateItemRepository.UpdateSalesClosedFlag(group.EventId, item.Type, item.Id, true);
            updateItemRepository.UpdateAdminOnlyFlag(group.EventId, item.Type, item.Id, true);
            updateItemRepository.UpdateDisplayFlag(group.EventId, item.Type, item.Id, false);

            closed++;
        }

        logger.LogInformation("{FeatureKey} Sales deadline {GroupId} ({GroupName}) closed {ItemCount} product row(s) across {CodeCount} item code(s) for EventId {EventId}",
            LogProperties.CutoffApplied, group.Id, group.Name, closed, group.Items.Count, group.EventId);

        return Task.FromResult(closed);
    }

    /// <summary>
    /// Expands the group's internal codes into the product rows to close.
    ///
    /// A row already carrying SalesClosed is skipped rather than rewritten, so a
    /// retry after a partial failure does not re-stamp audit rows for work that was
    /// already done.
    /// </summary>
    private List<EventItem> ResolveTargets(EventItemCutoffGroup group)
    {
        var eventItems = itemRepository.FindByEventId(group.EventId) ?? new List<EventItem>();

        var wanted = new HashSet<string>(
            group.Items.Select(item => BuildKey((int)item.ProductType, item.InternalCode)),
            StringComparer.OrdinalIgnoreCase);

        return eventItems
            .Where(item => !string.IsNullOrWhiteSpace(item.InternalCode))
            .Where(item => wanted.Contains(BuildKey((int)item.Type, item.InternalCode)))
            .Where(item => item.SalesClosed != true)
            .ToList();
    }

    private static string BuildKey(int productType, string internalCode)
        => $"{productType}:{internalCode.Trim()}";
}
