using DNSMonitor;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Formatting.Compact;
using System.Text.Json;

// Config
var config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new Exception("Couldn't load config.json");
var issues = config.Validate();
if (issues.Any())
{
	Console.WriteLine("Config error:");
	Console.WriteLine(string.Join("\n", issues));
	return 1;
}

// App Insights
TelemetryClient? tc = null;
try
{
	if (!string.IsNullOrWhiteSpace(config.AppInsightsConnectionString))
	{
		tc = new(new TelemetryConfiguration() { ConnectionString = config.AppInsightsConnectionString });
	}
	else
	{
		Console.WriteLine("Not sending to application insights, connection string not set");
	}
}
catch (Exception e)
{
	Console.WriteLine($"Not sending to application insights, {e.Message}");
}

// Logging
var loggerConfig = new LoggerConfiguration()
	.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Operation}] {Message:lj}{NewLine}{Exception}");

if (!string.IsNullOrWhiteSpace(config.FileLogFolder) && Directory.Exists(config.FileLogFolder))
{
	Console.WriteLine($"Logging to folder {config.FileLogFolder}");
	loggerConfig = loggerConfig.WriteTo.File(
		new CompactJsonFormatter(), 
		Path.Combine(config.FileLogFolder, "latencylog-.json"),
		rollingInterval: RollingInterval.Day
	);
}
else
{
	Console.WriteLine("Only logging to console, FileLogFolder value empty or folder does not exist");
}

using var logger = loggerConfig.CreateLogger();

// Monitor
var latencyMonitor = new DNSLatencyMonitor(config, tc, logger);
await latencyMonitor.RunAsync(CancellationToken.None);
return 0;