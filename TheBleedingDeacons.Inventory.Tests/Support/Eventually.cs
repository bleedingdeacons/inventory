using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>Polling, for what happens on the shipper's own thread.</summary>
public static class Eventually
{
	public static async Task<bool> True(Func<bool> condition, TimeSpan? within = null)
	{
		var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(15));
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(50);
		}

		return condition();
	}
}
