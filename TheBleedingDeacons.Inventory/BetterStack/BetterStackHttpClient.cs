// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Serilog.Debugging;
using Serilog.Sinks.Http;

namespace TheBleedingDeacons.Inventory.BetterStack;

/// <summary>
/// The <see cref="IHttpClient"/> the durable sink ships through: POSTs a batch
/// of NDJSON to the Better Stack ingest endpoint under the source token.
/// </summary>
/// <remarks>
/// <para><b>It never throws.</b> The sink calls it from a background shipper
/// loop and from its dispose path, and an exception out of the latter
/// propagates out of <c>Log.CloseAndFlush()</c> into application shutdown —
/// on Windows it surfaced at window-destroy time as "invalid response from
/// server". The sink's contract is simpler than that: it needs a response, and
/// a non-2xx one means "keep this batch on disk and try later", which is
/// exactly right for a transport failure. So every exception becomes a 599,
/// the conventional unofficial status for a client-side network failure, and
/// the cause goes to Serilog's SelfLog.</para>
///
/// <para>No event is lost while the buffer file survives, and it survives
/// process kills. That is the whole point.</para>
/// </remarks>
public sealed class BetterStackHttpClient : IHttpClient
{
	private readonly HttpClient _httpClient;
	private readonly string _sourceToken;

	/// <summary>
	/// Initializes a new instance of the <see cref="BetterStackHttpClient"/> class.
	/// </summary>
	/// <param name="sourceToken">The Better Stack source token, sent as a bearer token.</param>
	/// <param name="httpClient">The client to send through. Not owned: never disposed here.</param>
	public BetterStackHttpClient(string sourceToken, HttpClient httpClient)
	{
		_sourceToken = sourceToken ?? throw new ArgumentNullException(nameof(sourceToken));
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	}

	/// <summary>
	/// Called when the sink is configured from appsettings. This one is
	/// configured through its constructor, so there is nothing to do.
	/// </summary>
	/// <param name="configuration">Ignored.</param>
	public void Configure(IConfiguration configuration)
	{
		// Intentionally empty.
	}

	/// <inheritdoc/>
	public async Task<HttpResponseMessage> PostAsync(
		string requestUri,
		Stream contentStream,
		CancellationToken cancellationToken)
	{
		try
		{
			using var content = new StreamContent(contentStream);

			// One JSON event per line, as BetterStackNdjsonBatchFormatter writes.
			content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

			using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
			{
				Content = content,
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sourceToken);

			return await _httpClient
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Shutdown cancelling the shipper. Still a retryable response, so the
			// batch stays on disk for the next process.
			SelfLog.WriteLine("[BetterStackHttpClient] POST cancelled; batch retained for retry.");
			return TransportFailure("Request cancelled");
		}
#pragma warning disable CA1031 // The sink must never see a throw; see the remarks.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// Timeouts, DNS, TLS, WinHttpException 12152, a client disposed at
			// shutdown: all of them "try again later".
			SelfLog.WriteLine(
				"[BetterStackHttpClient] POST to {0} failed ({1}): {2}. Batch retained for retry.",
				requestUri,
				ex.GetType().Name,
				ex.Message);
			return TransportFailure(ex.Message);
		}
	}

	/// <summary>
	/// Does nothing: the <see cref="HttpClient"/> is shared, and disposing it
	/// here would kill it for everything else that uses it.
	/// </summary>
	public void Dispose()
	{
		// Not ours to dispose.
	}

	/// <summary>
	/// A disposable non-2xx response, so the sink keeps the batch and retries on
	/// its next tick.
	/// </summary>
	private static HttpResponseMessage TransportFailure(string reason) =>
		new((System.Net.HttpStatusCode)599)
		{
			ReasonPhrase = reason,
			Content = new StringContent(string.Empty),
		};
}
