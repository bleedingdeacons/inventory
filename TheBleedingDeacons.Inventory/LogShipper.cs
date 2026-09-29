// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog;
using Serilog.Core;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Sinks;

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// See <see cref="ILogShipper"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The base factory must build a <i>fresh</i> configuration every time
///       it is called: the previous logger is disposed on every rebuild, and a
///       disposed logger cannot be reused.</item>
/// <item>Rebuilds are serialised, so two settings saves in quick succession
///       cannot race into half-built pipelines.</item>
/// <item>The old pipeline is disposed only after the new one is installed, so
///       there is no moment in which <c>Log.Logger</c> points at nothing.</item>
/// </list>
/// </remarks>
public sealed class LogShipper : ILogShipper, IDisposable
{
	/// <summary>
	/// The buffer file prefix. Unchanged from Register and Link, so a device
	/// upgrading onto this library ships what it was already holding.
	/// </summary>
	internal const string BufferFilePrefix = "buffer";

	/// <summary>
	/// Where the holding sink says it is posting. It posts nowhere — the
	/// holding client answers every call itself — but the sink wants a URI.
	/// </summary>
	private const string HoldingUri = "https://holding.invalid/";

	private readonly Func<LoggerConfiguration> _baseLogger;
	private readonly InventoryOptions _options;
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;
	private readonly TimeProvider _time;
	private readonly Lock _gate = new();

	private Logger? _current;
	private BetterStackConfiguration? _configuration;
	private DateTimeOffset _lastFlush = DateTimeOffset.MinValue;
	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="LogShipper"/> class. Nothing
	/// changes until the first <see cref="Reconfigure"/>.
	/// </summary>
	/// <param name="baseLogger">
	/// Builds the app's fixed pipeline — local sinks and enrichers — afresh on
	/// every call. The Better Stack sink is added to what it returns.
	/// </param>
	/// <param name="options">Buffering and shipping settings.</param>
	/// <param name="httpClient">
	/// The client to ship through. Null creates one with
	/// <see cref="BetterStackHttp.CreateClient"/>, owned and disposed here.
	/// </param>
	/// <param name="timeProvider">The clock flush debouncing reads. Null for the system clock.</param>
	public LogShipper(
		Func<LoggerConfiguration> baseLogger,
		InventoryOptions options,
		HttpClient? httpClient = null,
		TimeProvider? timeProvider = null)
	{
		_baseLogger = baseLogger ?? throw new ArgumentNullException(nameof(baseLogger));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentException.ThrowIfNullOrWhiteSpace(options.BufferDirectory, nameof(options));

		_ownsHttpClient = httpClient is null;
		_httpClient = httpClient ?? BetterStackHttp.CreateClient();
		_time = timeProvider ?? TimeProvider.System;

		Current = this;
	}

	/// <summary>
	/// Gets the most recently constructed shipper, for the few callers that
	/// cannot be handed one: crash handlers, registered before any container
	/// exists. A static is the smaller evil against making the crash path
	/// depend on service resolution.
	/// </summary>
	public static ILogShipper? Current { get; internal set; }

	/// <inheritdoc/>
	public ShippingState State { get; private set; } = ShippingState.NotStarted;

	/// <inheritdoc/>
	public void Reconfigure(BetterStackConfiguration? configuration)
	{
		// A copy: the caller's object is theirs to go on changing, and the
		// pipeline built from it is not.
		Rebuild(
			configuration is null
				? null
				: new BetterStackConfiguration { Endpoint = configuration.Endpoint, SourceToken = configuration.SourceToken },
			announce: true);
	}

	/// <summary>
	/// Build the pipeline for a configuration, swap it in and dispose the old one.
	/// </summary>
	/// <param name="configuration">A copy the caller no longer holds.</param>
	/// <param name="announce">
	/// Whether to log what changed. Not for a flush, which changes nothing and
	/// would otherwise add an Information line after every error.
	/// </param>
	private void Rebuild(BetterStackConfiguration? configuration, bool announce)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			Logger? replacement = null;
			ShippingState state;

