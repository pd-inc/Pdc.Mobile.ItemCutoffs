using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services;

/// <summary>The real clock.</summary>
public class UtcClock : IUtcClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
