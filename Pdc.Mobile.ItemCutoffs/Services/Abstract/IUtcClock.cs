namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// The current UTC instant, injected so a tick can be evaluated at a fixed time in
/// tests rather than against the wall clock.
/// </summary>
public interface IUtcClock
{
    DateTime UtcNow { get; }
}
