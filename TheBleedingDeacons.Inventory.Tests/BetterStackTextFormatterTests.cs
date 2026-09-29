using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The Better Stack wire format. Ported from Link's suite, which had the only
/// tests either app's copy of this code ever had.
///
/// <para>All of this exists because Serilog's stock JSON formatter emits
/// field names Better Stack does not recognise — most importantly it does
/// not emit <c>dt</c>, without which every event in a batch is stamped
/// with the moment the HTTP request arrived. On a handset that has
/// been out of signal for hours, that collapses a whole shift's
/// chronology onto one instant, which is the opposite of what the
/// diagnostics are for.</para>
/// </summary>
public sealed class BetterStackTextFormatterTests
{
	private static LogEvent Event(
		LogEventLevel level = LogEventLevel.Information,
		string template = "Message {MessageId} delivered",
		Exception? exception = null,
		params LogEventProperty[] properties)
	{
		var parsed = new MessageTemplateParser().Parse(template);
		return new LogEvent(
			new DateTimeOffset(2026, 8, 15, 21, 4, 31, TimeSpan.FromHours(1)),
			level,
			exception,
			parsed,
			properties);
	}

	private static JsonElement Format(LogEvent logEvent)
	{
		var writer = new StringWriter(CultureInfo.InvariantCulture);
		new BetterStackTextFormatter().Format(logEvent, writer);

		var line = writer.ToString().TrimEnd('\r', '\n');
		return JsonDocument.Parse(line).RootElement.Clone();
	}

	/// <summary>
	/// The timestamp must be the event's own, in UTC, in a shape Better
	/// Stack parses. This is the single most load-bearing assertion here.
	/// </summary>
	[Fact]
	public void StampsTheEventsOwnTimeInUtc()
	{
		var json = Format(Event());

		Assert.Equal("2026-08-15T20:04:31.0000000Z", json.GetProperty("dt").GetString());
	}

	/// <summary>
	/// Serilog's level names are not all names Better Stack recognises.
	/// </summary>
	[Theory]
	[InlineData(LogEventLevel.Verbose, "TRACE")]
	[InlineData(LogEventLevel.Debug, "DEBUG")]
	[InlineData(LogEventLevel.Information, "INFO")]
	[InlineData(LogEventLevel.Warning, "WARN")]
	[InlineData(LogEventLevel.Error, "ERROR")]
	[InlineData(LogEventLevel.Fatal, "FATAL")]
	public void MapsSerilogsLevelNamesOntoTheOnesBetterStackUses(LogEventLevel level, string expected) =>
		Assert.Equal(expected, Format(Event(level)).GetProperty("level").GetString());

	[Fact]
	public void WritesTheRenderedMessageAndTheTemplateItCameFrom()
	{
		var json = Format(Event(
			template: "Message {MessageId} delivered",
			properties: new LogEventProperty("MessageId", new ScalarValue(4242))));

		Assert.Equal("Message 4242 delivered", json.GetProperty("message").GetString());
		Assert.Equal("Message {MessageId} delivered", json.GetProperty("messageTemplate").GetString());
	}

	[Fact]
	public void NestsEnrichedPropertiesSoTheyCannotShadowTheReservedFields()
	{
		var json = Format(Event(
			template: "Message {MessageId} delivered",
			properties:
			[
				new LogEventProperty("MessageId", new ScalarValue(4242)),
				new LogEventProperty("level", new ScalarValue("not-a-level")),
				new LogEventProperty("dt", new ScalarValue("not-a-time")),
			]));

		Assert.Equal("INFO", json.GetProperty("level").GetString());
		Assert.Equal("2026-08-15T20:04:31.0000000Z", json.GetProperty("dt").GetString());
		Assert.Equal("not-a-level", json.GetProperty("properties").GetProperty("level").GetString());
	}

	[Fact]
	public void OmitsThePropertiesObjectWhenThereAreNone() =>
		Assert.False(Format(Event(template: "Nothing to say")).TryGetProperty("properties", out _));

	[Fact]
	public void IncludesTheExceptionWhenThereIsOne()
	{
		var json = Format(Event(exception: new InvalidOperationException("audio focus refused")));

		Assert.Contains("audio focus refused", json.GetProperty("exception").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void OmitsTheExceptionWhenThereIsNot() =>
		Assert.False(Format(Event()).TryGetProperty("exception", out _));

	/// <summary>Every event is one line — the buffer file is read back a line at a time.</summary>
	[Fact]
	public void WritesExactlyOneLinePerEvent()
	{
		var writer = new StringWriter(CultureInfo.InvariantCulture);
		var formatter = new BetterStackTextFormatter();

		formatter.Format(Event(), writer);
		formatter.Format(Event(), writer);

		var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.All(lines, line => JsonDocument.Parse(line.Trim()));
	}

	/// <summary>
	/// A message containing quotes and newlines has to survive as valid
	/// JSON, or the row it lands on is unreadable.
	/// </summary>
	[Fact]
	public void EscapesAwkwardText()
	{
		var json = Format(Event(
			template: "Said {What}",
			properties: new LogEventProperty("What", new ScalarValue("a \"quote\"\nand a newline"))));

		// Asserting on the parsed value rather than the escaped line: what has
		// to hold is that the line is valid JSON — JsonDocument.Parse in
		// Format would have thrown otherwise — and that the awkward
		// characters came back out of it intact. How Serilog chose to render
		// the scalar on the way in is its business.
		var message = json.GetProperty("message").GetString()!;
		Assert.Contains("quote", message, StringComparison.Ordinal);
		Assert.Contains('"', message);
		Assert.Contains('\n', message);
		Assert.Contains("and a newline", message, StringComparison.Ordinal);
	}

	[Fact]
	public void RejectsNulls()
	{
		var formatter = new BetterStackTextFormatter();

		Assert.Throws<ArgumentNullException>(() => formatter.Format(null!, new StringWriter()));
		Assert.Throws<ArgumentNullException>(() => formatter.Format(Event(), null!));
	}

	/// <summary>
	/// Dropping one event beats taking down the shipper loop, so a writer
	/// that fails must not propagate.
	/// </summary>
	[Fact]
	public void DropsAnEventItCannotWriteRatherThanThrowing()
	{
		new BetterStackTextFormatter().Format(Event(), new ThrowingWriter());
	}

	private sealed class ThrowingWriter : StringWriter
	{
		public override void WriteLine(string? value) => throw new IOException("disk full");
	}
}
