using Pdc.Mobile.ItemCutoffs.Models;

namespace Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

/// <summary>
/// Delivers a rendered sales deadline email. The transport seam, so the
/// orchestration is testable without SMTP.
/// </summary>
public interface IItemCutoffEmailSender
{
    /// <summary>Sends the message. Throws on a transport failure.</summary>
    Task Send(ItemCutoffEmailMessage message);
}
