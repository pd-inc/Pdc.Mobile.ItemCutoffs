namespace Pdc.Mobile.ItemCutoffs.Models;

///////////////////////////////////////////////
/// ITEM CUTOFF EMAIL MODELS
///////////////////////////////////////////////

/// <summary>One resolved recipient.</summary>
public class ItemCutoffRecipient
{
    public int PatronId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// The roles this person holds on the event. A patron holding two of the
    /// configured roles is one recipient carrying both, never two emails.
    /// </summary>
    public List<string> RoleNames { get; set; } = new();
}

/// <summary>A rendered email, ready to send.</summary>
public class ItemCutoffEmailMessage
{
    public string Subject { get; set; } = string.Empty;

    /// <summary>Plain text. These are operational emails read on a phone at an event.</summary>
    public string Body { get; set; } = string.Empty;

    public List<string> ToAddresses { get; set; } = new();
}

/// <summary>One closing time on the digest, with the items closing at it.</summary>
public class DigestCloseTime
{
    public DateTime CutoffLocal { get; set; }

    /// <summary>Null when the deadline has no announcement configured.</summary>
    public DateTime? AnnouncementLocal { get; set; }

    public List<string> ItemNames { get; set; } = new();
}
