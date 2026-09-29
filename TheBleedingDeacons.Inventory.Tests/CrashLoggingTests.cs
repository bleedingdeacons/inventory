using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Inventory.Sinks;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The crash path: it logs, it flushes, and it never throws.
/// </summary>
public sealed class CrashLoggingTests : IDisposable
{
	private readonly GlobalLogScope _scope = new();
	private readonly CollectingSink _sink = new();
	private readonly RecordingShipper _shipper = new();

	public CrashLoggingTests()
	{
		Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
		LogShipper.Current = _shipper;
	}

	public void Dispose() => _scope.Dispose();

	[Fact]
	public void LogsAnUnhandledExceptionAsFatalAndCloses()
	{
		CrashLogging.OnUnhandled(new InvalidOperationException("boom"), isTerminating: true);

		var logEvent = Assert.Single(_sink.Events);
		Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
		Assert.IsType<InvalidOperationException>(logEvent.Exception);
		Assert.Equal("True", logEvent.Properties["IsTerminating"].ToString(null, System.Globalization.CultureInfo.InvariantCulture));

		Log.Information("After the crash");
		Assert.Single(_sink.Events);
	}

	[Fact]
	public void LogsAnUnhandledObjectThatIsNotAnException()
	{
		CrashLogging.OnUnhandled("a string thrown from native code", isTerminating: false);

		var logEvent = Assert.Single(_sink.Events);
		Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
		Assert.Contains("a string thrown from native code", logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	/// <summary>
	/// The app survives an unobserved task exception, so logging must survive
	/// it too: a flush, not a close.
	/// </summary>
	[Fact]
	public void ShipsAnUnobservedTaskExceptionAndKeepsLogging()
	{
		CrashLogging.OnUnobserved(new AggregateException(new TimeoutException()));

		Assert.Equal(LogEventLevel.Error, Assert.Single(_sink.Events).Level);
		Assert.Equal(1, _shipper.Flushes);

		Log.Information("Still here");
		Assert.Equal(2, _sink.Events.Count);
	}

	[Fact]
	public void NeverThrowsFromTheCrashPath()
	{
		_shipper.FlushThrows = new InvalidOperationException("flush failed");
		Log.Logger = new LoggerConfiguration().WriteTo.Sink(new ThrowingSink()).CreateLogger();

		CrashLogging.OnUnobserved(new InvalidOperationException());
		CrashLogging.OnUnhandled(new InvalidOperationException(), isTerminating: true);
		CrashLogging.ReportFatal(new InvalidOperationException(), "Unhandled Android exception");
		CrashLogging.TryFlush(TimeSpan.FromMilliseconds(10));
	}

	[Fact]
	public void ReportsAPlatformCrashAsFatal()
	{
		CrashLogging.ReportFatal(new InvalidOperationException("java side"), "Unhandled Android exception");

		var logEvent = Assert.Single(_sink.Events);
		Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
		Assert.Equal("Unhandled Android exception", logEvent.MessageTemplate.Text);
	}

	[Fact]
	public void RegistersOnceHoweverOftenAsked()
	{
		CrashLogging.Register();
		CrashLogging.Register();
	}

	private sealed class ThrowingSink : Serilog.Core.ILogEventSink
	{
		public void Emit(LogEvent logEvent) => throw new InvalidOperationException("sink broken");
	}
}
