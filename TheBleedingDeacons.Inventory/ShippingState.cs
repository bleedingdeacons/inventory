// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// What a <see cref="LogShipper"/> is doing with the events it is given.
/// </summary>
public enum ShippingState
{
	/// <summary>
	/// No pipeline built yet: <see cref="ILogShipper.Reconfigure"/> has not been called.
	/// </summary>
	NotStarted,

	/// <summary>
	/// Not told where to ship. Events go to a small on-disk buffer and wait,
	/// to be shipped with their own timestamps once the app is told.
	/// </summary>
	Holding,

	/// <summary>
	/// Shipping to Better Stack, disk first.
	/// </summary>
	Shipping,

	/// <summary>
	/// Told not to ship. Events reach the local sinks only, and anything held
	/// was dropped.
	/// </summary>
	LocalOnly,
}
