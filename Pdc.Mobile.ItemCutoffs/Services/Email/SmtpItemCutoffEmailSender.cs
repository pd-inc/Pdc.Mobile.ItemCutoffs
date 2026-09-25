using Microsoft.Extensions.Logging;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Pdc.Mobile.ItemCutoffs.Services.Email;

///////////////////////////////////////////////
/// SMTP ITEM CUTOFF EMAIL SENDER
///////////////////////////////////////////////

/// <summary>
/// SMTP delivery on port 587 with STARTTLS, matching the waiver
/// SyncResultEmailService and the API's SmtpSupportEmailService.
///
/// Recipients go in BCC. These emails name what an event is closing and when,
/// which is operational information about that event, and the registration team
/// does not need every colleague's address disclosed on every send.
/// </summary>
public class SmtpItemCutoffEmailSender : IItemCutoffEmailSender
{
    private const int SmtpPort = 587;

    private readonly ILogger logger;
    private readonly string smtpHost;
    private readonly string smtpUsername;
    private readonly string smtpPassword;
    private readonly string fromAddress;

    public SmtpItemCutoffEmailSender(
        ILogger logger,
        string smtpHost,
        string smtpUsername,
        string smtpPassword,
        string fromAddress)
    {
        this.logger = logger;
        this.smtpHost = smtpHost;
        this.smtpUsername = smtpUsername;
        this.smtpPassword = smtpPassword;
        this.fromAddress = fromAddress;
    }

    /// <summary>
    /// Sends the message.
    /// </summary>
    public async Task Send(ItemCutoffEmailMessage message)
    {
        if (message.ToAddresses.Count == 0)
        {
            // Not an error worth throwing over: the caller has already logged why
            // there is nobody to tell, and a deadline must not fail because its
            // event has no staff with an address on file.
            logger.LogWarning("{FeatureKey} No recipients for '{Subject}'; nothing sent",
                LogProperties.EmailNoRecipients, message.Subject);
            return;
        }

        using var smtpClient = new SmtpClient(smtpHost)
        {
            Credentials = new NetworkCredential(smtpUsername, smtpPassword),
            EnableSsl = true,
            Port = SmtpPort
        };

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = message.Subject,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        // The From address is also the To, so the message is well formed with
        // every real recipient in BCC.
        mailMessage.To.Add(new MailAddress(fromAddress));

        foreach (var address in message.ToAddresses)
        {
            mailMessage.Bcc.Add(new MailAddress(address));
        }

        await smtpClient.SendMailAsync(mailMessage);

        logger.LogInformation("{FeatureKey} Sent '{Subject}' to {RecipientCount} recipient(s)",
            LogProperties.EmailSent, message.Subject, message.ToAddresses.Count);
    }
}
