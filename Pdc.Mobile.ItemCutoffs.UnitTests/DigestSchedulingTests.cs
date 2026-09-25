using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// DIGEST SCHEDULING TESTS
///////////////////////////////////////////////

/// <summary>
/// When the daily digest goes out.
///
/// The decision cannot be made in SQL, because MariaDB cannot resolve an IANA
/// zone, so the candidate query is coarse and everything that matters happens
/// here. The failure these guard against is an event getting its digest at the
/// wrong hour, or twice, or not at all.
/// </summary>
[TestClass]
public class DigestSchedulingTests
{
    private const string Eastern = "America/New_York";

    private Mock<IEventItemCutoffGroupRepository> repository = null!;
    private Mock<IItemCutoffEmailService> email = null!;
    private ItemCutoffExecutorService service = null!;
    private DateTime now;

    [TestInitialize]
    public void Setup()
    {
        repository = new Mock<IEventItemCutoffGroupRepository>();
        email = new Mock<IItemCutoffEmailService>();

        repository.Setup(x => x.FindDue(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(new List<EventItemCutoffGroup>());
        repository.Setup(x => x.FindByEventId(It.IsAny<int>()))
            .Returns(new List<EventItemCutoffGroup>());
        repository.Setup(x => x.FindItems(It.IsAny<int>()))
            .Returns(new List<EventItemCutoffGroupItem>());

        email.Setup(x => x.SendDigest(
                It.IsAny<EventItemCutoffSettings>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(),
                It.IsAny<bool>()))
            .ReturnsAsync(true);
    }

    /// <summary>Builds the executor with the tick at a given UTC instant.</summary>
    private void GivenNowUtc(DateTime utc)
    {
        now = utc;

        var clock = new Mock<IUtcClock>();
        clock.SetupGet(x => x.UtcNow).Returns(utc);

        service = new ItemCutoffExecutorService(
            NullLogger.Instance,
            repository.Object,
            Mock.Of<IAnnouncementPublisher>(),
            Mock.Of<IItemCutoffApplier>(),
            Mock.Of<IEcommerceCacheService>(),
            email.Object,
            new EventTimeZoneResolver(),
            clock.Object,
            new ItemCutoffOptions());
    }

    private static EventItemCutoffSettings Settings(
        bool digestEnabled = true,
        int sendHour = 8,
        DateTime? lastSent = null,
        DateTime? requested = null,
        string timeZone = Eastern) => new()
    {
        EventId = 500,
        EventUuid = "event-uuid",
        EventName = "Jax Westie Fest",
        DigestEnabled = digestEnabled,
        DigestLocalTime = new TimeSpan(sendHour, 0, 0),
        TimeZone = timeZone,
        DigestLastSentLocalDate = lastSent,
        DigestRequestedForDate = requested,
        CompletedEmailEnabled = true
    };

    private void GivenCandidates(params EventItemCutoffSettings[] settings)
    {
        repository.Setup(x => x.FindSettingsDue(It.IsAny<DateTime>())).Returns(settings.ToList());
    }

    private Task Tick(bool dryRun = false) => service.Execute(new ItemCutoffTrigger
    {
        MessageType = MessageTypes.ItemCutoffTick,
        DryRun = dryRun
    });

    ///////////////////////////////////////////////
    /// THE SCHEDULED SEND
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task NotSentBeforeTheConfiguredLocalTime()
    {
        // 11:00 UTC is 07:00 in New York, an hour before the 08:00 send time.
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task SentOnceTheConfiguredLocalTimeHasPassed()
    {
        // 13:00 UTC is 09:00 in New York, past the 08:00 send time.
        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), false), Times.Once);
        repository.Verify(x => x.SetDigestSent(500, new DateTime(2026, 9, 5)), Times.Once);
    }

