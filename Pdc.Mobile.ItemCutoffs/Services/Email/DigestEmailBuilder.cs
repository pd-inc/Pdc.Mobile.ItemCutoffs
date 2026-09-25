using Pdc.EventPro.Domain.Entities;
using Pdc.Mobile.ItemCutoffs.Models;
using System.Globalization;
using System.Text;

namespace Pdc.Mobile.ItemCutoffs.Services.Email;

///////////////////////////////////////////////
/// DIGEST EMAIL BUILDER
///////////////////////////////////////////////

/// <summary>
/// Builds the registration team's daily workflow email.
///
/// This reproduces, from real data, the email an operator has been filling in by
/// hand from wdr-news-feed-planner's reg-team-email template. The wording is
/// deliberately kept close to that template, because the registration teams at
/// these events already know it and a rewrite would cost them a re-read on a
/// busy morning.
///
/// One change from the manual version: step 3 and the footer now say EMAIL
/// rather than text, because the cutoff-completed email replaces the manual text
/// message that used to prompt registration to clear the point of sale cache.
///
/// Pure and static, so the wording is pinned by tests rather than discovered in
/// an inbox.
/// </summary>
public static class DigestEmailBuilder
{
    private const string TimeFormat = "h:mm tt";
    private const string DayFormat = "dddd";
    private const string DateFormat = "dddd, MMMM d";

    /// <summary>
    /// Builds the digest for one event-local day.
    /// </summary>
    /// <param name="eventName">The event, for the subject and heading.</param>
    /// <param name="localDate">The event-local day being reported.</param>
    /// <param name="today">The event's own "today", so the copy can say "today".</param>
    /// <param name="closeTimes">The day's closing times, in chronological order.</param>
    public static ItemCutoffEmailMessage Build(
        string eventName,
        DateTime localDate,
        DateTime today,
        IReadOnlyList<DigestCloseTime> closeTimes)
    {
        var isToday = localDate.Date == today.Date;
        var dayWord = localDate.ToString(DayFormat, CultureInfo.InvariantCulture);

        var builder = new StringBuilder();

        builder.AppendLine($"{eventName} - {dayWord} item close workflow");
        builder.AppendLine();
        builder.AppendLine("WDR will handle the app posts and the actual item closures.");
        builder.AppendLine();
        builder.AppendLine($"Steps for each closure {(isToday ? "today" : $"on {dayWord}")}:");
        builder.AppendLine();
        builder.AppendLine("1. Before the close time - WDR posts a note in the app news feed telling attendees what is about to close.");
        builder.AppendLine("2. At the close time - WDR closes the items for sale online.");
        builder.AppendLine("3. Right after that - WDR emails registration to stop selling those items, and registration clears the point of sale cache.");
        builder.AppendLine();
        builder.AppendLine(isToday
            ? "Today's Close Times"
            : $"{dayWord} Close Times");
        builder.AppendLine();

        if (closeTimes.Count == 0)
        {
            // Said plainly rather than sending an email that looks broken. An
            // empty day is a normal answer, and the team should not have to guess
            // whether the list failed to load.
            builder.AppendLine($"Nothing is scheduled to close on {localDate.ToString(DateFormat, CultureInfo.InvariantCulture)}.");
        }
        else
        {
            foreach (var closeTime in closeTimes)
            {
                builder.Append(FormatTime(closeTime.CutoffLocal));
                builder.Append(" close");

                if (closeTime.AnnouncementLocal.HasValue)
                {
                    builder.Append($" (news feed at {FormatTime(closeTime.AnnouncementLocal.Value)})");
                }

                builder.AppendLine();
                builder.AppendLine();

                foreach (var itemName in closeTime.ItemNames)
                {
                    builder.AppendLine($"- {itemName}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("Do not stop selling when the news feed post goes out. Wait for the email after WDR has closed the items.");

        return new ItemCutoffEmailMessage
        {
            Subject = isToday
                ? $"{eventName} - today's close times"
                : $"{eventName} - close times for {dayWord}",
            Body = builder.ToString().TrimEnd()
        };
    }

    /// <summary>
    /// Groups a day's deadlines into the close times the email lists.
    ///
    /// Deadlines sharing a closing minute collapse into one block, because that is
    /// how registration reads them: one instruction per moment, not one per
    /// deadline. Disabled deadlines are excluded - they will not run, so listing
    /// them would have the team stop selling something that stays on sale.
    /// </summary>
    public static List<DigestCloseTime> GroupCloseTimes(IEnumerable<EventItemCutoffGroup> deadlines)
    {
        return deadlines
            .Where(deadline => deadline.Enabled)
            .GroupBy(deadline => deadline.CutoffLocal)
            .OrderBy(group => group.Key)
            .Select(group => new DigestCloseTime
            {
                CutoffLocal = group.Key,

                // When several deadlines share a close time their announcements
                // may differ; the earliest is the one the team will see first.
                AnnouncementLocal = group
                    .Where(deadline => deadline.PostEnabled && deadline.PostLeadMinutes.HasValue)
                    .Select(deadline => (DateTime?)deadline.CutoffLocal.AddMinutes(-deadline.PostLeadMinutes!.Value))
                    .OrderBy(announcement => announcement)
                    .FirstOrDefault(),

                ItemNames = group
                    .SelectMany(deadline => deadline.Items.Select(item => item.DisplayName))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();
    }

    private static string FormatTime(DateTime value)
        => value.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
