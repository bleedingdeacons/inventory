// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Http;

namespace TheBleedingDeacons.Inventory.BetterStack;

/// <summary>
/// Frames a batch of events as newline-delimited JSON: one object per line, no
/// outer array, which is the <c>application/x-ndjson</c> body Better Stack's
/// ingest API accepts.
/// </summary>
/// <remarks>
/// The per-event JSON comes from <see cref="BetterStackTextFormatter"/>; this
/// only frames it. NDJSON rather than an array because it streams well from a
/// buffer file, and one malformed row cannot poison a whole batch.
/// </remarks>
public sealed class BetterStackNdjsonBatchFormatter : IBatchFormatter
{
#pragma warning disable S2325 // Implements IBatchFormatter; it cannot be static.

	/// <inheritdoc/>
	public void Format(IEnumerable<string> logEvents, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(output);

		// Replayed from the durable buffer, events arrive already rendered as
		// JSON, one per line. They pass straight through.
		if (logEvents == null)
		{
			return;
		}

		foreach (var logEvent in logEvents)
		{
			// A blank row would be a body line Better Stack cannot parse, for
			// no event at all.
			if (string.IsNullOrWhiteSpace(logEvent))
			{
				continue;
			}

			output.WriteLine(logEvent);
		}
	}

	/// <summary>
	/// Render and frame events that were never buffered.
	/// </summary>
	/// <param name="logEvents">The events.</param>
	/// <param name="formatter">Renders each event; <see cref="BetterStackTextFormatter"/> for Better Stack.</param>
	/// <param name="output">Where the NDJSON goes.</param>
	public void Format(IEnumerable<LogEvent> logEvents, ITextFormatter formatter, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(output);

		// Not what the durable sink calls — it persists formatted JSON first and
		// always uses the string overload — but the interface asks for it, and
		// it has to keep the same one-per-line rule.
		if (logEvents == null || formatter == null)
		{
			return;
		}

		foreach (var logEvent in logEvents)
		{
			// Via a buffer so the line ending can be normalised: the formatter
			// may or may not terminate the event, and NDJSON needs exactly one
			// newline per event.
			var buffer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
			formatter.Format(logEvent, buffer);

			var json = buffer.ToString().TrimEnd('\r', '\n');
			if (json.Length == 0)
			{
				continue;
			}

			output.WriteLine(json);
		}
	}

#pragma warning restore S2325
}
