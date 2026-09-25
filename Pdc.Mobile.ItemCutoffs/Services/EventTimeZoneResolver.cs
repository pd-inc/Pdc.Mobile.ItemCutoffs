using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services;

///////////////////////////////////////////////
/// EVENT TIME ZONE RESOLVER
///////////////////////////////////////////////

/// <summary>
/// Resolves an event's local time from the IANA zone stored on its settings row.
///
/// The legacy event time_zones row is not used here for the same reason it is
/// not used anywhere else in this feature: it holds a fixed standard-time offset
/// string, and the conversion the ecommerce application pairs it with corrects
/// for daylight saving by asking whether the SERVER is in it - which on a UTC
/// Lambda is never true.
/// </summary>
public class EventTimeZoneResolver : IEventTimeZoneResolver
{
    public bool IsKnownTimeZone(string timeZoneId) => TryFind(timeZoneId) != null;

    public DateTime ToLocal(DateTime utc, string timeZoneId)
    {
        var zone = TryFind(timeZoneId)
            ?? throw new TimeZoneNotFoundException($"Unknown time zone '{timeZoneId}'.");

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    }

    private static TimeZoneInfo? TryFind(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
