using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The batch framing: one JSON object per line, no outer array.
/// </summary>
public sealed class BetterStackNdjsonBatchFormatterTests
{
	[Fact]
	public void RejectsANullWriter()
	{
		var formatter = new BetterStackNdjsonBatchFormatter();

		Assert.Throws<ArgumentNullException>(() => formatter.Format(["{}"], null!));
		Assert.Throws<ArgumentNullException>(() => formatter.Format([], new BetterStackTextFormatter(), null!));
	}

	/// <summary>A formatter that writes nothing for an event frames no line for it.</summary>
	[Fact]
	public void SkipsAnEventTheFormatterWroteNothingFor()
	{
		var output = new StringWriter();
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow, LogEventLevel.Information, null,
			new MessageTemplateParser().Parse("Hello"), []);

		new BetterStackNdjsonBatchFormatter().Format([logEvent], new SilentFormatter(), output);

		Assert.Equal(string.Empty, output.ToString());
	}

	private sealed class SilentFormatter : Serilog.Formatting.ITextFormatter
	{
		public void Format(LogEvent logEvent, TextWriter output)
		{
			// Writes nothing.
		}
	}

	[Fact]
	public void PassesBufferedEventsThroughOnePerLine()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format(["""{"dt":"a"}""", """{"dt":"b"}"""], output);

		Assert.Equal(
			["""{"dt":"a"}""", """{"dt":"b"}"""],
			output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()),
			StringComparer.Ordinal);
	}

	/// <summary>
	/// A blank row would be a body line Better Stack cannot parse, for no
	/// event at all.
	/// </summary>
	[Fact]
	public void SkipsBlankRows()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format(["""{"dt":"a"}""", string.Empty, "   ", null!], output);

		Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public void WritesNothingForANullBatch()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format((IEnumerable<string>)null!, output);

		Assert.Equal(string.Empty, output.ToString());
	}

	/// <summary>
	/// The unbuffered overload is not what the durable sink calls, but it
	/// has to frame events to the same one-per-line rule — the configured
	/// formatter may or may not terminate the event itself.
	/// </summary>
	[Fact]
	public void NormalisesLineEndingsInTheUnbufferedOverload()
	{
		var output = new StringWriter();
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow, LogEventLevel.Information, null,
			new MessageTemplateParser().Parse("Hello"), []);

		new BetterStackNdjsonBatchFormatter().Format([logEvent, logEvent], new BetterStackTextFormatter(), output);

		var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.All(lines, line => JsonDocument.Parse(line.Trim()));
	}

	[Fact]
	public void WritesNothingWhenTheUnbufferedOverloadHasNothingToWorkWith()
	{
		var output = new StringWriter();
		var formatter = new BetterStackNdjsonBatchFormatter();

		formatter.Format(null!, new BetterStackTextFormatter(), output);
		formatter.Format([], null!, output);

		Assert.Equal(string.Empty, output.ToString());
	}
}
