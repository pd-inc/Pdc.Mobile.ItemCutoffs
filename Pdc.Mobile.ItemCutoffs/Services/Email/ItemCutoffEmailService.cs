using Microsoft.Extensions.Logging;
using Pdc.EventPro.Domain.Entities;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Services.Email;

///////////////////////////////////////////////
/// ITEM CUTOFF EMAIL SERVICE
///////////////////////////////////////////////

/// <summary>
/// Resolves recipients, renders, and sends the two sales deadline emails.
///
/// EVERY PATH SWALLOWS ITS FAILURES. An email reports work that has already
/// happened: the items are closed, the announcement is posted. Letting a mail
/// problem mark a completed cutoff as failed would make the executor retry it,
/// and a retried cutoff re-stamps audit rows for work already done. So failures
/// are logged loudly and the tick carries on.
/// </summary>
public class ItemCutoffEmailService : IItemCutoffEmailService
{
    private readonly ILogger logger;
    private readonly IItemCutoffRecipientResolver recipientResolver;
    private readonly IItemCutoffEmailSender sender;

    public ItemCutoffEmailService(
        ILogger logger,
        IItemCutoffRecipientResolver recipientResolver,
        IItemCutoffEmailSender sender)
    {
        this.logger = logger;
        this.recipientResolver = recipientResolver;
        this.sender = sender;
    }

    /// <summary>
    /// The "stop selling and clear the point of sale cache" email.
    /// </summary>
    public async Task SendCutoffCompleted(EventItemCutoffGroup deadline, int itemsClosed, bool dryRun)
    {
        try
        {
            var message = CutoffCompletedEmailBuilder.Build(deadline, itemsClosed);
            message.ToAddresses = ResolveAddresses(deadline.EventUuid);

            if (dryRun)
            {
                logger.LogInformation("{FeatureKey} Would email {RecipientCount} recipient(s): {Subject}",
                    LogProperties.DryRun, message.ToAddresses.Count, message.Subject);
                return;
            }

            await sender.Send(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{FeatureKey} Could not email the completed notice for sales deadline {GroupId}; the items ARE closed online but the point of sale has not been told",
                LogProperties.EmailFailed, deadline.Id);
        }
    }

    /// <summary>
    /// The day's close-times digest.
    /// </summary>
    public async Task<bool> SendDigest(
        EventItemCutoffSettings settings,
        DateTime localDate,
        DateTime eventToday,
        IReadOnlyList<EventItemCutoffGroup> deadlines,
        bool dryRun)
    {
        try
        {
            var closeTimes = DigestEmailBuilder.GroupCloseTimes(deadlines);

            var message = DigestEmailBuilder.Build(
                settings.EventName,
                localDate,
                eventToday,
                closeTimes);

            message.ToAddresses = ResolveAddresses(settings.EventUuid);

            if (message.ToAddresses.Count == 0)
            {
                return false;
            }

            if (dryRun)
            {
                logger.LogInformation("{FeatureKey} Would email the {LocalDate:yyyy-MM-dd} digest ({CloseTimeCount} close time(s)) to {RecipientCount} recipient(s)",
                    LogProperties.DryRun, localDate, closeTimes.Count, message.ToAddresses.Count);
                return true;
            }

            await sender.Send(message);

            logger.LogInformation("{FeatureKey} Digest for EventId {EventId} on {LocalDate:yyyy-MM-dd} sent to {RecipientCount} recipient(s), {CloseTimeCount} close time(s)",
                LogProperties.DigestSent, settings.EventId, localDate, message.ToAddresses.Count, closeTimes.Count);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{FeatureKey} Could not send the digest for EventId {EventId} on {LocalDate:yyyy-MM-dd}",
                LogProperties.DigestFailed, settings.EventId, localDate);
            return false;
        }
    }

    /// <summary>
    /// The event's recipient addresses, logging when there are none.
    /// </summary>
    private List<string> ResolveAddresses(string eventUuid)
    {
        var recipients = recipientResolver.Resolve(eventUuid);

        if (recipients.Count == 0)
        {
            logger.LogWarning("{FeatureKey} EventUuid {EventUuid} has nobody holding a recipient role with an email address",
                LogProperties.EmailNoRecipients, eventUuid);
        }

        return recipients.Select(recipient => recipient.Email).ToList();
    }
}
