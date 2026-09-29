// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

// Inventory outside MAUI: hold, get told, ship.
//
//   dotnet run --project example/Inventory-cli
//       Holds. The lines stay in .inventory/buffer, and a later run that is
//       told where to ship sends them, stamped with the time they were logged.
//
//   INVENTORY_ENDPOINT=s1234567.eu-central-1a.betterstackdata.com \
//   INVENTORY_TOKEN=<source token> dotnet run --project example/Inventory-cli
//       Ships: what this run logged, anything earlier runs held, and an error
//       that goes at once rather than on the timer.
using System.Globalization;
using Serilog;
using TheBleedingDeacons.Inventory;
using TheBleedingDeacons.Inventory.Enrichment;

var buffer = Path.Combine(Directory.GetCurrentDirectory(), ".inventory", "buffer");

using var shipper = new LogShipper(
	() => new LoggerConfiguration()
		.MinimumLevel.Debug()
		.Enrich.WithProperty("Application", "Inventory-cli")
		.Enrich.With<ExceptionEnricher>()
		.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture),
	new InventoryOptions { BufferDirectory = buffer });

CrashLogging.Register();

// Not told yet: this is held.
shipper.Reconfigure(null);
Log.Information("Inventory example started at {Started:O}; not told where to ship yet", DateTimeOffset.Now);

var endpoint = Environment.GetEnvironmentVariable("INVENTORY_ENDPOINT");
var token = Environment.GetEnvironmentVariable("INVENTORY_TOKEN");

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
{
	Log.Warning("Set INVENTORY_ENDPOINT and INVENTORY_TOKEN to ship. Until then everything is held in {Buffer}", buffer);
}
else
{
	shipper.Reconfigure(new BetterStackConfiguration { Endpoint = endpoint, SourceToken = token });
	Log.Information("Told where to ship; what was held is on its way");

	try
	{
		throw new InvalidOperationException("An example error", new TimeoutException("with an inner one"));
	}
	catch (InvalidOperationException ex)
	{
		// Flush-on-error sends this now, not on the next tick.
		Log.Error(ex, "Something went wrong, on purpose");
	}

	// Long enough for the shipper to send.
	await Task.Delay(TimeSpan.FromSeconds(3));
}

Console.WriteLine($"State: {shipper.State}");
