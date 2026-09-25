namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// Converts a UTC instant to an event's local wall-clock time.
///
/// DELIBERATELY NARROWER THAN THE API'S RESOLVER. The API owns the wall clock to
/// UTC direction, with its rules for the daylight saving gap and the ambiguous
/// hour, because it is the only writer of the derived eicg_cutoff_utc and
/// eicg_post_send_utc columns. Duplicating those rules here would create exactly
/// the drift this feature has avoided everywhere else.
///
/// This direction needs none of that: every UTC instant maps to exactly one
/// local time in a zone, with no gap and no ambiguity. So the executor gets the
/// one conversion it actually needs to decide an event's local date, and nothing
/// more.
/// </summary>
public interface IEventTimeZoneResolver
{
    /// <summary>True when the id names a zone this machine can resolve.</summary>
    bool IsKnownTimeZone(string timeZoneId);

    /// <summary>UTC to wall clock. Throws when the zone is unknown.</summary>
    DateTime ToLocal(DateTime utc, string timeZoneId);
}
