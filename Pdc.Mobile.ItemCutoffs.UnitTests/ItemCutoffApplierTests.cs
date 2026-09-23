using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pdc.EventPro.Domain.Entities;
using Pdc.EventPro.Domain.Enumerations;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Services;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// ITEM CUTOFF APPLIER TESTS
///////////////////////////////////////////////

/// <summary>
/// What closing actually does to the database.
///
/// The expansion from one internal code to every price point is the feature's
/// central promise: an admin closes "Novice J&amp;J" once and no variant of it is
/// left quietly on sale past the deadline.
/// </summary>
[TestClass]
public class ItemCutoffApplierTests
{
    private const int EventId = 500;

    private Mock<IItemRepository> itemRepository = null!;
    private Mock<IUpdateEventItemRepository> updateRepository = null!;
    private Mock<IEventItemCutoffGroupRepository> cutoffRepository = null!;
    private ItemCutoffApplier applier = null!;

    [TestInitialize]
    public void Setup()
    {
        itemRepository = new Mock<IItemRepository>();
        updateRepository = new Mock<IUpdateEventItemRepository>();
        cutoffRepository = new Mock<IEventItemCutoffGroupRepository>();

        applier = new ItemCutoffApplier(
            NullLogger.Instance,
            itemRepository.Object,
            updateRepository.Object,
            cutoffRepository.Object);
    }

    private static EventItem Item(int id, string code, string name, ProductType type = ProductType.Division,
        bool display = true, bool adminOnly = false, bool? salesClosed = null) => new()
    {
        Id = id,
        ItemId = id,
        Type = type,
        InternalCode = code,
        Name = name,
        Display = display,
        AdminOnly = adminOnly,
        SalesClosed = salesClosed
    };

    private static EventItemCutoffGroup Group(params (ProductType Type, string Code)[] codes) => new()
    {
        Id = 1,
        EventId = EventId,
        Name = "Saturday 1 PM",
        Items = codes.Select(entry => new EventItemCutoffGroupItem
        {
            ProductType = entry.Type,
            InternalCode = entry.Code,
            DisplayName = entry.Code
        }).ToList()
    };

    private void GivenEventItems(params EventItem[] items)
    {
        itemRepository.Setup(x => x.FindByEventId(EventId)).Returns(items.ToList());
    }

