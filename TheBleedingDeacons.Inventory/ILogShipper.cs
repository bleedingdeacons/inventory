// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// Owns Serilog's global <c>Log.Logger</c> and rebuilds it when where-to-ship
/// changes.
/// </summary>
/// <remarks>
/// <para>A Serilog logger captures its sinks when it is built, and never picks
/// up a change afterwards. Change the Better Stack endpoint or token and the
/// pipeline already running keeps shipping to the old place under the old
/// token, forever, unless it is torn down and rebuilt. This does the rebuild:
/// it composes the app's fixed pipeline plus the right Better Stack sink from
/// scratch, swaps it in, and only then disposes the old one. That avoids the
/// two ways the naive version goes wrong — sinks stacking up on every change,
/// and the old durable sink's timer carrying on against the old endpoint.</para>
/// </remarks>
public interface ILogShipper
{
	/// <summary>
	/// Gets what the live pipeline is doing.
	/// </summary>
	ShippingState State { get; }

	/// <summary>
	/// Rebuild <c>Log.Logger</c> for a new answer to "where do logs go?". Safe
	/// from any thread; calls are serialised.
	/// </summary>
	/// <param name="configuration">
	/// <para><b>Null: not told yet.</b> Events are held in a small on-disk buffer
	/// and ship, with their own timestamps, once a valid configuration arrives.
	/// A device's first minutes — the sign-in that failed, say — are exactly the
	/// ones worth having.</para>
	/// <para><b>Valid: ship.</b> Disk first, then Better Stack.</para>
	/// <para><b>Not valid: told not to ship.</b> The Better Stack sink goes, and
	/// anything held for it is deleted, because it is never going anywhere.</para>
	/// </param>
	/// <remarks>
	/// If the new pipeline cannot be built, the old one stays in place and says
	/// so. A broken configuration never takes logging down.
	/// </remarks>
	void Reconfigure(BetterStackConfiguration? configuration);

	/// <summary>
	/// Ship whatever is in the buffer now, without stopping logging.
	/// </summary>
	/// <remarks>
	/// <para>Nothing is ever lost waiting — every event is on disk as soon as it
	/// is written, and a device that dies ships its backlog on the next launch.
	/// What can wait is <i>arrival</i>: the shipper runs on a timer, and if the
	/// process is killed in between, the event waits until the app is next
	/// opened. On a phone in someone's pocket that is the difference between
	/// seeing a fault tonight and whenever it happens to be opened again.</para>
	///
	/// <para>Not <c>Log.CloseAndFlush()</c>, which flushes too but leaves logging
	/// dead — right on a crash path, where the process is going anyway, and
	/// wrong everywhere else. This disposes the pipeline, which flushes it, and
	/// rebuilds it at once.</para>
	///
	/// <para>Debounced: a burst of errors is one flush. Does nothing while
	/// holding or not shipping, since there is nowhere to send anything.</para>
	/// </remarks>
	void Flush();
}
