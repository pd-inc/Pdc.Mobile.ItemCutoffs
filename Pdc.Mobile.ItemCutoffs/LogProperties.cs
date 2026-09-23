namespace Pdc.Mobile.ItemCutoffs;

///////////////////////////////////////////////
/// LOG PROPERTIES
///////////////////////////////////////////////

/// <summary>
/// Structured-logging feature keys.
///
/// Each value is used as the VALUE of a {FeatureKey} property, never as a property
/// name: "{FeatureKey} Closed {ItemCount} items", LogProperties.CutoffApplied, count.
/// Used as a property name instead, Elasticsearch would reject the document.
/// </summary>
public static class LogProperties
{
    public const string TickStarted = "##_ITEMCUTOFF_TICK_STARTED";
    public const string TickCompleted = "##_ITEMCUTOFF_TICK_COMPLETED";
    public const string TriggerIgnored = "##_ITEMCUTOFF_TRIGGER_IGNORED";

    public const string PostSent = "##_ITEMCUTOFF_POST_SENT";
    public const string PostSkipped = "##_ITEMCUTOFF_POST_SKIPPED";
    public const string PostFailed = "##_ITEMCUTOFF_POST_FAILED";

    public const string CutoffApplied = "##_ITEMCUTOFF_APPLIED";
    public const string CutoffApplyFailed = "##_ITEMCUTOFF_APPLY_FAILED";

    public const string CacheCleared = "##_ITEMCUTOFF_CACHE_CLEARED";
    public const string CacheClearFailed = "##_ITEMCUTOFF_CACHE_CLEAR_FAILED";

    public const string EmailSent = "##_ITEMCUTOFF_EMAIL_SENT";
    public const string EmailFailed = "##_ITEMCUTOFF_EMAIL_FAILED";
    public const string EmailNoRecipients = "##_ITEMCUTOFF_EMAIL_NO_RECIPIENTS";

    public const string DigestSent = "##_ITEMCUTOFF_DIGEST_SENT";
    public const string DigestFailed = "##_ITEMCUTOFF_DIGEST_FAILED";

    public const string GroupFailed = "##_ITEMCUTOFF_GROUP_FAILED";
    public const string DryRun = "##_ITEMCUTOFF_DRY_RUN";
}
