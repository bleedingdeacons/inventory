// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Net;

namespace TheBleedingDeacons.Inventory.BetterStack;

/// <summary>
/// The <see cref="HttpClient"/> log shipping uses when an app does not supply one.
/// </summary>
public static class BetterStackHttp
{
	/// <summary>
	/// A client tuned for a shipper that is idle most of the time.
	/// </summary>
	/// <remarks>
	/// <para>The managed <see cref="SocketsHttpHandler"/> on every platform, not
	/// the platform-native handler an app uses for its own API calls. Better
	/// Stack's ingest endpoint is not behind the TLS-fingerprinting firewall
	/// that native handler exists to get past, and this one exposes the pool
	/// settings below.</para>
	///
	/// <para><b>PooledConnectionIdleTimeout of 30 seconds</b> is the important
	/// one. Without it the client keeps idle keep-alive connections until the
	/// server closes them, and on Windows the next POST lands on a half-closed
	/// socket as WinHttpException 12152, "the server returned an invalid or
	/// unrecognized response" (dotnet/runtime#22749). The usual trigger is the
	/// final flush at shutdown after a long idle spell. Closing client-side
	/// first means the next request opens a fresh connection.</para>
	///
	/// <para><b>PooledConnectionLifetime of 5 minutes</b> recycles connections
	/// too, for mobile NATs and proxies that silently drop long-lived
	/// sockets.</para>
	///
	/// <para><b>A 30-second timeout</b>, tighter than the default: better to
	/// fail fast and let the durable sink retry from disk than hold shutdown
	/// behind a slow response.</para>
	/// </remarks>
	/// <param name="userAgent">
	/// A User-Agent to send, such as <c>Link/1.27.0 (Android 14)</c>, or null for
	/// none. Better Stack does not need one, but a shipper that names itself is
	/// easier to pick out.
	/// </param>
	/// <returns>A new client the caller owns.</returns>
	public static HttpClient CreateClient(string? userAgent = null)
	{
		var handler = new SocketsHttpHandler
		{
			PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			AutomaticDecompression = DecompressionMethods.GZip
				| DecompressionMethods.Deflate
				| DecompressionMethods.Brotli,
		};

		var client = new HttpClient(handler, disposeHandler: true)
		{
			Timeout = TimeSpan.FromSeconds(30),
		};

		// TryParseAdd rather than ParseAdd: a malformed agent string is not
		// worth failing to log over.
		if (!string.IsNullOrWhiteSpace(userAgent))
		{
			client.DefaultRequestHeaders.UserAgent.TryParseAdd(userAgent);
		}

		return client;
	}
}
