// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Serilog;
using Serilog.Events;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Enrichment;

namespace TheBleedingDeacons.Inventory.Maui;

/// <summary>
/// Everything a MAUI app needs to log with Inventory, in one call.
/// </summary>
public static class InventoryMauiExtensions
{
	/// <summary>
	/// Build the logger, start holding, hook the crash handlers, route
	/// <c>ILogger&lt;T&gt;</c> through it, and register the
	/// <see cref="ILogShipper"/> the app tells where to ship.
	/// </summary>
	/// <remarks>
	/// <para>Call it first, so what goes wrong while the rest of the app is being
	/// built is on record. Logging is running when it returns.</para>
	///
	/// <para>Where to ship is the app's business, and arrives later — from its
	/// settings, or a server after sign-in. Until the app calls
	/// <see cref="ILogShipper.Reconfigure"/>, events are held on disk (see
	/// <see cref="InventoryMauiOptions.HoldUntilConfigured"/>).</para>
	/// </remarks>
	/// <example>
	/// <code>
	/// builder.UseInventory(new InventoryMauiOptions
	/// {
	///     Application = "Register",
	///     Environment = "Production",
	///     DeveloperSinks = BuildInfo.IsDebug,
	///     Configure = cfg => cfg.ReadFrom.Configuration(builder.Configuration),
	/// });
	///
	/// // Later, once the app knows:
	/// app.Services.GetRequiredService&lt;ILogShipper&gt;().Reconfigure(betterStack);
	/// </code>
	/// </example>
	/// <param name="builder">The app builder.</param>
	/// <param name="options">What to build.</param>
	/// <returns>The builder.</returns>
	public static MauiAppBuilder UseInventory(this MauiAppBuilder builder, InventoryMauiOptions options)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.Application, nameof(options));

		var logDirectory = Path.Combine(FileSystem.AppDataDirectory, "logs");
		var appVersion = options.AppVersion ?? DefaultAppVersion;

		// The same buffer directory Register and Link used, so a device
		// upgrading onto this ships what it was already holding.
		var shipper = new LogShipper(
			() => BaseConfiguration(options, logDirectory, appVersion),
			new InventoryOptions { BufferDirectory = Path.Combine(logDirectory, "betterstack-buffer") },
			BetterStackHttp.CreateClient(options.UserAgent ?? $"{Token(options.Application)}/{Token(SafeVersion(appVersion))}"));

		try
		{
			Directory.CreateDirectory(logDirectory);

			if (options.HoldUntilConfigured)
			{
				shipper.Reconfigure(null);
			}
			else
			{
				Log.Logger = BaseConfiguration(options, logDirectory, appVersion).CreateLogger();
			}
		}