    ///////////////////////////////////////////////
    /// CODE EXPANSION
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task OneCodeClosesEveryPricePointSharingIt()
    {
        GivenEventItems(
            Item(10, "JJ-NOV", "Novice"),
            Item(11, "JJ-NOV", "Novice (Thru Aug 16)"),
            Item(12, "JJ-INT", "Intermediate"));

        var closed = await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        Assert.AreEqual(2, closed);
        updateRepository.Verify(x => x.UpdateDisplayFlag(EventId, ProductType.Division, 10, false), Times.Once);
        updateRepository.Verify(x => x.UpdateDisplayFlag(EventId, ProductType.Division, 11, false), Times.Once);
        updateRepository.Verify(x => x.UpdateDisplayFlag(EventId, ProductType.Division, 12, It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task TheSameCodeUnderAnotherProductType_IsNotClosed()
    {
        // A code alone is ambiguous, so closing a division must not close a workshop.
        GivenEventItems(
            Item(10, "SHARED", "A Division", ProductType.Division),
            Item(20, "SHARED", "A Workshop", ProductType.Workshop));

        var closed = await applier.Apply(Group((ProductType.Division, "SHARED")), dryRun: false);

        Assert.AreEqual(1, closed);
        updateRepository.Verify(x => x.UpdateDisplayFlag(EventId, ProductType.Workshop, 20, It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task AGroupSpanningProductTypes_ClosesBoth()
    {
        GivenEventItems(
            Item(10, "JJ-NOV", "Novice", ProductType.Division),
            Item(20, "WK-1", "A Workshop", ProductType.Workshop));

        var closed = await applier.Apply(
            Group((ProductType.Division, "JJ-NOV"), (ProductType.Workshop, "WK-1")), dryRun: false);

        Assert.AreEqual(2, closed);
    }

    ///////////////////////////////////////////////
    /// THE THREE FLAGS
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task AllThreeFlagsAreSetOnEveryRow()
    {
        GivenEventItems(Item(10, "JJ-NOV", "Novice"));

        await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        updateRepository.Verify(x => x.UpdateSalesClosedFlag(EventId, ProductType.Division, 10, true), Times.Once);
        updateRepository.Verify(x => x.UpdateAdminOnlyFlag(EventId, ProductType.Division, 10, true), Times.Once);
        updateRepository.Verify(x => x.UpdateDisplayFlag(EventId, ProductType.Division, 10, false), Times.Once);
    }

    [TestMethod]
    public async Task SalesClosedIsSetBeforeDisplayIsCleared()
    {
        // If the process dies mid-item, the visibility jobs must already know not to
        // reopen it. The reverse order would leave a window where the item is closed
        // but unprotected.
        var order = new List<string>();

        updateRepository.Setup(x => x.UpdateSalesClosedFlag(It.IsAny<int>(), It.IsAny<ProductType>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback(() => order.Add("salesClosed"));
        updateRepository.Setup(x => x.UpdateDisplayFlag(It.IsAny<int>(), It.IsAny<ProductType>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback(() => order.Add("display"));

        GivenEventItems(Item(10, "JJ-NOV", "Novice"));

        await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        Assert.IsTrue(order.IndexOf("salesClosed") < order.IndexOf("display"));
    }

    ///////////////////////////////////////////////
    /// THE AUDIT TRAIL
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task PriorFlagsAreRecordedBeforeTheRowIsChanged()
    {
        GivenEventItems(Item(10, "JJ-NOV", "Novice", display: true, adminOnly: true, salesClosed: null));

        await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        cutoffRepository.Verify(x => x.InsertItemResult(It.Is<EventItemCutoffGroupItemResult>(result =>
            result.ItemId == 10
            && result.PriorDisplay == true
            // The item was legitimately admin-only before the deadline. Recording
            // that is what would make a future reopen honest rather than a guess.
            && result.PriorAdminOnly == true
            && result.PriorSalesClosed == null)), Times.Once);
    }

    ///////////////////////////////////////////////
    /// IDEMPOTENCE
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task AlreadyClosedRowsAreSkippedOnARetry()
    {
        // A retry after a partial failure must not re-stamp audit rows for work that
        // was already done.
        GivenEventItems(
            Item(10, "JJ-NOV", "Novice", salesClosed: true),
            Item(11, "JJ-NOV", "Novice (Thru Aug 16)", salesClosed: null));

        var closed = await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        Assert.AreEqual(1, closed);
        cutoffRepository.Verify(x => x.InsertItemResult(It.IsAny<EventItemCutoffGroupItemResult>()), Times.Once);
    }

    ///////////////////////////////////////////////
    /// EDGE CASES
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task NoMatchingRows_ClosesNothingAndDoesNotThrow()
    {
        // The item codes were removed from the event after the deadline was saved.
        GivenEventItems(Item(10, "OTHER", "Something else"));

        var closed = await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        Assert.AreEqual(0, closed);
        updateRepository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DryRun_CountsWithoutWriting()
    {
        GivenEventItems(Item(10, "JJ-NOV", "Novice"), Item(11, "JJ-NOV", "Novice (Thru Aug 16)"));

        var closed = await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: true);

        Assert.AreEqual(2, closed);
        updateRepository.VerifyNoOtherCalls();
        cutoffRepository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task CodeMatchingIgnoresSurroundingWhitespace()
    {
        GivenEventItems(Item(10, " JJ-NOV ", "Novice"));

        var closed = await applier.Apply(Group((ProductType.Division, "JJ-NOV")), dryRun: false);

        Assert.AreEqual(1, closed);
    }
}
