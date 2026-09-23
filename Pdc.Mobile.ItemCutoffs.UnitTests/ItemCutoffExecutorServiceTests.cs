using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Domain.Enumerations;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// ITEM CUTOFF EXECUTOR TESTS
///////////////////////////////////////////////

/// <summary>
/// The tick's decisions, pinned.
///
/// The one that matters most is the SKIP RULE: an announcement whose deadline has
/// already passed must never be posted. After an outage a backlog would otherwise
/// tell attendees to hurry for a contest that closed an hour ago.
/// </summary>
[TestClass]
public class ItemCutoffExecutorServiceTests
{
    private static readonly DateTime Now = new DateTime(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc);

    private Mock<IEventItemCutoffGroupRepository> repository = null!;
    private Mock<IAnnouncementPublisher> publisher = null!;
    private Mock<IItemCutoffApplier> applier = null!;
    private Mock<IEcommerceCacheService> cache = null!;
    private ItemCutoffExecutorService service = null!;

    [TestInitialize]
    public void Setup()
    {
        repository = new Mock<IEventItemCutoffGroupRepository>();
        publisher = new Mock<IAnnouncementPublisher>();
        applier = new Mock<IItemCutoffApplier>();
        cache = new Mock<IEcommerceCacheService>();

        repository.Setup(x => x.FindItems(It.IsAny<int>())).Returns(new List<EventItemCutoffGroupItem>());
        publisher.Setup(x => x.Publish(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>())).ReturnsAsync("post-abc");
        applier.Setup(x => x.Apply(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>())).ReturnsAsync(4);
        cache.Setup(x => x.InvalidateCacheAsync()).ReturnsAsync(true);

        var clock = new Mock<IUtcClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        service = new ItemCutoffExecutorService(
            NullLogger.Instance,
            repository.Object,
            publisher.Object,
            applier.Object,
            cache.Object,
            clock.Object,
            new ItemCutoffOptions());
    }

    private static ItemCutoffTrigger Tick(bool dryRun = false, int? groupId = null) => new()
    {
        MessageType = MessageTypes.ItemCutoffTick,
        DryRun = dryRun,
        GroupId = groupId
    };

    private static EventItemCutoffGroup Group(
        int id = 1,
        bool postDue = false,
        bool cutoffDue = false,
        DateTime? cutoffUtc = null) => new()
    {
        Id = id,
        EventId = 500,
        EventUuid = "event-uuid",
        Name = "Saturday 1 PM",
        CutoffUtc = cutoffUtc ?? Now.AddMinutes(30),
        PostDue = postDue,
        CutoffDue = cutoffDue,
        PostEnabled = postDue,
        PostNotify = true
    };

    private void GivenDue(params EventItemCutoffGroup[] groups)
    {
        repository.Setup(x => x.FindDue(It.IsAny<DateTime>(), It.IsAny<int>())).Returns(groups.ToList());
    }

    ///////////////////////////////////////////////
    /// THE QUIET TICK
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task NothingDue_DoesNoWorkAtAll()
    {
        GivenDue();

        var summary = await service.Execute(Tick());

        Assert.AreEqual(0, summary.GroupsEvaluated);
        publisher.VerifyNoOtherCalls();
        applier.VerifyNoOtherCalls();
        cache.Verify(x => x.InvalidateCacheAsync(), Times.Never);
    }

    ///////////////////////////////////////////////
    /// THE SKIP RULE
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task AnnouncementWhoseDeadlineHasPassed_IsNeverPosted()
    {
        // The executor caught up after an outage. Telling attendees to hurry for a
        // contest that already closed is worse than saying nothing.
        GivenDue(Group(postDue: true, cutoffUtc: Now.AddMinutes(-5)));

        var summary = await service.Execute(Tick());

        publisher.Verify(x => x.Publish(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>()), Times.Never);
        Assert.AreEqual(1, summary.AnnouncementsSkipped);
        repository.Verify(x => x.SetPostOutcome(1, "skipped", null, null), Times.Once);
    }

    [TestMethod]
    public async Task AnnouncementExactlyAtTheDeadline_IsSkipped()
    {
        // The boundary belongs to the cutoff, not the announcement.
        GivenDue(Group(postDue: true, cutoffUtc: Now));

        var summary = await service.Execute(Tick());

        Assert.AreEqual(1, summary.AnnouncementsSkipped);
        Assert.AreEqual(0, summary.AnnouncementsSent);
    }

    [TestMethod]
    public async Task AnnouncementBeforeTheDeadline_IsPostedAndRecorded()
    {
        GivenDue(Group(postDue: true, cutoffUtc: Now.AddMinutes(30)));

        var summary = await service.Execute(Tick());

        publisher.Verify(x => x.Publish(It.Is<EventItemCutoffGroup>(g => g.Id == 1), false), Times.Once);
        Assert.AreEqual(1, summary.AnnouncementsSent);
        repository.Verify(x => x.SetPostOutcome(1, "sent", "post-abc", null), Times.Once);
    }

