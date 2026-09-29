// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Reqnroll;
using Serilog;
using Serilog.Events;
using Shouldly;
using TheBleedingDeacons.Inventory.Specs.Support;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Specs.Steps;

/// <summary>
/// What came of it: what Better Stack received, what stayed on the device,
/// and what the shipper is doing.
/// </summary>
[Binding]
public sealed class OutcomeSteps(World world)
{
	/// <summary>
	/// Long enough for several ticks of the 100 ms timer the scenarios run
	/// with, so "nothing arrived" means nothing was going to.
	/// </summary>
	private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(700);

	// ── Better Stack ──────────────────────────────────────────────────
	[Then(@"^Better Stack receives ""(.+)""$")]
	public async Task Receives(string message)
	{
		(await Eventually.True(() => world.Server.Containing(message).Count > 0))
			.ShouldBeTrue($"\"{message}\" never reached Better Stack");
		world.Server.Containing(message).Count.ShouldBe(1, $"\"{message}\" arrived more than once");
		world.LastShipped = message;
	}

	[Then(@"^Better Stack receives nothing$")]
	public async Task ReceivesNothing()
	{
		await Task.Delay(Quiet);
		world.Server.Posts.ShouldBe(0);
	}

	[Then(@"^Better Stack does not receive ""(.+)""$")]
	public async Task DoesNotReceive(string message)
	{
		await Task.Delay(Quiet);
		world.Server.Containing(message).ShouldBeEmpty();
	}

	[Then(@"^""(.+)"" arrives stamped with the time it was logged$")]
	public void StampedWhenLogged(string message) =>
		world.Server.Containing(message)[0].Dt.UtcDateTime.ShouldBe(world.LoggedAt[message].UtcDateTime);

	[Then(@"^it arrives as INFO under the source token$")]
	public void ArrivesAsInfo()
	{
		var post = world.Server.PostCarrying(world.LastShipped!);
		post.Token.ShouldBe(World.BetterStack.SourceToken);
		post.MediaType.ShouldBe("application/x-ndjson");
		world.Server.Containing(world.LastShipped!)[0].Level.ShouldBe("INFO");
	}

	[Then(@"^it arrives under the token ""(.+)""$")]
	public void ArrivesUnderToken(string token) =>
		world.Server.PostCarrying(world.LastShipped!).Token.ShouldBe(token);

	// ── The shipper ───────────────────────────────────────────────────
	[Then(@"^the app is (holding|shipping|logging locally only)$")]
	public void AppIs(string state) =>
		world.Shipper.State.ShouldBe(state switch
		{
			"holding" => ShippingState.Holding,
			"shipping" => ShippingState.Shipping,
			_ => ShippingState.LocalOnly,
		});

	[Then(@"^the buffer is gone$")]
	public void BufferGone() => Directory.Exists(world.BufferDirectory).ShouldBeFalse();

	[Then(@"^the pipeline was rebuilt once for them$")]
	public async Task RebuiltOnce()
	{
		(await Eventually.True(() => world.Builds > world.BuildsMark)).ShouldBeTrue("no flush happened");
		await Task.Delay(Quiet);
		(world.Builds - world.BuildsMark).ShouldBe(1);
	}

	[Then(@"^the pipeline was not rebuilt again$")]
	public void NotRebuilt() => world.Builds.ShouldBe(world.BuildsMark);

	// ── On the device ─────────────────────────────────────────────────
	[Then(@"^""(.+)"" is on the device$")]
	public void OnTheDevice(string message) =>
		world.OnDevice.Messages.ShouldContain(m => string.Equals(m, message, StringComparison.Ordinal));

	[Then(@"^""(.+)"" is on the device once$")]
	public void OnTheDeviceOnce(string message) =>
		world.OnDevice.Messages.Count(m => string.Equals(m, message, StringComparison.Ordinal)).ShouldBe(1);

	[Then(@"^a warning on the device says the new settings could not be used$")]
	public void WarnedOfBrokenSettings() =>
		world.OnDevice.Events.ShouldContain(e => e.Level == LogEventLevel.Warning && e.Exception is InvalidOperationException);

	[Then(@"^logging carries on$")]
	public void LoggingCarriesOn()
	{
		Log.Information("Still logging");
		world.OnDevice.Messages.ShouldContain(m => string.Equals(m, "Still logging", StringComparison.Ordinal));
	}

	// ── Told settings ─────────────────────────────────────────────────
	[Then(@"^the answer is stored$")]
	public void AnswerStored() => world.Store.Stored.ShouldNotBeNull().SameAs(World.BetterStack).ShouldBeTrue();

	[Then(@"^nothing is stored$")]
	public void NothingStored() => world.Store.Stored.ShouldBeNull();
}
