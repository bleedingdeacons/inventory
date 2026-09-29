// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// How a <see cref="LogShipper"/> buffers and ships. Everything but
/// <see cref="BufferDirectory"/> has the default Register and Link ran with.
/// </summary>
public sealed class InventoryOptions
{
	/// <summary>
	/// Gets the directory the durable buffer lives in. Both the holding buffer
	/// and the shipping one use it — that is how held events come to be shipped
	/// — so it must be the same directory for the life of the install.
	/// </summary>
	public required string BufferDirectory { get; init; }

	/// <summary>
	/// Gets how often the shipper sends a batch while shipping. Default 5 seconds.
	/// </summary>
	public TimeSpan ShippingPeriod { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Gets the size at which a shipping buffer file rolls over. Default 8 MB.
	/// </summary>
	public long BufferFileSizeLimitBytes { get; init; } = 8L * 1024 * 1024;

	/// <summary>
	/// Gets how many shipping buffer files are kept before the oldest is
	/// dropped. Default 16, so 128 MB before anything is lost offline.
	/// </summary>
	public int RetainedBufferFileCountLimit { get; init; } = 16;

	/// <summary>
	/// Gets how often the holding sink wakes to be told "not yet". Default 5
	/// minutes: a device that is never configured must not wake every few
	/// seconds to send nothing.
	/// </summary>
	public TimeSpan HoldingPeriod { get; init; } = TimeSpan.FromMinutes(5);

	/// <summary>
	/// Gets the size at which a holding buffer file rolls over. Default 1 MB.
	/// </summary>
	public long HoldingBufferFileSizeLimitBytes { get; init; } = 1L * 1024 * 1024;

	/// <summary>
	/// Gets how many holding buffer files are kept. Default 2: a device that is
	/// never configured must not fill its storage with logs.
	/// </summary>
	public int HoldingRetainedBufferFileCountLimit { get; init; } = 2;

	/// <summary>
	/// Gets the most events in one POST. Default 500.
	/// </summary>
	public int LogEventsInBatchLimit { get; init; } = 500;

	/// <summary>
	/// Gets the largest POST body. Default 5 MB.
	/// </summary>
	public long BatchSizeLimitBytes { get; init; } = 5L * 1024 * 1024;

	/// <summary>
	/// Gets a value indicating whether an error ships at once rather than on the
	/// next tick. Default true. See <see cref="ILogShipper.Flush"/>.
	/// </summary>
	public bool FlushOnError { get; init; } = true;

	/// <summary>
	/// Gets the shortest gap between forced flushes. Default 5 seconds. A fault
	/// rarely arrives alone — one failure often logs an error from several
	/// layers on the way up — and a rebuild per event would cost more than the
	/// promptness is worth.
	/// </summary>
	public TimeSpan FlushDebounce { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Gets where Serilog's own diagnostics go — a sink that failed to start, an
	/// event that could not be written. Default: <see cref="System.Diagnostics.Debug"/>
	/// output, prefixed <c>[Serilog]</c>. Null leaves SelfLog alone.
	/// </summary>
	public Action<string>? SelfLog { get; init; } =
		message => System.Diagnostics.Debug.WriteLine($"[Serilog] {message}");
}
