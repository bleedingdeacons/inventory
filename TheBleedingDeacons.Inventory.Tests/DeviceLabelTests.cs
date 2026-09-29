using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Inventory.Sinks;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The device label, as far as it can be tested off a device.
/// </summary>
public sealed class DeviceLabelTests
{
	[Theory]
	[InlineData("LENOVO", "TB330FU", "Android", "14", "LENOVO TB330FU (Android 14)")]
	[InlineData("samsung", "Samsung Galaxy Tab", "Android", "13", "Samsung Galaxy Tab (Android 13)")]
	[InlineData("", "iPhone15,2", "iOS", "18.1", "iPhone15,2 (iOS 18.1)")]
	[InlineData(null, null, "Android", null, "Device (Android)")]
	[InlineData(" Google ", " Pixel 8 ", "Android", " ", "Google Pixel 8 (Android)")]
	public void DescribesTheHardwareWithoutSayingTheMakerTwice(string? maker, string? model, string os, string? version, string expected) =>
		Assert.Equal(expected, DeviceLabel.Describe(maker, model, os, version));
}
