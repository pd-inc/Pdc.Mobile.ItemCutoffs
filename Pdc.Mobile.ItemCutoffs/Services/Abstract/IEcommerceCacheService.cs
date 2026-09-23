namespace Pdc.Mobile.ItemCutoffs.Services.Abstract;

/// <summary>
/// Invalidates the WDR ecommerce website cache so a closed item disappears from the
/// store without waiting for a natural expiry.
/// </summary>
public interface IEcommerceCacheService
{
    /// <summary>
    /// Sends the invalidation requests. Never throws: a cache that did not clear is
    /// a delay, and must not turn a completed cutoff into a failed one.
    /// </summary>
    /// <returns>True when at least one request succeeded.</returns>
    Task<bool> InvalidateCacheAsync();
}
