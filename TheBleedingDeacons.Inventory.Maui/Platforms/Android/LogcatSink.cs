// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Maui;

/// <summary>
/// Writes events to Android's log, so <c>adb logcat</c> shows them as they happen.
/// </summary>
/// <remarks>
/// <para><b>Why it exists: there was no live log at all.</b> On a handset the
/// pipeline was a rolling file plus the Debug sink, and the Debug sink reaches
/// a listening debugger and nothing else. With the app launched by adb rather
/// than an IDE there is no listener, so the only record was the file, readable
/// only afterwards with <c>run-as … cat files/logs/…</c>. That turned every
/// question into a round trip; three diagnoses in Link on 2026-09-10 went that
/// way, each of which would have been one line on a terminal already open.</para>
///
/// <para><b>Not the console sink.</b> Serilog's console sink calls
/// <c>Console.set_ForegroundColor</c>, which throws on Android; every event
/// then lands in SelfLog with a stack trace. This writes through
/// <c>Android.Util.Log</c>, the platform's own API, which colours nothing.</para>
///
/// <para><b>A tag of the app's own</b>, because the alternative is what Hand
/// ended up documenting: managed output under <c>app_process64</c>, the
/// runtime's tag, shared with every other managed message on the device.</para>
/// </remarks>
public sealed class LogcatSink : ILogEventSink
{
	private readonly string _tag;
	private readonly IFormatProvider? _formatProvider;

	/// <summary>
	/// Initializes a new instance of the <see cref="LogcatSink"/> class.
	/// </summary>
	/// <param name="tag">The logcat tag, and so what to filter on: <c>adb logcat -s Link:V</c>.</param>
	/// <param name="formatProvider">Renders the message; null for the current culture.</param>
	public LogcatSink(string tag, IFormatProvider? formatProvider = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tag);
		_tag = tag;
		_formatProvider = formatProvider;
	}

	/// <summary>
	/// Write one event to logcat at the matching priority.
	/// </summary>
	/// <param name="logEvent">The event.</param>
	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		var message = logEvent.RenderMessage(_formatProvider);

		// The exception on its own line: logcat wraps on width, and a stack
		// trace appended to a sentence is the shape truncated first.
		if (logEvent.Exception is not null)
		{
			message = message + System.Environment.NewLine + logEvent.Exception;
		}

		switch (logEvent.Level)
		{
			case LogEventLevel.Verbose:
				global::Android.Util.Log.Verbose(_tag, message);
				break;
			case LogEventLevel.Debug:
				global::Android.Util.Log.Debug(_tag, message);
				break;
			case LogEventLevel.Warning:
				global::Android.Util.Log.Warn(_tag, message);
				break;
			case LogEventLevel.Error:
			case LogEventLevel.Fatal:
				// Android has no Fatal that is not an assertion, and Wtf can be
				// configured to kill the process. An app that dies harder
				// because it logged is not a debugging aid.
				global::Android.Util.Log.Error(_tag, message);
				break;
			default:
				global::Android.Util.Log.Info(_tag, message);
				break;
		}
	}
}
