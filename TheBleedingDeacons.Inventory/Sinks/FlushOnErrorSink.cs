// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Sinks;

/// <summary>
/// Ships the buffer as soon as something goes wrong, rather than on the
/// shipper's next tick.
/// </summary>
/// <remarks>
/// <para>The durable sink is disk-first, so an error is never lost. What this
/// changes is how soon it <i>arrives</i>. A phone spends its life in a pocket;
/// the interesting failures happen while nobody is looking, and the OS is quite
/// likely to kill the process before anyone does. Without this, a message that
/// arrived and would not open waits for the next time the app is opened —
/// which, if the symptom is that messages stopped appearing, may be a while.
/// Written for Link, where exactly that happened.</para>
///
/// <para>Does nothing below <see cref="LogEventLevel.Error"/>. Routine events
/// can wait for the timer.</para>
/// </remarks>
internal sealed class FlushOnErrorSink : ILogEventSink
{
	private readonly ILogShipper _shipper;

	/// <summary>
	/// Initializes a new instance of the <see cref="FlushOnErrorSink"/> class.
	/// </summary>
	/// <param name="shipper">The shipper to ask for a flush.</param>
	public FlushOnErrorSink(ILogShipper shipper) =>
		_shipper = shipper ?? throw new ArgumentNullException(nameof(shipper));

	/// <summary>
	/// Ask for a flush, off this thread, when the event is an error or worse.
	/// </summary>
	/// <param name="logEvent">The event being written.</param>
	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		if (logEvent.Level < LogEventLevel.Error)
		{
			return;
		}

		// Never inline. A flush disposes and rebuilds the pipeline, and doing
		// that on the thread midway through emitting into it is a deadlock
		// waiting to happen. The shipper debounces, so a burst queues one
		// rebuild.
		_ = Task.Run(() =>
		{
			try
			{
				_shipper.Flush();
			}
#pragma warning disable CA1031 // This path exists to report problems, not become one.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Nothing to do.
			}
		});
	}
}
