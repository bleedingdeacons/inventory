// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Serilog;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Specs.Support;

/// <summary>
/// One app, its log shipper and Better Stack, for the length of a scenario.
///
/// <para>Reqnroll gives each scenario its own instance, so every step class
/// that takes this in its constructor sees the same app. The shipper, the
/// durable sink and the buffer files are real; Better Stack is
/// <see cref="FakeBetterStack"/>. The shipper is built on first use, so a
/// Given can still change how it is built.</para>
/// </summary>
public sealed class World : IDisposable
{
	public static readonly BetterStackConfiguration BetterStack = new()
	{
		Endpoint = "s1234567.eu-central-1a.betterstackdata.com",
		SourceToken = "the-source-token",
	};

	public static readonly BetterStackConfiguration NoEndpoint = new();

	private readonly GlobalLogScope _scope = new();
	private readonly TempDirectory _buffer = new();
	private LogShipper? _shipper;
	private ShippingSettings? _settings;
	private ILoggerFactory? _factory;
	private int _builds;

	public FakeBetterStack Server { get; } = new();

	/// <summary>Gets what reached the app's own sinks — its log file, in real life.</summary>
	public CollectingSink OnDevice { get; } = new();

	public InMemorySettingsStore Store { get; } = new();

	public TimeSpan ShippingPeriod { get; set; } = TimeSpan.FromMilliseconds(100);

	public bool FailBuilds { get; set; }

	public int Builds => Volatile.Read(ref _builds);

	/// <summary>Gets or sets the builds counted when a step last took a mark.</summary>
	public int BuildsMark { get; set; }

	/// <summary>Gets or sets the message the last "Better Stack receives" step found.</summary>
	public string? LastShipped { get; set; }

	public string BufferDirectory => _buffer.Path;

	public LogShipper Shipper => _shipper ??= new LogShipper(
		() =>
		{
			if (FailBuilds)
			{
				throw new InvalidOperationException("the sink could not be built");
			}

			Interlocked.Increment(ref _builds);
			return new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(OnDevice);
		},
		new InventoryOptions
		{
			BufferDirectory = _buffer.Path,
			ShippingPeriod = ShippingPeriod,
			HoldingPeriod = TimeSpan.FromMilliseconds(100),
			SelfLog = null,
		},
		new HttpClient(Server));

	public ShippingSettings Settings => _settings ??= new ShippingSettings(Store, Shipper);

	/// <summary>Gets an <c>ILogger</c> made the way UseInventory makes them.</summary>
	public Microsoft.Extensions.Logging.ILogger MelLogger { get; private set; } = null!;

	/// <summary>When a message was logged, as the device's own sink saw it.</summary>
	public Dictionary<string, DateTimeOffset> LoggedAt { get; } = new(StringComparer.Ordinal);

	public void MakeMelLogger()
	{
		_factory = LoggerFactory.Create(b => b.AddSerilog(CurrentLogger.Instance));
		MelLogger = _factory.CreateLogger("Unity");
	}

	public void Remember(string message) =>
		LoggedAt[message] = OnDevice.Events.Last(e => string.Equals(
			e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture),
			message,
			StringComparison.Ordinal)).Timestamp;

	public void Dispose()
	{
		_factory?.Dispose();
		_shipper?.Dispose();
		_scope.Dispose();
		_buffer.Dispose();
		Server.Dispose();
	}
}
