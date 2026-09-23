using Microsoft.Extensions.Logging;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Domain.Enumerations;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services;

///////////////////////////////////////////////
/// ITEM CUTOFF EXECUTOR SERVICE
///////////////////////////////////////////////

/// <summary>
/// One sales deadline tick.
///
/// On a quiet tick this is a single indexed query that returns nothing, which is
/// what makes polling every minute affordable. The reason to poll at all rather
/// than schedule each deadline individually is the failure mode: a missed tick is
/// LATE and the next tick catches everything that came due, in order, whereas a
/// lost one-time schedule is NEVER, silently and permanently. Sixty seconds late is
/// invisible at an event; a contest that stayed open past its announced deadline is
/// not.
///
/// Every deadline is wrapped, so one bad group cannot stop the others. The method
/// throws only when the tick itself cannot start, which is what makes the Lambda
/// Errors metric mean "the executor is down" rather than "one group had a bad day".
/// </summary>
public class ItemCutoffExecutorService : IItemCutoffExecutorService
{
    private readonly ILogger logger;
    private readonly IEventItemCutoffGroupRepository cutoffRepository;
    private readonly IAnnouncementPublisher announcementPublisher;
    private readonly IItemCutoffApplier cutoffApplier;
    private readonly IEcommerceCacheService cacheService;
    private readonly IUtcClock clock;
    private readonly ItemCutoffOptions options;

    public ItemCutoffExecutorService(
        ILogger logger,
        IEventItemCutoffGroupRepository cutoffRepository,
        IAnnouncementPublisher announcementPublisher,
        IItemCutoffApplier cutoffApplier,
        IEcommerceCacheService cacheService,
        IUtcClock clock,
        ItemCutoffOptions options)
    {
        this.logger = logger;
        this.cutoffRepository = cutoffRepository;
        this.announcementPublisher = announcementPublisher;
        this.cutoffApplier = cutoffApplier;
        this.cacheService = cacheService;
        this.clock = clock;
        this.options = options;
    }

    /// <summary>
    /// Runs the tick.
    /// </summary>
    public async Task<ItemCutoffRunSummary> Execute(ItemCutoffTrigger trigger)
    {
        var summary = new ItemCutoffRunSummary { DryRun = trigger.DryRun };
        var now = clock.UtcNow;

        var due = cutoffRepository.FindDue(now, options.MaxAttempts);

        // groupId narrows the tick without bypassing any gate: the group still had
        // to come back from the due query.
        if (trigger.GroupId.HasValue)
        {
            due = due.Where(group => group.Id == trigger.GroupId.Value).ToList();
        }

        summary.GroupsEvaluated = due.Count;

        if (due.Count == 0)
        {
            // Debug, not Information: a quiet tick happens 1,440 times a day and
            // logging it at Information would bury the ticks that did something.
            logger.LogDebug("{FeatureKey} Nothing due at {NowUtc:o}", LogProperties.TickStarted, now);
            return summary;
        }

        logger.LogInformation("{FeatureKey} {GroupCount} sales deadline(s) due at {NowUtc:o}{DryRunSuffix}",
            LogProperties.TickStarted, due.Count, now, trigger.DryRun ? " (dry run)" : string.Empty);

        foreach (var group in due)
        {
            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["GroupId"] = group.Id,
                ["EventId"] = group.EventId,
                ["EventUuid"] = group.EventUuid
            });

