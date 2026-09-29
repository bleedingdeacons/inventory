using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Inventory.Sinks;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The sink that asks for a flush.
/// </summary>
public sealed class FlushOnErrorSinkTests
{
	private static LogEvent Event(LogEventLevel level) =>
		new(DateTimeOffset.UtcNow, level, null, new MessageTemplateParser().Parse("x"), []);

	[Theory]
	[InlineData(LogEventLevel.Error)]
	[InlineData(LogEventLevel.Fatal)]
	public async Task AsksForAFlushOnAnError(LogEventLevel level)
	{
		var shipper = new RecordingShipper();

		new FlushOnErrorSink(shipper).Emit(Event(level));

		Assert.True(await Eventually.True(() => shipper.Flushes == 1));
	}

	[Theory]
	[InlineData(LogEventLevel.Verbose)]
	[InlineData(LogEventLevel.Debug)]
	[InlineData(LogEventLevel.Information)]
	[InlineData(LogEventLevel.Warning)]
	public async Task LeavesRoutineEventsToTheTimer(LogEventLevel level)
	{
		var shipper = new RecordingShipper();

		new FlushOnErrorSink(shipper).Emit(Event(level));
		await Task.Delay(100);

		Assert.Equal(0, shipper.Flushes);
	}

	[Fact]
	public async Task SwallowsAFlushThatFails()
	{
		var shipper = new RecordingShipper { FlushThrows = new InvalidOperationException() };

		new FlushOnErrorSink(shipper).Emit(Event(LogEventLevel.Error));

		Assert.True(await Eventually.True(() => shipper.Flushes == 1));
	}

	[Fact]
	public void RejectsNulls()
	{
		Assert.Throws<ArgumentNullException>(() => new FlushOnErrorSink(null!));
		Assert.Throws<ArgumentNullException>(() => new FlushOnErrorSink(new RecordingShipper()).Emit(null!));
	}
}