    ///////////////////////////////////////////////
    /// A LATE CUTOFF STILL RUNS
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task CutoffWhoseTimeHasLongPassed_StillRuns()
    {
        // Unlike an announcement there is no point after which closing is pointless.
        // An item that should be closed should be closed, however late.
        GivenDue(Group(cutoffDue: true, cutoffUtc: Now.AddHours(-3)));

        var summary = await service.Execute(Tick());

        applier.Verify(x => x.Apply(It.IsAny<EventItemCutoffGroup>(), false), Times.Once);
        Assert.AreEqual(1, summary.CutoffsApplied);
        repository.Verify(x => x.SetCutoffOutcome(1, "completed", 4, null), Times.Once);
    }

    [TestMethod]
    public async Task BothHalvesDue_AnnouncesBeforeClosing()
    {
        // When a short lead puts both on one tick, attendees should still see the
        // notice before the items vanish.
        var sequence = new List<string>();

        publisher.Setup(x => x.Publish(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>()))
            .Callback(() => sequence.Add("announce"))
            .ReturnsAsync("post-abc");

        applier.Setup(x => x.Apply(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>()))
            .Callback(() => sequence.Add("close"))
            .ReturnsAsync(4);

        GivenDue(Group(postDue: true, cutoffDue: true, cutoffUtc: Now.AddSeconds(30)));

        await service.Execute(Tick());

        CollectionAssert.AreEqual(new[] { "announce", "close" }, sequence);
    }

    ///////////////////////////////////////////////
    /// FAILURE ISOLATION
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task OneFailingDeadline_DoesNotStopTheOthers()
    {
        // One event's bad data must not stop another event's contest closing on time.
        applier.Setup(x => x.Apply(It.Is<EventItemCutoffGroup>(g => g.Id == 1), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        GivenDue(Group(id: 1, cutoffDue: true), Group(id: 2, cutoffDue: true));

        var summary = await service.Execute(Tick());

        Assert.AreEqual(1, summary.CutoffsFailed);
        Assert.AreEqual(1, summary.CutoffsApplied);
        repository.Verify(x => x.SetCutoffOutcome(2, "completed", 4, null), Times.Once);
    }

    [TestMethod]
    public async Task AFailingCutoffStaysPendingWhileAttemptsRemain()
    {
        applier.Setup(x => x.Apply(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var group = Group(cutoffDue: true);
        group.CutoffAttempts = 0;
        GivenDue(group);

        await service.Execute(Tick());

        repository.Verify(x => x.SetCutoffOutcome(1, "pending", null, It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public async Task AFailingCutoffIsMarkedFailedOnTheLastAttempt()
    {
        // Otherwise it would be retried every minute forever.
        applier.Setup(x => x.Apply(It.IsAny<EventItemCutoffGroup>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var group = Group(cutoffDue: true);
        group.CutoffAttempts = 2; // MaxAttempts defaults to 3
        GivenDue(group);

        await service.Execute(Tick());

        repository.Verify(x => x.SetCutoffOutcome(1, "failed", null, It.IsAny<string>()), Times.Once);
    }

    ///////////////////////////////////////////////
    /// CACHE
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task CacheIsClearedOncePerTick_NotOncePerDeadline()
    {
        // Several deadlines commonly share a closing minute; ten requests each would
        // be pointless load on the web tier.
        GivenDue(Group(id: 1, cutoffDue: true), Group(id: 2, cutoffDue: true));

        await service.Execute(Tick());

        cache.Verify(x => x.InvalidateCacheAsync(), Times.Once);
    }

    [TestMethod]
    public async Task CacheIsNotClearedWhenNothingClosed()
    {
        GivenDue(Group(postDue: true, cutoffUtc: Now.AddMinutes(30)));

        await service.Execute(Tick());

        cache.Verify(x => x.InvalidateCacheAsync(), Times.Never);
    }

    ///////////////////////////////////////////////
    /// DRY RUN
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task DryRun_WritesNothing()
    {
        GivenDue(Group(postDue: true, cutoffDue: true, cutoffUtc: Now.AddMinutes(30)));

        var summary = await service.Execute(Tick(dryRun: true));

        Assert.IsTrue(summary.DryRun);
        repository.Verify(x => x.SetPostOutcome(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        repository.Verify(x => x.SetCutoffOutcome(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        cache.Verify(x => x.InvalidateCacheAsync(), Times.Never);
    }

    ///////////////////////////////////////////////
    /// TARGETING ONE DEADLINE
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task GroupId_NarrowsTheTickWithoutBypassingTheDueGate()
    {
        // The group still had to come back from the due query; groupId only filters.
        GivenDue(Group(id: 1, cutoffDue: true), Group(id: 2, cutoffDue: true));

        var summary = await service.Execute(Tick(groupId: 2));

        Assert.AreEqual(1, summary.GroupsEvaluated);
        repository.Verify(x => x.SetCutoffOutcome(2, "completed", 4, null), Times.Once);
        repository.Verify(x => x.SetCutoffOutcome(1, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
    }
}
