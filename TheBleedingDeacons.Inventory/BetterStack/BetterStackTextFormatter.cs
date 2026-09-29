// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Globalization;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;

namespace TheBleedingDeacons.Inventory.BetterStack;

/// <summary>
/// Serialises a single <see cref="LogEvent"/> into the JSON shape Better
/// Stack's ingest API understands, one event per line (NDJSON).
/// </summary>
/// <remarks>
/// <para>Better Stack treats three field names as reserved and everything else
/// as free-form metadata:</para>
/// <list type="bullet">
/// <item><c>dt</c> — the event's timestamp. <b>Without it Better Stack stamps
///       the event with the time the HTTP request arrived.</b> That is why this
///       formatter exists: the durable sink buffers on disk, so a device that
///       has been out of signal for hours ships a batch whose events would
///       otherwise all collapse onto the moment of delivery.</item>
/// <item><c>level</c> — severity, for filtering and Live Tail's colours.</item>
/// <item><c>message</c> — the human-readable text Live Tail shows. Without it
///       every row is the raw JSON.</item>
/// </list>
///
/// <para>Serilog's stock <see cref="JsonFormatter"/> writes
/// <c>Timestamp</c>/<c>Level</c>/<c>RenderedMessage</c> instead, none of which
/// Better Stack recognises. The layout below mirrors Better Stack's own
/// <c>BetterStack.Logs.Serilog</c> client.</para>
///
/// <para>Enriched properties are nested under <c>properties</c> rather than
/// hoisted, again as the official client does, so an enricher can never shadow
/// <c>dt</c>, <c>level</c> or <c>message</c>. Query them in Better Stack as
/// <c>properties.DeviceLabel</c>, <c>properties.ExceptionType</c> and so on.</para>
///
/// <para>This is what is written into the buffer file, so it also decides the
/// timestamp Better Stack records for a batch that shipped late.</para>
/// </remarks>
public sealed class BetterStackTextFormatter : ITextFormatter
{
	private readonly JsonValueFormatter _valueFormatter = new();

	/// <inheritdoc/>
	public void Format(LogEvent logEvent, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(logEvent);
		ArgumentNullException.ThrowIfNull(output);

		// Formatted into a buffer first. The durable sink writes this straight
		// into its rolling buffer file and reads it back a line at a time, so a
		// half-written event would corrupt that row. Building the whole line
		// before touching the output makes the write all-or-nothing.
		try
		{
			var buffer = new StringWriter(CultureInfo.InvariantCulture);
			FormatContent(logEvent, buffer);
			output.WriteLine(buffer.ToString());
		}
#pragma warning disable CA1031 // Dropping one event beats taking down the shipper loop.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			SelfLog.WriteLine(
				"[BetterStackTextFormatter] Event at {0} with template {1} could not be serialised and was dropped: {2}",
				logEvent.Timestamp.ToString("o", CultureInfo.InvariantCulture),
				logEvent.MessageTemplate.Text,
				ex);
		}
	}

	/// <summary>
	/// Serilog's level names are not all ones Better Stack recognises. Its own
	/// client folds Information to INFO; Verbose and Warning map onto the
	/// conventional TRACE and WARN. Debug, Error and Fatal pass through.
	/// </summary>
	internal static string MapLevel(LogEventLevel level) => level switch
	{
		LogEventLevel.Verbose => "TRACE",
		LogEventLevel.Information => "INFO",
		LogEventLevel.Warning => "WARN",
		_ => level.ToString().ToUpperInvariant(),
	};

	private void FormatContent(LogEvent logEvent, TextWriter output)
	{
		// RFC 3339: "o" on a UTC DateTime gives 2026-08-10T19:04:31.1234567Z.
		output.Write("{\"dt\":\"");
		output.Write(logEvent.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));

		output.Write("\",\"level\":\"");
		output.Write(MapLevel(logEvent.Level));

		output.Write("\",\"message\":");
		JsonValueFormatter.WriteQuotedJsonString(
			logEvent.MessageTemplate.Render(logEvent.Properties, CultureInfo.InvariantCulture),
			output);

		// The raw template alongside the rendered text, so events of one kind
		// still group in Better Stack even though each rendering differs.
		output.Write(",\"messageTemplate\":");
		JsonValueFormatter.WriteQuotedJsonString(logEvent.MessageTemplate.Text, output);

		if (logEvent.Exception != null)
		{
			output.Write(",\"exception\":");
			JsonValueFormatter.WriteQuotedJsonString(logEvent.Exception.ToString(), output);
		}

		if (logEvent.Properties.Count != 0)
		{
			output.Write(",\"properties\":{");

			var delimiter = string.Empty;
			foreach (var property in logEvent.Properties)
			{
				output.Write(delimiter);
				delimiter = ",";

				JsonValueFormatter.WriteQuotedJsonString(property.Key, output);
				output.Write(':');
				_valueFormatter.Format(property.Value, output);
			}

			output.Write('}');
		}

		output.Write('}');
	}
}
