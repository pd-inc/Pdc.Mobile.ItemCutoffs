namespace Pdc.Mobile.ItemCutoffs.Models;

/// <summary>
/// Trigger message types this function actions. Anything else is logged and
/// skipped, so a payload meant for another consumer cannot make it do work.
/// </summary>
public static class MessageTypes
{
    public const string ItemCutoffTick = "item.cutoff.tick";
}
