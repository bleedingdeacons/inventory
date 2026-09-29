// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// Where an app keeps the shipping settings it was last told.
/// </summary>
/// <remarks>
/// Kept because the logger is built at process start, before anything can ask
/// a server. A device woken by a push, or opened with no signal, ships with
/// what it was last told rather than holding everything until it next reaches
/// the server. See <see cref="ShippingSettings"/>.
/// </remarks>
public interface ILoggingSettingsStore
{
	/// <summary>
	/// What was stored, or null when nothing has been told since the store was
	/// last cleared.
	/// </summary>
	/// <remarks>
	/// A stored configuration that is not valid means "told not to ship", which
	/// is a different thing from never having been told. A store that cannot be
	/// read should answer null — that holds logs rather than losing them.
	/// </remarks>
	/// <returns>The stored configuration, or null.</returns>
	Task<BetterStackConfiguration?> LoadAsync();

	/// <summary>
	/// Keep a configuration, replacing whatever was there.
	/// </summary>
	/// <param name="configuration">What the app was told.</param>
	/// <returns>A task that completes when it is stored.</returns>
	Task SaveAsync(BetterStackConfiguration configuration);

	/// <summary>
	/// Forget what was stored.
	/// </summary>
	/// <returns>A task that completes when it is gone.</returns>
	Task ClearAsync();
}
