using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>An <see cref="ILogShipper"/> that remembers what it was asked.</summary>
public sealed class RecordingShipper : ILogShipper
{
	private int _flushes;

	public ShippingState State { get; set; } = ShippingState.NotStarted;

	public List<BetterStackConfiguration?> Reconfigured { get; } = [];

	public int Flushes => Volatile.Read(ref _flushes);

	public Exception? FlushThrows { get; set; }

	public void Reconfigure(BetterStackConfiguration? configuration) => Reconfigured.Add(configuration);

	public void Flush()
	{
		Interlocked.Increment(ref _flushes);
		if (FlushThrows is not null)
		{
			throw FlushThrows;
		}
	}
}