    [TestMethod]
    public async Task NotSentTwiceInOneLocalDay()
    {
        // The guard that makes a minute-by-minute tick safe.
        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8, lastSent: new DateTime(2026, 9, 5)));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task SentAgainTheNextLocalDay()
    {
        GivenNowUtc(new DateTime(2026, 9, 6, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8, lastSent: new DateTime(2026, 9, 5)));

        await Tick();

        repository.Verify(x => x.SetDigestSent(500, new DateTime(2026, 9, 6)), Times.Once);
    }

    [TestMethod]
    public async Task NotSentWhenTheDigestIsSwitchedOff()
    {
        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(digestEnabled: false));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task ANotYetSentDayIsNotStampedWhenTheSendFails()
    {
        // Otherwise a transient SMTP failure would silently cost the event its
        // digest for the whole day.
        email.Setup(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8));

        await Tick();

        repository.Verify(x => x.SetDigestSent(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    ///////////////////////////////////////////////
    /// THE ON-DEMAND SEND
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task AnOnDemandRequestIsSentImmediately()
    {
        // 11:00 UTC is 07:00 local, before the 08:00 schedule - the request must
        // not wait for it.
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8, requested: new DateTime(2026, 9, 5)));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), new DateTime(2026, 9, 5),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), false), Times.Once);
    }

    [TestMethod]
    public async Task AnOnDemandRequestIsHonouredEvenWhenTheDigestIsOff()
    {
        // An event that wants no daily mail but wants today's list is a real case.
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(digestEnabled: false, requested: new DateTime(2026, 9, 5)));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), false), Times.Once);
    }

    [TestMethod]
    public async Task AnOnDemandRequestCanAskForAnotherDay()
    {
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(requested: new DateTime(2026, 9, 6)));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), new DateTime(2026, 9, 6),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), false), Times.Once);
    }

    [TestMethod]
    public async Task AnOnDemandRequestIsClearedAfterSending()
    {
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(requested: new DateTime(2026, 9, 5)));

        await Tick();

        repository.Verify(x => x.SetDigestRequested(500, null), Times.Once);
    }

    [TestMethod]
    public async Task AnOnDemandRequestIsClearedEvenWhenTheSendFails()
    {
        // Deliberate: an admin who sees no email presses the button again, which
        // is more predictable than retrying a bad address on every tick forever.
        email.Setup(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(requested: new DateTime(2026, 9, 5)));

        await Tick();

        repository.Verify(x => x.SetDigestRequested(500, null), Times.Once);
    }

    [TestMethod]
    public async Task AnOnDemandSendDoesNotConsumeTheScheduledOne()
    {
        // Asking for the list early must not cost the event its scheduled digest.
        GivenNowUtc(new DateTime(2026, 9, 5, 11, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8, requested: new DateTime(2026, 9, 5)));

        await Tick();

        repository.Verify(x => x.SetDigestSent(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    ///////////////////////////////////////////////
    /// ROBUSTNESS
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task AnUnknownTimeZoneSkipsThatEventWithoutThrowing()
    {
        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(timeZone: "Not/AZone"));

        await Tick();

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task OneFailingEventDoesNotStopAnother()
    {
        var broken = Settings(timeZone: "Not/AZone");
        var working = Settings();
        working.EventId = 501;

        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(broken, working);

        await Tick();

        repository.Verify(x => x.SetDigestSent(501, It.IsAny<DateTime>()), Times.Once);
    }

    [TestMethod]
    public async Task DryRunSendsAndStampsNothing()
    {
        GivenNowUtc(new DateTime(2026, 9, 5, 13, 0, 0, DateTimeKind.Utc));
        GivenCandidates(Settings(sendHour: 8));

        await Tick(dryRun: true);

        email.Verify(x => x.SendDigest(It.IsAny<EventItemCutoffSettings>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<EventItemCutoffGroup>>(), true), Times.Once);
        repository.Verify(x => x.SetDigestSent(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
        repository.Verify(x => x.SetDigestRequested(It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
    }
}
