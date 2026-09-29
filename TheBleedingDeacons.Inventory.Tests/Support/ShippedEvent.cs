using System.Text.Json;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>One NDJSON line, read back.</summary>
public sealed record ShippedEvent(DateTimeOffset Dt, string Level, string Message, JsonElement Raw)
{
	public static ShippedEvent Parse(string line)
	{
		var raw = JsonDocument.Parse(line).RootElement.Clone();
		return new ShippedEvent(
			DateTimeOffset.Parse(raw.GetProperty("dt").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
			raw.GetProperty("level").GetString()!,
			raw.GetProperty("message").GetString()!,
			raw);
	}
}
