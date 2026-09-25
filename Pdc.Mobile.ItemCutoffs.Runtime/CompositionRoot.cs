using Amazon;
using Amazon.SQS;
using Autofac;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pdc.EventPro.Repositories;
using Pdc.EventPro.Repositories.MySql;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Repositories;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;
using Pdc.Mobile.ItemCutoffs.Services;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using Pdc.Mobile.ItemCutoffs.Services.Email;
using Pdc.Mobile.ItemCutoffs.Services.Email.Abstract;
using System.Text;
using System.Text.Json;

namespace Pdc.Mobile.ItemCutoffs.Runtime;

///////////////////////////////////////////////
/// COMPOSITION ROOT
///////////////////////////////////////////////

/// <summary>
/// Wires the sales deadline executor: configuration, Firebase, SQS, the EventPro
/// repositories and every service one tick needs.
/// </summary>
public class CompositionRoot
{
    ///////////////////////////
    // CONFIG KEYS
    ///////////////////////////

    private const string ConfigConnectionString = "connectionString";
    private const string ConfigFirebaseDatabaseUrl = "firebaseDatabaseUrl";
    private const string ConfigFirebaseServiceAccount = "firebaseServiceAccount";
    private const string ConfigNotificationsQueueUrl = "notifications:sqsQueueUrl";
    private const string ConfigNotificationsRegion = "notifications:sqsRegion";
    private const string ConfigEcommerceCacheUri = "ecommerce:cacheUri";
    private const string ConfigEcommerceCacheKey = "ecommerce:cacheKey";
    private const string ConfigMaxAttempts = "cutoff:maxAttempts";
    private const string ConfigSmtpHost = "smtp:host";
    private const string ConfigSmtpUsername = "smtp:username";
    private const string ConfigSmtpPassword = "smtp:password";
    private const string ConfigEmailFromAddress = "email:fromAddress";

    private const string DefaultSqsRegion = "us-west-2";

    ///////////////////////////
    // ENTRY POINT
    ///////////////////////////

    public static IContainer Configure(IConfiguration configuration, ILogger logger)
    {
        var builder = new ContainerBuilder();

        ///////////////////////////
        // STATIC CONFIG
        ///////////////////////////

        var connectionString = configuration[ConfigConnectionString]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigConnectionString}");

