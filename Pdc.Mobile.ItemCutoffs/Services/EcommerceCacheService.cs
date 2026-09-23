using Microsoft.Extensions.Logging;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services;

///////////////////////////////////////////////
/// ECOMMERCE CACHE SERVICE
///////////////////////////////////////////////

/// <summary>
/// Clears the WDR ecommerce website cache.
///
/// Every web server behind the ALB holds its OWN in-process cache and there is no
/// way to address one directly, so the only available approach is to fire the
/// request repeatedly and rely on the load balancer spreading them. Ten requests at
/// 250 ms is what Pdc.Ecommerce.ScheduledTasks has always used, and this is a copy
/// of it rather than a new idea.
///
/// This does NOT reach the point of sale. A running WinClient serves items from its
/// own cache until its operator clears it, which is why the cutoff-completed email
/// exists.
/// </summary>
public class EcommerceCacheService : IEcommerceCacheService
{
    private const string CacheKeyHeader = "clear-cache-key";

    private readonly ILogger logger;
    private readonly HttpClient httpClient;
    private readonly ItemCutoffOptions options;
    private readonly string cacheUri;
    private readonly string cacheKey;

    public EcommerceCacheService(
        ILogger logger,
        HttpClient httpClient,
        ItemCutoffOptions options,
        string cacheUri,
        string cacheKey)
    {
        this.logger = logger;
        this.httpClient = httpClient;
        this.options = options;
        this.cacheUri = cacheUri;
        this.cacheKey = cacheKey;
    }

    /// <summary>
    /// Fires the invalidation requests and logs one summary.
    /// </summary>
    public async Task<bool> InvalidateCacheAsync()
    {
        if (string.IsNullOrWhiteSpace(cacheUri))
        {
            logger.LogWarning("{FeatureKey} No ecommerce cache URI configured; the store will clear on its own schedule",
                LogProperties.CacheClearFailed);
            return false;
        }

        var succeeded = 0;
        var failed = 0;

        for (var attempt = 0; attempt < options.CacheInvalidationAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, cacheUri);
                request.Headers.Add(CacheKeyHeader, cacheKey);
                request.Content = new StringContent(string.Empty);
                request.Content.Headers.ContentLength = 0;

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception)
            {
                // Counted, not thrown: the items are already closed in the database,
                // and a cache that did not clear must not fail the cutoff.
                failed++;
            }

            if (attempt < options.CacheInvalidationAttempts - 1)
            {
                await Task.Delay(options.CacheInvalidationDelayMs);
            }
        }

        logger.LogInformation("{FeatureKey} Ecommerce cache invalidation finished: {SucceededCount} succeeded, {FailedCount} failed of {AttemptCount}",
            LogProperties.CacheCleared, succeeded, failed, options.CacheInvalidationAttempts);

        return succeeded > 0;
    }
}