#pragma warning disable CA1031 // A logger that cannot be built must not stop the app starting.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Serilog's default is a silent logger, so every Log.* call becomes a
			// no-op rather than a null reference.
		}

		CrashLogging.Register();
		RegisterPlatformCrashHandlers();

		// CurrentLogger rather than AddSerilog()'s default, which binds each
		// ILogger<T> to the pipeline of the moment it was created — and every
		// rebuild disposes that pipeline. See CurrentLogger.
		Serilog.SerilogLoggingBuilderExtensions.AddSerilog(builder.Logging, CurrentLogger.Instance, dispose: false);

		builder.Services.AddSingleton<ILogShipper>(shipper);

		return builder;
	}

	/// <summary>
	/// The pipeline every rebuild starts from: the app's own configuration,
	/// the standard enrichers, and the local sinks. The shipper adds Better
	/// Stack to it.
	/// </summary>
	internal static LoggerConfiguration BaseConfiguration(
		InventoryMauiOptions options,
		string logDirectory,
		Func<string> appVersion)
	{
		var cfg = new LoggerConfiguration();
		options.Configure?.Invoke(cfg);

		if (options.DeveloperSinks)
		{
			// After the app's configuration, so it wins. A settings file asking
			// for Information is right for a device in someone's pocket and
			// wrong for a cable and a terminal: it hides every Debug line, and
			// those are the ones written for exactly this.
			cfg.MinimumLevel.Debug();
		}

		// Read on every rebuild, so a label the user changes shows up at the
		// next reconfigure without a restart.
		var deviceLabel = DeviceLabel.Resolve(options.DeviceLabelPreferenceKey);

		cfg.Enrich.FromLogContext()
			.Enrich.WithProperty("Application", options.Application)
			.Enrich.WithProperty("Environment", options.Environment)
			.Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
			.Enrich.WithProperty("PlatformVersion", DeviceInfo.VersionString)
			.Enrich.WithProperty("AppVersion", SafeVersion(appVersion))
			.Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
			.Enrich.WithProperty("DeviceName", DeviceInfo.Name)
			.Enrich.WithProperty("ProcessId", System.Environment.ProcessId)
			.Enrich.WithProperty("DeviceLabel", deviceLabel)
			.Enrich.With<ExceptionEnricher>();

		var prefix = options.LogFilePrefix ?? options.Application.ToLowerInvariant();

		// shared: true because a second writer in the same process — Link's
		// push service — writes here too. The sink must not itself become a
		// source of failure, and the cost is a little buffering.
		if (options.DeveloperSinks)
		{
			cfg.WriteTo.File(
					Path.Combine(logDirectory, $"{prefix}-debug-.log"),
					rollingInterval: RollingInterval.Day,
					retainedFileCountLimit: 21,
					shared: true,
					formatProvider: System.Globalization.CultureInfo.InvariantCulture)
				.WriteTo.Debug(formatProvider: System.Globalization.CultureInfo.InvariantCulture);

			// The console sink sets Console.ForegroundColor, which throws on
			// Android and iOS; every event would land in SelfLog. Desktop only.
			if (DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
			{
				cfg.WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
			}

			AddPlatformDeveloperSinks(cfg, options);
		}
		else
		{
			cfg.WriteTo.File(
				Path.Combine(logDirectory, $"{prefix}-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 7,
				shared: true,
				restrictedToMinimumLevel: LogEventLevel.Information,
				formatProvider: System.Globalization.CultureInfo.InvariantCulture);
		}

		return cfg;
	}

	private static void AddPlatformDeveloperSinks(LoggerConfiguration cfg, InventoryMauiOptions options)
	{
#if ANDROID
		// Live output on a handset, which there was none of: adb -s <serial> logcat -s <tag>:V
		cfg.WriteTo.Sink(new LogcatSink(options.LogcatTag ?? options.Application));
#else
		_ = cfg;
		_ = options;
#endif
	}

	private static void RegisterPlatformCrashHandlers()
	{
#if ANDROID
		// Java-side unhandled exceptions, bridged into .NET.
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
			CrashLogging.ReportFatal(args.Exception, "Unhandled Android exception");
#endif
	}

	/// <summary>
	/// The file version on Windows, where an unpackaged app's
	/// <c>AppInfo.VersionString</c> is no help; <c>AppInfo.VersionString</c>
	/// everywhere else.
	/// </summary>
	private static string DefaultAppVersion()
	{
		if (DeviceInfo.Platform == DevicePlatform.WinUI && System.Environment.ProcessPath is { } path)
		{
			return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? AppInfo.VersionString;
		}

		return AppInfo.VersionString;
	}

	private static string SafeVersion(Func<string> appVersion)
	{
		try
		{
			return appVersion();
		}
#pragma warning disable CA1031 // A version is not worth failing a launch for.
		catch (Exception)
#pragma warning restore CA1031
		{
			return "unknown";
		}
	}

	/// <summary>
	/// A string fit to be a User-Agent product token: no spaces or separators.
	/// </summary>
	private static string Token(string value) =>
		new(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
}
