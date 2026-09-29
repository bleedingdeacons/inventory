using Serilog;
using Serilog.Events;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The shipper against a real durable sink and real buffer files, with only
/// Better Stack faked.
/// </summary>
public sealed class LogShipperTests : IDisposable
{
	private static readonly BetterStackConfiguration Ship = new() { Endpoint = "https://in.logs.example/", SourceToken = "src-token" };
	private static readonly BetterStackConfiguration DoNotShip = new() { Endpoint = string.Empty, SourceToken = string.Empty };

	private readonly GlobalLogScope _scope = new();
	private readonly TempDirectory _buffer = new();
	private readonly FakeBetterStack _betterStack = new();
	private readonly CollectingSink _local = new();
	private readonly ManualTimeProvider _clock = new();
	private readonly List<LogShipper> _shippers = [];
	private int _builds;

	public void Dispose()
	{
		foreach (var shipper in _shippers)
		{
			shipper.Dispose();
		}

		_scope.Dispose();
		_buffer.Dispose();
		_betterStack.Dispose();
	}

	private LogShipper Shipper(
		TimeSpan? shipping = null,
		bool flushOnError = false,
		Func<LoggerConfiguration>? baseLogger = null)
	{
		var shipper = new LogShipper(
			baseLogger ?? (() =>
			{
				Interlocked.Increment(ref _builds);
				return new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_local);
			}),
			new InventoryOptions
			{
				BufferDirectory = _buffer.Path,
				ShippingPeriod = shipping ?? TimeSpan.FromMilliseconds(100),
				HoldingPeriod = TimeSpan.FromMilliseconds(100),
				FlushOnError = flushOnError,
				SelfLog = null,
			},
			new HttpClient(_betterStack),
			_clock);
		_shippers.Add(shipper);
		return shipper;
	}

	[Fact]
	public void HasNotStartedUntilReconfigured()
	{
		var before = Log.Logger;

		var shipper = Shipper();

		Assert.Equal(ShippingState.NotStarted, shipper.State);
		Assert.Same(before, Log.Logger);
		Assert.Same(shipper, LogShipper.Current);
	}

	[Fact]
	public void RejectsWhatItCannotWorkWith()
	{
		Assert.Throws<ArgumentNullException>(() => new LogShipper(null!, new InventoryOptions { BufferDirectory = _buffer.Path }));
		Assert.Throws<ArgumentNullException>(() => new LogShipper(() => new LoggerConfiguration(), null!));
		Assert.Throws<ArgumentException>(() => new LogShipper(() => new LoggerConfiguration(), new InventoryOptions { BufferDirectory = " " }));
	}

	[Fact]
	public async Task ShipsInTheBetterStackShapeUnderTheToken()
	{
		var shipper = Shipper();

		shipper.Reconfigure(Ship);
		Log.Information("Message {MessageId} delivered", 4242);

		Assert.Equal(ShippingState.Shipping, shipper.State);
		Assert.True(await Eventually.True(() => _betterStack.Containing("Message 4242 delivered").Count == 1));

		var post = _betterStack.PostCarrying("Message 4242 delivered");
		Assert.Equal(new Uri("https://in.logs.example/"), post.Uri);
		Assert.Equal("src-token", post.Token);
		Assert.Equal("application/x-ndjson", post.MediaType);
		Assert.Equal("INFO", post.Events.Single(e => string.Equals(e.Message, "Message 4242 delivered", StringComparison.Ordinal)).Level);
	}

	[Fact]
	public void KeepsWritingToTheLocalSinks()
	{
		var shipper = Shipper();

		shipper.Reconfigure(Ship);
		Log.Warning("Seen locally");

		Assert.Contains("Seen locally", _local.Messages, StringComparer.Ordinal);
	}

	/// <summary>
	/// The heart of it: what happens before the app is told where to ship is
	/// kept, and goes out with its own timestamp once it is.
	/// </summary>
	[Fact]
	public async Task ShipsWhatItHeldWithItsOwnTimeOnceTold()
	{
		var shipper = Shipper();
		shipper.Reconfigure(null);
		Log.Error("Sign-in failed before anyone knew where to ship");
		var heldAt = _local.Events.Single(e => e.MessageTemplate.Text.StartsWith("Sign-in failed", StringComparison.Ordinal)).Timestamp;

		await Task.Delay(400);
		Assert.Equal(0, _betterStack.Posts);

		shipper.Reconfigure(Ship);

		Assert.True(await Eventually.True(() => _betterStack.Containing("Sign-in failed").Count == 1));
		Assert.Equal(heldAt.UtcDateTime, _betterStack.Containing("Sign-in failed")[0].Dt.UtcDateTime);
	}

	[Fact]
	public async Task HoldingSendsNothingAtAll()
	{
		var shipper = Shipper();

		shipper.Reconfigure(null);
		Log.Information("Waiting");
		await Task.Delay(500);

		Assert.Equal(ShippingState.Holding, shipper.State);
		Assert.Equal(0, _betterStack.Posts);
		Assert.NotEmpty(Directory.GetFiles(_buffer.Path));
	}

	/// <summary>Told not to ship, what was held is never going anywhere.</summary>
	[Fact]
	public async Task DropsWhatItHeldWhenToldNotToShip()
	{
		var shipper = Shipper();
		shipper.Reconfigure(null);
		Log.Information("Held, then dropped");
		Assert.True(Directory.Exists(_buffer.Path));

		shipper.Reconfigure(DoNotShip);

		Assert.Equal(ShippingState.LocalOnly, shipper.State);
		Assert.False(Directory.Exists(_buffer.Path));

		shipper.Reconfigure(Ship);
		await Task.Delay(500);
		Assert.Empty(_betterStack.Containing("Held, then dropped"));
	}

	[Fact]
	public async Task KeepsABatchTheEndpointRefusedAndShipsItLater()
	{
		_betterStack.Then(System.Net.HttpStatusCode.InternalServerError);
		var shipper = Shipper();

		shipper.Reconfigure(Ship);
		Log.Information("Worth the wait");

		Assert.True(await Eventually.True(() => _betterStack.Containing("Worth the wait").Count == 1));
		Assert.True(_betterStack.Posts >= 2);
	}

	/// <summary>
	/// The naive version adds a sink on every change; the old one keeps
	/// running and every event goes out twice, the second time under the old
	/// token.
	/// </summary>
	[Fact]
	public async Task NeverStacksSinksAcrossChanges()
	{
		var shipper = Shipper();
		shipper.Reconfigure(Ship);
		shipper.Reconfigure(new BetterStackConfiguration { Endpoint = Ship.Endpoint, SourceToken = "rotated" });

		Log.Information("Once only");

		Assert.True(await Eventually.True(() => _betterStack.Containing("Once only").Count == 1));
		await Task.Delay(400);
		Assert.Single(_betterStack.Containing("Once only"));
		Assert.Equal("rotated", _betterStack.PostCarrying("Once only").Token);
		Assert.Single(_local.Messages, m => string.Equals(m, "Once only", StringComparison.Ordinal));
	}

	[Fact]
	public async Task IsNotChangedByTheCallerChangingItsObjectAfterwards()
	{
		var shipper = Shipper();
		var config = new BetterStackConfiguration { Endpoint = Ship.Endpoint, SourceToken = "src-token" };

		shipper.Reconfigure(config);
		config.SourceToken = "changed later";
		Log.Information("Under the token it was given");

		Assert.True(await Eventually.True(() => _betterStack.Containing("Under the token").Count == 1));
		Assert.Equal("src-token", _betterStack.PostCarrying("Under the token").Token);
	}

	[Fact]
	public void KeepsTheRunningPipelineWhenANewOneCannotBeBuilt()
	{
		var fail = false;
		var shipper = Shipper(baseLogger: () => fail
			? throw new InvalidOperationException("no")
			: new LoggerConfiguration().WriteTo.Sink(_local));
		shipper.Reconfigure(null);
		var running = Log.Logger;

		fail = true;
		shipper.Reconfigure(Ship);

		Assert.Same(running, Log.Logger);
		Assert.Equal(ShippingState.Holding, shipper.State);
		Assert.Contains(_local.Events, e => e.Level == LogEventLevel.Warning && e.Exception is InvalidOperationException);
	}

	[Fact]
	public void AnnouncesEachChange()
	{
		var shipper = Shipper();

		shipper.Reconfigure(null);
		shipper.Reconfigure(Ship);
		shipper.Reconfigure(DoNotShip);

		Assert.Contains("Better Stack sink holding until told where to ship", _local.Messages, StringComparer.Ordinal);
		Assert.Contains("Better Stack sink (re)attached to \"https://in.logs.example/\"", _local.Messages, StringComparer.Ordinal);
		Assert.Contains("Better Stack sink removed; logs stay on this device", _local.Messages, StringComparer.Ordinal);
		Assert.DoesNotContain(_local.Messages, m => m.Contains("src-token", StringComparison.Ordinal));
	}

	[Fact]
	public void FlushDoesNothingUnlessShipping()
	{
		var shipper = Shipper();
		shipper.Flush();
		Assert.Equal(0, _builds);

		shipper.Reconfigure(null);
		shipper.Flush();
		shipper.Reconfigure(DoNotShip);
		shipper.Flush();

		Assert.Equal(2, _builds);
	}

	[Fact]
	public void FlushRebuildsQuietlyAndIsDebounced()
	{
		var shipper = Shipper();
		shipper.Reconfigure(Ship);
		var announcements = _local.Messages.Count;

		shipper.Flush();
		shipper.Flush();
		Assert.Equal(2, _builds);

		_clock.Advance(TimeSpan.FromSeconds(6));
		shipper.Flush();

		Assert.Equal(3, _builds);
		Assert.Equal(ShippingState.Shipping, shipper.State);
		Assert.Equal(announcements, _local.Messages.Count);
	}

	/// <summary>
	/// An error on a phone in someone's pocket should not wait for the timer —
	/// here an hour — or for the app to be opened again.
	/// </summary>
	[Fact]
	public async Task ShipsAnErrorWithoutWaitingForTheTimer()
	{
		var shipper = Shipper(shipping: TimeSpan.FromHours(1), flushOnError: true);
		shipper.Reconfigure(Ship);

		Log.Information("Routine");
		await Task.Delay(300);
		Assert.Empty(_betterStack.Containing("Routine"));

		Log.Error("The message would not open");

		Assert.True(await Eventually.True(() => _betterStack.Containing("The message would not open").Count == 1));
		Assert.Single(_betterStack.Containing("Routine"));
	}

	[Fact]
	public void DisposingPutsASilentLoggerInItsPlace()
	{
		var shipper = Shipper();
		shipper.Reconfigure(null);
		var ours = Log.Logger;

		shipper.Dispose();
		shipper.Dispose();

		Assert.NotSame(ours, Log.Logger);
		Assert.Null(LogShipper.Current);
		Assert.Throws<ObjectDisposedException>(() => shipper.Reconfigure(Ship));
		shipper.Flush();
	}

	[Fact]
	public void DisposingLeavesALoggerSomebodyElseInstalledAlone()
	{
		var shipper = Shipper();
		shipper.Reconfigure(null);
		var theirs = new LoggerConfiguration().CreateLogger();
		Log.Logger = theirs;

		shipper.Dispose();

		Assert.Same(theirs, Log.Logger);
	}

	[Fact]
	public void OwnsAndDisposesAClientItMadeItself()
	{
		var shipper = new LogShipper(() => new LoggerConfiguration(), new InventoryOptions { BufferDirectory = _buffer.Path, SelfLog = null });
		shipper.Reconfigure(null);

		shipper.Dispose();

		Assert.Equal(ShippingState.Holding, shipper.State);
	}
}
