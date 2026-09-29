// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Net;
using Microsoft.Extensions.Configuration;
using Serilog.Sinks.Http;

namespace TheBleedingDeacons.Inventory.BetterStack;

/// <summary>
/// An <see cref="IHttpClient"/> that sends nothing and says so.
/// </summary>
/// <remarks>
/// <para>Used while an app has not yet been told where to ship. The durable
/// sink still writes every event to its buffer, and every attempt to ship a
/// batch is answered 503, which the sink reads as "keep it and try later".
/// When the app is told, the real client replaces this one over the same
/// buffer files, and what was held goes out with its original timestamps.</para>
///
/// <para>Makes no network call at all. There is nowhere to call.</para>
/// </remarks>
public sealed class HoldingHttpClient : IHttpClient
{
	/// <summary>
	/// Nothing to configure.
	/// </summary>
	/// <param name="configuration">Ignored.</param>
	public void Configure(IConfiguration configuration)
	{
		// Nothing to configure.
	}

	/// <inheritdoc/>
	public Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
		{
			ReasonPhrase = "Holding until told where to ship",
			Content = new StringContent(string.Empty),
		});

	/// <summary>
	/// Nothing held, so nothing to release.
	/// </summary>
	public void Dispose()
	{
		// Nothing held.
	}
}
