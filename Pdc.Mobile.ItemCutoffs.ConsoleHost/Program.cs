using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Runtime;
using Pdc.Mobile.ItemCutoffs.Services.Abstract;
using Serilog;
using System.Text.Json;

///////////////////////////////////////////////
/// SALES DEADLINES CONSOLE HOST
///////////////////////////////////////////////
///
/// Runs one tick locally against the configured environment.
///
///   --dry-run          evaluate and log, write nothing
///   --group <id>       narrow the tick to one deadline (no gate is bypassed)
///
/// Reads appsettings.json plus .NET User Secrets, so no KMS ciphertext is needed
/// for local work.

var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
int? groupId = null;

var groupFlagIndex = Array.FindIndex(args, arg => string.Equals(arg, "--group", StringComparison.OrdinalIgnoreCase));

if (groupFlagIndex >= 0)
{
    if (groupFlagIndex + 1 >= args.Length || !int.TryParse(args[groupFlagIndex + 1], out var parsedGroupId))
    {
        Console.Error.WriteLine("--group requires a numeric sales deadline id.");
        return 1;
    }

    groupId = parsedGroupId;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var seriLog = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var logger = new LoggerFactory().AddSerilog(seriLog).CreateLogger("ConsoleHost");

var container = CompositionRoot.Configure(configuration, logger);

var trigger = new ItemCutoffTrigger
{
    MessageType = MessageTypes.ItemCutoffTick,
    GroupId = groupId,
    DryRun = dryRun
};

if (dryRun)
{
    Console.WriteLine("DRY RUN: evaluating only, nothing will be written.");
}

var summary = await container.Resolve<IItemCutoffExecutorService>().Execute(trigger);

Console.WriteLine();
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

seriLog.Dispose();

return 0;

/// <summary>Named so AddUserSecrets has a type to hang the assembly off.</summary>
public partial class Program { }
