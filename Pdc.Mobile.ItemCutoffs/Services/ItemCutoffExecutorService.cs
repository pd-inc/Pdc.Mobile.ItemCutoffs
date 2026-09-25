using Microsoft.Extensions.Logging;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Domain.Enumerations;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

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
    private readonly IItemCutoffEmailService emailService;
    private readonly IEventTimeZoneResolver timeZoneResolver;
    private readonly IUtcClock clock;
    private readonly ItemCutoffOptions options;

    public ItemCutoffExecutorService(
        ILogger logger,
        IEventItemCutoffGroupRepository cutoffRepository,
        IAnnouncementPublisher announcementPublisher,
        IItemCutoffApplier cutoffApplier,
        IEcommerceCacheService cacheService,
        IItemCutoffEmailService emailService,
        IEventTimeZoneResolver timeZoneResolver,
        IUtcClock clock,
        ItemCutoffOptions options)
    {
        this.logger = logger;
        this.cutoffRepository = cutoffRepository;
        this.announcementPublisher = announcementPublisher;
        this.cutoffApplier = cutoffApplier;
        this.cacheService = cacheService;
        this.emailService = emailService;
        this.timeZoneResolver = timeZoneResolver;
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
            //
            // NOT an early return. Digests are independent of deadlines and are
            // usually due on a tick where nothing is closing - the digest goes out
            // in the morning and the contests close in the afternoon - so
            // returning here would mean the digest almost never sent.
            logger.LogDebug("{FeatureKey} No sales deadline due at {NowUtc:o}", LogProperties.TickStarted, now);
        }
        else
        {
            logger.LogInformation("{FeatureKey} {GroupCount} sales deadline(s) due at {NowUtc:o}{DryRunSuffix}",
                LogProperties.TickStarted, due.Count, now, trigger.DryRun ? " (dry run)" : string.Empty);
        }

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

        await ProcessDigests(now, trigger.DryRun, summary);

        // A tick that did nothing at all still happens 1,440 times a day, so the
        // summary is only worth an Information line when something happened.
        var didSomething = summary.GroupsEvaluated > 0 || summary.DigestsSent > 0;

        logger.Log(
            didSomething ? LogLevel.Information : LogLevel.Debug,
            "{FeatureKey} Tick finished: {GroupsEvaluated} evaluated, {AnnouncementsSent} posted, {AnnouncementsSkipped} skipped, {CutoffsApplied} closed, {ItemsClosed} product row(s), {DigestsSent} digest(s), cache cleared {CacheCleared}",
            LogProperties.TickCompleted,
            summary.GroupsEvaluated,
            summary.AnnouncementsSent,
            summary.AnnouncementsSkipped,
            summary.CutoffsApplied,
            summary.ItemsClosed,
            summary.DigestsSent,
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

            // AFTER the outcome is recorded, so a mail failure cannot leave a
            // completed cutoff looking unfinished and get it retried.
            await SendCompletedEmailIfEnabled(group, closed, dryRun);
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
    /// EMAIL
    ///////////////////////////////////////////////

    /// <summary>
    /// Emails the registration team that a deadline has closed, when the event
    /// has that switched on.
    ///
    /// This is the only thing that makes the POINT OF SALE stop selling: the
    /// database write hides the items from non-admin staff, but a running
    /// WinClient serves from its own cache until its operator clears it.
    /// </summary>
    private async Task SendCompletedEmailIfEnabled(EventItemCutoffGroup group, int itemsClosed, bool dryRun)
    {
        var settings = cutoffRepository.FindSettings(group.EventId);

        // No settings row means the event has never been configured, and the
        // default is ON: the point of sale instruction matters more than an
        // unwanted email, and an admin can switch it off.
        if (settings != null && !settings.CompletedEmailEnabled)
        {
            return;
        }

        await emailService.SendCutoffCompleted(group, itemsClosed, dryRun);
    }

    ///////////////////////////////////////////////
    /// DIGESTS
    ///////////////////////////////////////////////

    /// <summary>
    /// Sends any daily digest that is due, and any an admin has asked for.
    ///
    /// The candidate query is coarse because MariaDB cannot resolve an IANA zone,
    /// so the real decision is made here: convert the tick to the event's local
    /// time, and send when the configured time has passed and today's has not
    /// gone yet.
    /// </summary>
    private async Task ProcessDigests(DateTime now, bool dryRun, ItemCutoffRunSummary summary)
    {
        List<EventItemCutoffSettings> candidates;

        try
        {
            candidates = cutoffRepository.FindSettingsDue(now);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{FeatureKey} Could not read the digest candidates", LogProperties.DigestFailed);
            return;
        }

        foreach (var settings in candidates)
        {
            try
            {
                await ProcessDigest(settings, now, dryRun, summary);
            }
            catch (Exception ex)
            {
                // Isolated like the deadlines: one event's bad zone or bad address
                // must not stop another event's digest.
                logger.LogError(ex, "{FeatureKey} Digest failed for EventId {EventId}",
                    LogProperties.DigestFailed, settings.EventId);
            }
        }
    }

    /// <summary>
    /// Decides whether one event's digest is due, and sends it.
    /// </summary>
    private async Task ProcessDigest(EventItemCutoffSettings settings, DateTime now, bool dryRun, ItemCutoffRunSummary summary)
    {
        if (!timeZoneResolver.IsKnownTimeZone(settings.TimeZone))
        {
            logger.LogWarning("{FeatureKey} EventId {EventId} has an unrecognised digest time zone '{TimeZone}'",
                LogProperties.DigestFailed, settings.EventId, settings.TimeZone);
            return;
        }

        var eventNow = timeZoneResolver.ToLocal(now, settings.TimeZone);

        // An explicit request wins over the schedule, and is honoured even when
        // the daily digest is switched off.
        var requested = settings.DigestRequestedForDate;
        var isOnDemand = requested.HasValue;

        var localDate = isOnDemand ? requested!.Value.Date : eventNow.Date;

        if (!isOnDemand && !IsScheduledDigestDue(settings, eventNow))
        {
            return;
        }

        var deadlines = LoadDeadlinesForLocalDate(settings.EventId, localDate);
        var sent = await emailService.SendDigest(settings, localDate, eventNow.Date, deadlines, dryRun);

        if (sent)
        {
            summary.DigestsSent++;
        }

        if (dryRun)
        {
            return;
        }

        if (isOnDemand)
        {
            // Cleared whether or not the send worked. An admin who sees no email
            // presses the button again, which is more predictable than retrying a
            // bad address on every tick forever.
            cutoffRepository.SetDigestRequested(settings.EventId, null);
        }
        else if (sent)
        {
            // Only stamped on success, so a failed scheduled send is retried on
            // the next tick rather than silently skipped for the day.
            cutoffRepository.SetDigestSent(settings.EventId, localDate);
        }
    }

    /// <summary>
    /// True when the event's local time has passed the configured send time and
    /// today's digest has not already gone.
    /// </summary>
    private static bool IsScheduledDigestDue(EventItemCutoffSettings settings, DateTime eventNow)
    {
        if (!settings.DigestEnabled)
        {
            return false;
        }

        if (eventNow.TimeOfDay < settings.DigestLocalTime)
        {
            return false;
        }

        return settings.DigestLastSentLocalDate?.Date != eventNow.Date;
    }

    /// <summary>
    /// The event's deadlines closing on one local date, with their items loaded.
    ///
    /// Filtered on the WALL CLOCK date rather than the UTC instant, because the
    /// digest reports an event's day as the event experiences it.
    /// </summary>
    private List<EventItemCutoffGroup> LoadDeadlinesForLocalDate(int eventId, DateTime localDate)
    {
        var deadlines = cutoffRepository.FindByEventId(eventId)
            .Where(deadline => deadline.CutoffLocal.Date == localDate.Date)
            .ToList();

        foreach (var deadline in deadlines)
        {
            deadline.Items = cutoffRepository.FindItems(deadline.Id);
        }

        return deadlines;
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
