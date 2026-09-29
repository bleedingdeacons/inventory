// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog;

namespace TheBleedingDeacons.Inventory.Maui;

/// <summary>
/// What <see cref="InventoryMauiExtensions.UseInventory"/> builds. Only
/// <see cref="Application"/> is required.
/// </summary>
public sealed class InventoryMauiOptions
{
	/// <summary>
	/// Gets the app's name: the <c>Application</c> property on every event, the
	/// log file's prefix unless <see cref="LogFilePrefix"/> says otherwise, and
	/// the logcat tag unless <see cref="LogcatTag"/> does.
	/// </summary>
	public required string Application { get; init; }

	/// <summary>
	/// Gets the <c>Environment</c> property on every event. Default <c>Production</c>.
	/// </summary>
	public string Environment { get; init; } = "Production";

	/// <summary>
	/// Gets a value indicating whether this is a developer's build: Debug level
	/// and up, a <c>-debug-</c> log file kept 21 days, the IDE's output window,
	/// the console on desktop, and logcat on Android. Otherwise one log file of
	/// Information and up, kept 7 days. Pass it from the app — a library cannot
	/// see the app's DEBUG symbol.
	/// </summary>
	/// <example><c>DeveloperSinks = BuildInfo.IsDebug</c>, or <c>#if DEBUG</c> around <c>true</c>.</example>
	public bool DeveloperSinks { get; init; }

	/// <summary>
	/// Gets the log file name's prefix. Default: <see cref="Application"/> in lower case.
	/// </summary>
	public string? LogFilePrefix { get; init; }

	/// <summary>
	/// Gets the Preferences key a user-set device label is read from. Default
	/// <c>device_label</c>, which Register, Hand and Link all use. See
	/// <see cref="DeviceLabel"/>.
	/// </summary>
	public string DeviceLabelPreferenceKey { get; init; } = DeviceLabel.DefaultPreferenceKey;

	/// <summary>
	/// Gets the logcat tag in a developer's build on Android — and so the thing
	/// to filter on: <c>adb logcat -s Link:V</c>. Default: <see cref="Application"/>.
	/// </summary>
	public string? LogcatTag { get; init; }

	/// <summary>
	/// Gets anything else to apply to every pipeline before the enrichers and
	/// sinks, such as <c>cfg => cfg.ReadFrom.Configuration(builder.Configuration)</c>.
	/// Runs on every rebuild, so it must not depend on having run once.
	/// </summary>
	public Action<LoggerConfiguration>? Configure { get; init; }

	/// <summary>
	/// Gets where the <c>AppVersion</c> property comes from. Default: the
	/// file version on Windows, where an unpackaged app's
	/// <c>AppInfo.VersionString</c> says nothing useful, and
	/// <c>AppInfo.VersionString</c> everywhere else.
	/// </summary>
	public Func<string>? AppVersion { get; init; }

	/// <summary>
	/// Gets the User-Agent log uploads carry. Default:
	/// <c>{Application}/{AppVersion}</c>.
	/// </summary>
	public string? UserAgent { get; init; }

	/// <summary>
	/// Gets a value indicating whether to start holding — buffering on disk to
	/// ship once told where — rather than logging locally only until the first
	/// <see cref="ILogShipper.Reconfigure"/>. Default true: an app's first
	/// seconds, before it can know where to ship, are worth having.
	/// </summary>
	public bool HoldUntilConfigured { get; init; } = true;
}
