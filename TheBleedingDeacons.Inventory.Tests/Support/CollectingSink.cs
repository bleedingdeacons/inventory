using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>Keeps every event it is given.</summary>
public sealed class CollectingSink : ILogEventSink
{
	private readonly ConcurrentQueue<LogEvent> _events = new();

	public IReadOnlyList<LogEvent> Events => _events.ToList();

	public IReadOnlyList<string> Messages => _events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)).ToList();

	public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
