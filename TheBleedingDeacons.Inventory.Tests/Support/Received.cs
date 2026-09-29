namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>One POST as it arrived.</summary>
public sealed record Received(Uri Uri, string? Token, string? MediaType, IReadOnlyList<ShippedEvent> Events, bool Accepted);
