using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pdc.Cryptography.Aws.Configuration.Extensions;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Runtime;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using Serilog;
using Serilog.Context;
using Serilog.Sinks.Elasticsearch;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Pdc.Mobile.ItemCutoffs.Aws.Lambda;

///////////////////////////////////////////////
/// FUNCTION
///////////////////////////////////////////////

/// <summary>
/// Lambda entry point for sales deadlines. Invoked every minute by an EventBridge
/// Scheduler schedule (or by hand) with an item.cutoff.tick payload, it runs one
/// tick: publish any announcement that is due, close any deadline that is due, and
/// clear the ecommerce cache once if anything changed.
///
/// The tick summary is returned so a manual invocation shows its counts.
/// </summary>
public class Function
{
    private static Microsoft.Extensions.Logging.ILogger _logger = null!;
    private static Serilog.Core.Logger _seriLog = null!;
    private static IConfigurationRoot _configuration = null!;
    private static IContainer _container = null!;

    public async Task<ItemCutoffRunSummary?> FunctionHandler(ItemCutoffTrigger trigger, ILambdaContext context)
    {
        /////////////////////////////////////////////
        // BUILD CONFIGURATION
        /////////////////////////////////////////////

        if (_configuration == null)
        {
            var configCipherText = GetEncryptedConfig();
            var encryptionKeyId = Environment.GetEnvironmentVariable("APP_CONFIG_ENCRYPTION_KEY", EnvironmentVariableTarget.Process);
            var encryptionKeyRegion = Environment.GetEnvironmentVariable("APP_CONFIG_ENCRYPTION_KEY_REGION", EnvironmentVariableTarget.Process);

            if (configCipherText == null)
                throw new ApplicationException("APP_CONFIG_ENCRYPTED environment variable is missing. Exiting.");

            if (encryptionKeyId == null)
                throw new ApplicationException("APP_CONFIG_ENCRYPTION_KEY environment variable is missing. Exiting.");

            if (encryptionKeyRegion == null)
                throw new ApplicationException("APP_CONFIG_ENCRYPTION_KEY_REGION environment variable is missing. Exiting.");

            _configuration = new ConfigurationBuilder()
                .AddProtectedJsonFile(
                    encryptionKeyRegion,
                    encryptionKeyId,
                    configCipherText
                ).Build();
        }

        /////////////////////////////////////////////
        // BUILD LOGGER
        /////////////////////////////////////////////

        if (_logger == null)
        {
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

            _seriLog = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("pdc.app", "mobile-item-cutoffs")
                .Enrich.WithProperty("pdc.env", environment)
                .ReadFrom.Configuration(_configuration)
                .WriteTo.Elasticsearch(BuildElasticsearchSinkOptions(_configuration))
                .CreateLogger();

            var loggerFactory = new LoggerFactory()
                .AddSerilog(_seriLog);

            _logger = loggerFactory.CreateLogger("Logger");
        }

        /////////////////////////////////////////////
        // BUILD IOC CONTAINER
        /////////////////////////////////////////////

        if (_container == null)
        {
            _container = CompositionRoot.Configure(_configuration, _logger);
        }

        /////////////////////////////////////////////
        // RUN THE TICK
        /////////////////////////////////////////////

        ItemCutoffRunSummary? summary = null;

        try
        {
            summary = await RunAsync(trigger, context.AwsRequestId);
        }
        finally
        {
            /////////////////////////////////////////////
            // FLUSH LOGS TO ELASTICSEARCH
            /////////////////////////////////////////////

            _seriLog?.Dispose();
            _seriLog = null!;
            _logger = null!;
            _container = null!;
        }

        return summary;
    }

    /// <summary>
    /// Validates the trigger and runs the tick under a correlated log scope. A
    /// payload that is not a tick (the console's default test event, for example) is
    /// logged and skipped rather than treated as a run.
    /// </summary>
    private static async Task<ItemCutoffRunSummary?> RunAsync(ItemCutoffTrigger? trigger, string runId)
    {
        if (trigger?.MessageType != MessageTypes.ItemCutoffTick)
        {
            _logger.LogWarning(
                "{FeatureKey} Ignoring trigger with message type {MessageType}; expected {ExpectedMessageType}",
                LogProperties.TriggerIgnored, trigger?.MessageType, MessageTypes.ItemCutoffTick);
            return null;
        }

        using (LogContext.PushProperty("RunId", runId))
        using (LogContext.PushProperty("MessageType", trigger.MessageType))
        {
            var executor = _container.Resolve<IItemCutoffExecutorService>();
            return await executor.Execute(trigger);
        }
    }

    /// <summary>
    /// Builds Elasticsearch sink options using explicit BasicAuthentication to
    /// correctly handle special characters in credentials that are corrupted when
    /// wired through JSON config.
    /// </summary>
    private static ElasticsearchSinkOptions BuildElasticsearchSinkOptions(IConfigurationRoot configuration)
    {
        var nodeUri = configuration["elasticsearchNodeUri"]!;
        var indexFormat = configuration["elasticsearchIndexFormat"]!;
        var username = configuration["elasticsearchUsername"]!;
        var password = configuration["elasticsearchPassword"]!;

        return new ElasticsearchSinkOptions(new Uri(nodeUri))
        {
            IndexFormat = indexFormat,
            CustomFormatter = new FluentBitWrapperFormatter(),
            BatchAction = ElasticOpType.Create,
            ModifyConnectionSettings = conn => conn.BasicAuthentication(username, password)
        };
    }

    /// <summary>
    /// Loads the encrypted configuration from the environment variable or, when
    /// running the Lambda project locally, from Configuration/Target/appsettings.json
    /// (the KMS ciphertext produced by the Pdc.Cryptography.Aws.CLI tool).
    /// </summary>
    private string GetEncryptedConfig()
    {
        var encryptedConfig = Environment.GetEnvironmentVariable("APP_CONFIG_ENCRYPTED", EnvironmentVariableTarget.Process);

        if (encryptedConfig == null)
        {
            var localConfigFullPath = Path.Combine(Environment.CurrentDirectory, "Configuration", "Target", "appsettings.json");
            return File.ReadAllText(localConfigFullPath);
        }

        return encryptedConfig;
    }
}
