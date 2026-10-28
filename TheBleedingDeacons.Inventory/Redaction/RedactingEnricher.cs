// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Redaction;

/// <summary>
/// Rewrites the text of every property of a log event through an app's
/// redaction, before any sink sees it. A message is rendered from its
/// properties, so the message text is covered by the same pass.
/// </summary>
/// <remarks>
/// <para><b>Added last.</b> Inventory puts this after its own enrichers, so
/// what <see cref="Enrichment.ExceptionEnricher"/> lifts out of an
/// exception — its message, stack and inner chain — and whatever was pushed
/// onto the log context are redacted too. An enricher an app adds through
/// its own configuration runs before both, and so cannot reach them.</para>
///
/// <para>Strings are redacted wherever they sit: at the top level or inside
/// a destructured object, list or dictionary. Any other value that would
/// print as text is replaced by its redacted text when redaction changes it.
/// Numbers, dates and the like are left as they are.</para>
///
/// <para>The exception itself travels beside the properties, and sinks print
/// it whole; <see cref="RedactingSink"/> deals with that.</para>
/// </remarks>
public sealed class RedactingEnricher : ILogEventEnricher
{
	private readonly Func<string, string> _redact;

	/// <summary>Initializes a new instance of the <see cref="RedactingEnricher"/> class.</summary>
	/// <param name="redact">
	/// The app's redaction. Returns the same instance when there is nothing
	/// to change, which lets untouched values through without a copy.
	/// </param>
	public RedactingEnricher(Func<string, string> redact)
	{
		ArgumentNullException.ThrowIfNull(redact);
		_redact = redact;
	}

	/// <inheritdoc />
	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		// Copied first: the loop replaces properties as it goes.
		foreach (var (name, value) in logEvent.Properties.ToList())
		{
			var redacted = Redact(value);
			if (!ReferenceEquals(redacted, value))
			{
				logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
			}
		}
	}

	// Returns the same instance when nothing changed, so untouched values are
	// not copied.
	private LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
	{
		ScalarValue scalar => Redact(scalar),
		SequenceValue sequence => Redact(sequence),
		StructureValue structure => Redact(structure),
		DictionaryValue dictionary => Redact(dictionary),
		_ => value,
	};

	private ScalarValue Redact(ScalarValue scalar)
	{
		switch (scalar.Value)
		{
			case string text:
				var redacted = _redact(text);
				return ReferenceEquals(redacted, text) ? scalar : new ScalarValue(redacted);

			case null or bool or char or IConvertible and not string or DateTimeOffset or TimeSpan or Guid:
				return scalar;

			default:
				var printed = Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty;
				var hidden = _redact(printed);
				return ReferenceEquals(hidden, printed) ? scalar : new ScalarValue(hidden);
		}
	}

	private SequenceValue Redact(SequenceValue sequence)
	{
		var elements = sequence.Elements.Select(Redact).ToList();

		return elements.SequenceEqual(sequence.Elements, ReferenceComparer.Instance)
			? sequence
			: new SequenceValue(elements);
	}

	private StructureValue Redact(StructureValue structure)
	{
		var changed = false;
		var properties = new List<LogEventProperty>(structure.Properties.Count);

		foreach (var property in structure.Properties)
		{
			var redacted = Redact(property.Value);
			changed |= !ReferenceEquals(redacted, property.Value);
			properties.Add(ReferenceEquals(redacted, property.Value) ? property : new LogEventProperty(property.Name, redacted));
		}

		return changed ? new StructureValue(properties, structure.TypeTag) : structure;
	}

	private DictionaryValue Redact(DictionaryValue dictionary)
	{
		var changed = false;
		var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);

		foreach (var (key, value) in dictionary.Elements)
		{
			var redactedKey = Redact(key);
			var redactedValue = Redact(value);
			changed |= !ReferenceEquals(redactedKey, key) || !ReferenceEquals(redactedValue, value);
			elements.Add(new(redactedKey, redactedValue));
		}

		return changed ? new DictionaryValue(elements) : dictionary;
	}

	private sealed class ReferenceComparer : IEqualityComparer<LogEventPropertyValue>
	{
		public static readonly ReferenceComparer Instance = new();

		public bool Equals(LogEventPropertyValue? x, LogEventPropertyValue? y) => ReferenceEquals(x, y);

		public int GetHashCode(LogEventPropertyValue obj) => RuntimeHelpers.GetHashCode(obj);
	}
}
