// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Redaction;

/// <summary>
/// Wraps a sink so that an event's exception reaches it as redacted text.
/// </summary>
/// <remarks>
/// <para>The file, logcat and Better Stack sinks all print
/// <see cref="Exception.ToString"/> whole, and an enricher cannot change an
/// event's exception. So the sink is handed a copy of the event whose
/// exception is a <see cref="RedactedException"/>: the original's text, type
/// name and stack included, passed through the redaction. Events without an
/// exception pass straight through, untouched and uncopied.</para>
///
/// <para>Sinks see events after every enricher has run, so
/// <see cref="Enrichment.ExceptionEnricher"/> still worked from the real
/// exception, and <see cref="RedactingEnricher"/> has already redacted what
/// it lifted out.</para>
/// </remarks>
public sealed class RedactingSink : ILogEventSink, IDisposable, IAsyncDisposable
{
	private readonly ILogEventSink _inner;
	private readonly Func<string, string> _redact;

	/// <summary>Initializes a new instance of the <see cref="RedactingSink"/> class.</summary>
	/// <param name="inner">The sink to hand redacted events to.</param>
	/// <param name="redact">The app's redaction.</param>
	public RedactingSink(ILogEventSink inner, Func<string, string> redact)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(redact);
		_inner = inner;
		_redact = redact;
	}

	/// <inheritdoc />
	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		if (logEvent.Exception is null or RedactedException)
		{
			_inner.Emit(logEvent);
			return;
		}

		_inner.Emit(new LogEvent(
			logEvent.Timestamp,
			logEvent.Level,
			new RedactedException(logEvent.Exception, _redact),
			logEvent.MessageTemplate,
			logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value)),
			logEvent.TraceId ?? default,
			logEvent.SpanId ?? default));
	}

	/// <inheritdoc />
	public void Dispose() => (_inner as IDisposable)?.Dispose();

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (_inner is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		Dispose();
		return ValueTask.CompletedTask;
	}
}
