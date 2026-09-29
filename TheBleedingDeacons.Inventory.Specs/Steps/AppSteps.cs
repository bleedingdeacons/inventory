// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Reqnroll;
using Serilog;
using Serilog.Events;
using TheBleedingDeacons.Inventory.Specs.Support;

namespace TheBleedingDeacons.Inventory.Specs.Steps;

/// <summary>
/// What the app does, and what it is told.
/// </summary>
[Binding]
public sealed class AppSteps(World world)
{
	// ── Where to ship ─────────────────────────────────────────────────
	[Given(@"^the app has not been told where to ship$")]
	public void NotTold() => world.Shipper.Reconfigure(null);

	[Given(@"^the app has been told to ship to Better Stack$")]
	[When(@"^the app is told to ship to Better Stack$")]
	public void ToldToShip() => world.Shipper.Reconfigure(World.BetterStack);

	[Given(@"^the app is told not to ship$")]
	[When(@"^the app is told not to ship$")]
	public void ToldNotToShip() => world.Shipper.Reconfigure(World.NoEndpoint);

	[When(@"^the token changes to ""(.+)""$")]
	public void TokenChanges(string token) =>
		world.Shipper.Reconfigure(new BetterStackConfiguration { Endpoint = World.BetterStack.Endpoint, SourceToken = token });

	[Given(@"^the shipper's timer is an hour$")]
	public void TimerIsAnHour() => world.ShippingPeriod = TimeSpan.FromHours(1);

	[Given(@"^new settings cannot be built$")]
	public void BuildsFail() => world.FailBuilds = true;

	// ── Logging ───────────────────────────────────────────────────────
	[Given(@"^the app logs ""(.+)""$")]
	[When(@"^the app logs ""(.+)""$")]
	public void Logs(string message) => Write(LogEventLevel.Information, message);

	[Given(@"^the app logs an error ""(.+)""$")]
	[When(@"^the app logs an error ""(.+)""$")]
	public void LogsAnError(string message) => Write(LogEventLevel.Error, message);

	[When(@"^the app logs (\d+) errors at once$")]
	public void LogsErrorsAtOnce(int count)
	{
		world.BuildsMark = world.Builds;
		for (var i = 0; i < count; i++)
		{
			Log.Error("Layer {Layer} of one failure", i);
		}
	}

	[Given(@"^an ILogger was made at start-up$")]
	public void MelLoggerMade() => world.MakeMelLogger();

	[When(@"^the ILogger logs ""(.+)""$")]
	public void MelLoggerLogs(string message)
	{
		// The scenario's text as the template; it has no placeholders.
		world.MelLogger.LogInformation(message);
		world.Remember(message);
	}

	// ── Better Stack ──────────────────────────────────────────────────
	[Given(@"^Better Stack refuses the next batch$")]
	public void RefusesNextBatch() => world.Server.Then(System.Net.HttpStatusCode.InternalServerError);

	// ── Told settings ─────────────────────────────────────────────────
	[Given(@"^nothing was stored$")]
	public void NothingStored() => world.Store.Stored = null;

	[Given(@"^the device was last told to ship to Better Stack$")]
	public void StoredBetterStack() => world.Store.Stored = World.BetterStack;

	[Given(@"^the app starts$")]
	[When(@"^the app starts$")]
	public async Task AppStarts()
	{
		await world.Settings.ApplyStoredAsync();
		world.BuildsMark = world.Builds;
	}

	[When(@"^the server answers with a log endpoint$")]
	public Task ServerAnswers() => world.Settings.ApplyAsync(World.BetterStack);

	[When(@"^the server answers with the same endpoint again$")]
	public Task ServerAnswersTheSame() =>
		world.Settings.ApplyAsync(new BetterStackConfiguration { Endpoint = World.BetterStack.Endpoint, SourceToken = World.BetterStack.SourceToken });

	[When(@"^the server cannot be reached$")]
	public void ServerUnreachable()
	{
		// Nothing to act on: the app calls nothing, and keeps what it has.
	}

	[When(@"^the member signs out$")]
	public Task SignsOut() => world.Settings.ForgetAsync();

	private void Write(LogEventLevel level, string message)
	{
		// A shipper built on first use: logging starts it if nothing else has.
		_ = world.Shipper;

		// The scenario's text as the template; it has no placeholders.
		Log.Write(level, message);
		world.Remember(message);
	}
}
