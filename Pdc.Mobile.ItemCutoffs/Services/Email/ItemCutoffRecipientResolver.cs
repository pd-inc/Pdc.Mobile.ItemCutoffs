using Microsoft.Extensions.Logging;
using Pdc.EventPro.Repositories;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services.Email;

///////////////////////////////////////////////
/// ITEM CUTOFF RECIPIENT RESOLVER
///////////////////////////////////////////////

/// <summary>
/// Resolves sales deadline email recipients from the event's role assignments.
///
/// The role set is the hardcoded EventRoleRecipients.DefaultRoleNames, which
/// lives in the shared package precisely because these emails have two senders
/// (this Lambda and the REST API) and two copies of that list would drift.
///
/// The underlying procedure has no device-token join, unlike the older
/// select_event_users_by_role_and_event_id, so a director who never installed
/// the app is still emailed.
/// </summary>
public class ItemCutoffRecipientResolver : IItemCutoffRecipientResolver
{
    private readonly ILogger logger;
    private readonly IFindEventUsersByRoleRepository roleRepository;

    public ItemCutoffRecipientResolver(
        ILogger logger,
        IFindEventUsersByRoleRepository roleRepository)
    {
        this.logger = logger;
        this.roleRepository = roleRepository;
    }

    /// <summary>
    /// The event's role holders, one entry per person.
    /// </summary>
    public List<ItemCutoffRecipient> Resolve(string eventUuid)
    {
        var rows = roleRepository
            .FindRecipients(eventUuid, EventRoleRecipients.DefaultRoleNames)
            .ToList();

        // A patron holding two of the configured roles comes back twice. Grouping
        // by ADDRESS rather than patron id also collapses the case of two accounts
        // sharing a mailbox, which happens with event staff addresses.
        var recipients = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.PatronEmail))
            .GroupBy(row => row.PatronEmail.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ItemCutoffRecipient
            {
                PatronId = group.First().PatronId,
                Email = group.Key,
                FullName = $"{group.First().PatronFirstName} {group.First().PatronLastName}".Trim(),
                RoleNames = group
                    .Select(row => row.RoleDisplayName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(recipient => recipient.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // WDR support is always copied, so there is always at least one recipient
        // and an event with no staff roles filled in still produces a send. Added
        // only when no role holder already uses that address, so a staff member on
        // the support mailbox is not emailed twice.
        if (!recipients.Any(recipient =>
                string.Equals(recipient.Email, EventRoleRecipients.AlwaysIncludedAddress, StringComparison.OrdinalIgnoreCase)))
        {
            recipients.Add(new ItemCutoffRecipient
            {
                PatronId = int.MinValue,
                Email = EventRoleRecipients.AlwaysIncludedAddress,
                FullName = "WDR Support",
                RoleNames = new List<string> { "WDR" }
            });
        }

        var skipped = rows.Count(row => string.IsNullOrWhiteSpace(row.PatronEmail));

        if (skipped > 0)
        {
            // Surfaced rather than silently dropped: a role holder with no address
            // is an event data problem someone can fix.
            logger.LogWarning("{FeatureKey} {SkippedCount} role holder(s) for EventUuid {EventUuid} have no email address",
                LogProperties.EmailNoRecipients, skipped, eventUuid);
        }

        return recipients;
    }
}