        var firebaseDatabaseUrl = configuration[ConfigFirebaseDatabaseUrl]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigFirebaseDatabaseUrl}");

        var notificationsQueueUrl = configuration[ConfigNotificationsQueueUrl]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigNotificationsQueueUrl}");

        var firebaseServiceAccountJson = SerializeConfigSection(
            configuration.GetSection(ConfigFirebaseServiceAccount));

        // The cache endpoint is optional on purpose. Without it the items still
        // close in the database and the store picks the change up on its own
        // schedule, which is a delay rather than a failure - so a missing value must
        // not stop deadlines running.
        var ecommerceCacheUri = configuration[ConfigEcommerceCacheUri] ?? string.Empty;
        var ecommerceCacheKey = configuration[ConfigEcommerceCacheKey] ?? string.Empty;

        var sqsRegion = configuration[ConfigNotificationsRegion] ?? DefaultSqsRegion;

        // Email configuration. Required: both emails are how the registration desk
        // learns to stop selling, so a missing value is a misconfiguration rather
        // than a feature that quietly does nothing.
        var smtpHost = configuration[ConfigSmtpHost]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigSmtpHost}");
        var smtpUsername = configuration[ConfigSmtpUsername]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigSmtpUsername}");
        var smtpPassword = configuration[ConfigSmtpPassword]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigSmtpPassword}");
        var emailFromAddress = configuration[ConfigEmailFromAddress]
            ?? throw new InvalidOperationException($"Missing configuration: {ConfigEmailFromAddress}");

        var options = new ItemCutoffOptions
        {
            MaxAttempts = ResolveMaxAttempts(configuration)
        };

        ///////////////////////////
        // FIREBASE APP (SINGLETON)
        ///////////////////////////

        if (FirebaseApp.DefaultInstance == null)
        {
            var serviceAccount = ServiceAccountCredential.FromServiceAccountData(
                new MemoryStream(Encoding.UTF8.GetBytes(firebaseServiceAccountJson)));

            FirebaseApp.Create(new AppOptions
            {
                Credential = serviceAccount.ToGoogleCredential()
            });
        }

        ///////////////////////////
        // INFRASTRUCTURE
        ///////////////////////////

        builder.RegisterInstance(logger);
        builder.RegisterInstance(new HttpClient()).SingleInstance();
        builder.RegisterInstance(options).SingleInstance();
        builder.RegisterType<UtcClock>().As<IUtcClock>().SingleInstance();
        builder.RegisterType<EventTimeZoneResolver>().As<IEventTimeZoneResolver>().SingleInstance();

        builder.RegisterInstance<IAmazonSQS>(
            new AmazonSQSClient(RegionEndpoint.GetBySystemName(sqsRegion)))
            .SingleInstance();

        ///////////////////////////
        // EVENTPRO REPOSITORIES
        ///////////////////////////

        // No cache anywhere here. A tick reads the item rows it is about to close,
        // and a stale read would mean closing the wrong thing or recording the wrong
        // prior state in the audit trail.
        builder.Register(c => new ItemRepository(connectionString))
            .As<IItemRepository>()
            .SingleInstance();

        builder.Register(c => new UpdateEventItemRepository(connectionString))
            .As<IUpdateEventItemRepository>()
            .SingleInstance();

        builder.Register(c => new EventItemCutoffGroupRepository(connectionString))
            .As<IEventItemCutoffGroupRepository>()
            .SingleInstance();

        builder.Register(c => new FindEventUsersByRoleRepository(connectionString))
            .As<IFindEventUsersByRoleRepository>()
            .SingleInstance();

        ///////////////////////////
        // FIREBASE + SQS REPOSITORIES
        ///////////////////////////

        builder.RegisterType<FirebaseAdminAccessTokenProvider>()
            .As<IFirebaseAccessTokenProvider>()
            .SingleInstance();

        builder.Register(c => new FirebaseNewsFeedPostRepository(
                logger,
                c.Resolve<HttpClient>(),
                c.Resolve<IFirebaseAccessTokenProvider>(),
                firebaseDatabaseUrl))
            .As<IFirebaseNewsFeedPostRepository>()
            .SingleInstance();

        builder.Register(c => new NewsFeedPushEnqueuer(
                logger,
                c.Resolve<IAmazonSQS>(),
                notificationsQueueUrl))
            .As<INewsFeedPushEnqueuer>()
            .SingleInstance();

        ///////////////////////////
        // DOMAIN SERVICES
        ///////////////////////////

        builder.Register(c => new EcommerceCacheService(
                logger,
                c.Resolve<HttpClient>(),
                c.Resolve<ItemCutoffOptions>(),
                ecommerceCacheUri,
                ecommerceCacheKey))
            .As<IEcommerceCacheService>()
            .SingleInstance();

        builder.Register(c => new AnnouncementPublisher(
                logger,
                c.Resolve<IFirebaseNewsFeedPostRepository>(),
                c.Resolve<INewsFeedPushEnqueuer>()))
            .As<IAnnouncementPublisher>()
            .SingleInstance();

        builder.Register(c => new ItemCutoffApplier(
                logger,
                c.Resolve<IItemRepository>(),
                c.Resolve<IUpdateEventItemRepository>(),
                c.Resolve<IEventItemCutoffGroupRepository>()))
            .As<IItemCutoffApplier>()
            .SingleInstance();

        ///////////////////////////
        // EMAIL
        ///////////////////////////

        builder.Register(c => new ItemCutoffRecipientResolver(
                logger,
                c.Resolve<IFindEventUsersByRoleRepository>()))
            .As<IItemCutoffRecipientResolver>()
            .SingleInstance();

        builder.Register(c => new SmtpItemCutoffEmailSender(
                logger,
                smtpHost,
                smtpUsername,
                smtpPassword,
                emailFromAddress))
            .As<IItemCutoffEmailSender>()
            .SingleInstance();

        builder.Register(c => new ItemCutoffEmailService(
                logger,
                c.Resolve<IItemCutoffRecipientResolver>(),
                c.Resolve<IItemCutoffEmailSender>()))
            .As<IItemCutoffEmailService>()
            .SingleInstance();

        ///////////////////////////
        // ORCHESTRATOR
        ///////////////////////////

        builder.Register(c => new ItemCutoffExecutorService(
                logger,
                c.Resolve<IEventItemCutoffGroupRepository>(),
                c.Resolve<IAnnouncementPublisher>(),
                c.Resolve<IItemCutoffApplier>(),
                c.Resolve<IEcommerceCacheService>(),
                c.Resolve<IItemCutoffEmailService>(),
                c.Resolve<IEventTimeZoneResolver>(),
                c.Resolve<IUtcClock>(),
                c.Resolve<ItemCutoffOptions>()))
            .As<IItemCutoffExecutorService>()
            .SingleInstance();

        return builder.Build();
    }

    ///////////////////////////
    // CONFIG HELPERS
    ///////////////////////////

    /// <summary>
    /// The retry cap. Absent means the default; present must be a positive integer,
    /// because a zero or negative value would stop every deadline running at all.
    /// </summary>
    private static int ResolveMaxAttempts(IConfiguration configuration)
    {
        var raw = configuration[ConfigMaxAttempts];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ItemCutoffOptions().MaxAttempts;
        }

        if (!int.TryParse(raw, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"Configuration '{ConfigMaxAttempts}' must be a positive integer, was '{raw}'");
        }

        return value;
    }

    /// <summary>
    /// Re-serializes a configuration section to JSON. The Firebase service account
    /// arrives as nested configuration keys rather than a JSON string, so it has to
    /// be reassembled before the SDK will read it.
    /// </summary>
    private static string SerializeConfigSection(IConfigurationSection section)
    {
        var values = section.GetChildren().ToDictionary(child => child.Key, child => child.Value);

        return JsonSerializer.Serialize(values);
    }
}