			try
			{
				var builder = _baseLogger();

				if (configuration is null)
				{
					// Not told yet. Written to the same buffer the real sink reads,
					// so what is held now ships when the app is told.
					state = ShippingState.Holding;
					builder = builder.WriteTo.DurableHttpUsingFileSizeRolledBuffers(
						requestUri: HoldingUri,
						bufferBaseFileName: BufferBaseFileName(),
						bufferFileSizeLimitBytes: _options.HoldingBufferFileSizeLimitBytes,
						retainedBufferFileCountLimit: _options.HoldingRetainedBufferFileCountLimit,
						logEventsInBatchLimit: _options.LogEventsInBatchLimit,
						batchSizeLimitBytes: _options.BatchSizeLimitBytes,
						period: _options.HoldingPeriod,
						textFormatter: new BetterStackTextFormatter(),
						batchFormatter: new BetterStackNdjsonBatchFormatter(),
						httpClient: new HoldingHttpClient());
				}
				else if (configuration.IsValid())
				{
					state = ShippingState.Shipping;
					builder = builder.WriteTo.DurableHttpUsingFileSizeRolledBuffers(
						requestUri: configuration.Endpoint,
						bufferBaseFileName: BufferBaseFileName(),
						bufferFileSizeLimitBytes: _options.BufferFileSizeLimitBytes,
						retainedBufferFileCountLimit: _options.RetainedBufferFileCountLimit,
						logEventsInBatchLimit: _options.LogEventsInBatchLimit,
						batchSizeLimitBytes: _options.BatchSizeLimitBytes,
						period: _options.ShippingPeriod,
						textFormatter: new BetterStackTextFormatter(),
						batchFormatter: new BetterStackNdjsonBatchFormatter(),
						httpClient: new BetterStackHttpClient(configuration.SourceToken, _httpClient));
				}
				else
				{
					state = ShippingState.LocalOnly;
				}

				if (_options.FlushOnError)
				{
					builder = builder.WriteTo.Sink(new FlushOnErrorSink(this));
				}

				replacement = builder.CreateLogger();
			}
#pragma warning disable CA1031 // A broken configuration must never take logging down.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				// The warning goes to the pipeline still running.
				Log.Warning(ex, "Failed to build new Serilog pipeline for Better Stack config — retaining previous logger");
				replacement?.Dispose();
				return;
			}

			var previous = _current;
			_current = replacement;
			_configuration = configuration;
			State = state;
			Log.Logger = replacement;

			if (_options.SelfLog is { } selfLog)
			{
				Serilog.Debugging.SelfLog.Enable(selfLog);
			}

			switch (announce ? state : ShippingState.NotStarted)
			{
				case ShippingState.Holding:
					Log.Information("Better Stack sink holding until told where to ship");
					break;
				case ShippingState.Shipping:
					Log.Information("Better Stack sink (re)attached to {Endpoint}", configuration!.ToLogSafe().Endpoint);
					break;
				case ShippingState.LocalOnly:
					Log.Information("Better Stack sink removed; logs stay on this device");
					break;
				default:
					// A flush: nothing to say.
					break;
			}

			// Disposing the old pipeline stops its shipper and releases the
			// buffer's bookmark file, so the new one can claim it. It is also
			// what flushes it.
			try
			{
				previous?.Dispose();
			}
#pragma warning disable CA1031 // A failed dispose does not affect the new pipeline.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				Log.Debug(ex, "Error disposing previous Serilog pipeline");
			}

			// Told not to ship, so nothing held is going anywhere. Only now: until
			// the old pipeline was gone its sink had the files open.
			if (state == ShippingState.LocalOnly)
			{
				DeleteBuffer();
			}
		}
	}

	/// <inheritdoc/>
	public void Flush()
	{
		BetterStackConfiguration? configuration;

		lock (_gate)
		{
			if (_disposed || State != ShippingState.Shipping)
			{
				// Holding, or not shipping: nowhere a flush could send anything.
				return;
			}

			var now = _time.GetUtcNow();
			if (now - _lastFlush < _options.FlushDebounce)
			{
				return;
			}

			_lastFlush = now;
			configuration = _configuration;
		}

		// Disposing the pipeline is what flushes the durable sink, and a
		// rebuild disposes the old one only after the new one is in place.
		Rebuild(configuration, announce: false);
	}

	/// <summary>
	/// Flush and close the pipeline this shipper built, and put a silent logger
	/// in its place if it is still the global one.
	/// </summary>
	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			if (_current is not null)
			{
				if (ReferenceEquals(Log.Logger, _current))
				{
					Log.Logger = new LoggerConfiguration().CreateLogger();
				}

				_current.Dispose();
				_current = null;
			}

			if (_ownsHttpClient)
			{
				_httpClient.Dispose();
			}

			if (ReferenceEquals(Current, this))
			{
				Current = null;
			}
		}
	}

	private string BufferBaseFileName()
	{
		Directory.CreateDirectory(_options.BufferDirectory);
		return Path.Combine(_options.BufferDirectory, BufferFilePrefix);
	}

	private void DeleteBuffer()
	{
		try
		{
			if (Directory.Exists(_options.BufferDirectory))
			{
				Directory.Delete(_options.BufferDirectory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Left for the next time the app is told not to ship.
			Log.Debug(ex, "The Better Stack buffer could not be deleted");
		}
	}
}
