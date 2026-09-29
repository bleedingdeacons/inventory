using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// Loggers that outlive a rebuild. Before CurrentLogger, every
/// <c>ILogger&lt;T&gt;</c> in Register and Link wrote into the pipeline of
/// the moment it was made, and every rebuild disposed that pipeline.
/// </summary>
public sealed class CurrentLoggerTests : IDisposable
{
	private readonly GlobalLogScope _scope = new();

	public void Dispose() => _scope.Dispose();

	private static (Logger Logger, CollectingSink Sink) Pipeline(LogEventLevel minimum = LogEventLevel.Verbose)
	{
		var sink = new CollectingSink();
		return (new LoggerConfiguration().MinimumLevel.Is(minimum).WriteTo.Sink(sink).CreateLogger(), sink);
	}

	/// <summary>
	/// What the apps had: the provider's default binds each logger once.
	/// Kept as a test so the reason for CurrentLogger stays demonstrable.
	/// </summary>
	[Fact]
	public void WithoutItAnILoggerKeepsTheOldPipeline()
	{
		var (first, firstSink) = Pipeline();
		Log.Logger = first;
		using var factory = LoggerFactory.Create(b => b.AddSerilog());
		var logger = factory.CreateLogger("Unity");

		var (second, secondSink) = Pipeline();
		Log.Logger = second;
		logger.LogInformation("Where does this go?");

		Assert.Single(firstSink.Events);
		Assert.Empty(secondSink.Events);
	}

	[Fact]
	public void WithItAnILoggerFollowsThePipeline()
	{
		var (first, firstSink) = Pipeline();
		Log.Logger = first;
		using var factory = LoggerFactory.Create(b => b.AddSerilog(CurrentLogger.Instance));
		var logger = factory.CreateLogger("Unity");

		var (second, secondSink) = Pipeline();
		Log.Logger = second;
		first.Dispose();
		logger.LogInformation("Sync {Count} members", 12);

		Assert.Empty(firstSink.Events);
		var logEvent = Assert.Single(secondSink.Events);
		Assert.Equal("Sync 12 members", logEvent.RenderMessage(CultureInfo.InvariantCulture));
		Assert.Equal("\"Unity\"", logEvent.Properties["SourceContext"].ToString(null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void CarriesScopesThroughTheProvider()
	{
		var (logger, sink) = Pipeline();
		Log.Logger = logger;
		using var factory = LoggerFactory.Create(b => b.AddSerilog(CurrentLogger.Instance));
		var mel = factory.CreateLogger("Freedom");

		using (mel.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal) { ["Application"] = "register" }))
		{
			mel.LogWarning("Sync failed");
		}

		Assert.Equal("\"register\"", Assert.Single(sink.Events).Properties["Application"].ToString(null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void AppliesItsContextThenTheLivePipelines()
	{
		var (logger, sink) = Pipeline();
		Log.Logger = logger;

		CurrentLogger.Instance
			.ForContext("Screen", "Settings")
			.ForContext(new[] { new FixedEnricher("Tablet", "TB330FU") })
			.ForContext<CurrentLoggerTests>()
			.Information("Saved");

		var logEvent = Assert.Single(sink.Events);
		Assert.Equal("\"Settings\"", logEvent.Properties["Screen"].ToString(null, CultureInfo.InvariantCulture));
		Assert.Equal("\"TB330FU\"", logEvent.Properties["Tablet"].ToString(null, CultureInfo.InvariantCulture));
		Assert.Equal($"\"{typeof(CurrentLoggerTests).FullName}\"", logEvent.Properties["SourceContext"].ToString(null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void LeavesTheLoggerItCameFromUnchanged()
	{
		var (logger, sink) = Pipeline();
		Log.Logger = logger;

		_ = CurrentLogger.Instance.ForContext("Screen", "Settings");
		CurrentLogger.Instance.Information("Plain");

		Assert.False(Assert.Single(sink.Events).Properties.ContainsKey("Screen"));
	}

	[Fact]
	public void AsksTheLivePipelineWhatIsEnabled()
	{
		Log.Logger = Pipeline(LogEventLevel.Warning).Logger;
		Assert.False(CurrentLogger.Instance.IsEnabled(LogEventLevel.Information));

		Log.Logger = Pipeline(LogEventLevel.Verbose).Logger;
		Assert.True(CurrentLogger.Instance.IsEnabled(LogEventLevel.Information));
	}

	[Fact]
	public void BindsAsTheLivePipelineDoes()
	{
		Log.Logger = Pipeline().Logger;

		Assert.True(CurrentLogger.Instance.BindMessageTemplate("Hello {Name}", ["Ann"], out var template, out var properties));
		Assert.Equal("Hello {Name}", template.Text);
		Assert.Single(properties);
		Assert.True(CurrentLogger.Instance.BindProperty("Name", "Ann", false, out var property));
		Assert.Equal("Name", property.Name);
	}

	/// <summary>
	/// A silent pipeline cannot bind anything; the context still has to reach
	/// the event without an exception.
	/// </summary>
	[Fact]
	public void EnrichesEvenWhenTheLivePipelineCannotBind()
	{
		Log.Logger = new LoggerConfiguration().CreateLogger();
		var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, new Serilog.Parsing.MessageTemplateParser().Parse("x"), []);

		CurrentLogger.Instance.ForContext("Screen", "Settings").Write(logEvent);

		Assert.True(logEvent.Properties.ContainsKey("Screen"));
	}

	[Fact]
	public void RejectsNulls()
	{
		Assert.Throws<ArgumentNullException>(() => CurrentLogger.Instance.ForContext((ILogEventEnricher)null!));
		Assert.Throws<ArgumentNullException>(() => CurrentLogger.Instance.ForContext((IEnumerable<ILogEventEnricher>)null!));
		Assert.Throws<ArgumentNullException>(() => CurrentLogger.Instance.Write(null!));
	}

	private sealed class FixedEnricher(string name, string value) : ILogEventEnricher
	{
		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
			logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
	}
}
