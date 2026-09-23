namespace Pdc.Mobile.ItemCutoffs.Models;

///////////////////////////////////////////////
/// ITEM CUTOFF RUN SUMMARY
///////////////////////////////////////////////

/// <summary>
/// What one tick did. Returned from the handler so a manual invoke shows its
/// counts without going to CloudWatch.
/// </summary>
public class ItemCutoffRunSummary
{
    public int GroupsEvaluated { get; set; }

    public int AnnouncementsSent { get; set; }

    public int AnnouncementsSkipped { get; set; }

    public int AnnouncementsFailed { get; set; }

    public int CutoffsApplied { get; set; }

    public int CutoffsFailed { get; set; }

    /// <summary>Product rows closed, not internal codes.</summary>
    public int ItemsClosed { get; set; }

    public bool CacheCleared { get; set; }

    public int CompletedEmailsSent { get; set; }

    public int DigestsSent { get; set; }

    public bool DryRun { get; set; }
}
