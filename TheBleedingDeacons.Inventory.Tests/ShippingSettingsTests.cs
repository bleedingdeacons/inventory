using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Inventory.Sinks;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// Settings the app is told rather than built with.
/// </summary>
public sealed class ShippingSettingsTests : IDisposable
{
	private static readonly BetterStackConfiguration Told = new() { Endpoint = "https://in.logs.example", SourceToken = "t" };

	private readonly GlobalLogScope _scope = new();
	private readonly InMemorySettingsStore _store = new();
	private readonly RecordingShipper _shipper = new();

	public void Dispose() => _scope.Dispose();

	private ShippingSettings Settings() => new(_store, _shipper);

	[Fact]
	public async Task HoldsAtStartWhenNothingWasStored()
	{
		await Settings().ApplyStoredAsync();

		Assert.Null(Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public async Task ShipsAtStartWithWhatWasStored()
	{
		_store.Stored = Told;

		await Settings().ApplyStoredAsync();

		Assert.Same(Told, Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public async Task StoresAndActsOnANewAnswer()
	{
		Assert.True(await Settings().ApplyAsync(Told));

		Assert.Same(Told, _store.Stored);
		Assert.Same(Told, Assert.Single(_shipper.Reconfigured));
	}

	/// <summary>Every launch asks; an unchanged answer must not rebuild the logger.</summary>
	[Fact]
	public async Task IgnoresTheSameAnswerTwice()
	{
		_store.Stored = Told;

		Assert.False(await Settings().ApplyAsync(new BetterStackConfiguration { Endpoint = Told.Endpoint, SourceToken = Told.SourceToken }));

		Assert.Empty(_shipper.Reconfigured);
		Assert.Equal(0, _store.Saves);
	}

	[Fact]
	public async Task StoresBeingToldNotToShip()
	{
		var none = new BetterStackConfiguration();

		Assert.True(await Settings().ApplyAsync(none));

		Assert.Same(none, _store.Stored);
		Assert.False(Assert.Single(_shipper.Reconfigured)!.IsValid());
	}

	[Fact]
	public async Task ForgetsAndHoldsAgainAtSignOut()
	{
		_store.Stored = Told;

		await Settings().ForgetAsync();

		Assert.Null(_store.Stored);
		Assert.Null(Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public async Task ForgettingNothingChangesNothing()
	{
		await Settings().ForgetAsync();

		Assert.Equal(0, _store.Clears);
		Assert.Empty(_shipper.Reconfigured);
	}

	[Fact]
	public async Task RejectsNulls()
	{
		Assert.Throws<ArgumentNullException>(() => new ShippingSettings(null!, _shipper));
		Assert.Throws<ArgumentNullException>(() => new ShippingSettings(_store, null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => Settings().ApplyAsync(null!));
	}
}
