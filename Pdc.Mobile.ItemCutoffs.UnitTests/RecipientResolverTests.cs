using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pdc.EventPro.Domain;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Services.Email;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// RECIPIENT RESOLVER TESTS
///////////////////////////////////////////////

/// <summary>
/// Who receives the sales deadline emails.
///
/// Two rules matter here. WDR support is always copied, so there is always at
/// least one recipient and an event with no staff roles still produces a send.
/// And role_admin is excluded, because its holders are platform administrators
/// rather than the people working a registration desk - including it sent them
/// every closure notice for every event.
/// </summary>
[TestClass]
public class RecipientResolverTests
{
    private const string EventUuid = "event-uuid";
    private const string SupportAddress = "support@worlddanceregistry.com";

    private Mock<IFindEventUsersByRoleRepository> roleRepository = null!;
    private ItemCutoffRecipientResolver resolver = null!;

    [TestInitialize]
    public void Setup()
    {
        roleRepository = new Mock<IFindEventUsersByRoleRepository>();
        resolver = new ItemCutoffRecipientResolver(NullLogger.Instance, roleRepository.Object);
    }

    private static EventUserRole Row(
        string email,
        string first = "Jane",
        string last = "Doe",
        string roleDisplay = "Director",
        int patronId = 1) => new()
    {
        PatronId = patronId,
        PatronFirstName = first,
        PatronLastName = last,
        PatronEmail = email,
        RoleDisplayName = roleDisplay
    };

    private void GivenRoleHolders(params EventUserRole[] rows)
    {
        roleRepository.Setup(x => x.FindRecipients(EventUuid, It.IsAny<string>())).Returns(rows);
    }

    ///////////////////////////////////////////////
    /// THE ROLE SET
    ///////////////////////////////////////////////

    [TestMethod]
    public void TheRoleSetExcludesRoleAdmin()
    {
        // Platform administrators are not registration staff, and including them
        // meant every closure notice for every event reached them.
        Assert.IsFalse(EventRoleRecipients.DefaultRoleNames.Contains("role_admin"));
    }

    [TestMethod]
    public void TheRoleSetCarriesTheSevenOperationsRoles()
    {
        var roles = EventRoleRecipients.DefaultRoleNames.Split(',');

        CollectionAssert.AreEquivalent(
            new[]
            {
                "role_registration",
                "role_chief_judge",
                "role_director",
                "role_registration_manager",
                "role_biography_admin",
                "role_registration_admin",
                "role_contest_admin"
            },
            roles);
    }

    [TestMethod]
    public void TheRoleSetHasNoSpaces()
    {
        // FIND_IN_SET matches the value verbatim, so a space would silently drop
        // the role either side of it.
        Assert.IsFalse(EventRoleRecipients.DefaultRoleNames.Contains(' '));
    }

    ///////////////////////////////////////////////
    /// WDR SUPPORT IS ALWAYS COPIED
    ///////////////////////////////////////////////

    [TestMethod]
    public void SupportIsAddedAlongsideTheRoleHolders()
    {
        GivenRoleHolders(Row("director@event.com"));

        var recipients = resolver.Resolve(EventUuid);

        Assert.AreEqual(2, recipients.Count);
        Assert.IsTrue(recipients.Any(r => r.Email == SupportAddress));
        Assert.IsTrue(recipients.Any(r => r.Email == "director@event.com"));
    }

    [TestMethod]
    public void SupportIsTheOnlyRecipientWhenTheEventHasNoRoleHolders()
    {
        // The reason the "is anyone listening" refusal was removed: there is
        // always somebody, so enabling an email can never deliver to nobody.
        GivenRoleHolders();

        var recipients = resolver.Resolve(EventUuid);

        Assert.AreEqual(1, recipients.Count);
        Assert.AreEqual(SupportAddress, recipients[0].Email);
    }

    [TestMethod]
    public void SupportIsNotDuplicatedWhenARoleHolderUsesThatAddress()
    {
        // Otherwise the mailbox would receive every notice twice.
        GivenRoleHolders(Row(SupportAddress, first: "WDR", last: "Staff"));

        var recipients = resolver.Resolve(EventUuid);

        Assert.AreEqual(1, recipients.Count);
    }

    [TestMethod]
    public void SupportMatchingIgnoresCase()
    {
        GivenRoleHolders(Row("SUPPORT@WorldDanceRegistry.com"));

        var recipients = resolver.Resolve(EventUuid);

        Assert.AreEqual(1, recipients.Count);
    }

    ///////////////////////////////////////////////
    /// DEDUPLICATION AND FILTERING
    ///////////////////////////////////////////////

    [TestMethod]
    public void APersonHoldingTwoRolesIsOneRecipientCarryingBoth()
    {
        GivenRoleHolders(
            Row("jane@event.com", roleDisplay: "Director"),
            Row("jane@event.com", roleDisplay: "Registration Admin"));

        var recipients = resolver.Resolve(EventUuid);
        var jane = recipients.Single(r => r.Email == "jane@event.com");

        CollectionAssert.AreEqual(new[] { "Director", "Registration Admin" }, jane.RoleNames);
    }

    [TestMethod]
    public void TwoAccountsSharingAMailboxCollapseToOneRecipient()
    {
        GivenRoleHolders(
            Row("desk@event.com", first: "Jane", patronId: 1),
            Row("desk@event.com", first: "Sam", patronId: 2));

        var recipients = resolver.Resolve(EventUuid);

        Assert.AreEqual(1, recipients.Count(r => r.Email == "desk@event.com"));
    }

    [TestMethod]
    public void RoleHoldersWithNoAddressAreSkipped()
    {
        GivenRoleHolders(Row("director@event.com"), Row(string.Empty, first: "No", last: "Email"));

        var recipients = resolver.Resolve(EventUuid);

        Assert.IsFalse(recipients.Any(r => string.IsNullOrWhiteSpace(r.Email)));
    }

    [TestMethod]
    public void AddressesAreTrimmed()
    {
        GivenRoleHolders(Row("  director@event.com  "));

        var recipients = resolver.Resolve(EventUuid);

        Assert.IsTrue(recipients.Any(r => r.Email == "director@event.com"));
    }
}
