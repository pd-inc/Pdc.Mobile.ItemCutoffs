namespace Pdc.Mobile.ItemCutoffs.Models;

///////////////////////////////////////////////
/// ITEM CUTOFF TRIGGER
///////////////////////////////////////////////

/// <summary>
/// The Scheduler input, and the shape of a manual `aws lambda invoke`.
///
/// { "messageType": "item.cutoff.tick", "groupId": 12, "dryRun": true }
/// </summary>
public class ItemCutoffTrigger
{
    public string? MessageType { get; set; }

    /// <summary>
    /// Narrows the tick to one deadline. It does NOT bypass any gate: the group
    /// still has to be enabled and actually due.
    /// </summary>
    public int? GroupId { get; set; }

    /// <summary>
    /// Evaluate and log, write nothing. Used for the first production run.
    /// </summary>
    public bool DryRun { get; set; }
}
