namespace Pdc.Mobile.ItemCutoffs.Models;

///////////////////////////////////////////////
/// ITEM CUTOFF OPTIONS
///////////////////////////////////////////////

/// <summary>
/// Tunables for one tick.
/// </summary>
public class ItemCutoffOptions
{
    /// <summary>
    /// Attempts after which a half of a deadline stops being retried.
    ///
    /// Bounded so a permanently failing group does not consume every tick forever;
    /// re-enabling the group resets the counters, which is the operator's way of
    /// saying "try again".
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Ten unconditional cache invalidation requests, 250 ms apart, because each web
    /// server behind the ALB holds its own local cache and there is no way to address
    /// them individually. Copied from Pdc.Ecommerce.ScheduledTasks.
    /// </summary>
    public int CacheInvalidationAttempts { get; set; } = 10;

    public int CacheInvalidationDelayMs { get; set; } = 250;
}