            try
            {
                await ProcessGroup(group, now, trigger.DryRun, summary);
            }
            catch (Exception ex)
            {
                // Isolated on purpose: one event's bad data must not stop another
                // event's contest from closing on time.
                logger.LogError(ex, "{FeatureKey} Sales deadline {GroupId} ({GroupName}) failed",
                    LogProperties.GroupFailed, group.Id, group.Name);
            }
        }

        if (summary.ItemsClosed > 0 && !trigger.DryRun)
        {
            // Once per tick, not once per deadline: several deadlines can share a
            // closing minute, and ten requests each would be pointless load.
            summary.CacheCleared = await cacheService.InvalidateCacheAsync();
        }

        logger.LogInformation(
            "{FeatureKey} Tick finished: {GroupsEvaluated} evaluated, {AnnouncementsSent} posted, {AnnouncementsSkipped} skipped, {CutoffsApplied} closed, {ItemsClosed} product row(s), cache cleared {CacheCleared}",
            LogProperties.TickCompleted,
            summary.GroupsEvaluated,
            summary.AnnouncementsSent,
            summary.AnnouncementsSkipped,
            summary.CutoffsApplied,
            summary.ItemsClosed,
            summary.CacheCleared);

        return summary;
    }

    ///////////////////////////////////////////////
    /// ONE DEADLINE
    ///////////////////////////////////////////////

    /// <summary>
    /// Handles whichever halves of one deadline are due.
    ///
    /// The announcement is handled before the cutoff, because when both fall on the
    /// same tick (a lead shorter than the tick interval, or a backlog after an
    /// outage) attendees should still see the notice before the items vanish.
    /// </summary>
    private async Task ProcessGroup(EventItemCutoffGroup group, DateTime now, bool dryRun, ItemCutoffRunSummary summary)
    {
        group.Items = cutoffRepository.FindItems(group.Id);

        if (group.PostDue)
        {
            await ProcessAnnouncement(group, now, dryRun, summary);
        }

        if (group.CutoffDue)
        {
            await ProcessCutoff(group, dryRun, summary);
        }
    }

    /// <summary>
    /// Posts the announcement, or skips it when its moment has gone.
    /// </summary>
    private async Task ProcessAnnouncement(EventItemCutoffGroup group, DateTime now, bool dryRun, ItemCutoffRunSummary summary)
    {
        // THE SKIP RULE. An announcement whose deadline has already passed would
        // tell attendees to hurry for something that is already closed. After an
        // outage that is exactly what a backlog would produce, so it is refused
        // rather than posted late.
        if (now >= group.CutoffUtc)
        {
            logger.LogWarning("{FeatureKey} Announcement for sales deadline {GroupId} ({GroupName}) skipped: its closing time has already passed",
                LogProperties.PostSkipped, group.Id, group.Name);

            summary.AnnouncementsSkipped++;

            if (!dryRun)
            {
                cutoffRepository.SetPostOutcome(group.Id, ItemCutoffStatusType.Skipped.ToString().ToLowerInvariant(), null, null);
            }

            return;
        }

        try
        {
            var postId = await announcementPublisher.Publish(group, dryRun);

            summary.AnnouncementsSent++;

            if (!dryRun)
            {
                cutoffRepository.SetPostOutcome(group.Id, ItemCutoffStatusType.Sent.ToString().ToLowerInvariant(), postId, null);
            }
        }
        catch (Exception ex)
        {
            summary.AnnouncementsFailed++;

            logger.LogError(ex, "{FeatureKey} Announcement for sales deadline {GroupId} ({GroupName}) failed",
                LogProperties.PostFailed, group.Id, group.Name);

            if (!dryRun)
            {
                // Left pending so the next tick retries, until the attempt cap or the
                // deadline passes and the skip rule takes over.
                RecordRetryablePostFailure(group, ex);
            }
        }
    }

    /// <summary>
    /// Closes the deadline's items.
    /// </summary>
    private async Task ProcessCutoff(EventItemCutoffGroup group, bool dryRun, ItemCutoffRunSummary summary)
    {
        try
        {
            var closed = await cutoffApplier.Apply(group, dryRun);

            summary.CutoffsApplied++;
            summary.ItemsClosed += closed;

            if (!dryRun)
            {
                cutoffRepository.SetCutoffOutcome(group.Id, ItemCutoffStatusType.Completed.ToString().ToLowerInvariant(), closed, null);
            }
        }
        catch (Exception ex)
        {
            summary.CutoffsFailed++;

            logger.LogError(ex, "{FeatureKey} Cutoff for sales deadline {GroupId} ({GroupName}) failed",
                LogProperties.CutoffApplyFailed, group.Id, group.Name);

            if (!dryRun)
            {
                // A LATE cutoff still runs. Unlike the announcement there is no point
                // after which closing is pointless: an item that should be closed
                // should be closed, however late the system is.
                RecordRetryableCutoffFailure(group, ex);
            }
        }
    }

    ///////////////////////////////////////////////
    /// FAILURE RECORDING
    ///////////////////////////////////////////////

    /// <summary>
    /// Records a failed announcement, marking it failed once attempts run out so it
    /// stops being retried every minute.
    /// </summary>
    private void RecordRetryablePostFailure(EventItemCutoffGroup group, Exception ex)
    {
        var exhausted = group.PostAttempts + 1 >= options.MaxAttempts;

        var status = exhausted
            ? ItemCutoffStatusType.Failed
            : ItemCutoffStatusType.Pending;

        cutoffRepository.SetPostOutcome(group.Id, status.ToString().ToLowerInvariant(), null, Summarize(ex));
    }

    /// <summary>
    /// Records a failed cutoff, marking it failed once attempts run out.
    /// </summary>
    private void RecordRetryableCutoffFailure(EventItemCutoffGroup group, Exception ex)
    {
        var exhausted = group.CutoffAttempts + 1 >= options.MaxAttempts;

        var status = exhausted
            ? ItemCutoffStatusType.Failed
            : ItemCutoffStatusType.Pending;

        cutoffRepository.SetCutoffOutcome(group.Id, status.ToString().ToLowerInvariant(), null, Summarize(ex));
    }

    /// <summary>
    /// A short error for the stored row, which the admin screen shows. The full
    /// exception is already in the log; the column is 500 characters.
    /// </summary>
    private static string Summarize(Exception ex)
    {
        var message = ex.Message ?? ex.GetType().Name;

        return message.Length <= 500 ? message : message.Substring(0, 500);
    }
}
