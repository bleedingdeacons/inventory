using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>A clock that moves only when told.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
	private DateTimeOffset _now = start;

	public ManualTimeProvider()
		: this(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))
	{
	}

	public override DateTimeOffset GetUtcNow() => _now;

	public void Advance(TimeSpan by) => _now += by;
}
