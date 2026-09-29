// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace TheBleedingDeacons.Inventory.Maui;

/// <summary>
/// A name for this device in a live tail.
/// </summary>
/// <remarks>
/// <c>Environment.MachineName</c> says <c>localhost</c> on Android and a
/// sandbox name on iOS, so it tells devices apart nowhere that matters. This
/// reads a label the user set, and otherwise builds one from what the platform
/// can say about the hardware.
/// </remarks>
public static class DeviceLabel
{
	/// <summary>
	/// The Preferences key Register, Hand and Link store a user-set label under.
	/// </summary>
	public const string DefaultPreferenceKey = "device_label";

	/// <summary>
	/// The label: the stored one if there is one, else the machine name on
	/// desktop, else maker, model and OS — <c>LENOVO TB330FU (Android 14)</c>.
	/// </summary>
	/// <param name="preferenceKey">Where a user-set label is stored, or null to skip looking.</param>
	/// <returns>A label, never empty.</returns>
	public static string Resolve(string? preferenceKey = DefaultPreferenceKey)
	{
		if (preferenceKey is not null)
		{
			try
			{
				var stored = Preferences.Get(preferenceKey, string.Empty);
				if (!string.IsNullOrWhiteSpace(stored))
				{
					return stored;
				}
			}
#pragma warning disable CA1031 // Preferences unavailable: the default below is the answer.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Fall through.
			}
		}

		try
		{
			var platform = DeviceInfo.Platform;

			if (platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst)
			{
				var machine = System.Environment.MachineName;
				if (!string.IsNullOrWhiteSpace(machine)
					&& !string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
				{
					return machine;
				}
			}

			return Describe(
				DeviceInfo.Manufacturer,
				DeviceInfo.Model,
				platform.ToString(),
				DeviceInfo.VersionString);
		}
#pragma warning disable CA1031 // A label is not worth failing a launch for.
		catch (Exception)
#pragma warning restore CA1031
		{
			return "UnknownDevice";
		}
	}

	/// <summary>
	/// Maker, model and OS as one label, without saying the maker twice when
	/// the model already starts with it.
	/// </summary>
	internal static string Describe(string? manufacturer, string? model, string osName, string? osVersion)
	{
		var maker = (manufacturer ?? string.Empty).Trim();
		var device = (model ?? string.Empty).Trim();
		var version = (osVersion ?? string.Empty).Trim();

		var hardware = maker.Length > 0 && !device.StartsWith(maker, StringComparison.OrdinalIgnoreCase)
			? $"{maker} {device}".Trim()
			: device;

		if (string.IsNullOrWhiteSpace(hardware))
		{
			hardware = "Device";
		}

		return version.Length == 0
			? $"{hardware} ({osName})"
			: $"{hardware} ({osName} {version})";
	}
}
