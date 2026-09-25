using Pdc.EventPro.Domain.Entities;
using Pdc.Mobile.ItemCutoffs.Models;
using System.Globalization;
using System.Text;

namespace Pdc.Mobile.ItemCutoffs.Services.Email;

///////////////////////////////////////////////
/// CUTOFF COMPLETED EMAIL BUILDER
///////////////////////////////////////////////

/// <summary>
/// Builds the email sent the moment a sales deadline closes.
///
/// This replaces the manual text message that step 3 of the registration
/// workflow has always described: "WDR texts registration asking to stop selling
/// those divisions and registration clears the point of sale cache."
///
/// IT EXISTS BECAUSE THE POINT OF SALE DOES NOT CLOSE BY ITSELF. Setting
/// admin_only hides the items from non-admin staff in the database, but every
/// running WinClient serves from its own in-process cache until its operator
/// uses Clear Cache. So this email is not a courtesy notification - it is the
/// only thing that makes the point of sale actually stop selling, and the
/// instruction has to be unmissable.
///
/// Pure and static, so the wording is pinned by tests.
/// </summary>
public static class CutoffCompletedEmailBuilder
{
    private const string TimeFormat = "h:mm tt";

    /// <summary>
    /// Builds the email for one completed deadline.
    /// </summary>
    /// <param name="deadline">The deadline that just closed.</param>
    /// <param name="itemsClosed">Product rows changed, which exceeds the item count when items have several price points.</param>
    public static ItemCutoffEmailMessage Build(EventItemCutoffGroup deadline, int itemsClosed)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"These items have just closed for sale online at {FormatTime(deadline.CutoffLocal)}:");
        builder.AppendLine();

        foreach (var item in deadline.Items.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {item.DisplayName}");
        }

        builder.AppendLine();
        builder.AppendLine("ACTION NEEDED AT THE REGISTRATION DESK");
        builder.AppendLine();
        builder.AppendLine("1. Stop selling these items.");
        builder.AppendLine("2. Clear the point of sale cache on every station. (Tools --> Clear Cache)");
        builder.AppendLine();

        // Said explicitly rather than assumed. A station that has not cleared its
        // cache will keep offering these items and the sale will go through, which
        // is exactly the failure this email exists to prevent.
        builder.AppendLine("Until a station clears its cache it will still show these items and will still take payment for them.");
        builder.AppendLine();
        builder.AppendLine($"Online sales are already closed. {itemsClosed} price point(s) were closed across {deadline.Items.Count} item(s).");

        return new ItemCutoffEmailMessage
        {
            Subject = $"{deadline.EventName} - CLOSED: {deadline.Name} - clear the point of sale cache",
            Body = builder.ToString().TrimEnd()
        };
    }

    private static string FormatTime(DateTime value)
        => value.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
