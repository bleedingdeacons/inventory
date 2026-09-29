// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace TheBleedingDeacons.Inventory.Maui;

/// <summary>
/// The shipping settings an app was last told, in <see cref="SecureStorage"/>.
/// </summary>
/// <remarks>
/// Secure storage rather than Preferences because the source token is a
/// credential. It can only write to one log source, but whoever holds it can
/// fill that source with anything.
/// </remarks>
public sealed class SecureStorageLoggingSettingsStore : ILoggingSettingsStore
{
	private readonly string _name;

	/// <summary>
	/// Initializes a new instance of the <see cref="SecureStorageLoggingSettingsStore"/> class.
	/// </summary>
	/// <param name="name">
	/// The SecureStorage key. Link's is <c>link_logging</c>; keeping an app's
	/// existing key keeps what its devices were already told.
	/// </param>
	public SecureStorageLoggingSettingsStore(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		_name = name;
	}

	/// <inheritdoc/>
	public async Task<BetterStackConfiguration?> LoadAsync()
	{
		try
		{
			return BetterStackConfiguration.FromJson(await SecureStorage.GetAsync(_name).ConfigureAwait(false));
		}
		catch (Exception e) when (e is System.Security.SecurityException or CryptographicException or InvalidOperationException)
		{
			// The keystore invalidated its key, or a push woke the device
			// before its first unlock. Either way it reads as not told yet,
			// which holds logs rather than losing them.
			return null;
		}
	}

	/// <inheritdoc/>
	public Task SaveAsync(BetterStackConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return SecureStorage.SetAsync(_name, configuration.ToJson());
	}

	/// <inheritdoc/>
	public Task ClearAsync()
	{
		SecureStorage.Remove(_name);

		return Task.CompletedTask;
	}
}
