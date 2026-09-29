// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog;

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// Keeps a <see cref="LogShipper"/> in step with settings an app is <i>told</i>
/// — by a server, after sign-in — rather than ones built into it.
/// </summary>
/// <remarks>
/// <para><b>Why not built in.</b> A token in the app's settings file is in
/// every copy of the package, readable by anyone who unzips one, and replacing
/// it takes a release. A server can hand it only to a device that has signed
/// in, refuse a revoked one, and change or withdraw it at will. Link gets its
/// settings from Fellowship this way; Register from Freedom.</para>
///
/// <para>What the app does is fetch; this does the rest. Start with
/// <see cref="ApplyStoredAsync"/>, before the network; call
/// <see cref="ApplyAsync"/> with each answer; call <see cref="ForgetAsync"/>
/// when the session ends. With no answer at all — offline, a server having a
/// moment — call nothing: losing signal is not a reason to stop shipping, or to
/// drop what is held.</para>
/// </remarks>
public sealed class ShippingSettings
{
	private readonly ILoggingSettingsStore _store;
	private readonly ILogShipper _shipper;

	/// <summary>
	/// Initializes a new instance of the <see cref="ShippingSettings"/> class.
	/// </summary>
	/// <param name="store">Where the last answer is kept.</param>
	/// <param name="shipper">The shipper to keep in step.</param>
	public ShippingSettings(ILoggingSettingsStore store, ILogShipper shipper)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_shipper = shipper ?? throw new ArgumentNullException(nameof(shipper));
	}

	/// <summary>
	/// Build the logger from what was last stored. Once, at process start,
	/// before the network has been tried. Nothing stored means holding.
	/// </summary>
	/// <returns>A task that completes when the logger is rebuilt.</returns>
	public async Task ApplyStoredAsync()
	{
		_shipper.Reconfigure(await _store.LoadAsync().ConfigureAwait(false));
	}

	/// <summary>
	/// Act on an answer: store it and rebuild the logger, unless it is what the
	/// app already has.
	/// </summary>
	/// <param name="told">
	/// What the server said. Valid means ship; not valid means "no log
	/// endpoint set", which drops anything held.
	/// </param>
	/// <returns>True when the answer changed something.</returns>
	public async Task<bool> ApplyAsync(BetterStackConfiguration told)
	{
		ArgumentNullException.ThrowIfNull(told);

		var stored = await _store.LoadAsync().ConfigureAwait(false);
		if (told.SameAs(stored))
		{
			return false;
		}

		await _store.SaveAsync(told).ConfigureAwait(false);
		_shipper.Reconfigure(told);

		if (told.IsValid())
		{
			Log.Information("Told where to ship logs; shipping to {Endpoint}", told.ToLogSafe().Endpoint);
		}
		else
		{
			Log.Information("Told there is no log endpoint; logs stay on this device");
		}

		return true;
	}

	/// <summary>
	/// Drop what was stored and go back to holding — at sign-out, or when the
	/// server refuses the device. A device keeps nothing it was only given
	/// because it was signed in.
	/// </summary>
	/// <remarks>
	/// Does nothing when nothing is stored, so a signed-out launch does not
	/// rebuild a logger that is already holding.
	/// </remarks>
	/// <returns>A task that completes when the logger is holding again.</returns>
	public async Task ForgetAsync()
	{
		if (await _store.LoadAsync().ConfigureAwait(false) is null)
		{
			return;
		}

		await _store.ClearAsync().ConfigureAwait(false);
		_shipper.Reconfigure(null);
	}
}
