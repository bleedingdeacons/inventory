using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>
/// Puts <c>Log.Logger</c> and <see cref="LogShipper.Current"/> back as they
/// were. Every test that touches the global logger holds one.
/// </summary>
public sealed class GlobalLogScope : IDisposable
{
	private readonly Serilog.ILogger _logger = Serilog.Log.Logger;
	private readonly ILogShipper? _current = LogShipper.Current;

	public void Dispose()
	{
		Serilog.Log.Logger = _logger;
		LogShipper.Current = _current;
	}
}
