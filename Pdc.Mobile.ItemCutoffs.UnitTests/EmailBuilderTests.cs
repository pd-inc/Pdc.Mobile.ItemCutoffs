using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Domain.Enumerations;
using Pdc.Mobile.ItemCutoffs.Services.Email;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// EMAIL BUILDER TESTS
///////////////////////////////////////////////

/// <summary>
/// The wording of both operational emails.
///
/// These are pinned because the registration team acts on them. The
/// cutoff-completed email in particular is the ONLY thing that makes the point
/// of sale stop selling, since a running WinClient serves from its own cache
/// until its operator clears it - so if its instruction ever goes missing, the
/// desk keeps taking money for a contest that closed.
/// </summary>
[TestClass]
public class EmailBuilderTests
{
    private static EventItemCutoffGroup Deadline(
        string name = "Saturday 1 PM",
        int hour = 13,
        int? leadMinutes = 30,
        bool enabled = true,
        params string[] itemNames)
    {
        return new EventItemCutoffGroup
        {
            Id = 1,
            EventId = 500,
            EventUuid = "event-uuid",
            EventName = "Jax Westie Fest",
            Name = name,
            Enabled = enabled,
            CutoffLocal = new DateTime(2026, 9, 5, hour, 0, 0),
            PostEnabled = leadMinutes.HasValue,
            PostLeadMinutes = leadMinutes,
            Items = itemNames.Select(itemName => new EventItemCutoffGroupItem
            {
                ProductType = ProductType.Division,
                InternalCode = itemName,
                DisplayName = itemName
            }).ToList()
        };
    }

    ///////////////////////////////////////////////
    /// CUTOFF COMPLETED
    ///////////////////////////////////////////////

    [TestMethod]
    public void CompletedEmail_TellsThemToClearThePointOfSaleCache()
    {
        // The whole reason this email exists.
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(itemNames: new[] { "Novice J&J" }), itemsClosed: 2);

