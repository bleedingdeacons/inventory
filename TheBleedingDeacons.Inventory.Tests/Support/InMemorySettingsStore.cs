using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>A settings store in memory.</summary>
public sealed class InMemorySettingsStore : ILoggingSettingsStore
{
	public BetterStackConfiguration? Stored { get; set; }

	public int Saves { get; private set; }

	public int Clears { get; private set; }

	public Task<BetterStackConfiguration?> LoadAsync() => Task.FromResult(Stored);

	public Task SaveAsync(BetterStackConfiguration configuration)
	{
		Saves++;
		Stored = configuration;
		return Task.CompletedTask;
	}

	public Task ClearAsync()
	{
		Clears++;
		Stored = null;
		return Task.CompletedTask;
	}
}