        StringAssert.Contains(message.Body, "Clear the point of sale cache");
        StringAssert.Contains(message.Subject, "clear the point of sale cache");
    }

    [TestMethod]
    public void CompletedEmail_SaysAnUnclearedStationWillStillTakePayment()
    {
        // Stated explicitly rather than implied, because the consequence of
        // skipping the step is money taken for something that has closed.
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(itemNames: new[] { "Novice J&J" }), itemsClosed: 2);

        StringAssert.Contains(message.Body, "will still take payment");
    }

    [TestMethod]
    public void CompletedEmail_ListsEveryItemThatClosed()
    {
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(itemNames: new[] { "Novice J&J", "Intermediate J&J" }), itemsClosed: 4);

        StringAssert.Contains(message.Body, "- Novice J&J");
        StringAssert.Contains(message.Body, "- Intermediate J&J");
    }

    [TestMethod]
    public void CompletedEmail_ReportsPricePointsSeparatelyFromItems()
    {
        // 2 items can be 4 product rows. The team sees the item names; the count
        // explains why the number in the log is bigger.
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(itemNames: new[] { "Novice J&J", "Intermediate J&J" }), itemsClosed: 4);

        StringAssert.Contains(message.Body, "4 price point(s)");
        StringAssert.Contains(message.Body, "2 item(s)");
    }

    [TestMethod]
    public void CompletedEmail_SubjectNamesTheEventAndTheDeadline()
    {
        // Registration staff work several events; the subject has to disambiguate
        // at a glance in a phone notification.
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(name: "Saturday 1 PM", itemNames: new[] { "Novice J&J" }), itemsClosed: 1);

        StringAssert.Contains(message.Subject, "Jax Westie Fest");
        StringAssert.Contains(message.Subject, "Saturday 1 PM");
    }

    [TestMethod]
    public void CompletedEmail_UsesNoEmDashes()
    {
        var message = CutoffCompletedEmailBuilder.Build(
            Deadline(itemNames: new[] { "Novice J&J" }), itemsClosed: 1);

        Assert.IsFalse(message.Body.Contains('—'));
        Assert.IsFalse(message.Subject.Contains('—'));
    }

    ///////////////////////////////////////////////
    /// DIGEST
    ///////////////////////////////////////////////

    [TestMethod]
    public void Digest_CarriesTheThreeWorkflowSteps()
    {
        // The team already knows this shape from the hand-written version.
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(itemNames: new[] { "Novice J&J" })
        });

        var message = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5), closeTimes);

        StringAssert.Contains(message.Body, "1. Before the close time");
        StringAssert.Contains(message.Body, "2. At the close time");
        StringAssert.Contains(message.Body, "3. Right after that");
    }

    [TestMethod]
    public void Digest_SaysEmailNotText()
    {
        // The manual workflow said "WDR texts registration". The completed email
        // replaces that message, so the instruction has to point at the inbox the
        // team should actually watch.
        var message = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5),
            DigestEmailBuilder.GroupCloseTimes(new[] { Deadline(itemNames: new[] { "Novice J&J" }) }));

        StringAssert.Contains(message.Body, "WDR emails registration");
        StringAssert.Contains(message.Body, "Wait for the email");
        Assert.IsFalse(message.Body.Contains("texts registration"));
    }

    [TestMethod]
    public void Digest_ShowsTheCloseTimeAndTheAnnouncementTime()
    {
        var message = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5),
            DigestEmailBuilder.GroupCloseTimes(new[] { Deadline(hour: 13, leadMinutes: 30, itemNames: new[] { "Novice J&J" }) }));

        StringAssert.Contains(message.Body, "1:00 PM close");
        StringAssert.Contains(message.Body, "news feed at 12:30 PM");
    }

    [TestMethod]
    public void Digest_OmitsTheAnnouncementTimeWhenThereIsNoAnnouncement()
    {
        var message = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5),
            DigestEmailBuilder.GroupCloseTimes(new[] { Deadline(leadMinutes: null, itemNames: new[] { "Novice J&J" }) }));

        StringAssert.Contains(message.Body, "1:00 PM close");
        Assert.IsFalse(message.Body.Contains("news feed at"));
    }

    [TestMethod]
    public void Digest_SaysSoPlainlyWhenNothingCloses()
    {
        // An empty day is a normal answer. Sending a blank list would read as a
        // broken email and have someone chase it.
        var message = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5),
            new List<Pdc.Mobile.ItemCutoffs.Models.DigestCloseTime>());

        StringAssert.Contains(message.Body, "Nothing is scheduled to close");
    }

    [TestMethod]
    public void Digest_SaysTodayOnlyWhenItIsToday()
    {
        var today = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5),
            DigestEmailBuilder.GroupCloseTimes(new[] { Deadline(itemNames: new[] { "Novice J&J" }) }));

        StringAssert.Contains(today.Body, "Today's Close Times");
        StringAssert.Contains(today.Subject, "today's close times");

        var tomorrow = DigestEmailBuilder.Build(
            "Jax Westie Fest", new DateTime(2026, 9, 6), new DateTime(2026, 9, 5),
            DigestEmailBuilder.GroupCloseTimes(new[] { Deadline(itemNames: new[] { "Novice J&J" }) }));

        StringAssert.Contains(tomorrow.Body, "Sunday Close Times");
        Assert.IsFalse(tomorrow.Body.Contains("Today's Close Times"));
    }

    ///////////////////////////////////////////////
    /// DIGEST GROUPING
    ///////////////////////////////////////////////

    [TestMethod]
    public void Grouping_CollapsesDeadlinesSharingACloseTime()
    {
        // Registration reads one instruction per moment, not one per deadline.
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(name: "A", hour: 13, itemNames: new[] { "Novice J&J" }),
            Deadline(name: "B", hour: 13, itemNames: new[] { "Intermediate J&J" })
        });

        Assert.AreEqual(1, closeTimes.Count);
        CollectionAssert.AreEqual(
            new[] { "Intermediate J&J", "Novice J&J" },
            closeTimes[0].ItemNames);
    }

    [TestMethod]
    public void Grouping_OrdersCloseTimesChronologically()
    {
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(hour: 18, itemNames: new[] { "Champions J&J" }),
            Deadline(hour: 13, itemNames: new[] { "Novice J&J" })
        });

        Assert.AreEqual(13, closeTimes[0].CutoffLocal.Hour);
        Assert.AreEqual(18, closeTimes[1].CutoffLocal.Hour);
    }

    [TestMethod]
    public void Grouping_ExcludesDisabledDeadlines()
    {
        // A disabled deadline will not run. Listing it would have the team stop
        // selling something that stays on sale.
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(hour: 13, enabled: false, itemNames: new[] { "Novice J&J" })
        });

        Assert.AreEqual(0, closeTimes.Count);
    }

    [TestMethod]
    public void Grouping_DeduplicatesAnItemNamedByTwoDeadlines()
    {
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(name: "A", hour: 13, itemNames: new[] { "Novice J&J" }),
            Deadline(name: "B", hour: 13, itemNames: new[] { "Novice J&J" })
        });

        Assert.AreEqual(1, closeTimes[0].ItemNames.Count);
    }

    [TestMethod]
    public void Grouping_TakesTheEarliestAnnouncementWhenDeadlinesShareATime()
    {
        // Two deadlines closing together may announce at different leads; the
        // earliest is the one the team will see first.
        var closeTimes = DigestEmailBuilder.GroupCloseTimes(new[]
        {
            Deadline(name: "A", hour: 13, leadMinutes: 30, itemNames: new[] { "Novice J&J" }),
            Deadline(name: "B", hour: 13, leadMinutes: 60, itemNames: new[] { "Int J&J" })
        });

        Assert.AreEqual(12, closeTimes[0].AnnouncementLocal!.Value.Hour);
        Assert.AreEqual(0, closeTimes[0].AnnouncementLocal!.Value.Minute);
    }
}
