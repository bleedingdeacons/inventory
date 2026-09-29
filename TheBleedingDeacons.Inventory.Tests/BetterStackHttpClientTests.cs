using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The transport underneath the durable sink.
///
/// <para>Its one absolute requirement is that it never throws. The sink
/// calls it from a background shipper loop <i>and</i> from its dispose
/// path, and an exception out of the latter propagates into application
/// shutdown.</para>
/// </summary>
public sealed class BetterStackHttpClientTests
{
	private static (BetterStackHttpClient Client, StubHttpMessageHandler Handler) Build(
		HttpStatusCode status = HttpStatusCode.Accepted)
	{
		var handler = StubHttpMessageHandler.Always(status, "{}");
		return (new BetterStackHttpClient("src-token", new HttpClient(handler)), handler);
	}

	private static Stream Batch(string ndjson) => new MemoryStream(Encoding.UTF8.GetBytes(ndjson));

	[Fact]
	public void RejectsItsDependenciesBeingNull()
	{
		Assert.Throws<ArgumentNullException>(() => new BetterStackHttpClient(null!, new HttpClient()));
		Assert.Throws<ArgumentNullException>(() => new BetterStackHttpClient("t", null!));
	}

	[Fact]
	public async Task PostsTheBatchAsNdjsonUnderTheSourceToken()
	{
		var (client, handler) = Build();

		var response = await client.PostAsync(
			"https://s1.betterstackdata.com", Batch("""{"dt":"a"}"""), CancellationToken.None);

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
		Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
		Assert.Equal("src-token", handler.Requests[0].Headers.Authorization!.Parameter);
		Assert.Equal("application/x-ndjson", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
		Assert.Equal("""{"dt":"a"}""", handler.Bodies[0]);
	}

	/// <summary>
	/// A non-2xx tells the sink to keep the batch on disk and try again,
	/// which is exactly what a transport failure should produce — so the
	/// failure is mapped to a status rather than thrown.
	/// </summary>
	[Fact]
	public async Task TurnsATransportFailureIntoARetryableResponse()
	{
		var client = new BetterStackHttpClient(
			"src-token",
			new HttpClient(new ThrowingHandler(new HttpRequestException("no route to host"))));

		var response = await client.PostAsync(
			"https://s1.betterstackdata.com", Batch("{}"), CancellationToken.None);

		Assert.Equal(599, (int)response.StatusCode);
		Assert.False(response.IsSuccessStatusCode);
		Assert.Equal("no route to host", response.ReasonPhrase);
	}

	/// <summary>
	/// Shutdown cancels the shipper. The batch must be retained, and
	/// nothing may escape into the dispose path.
	/// </summary>
	[Fact]
	public async Task RetainsTheBatchWhenTheShipperIsCancelled()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var client = new BetterStackHttpClient(
			"src-token",
			new HttpClient(new ThrowingHandler(new OperationCanceledException())));

		var response = await client.PostAsync("https://s1.betterstackdata.com", Batch("{}"), cts.Token);

		Assert.Equal(599, (int)response.StatusCode);
	}

	/// <summary>
	/// The HttpClient may be the app-wide one. Disposing it here would kill
	/// every other outbound call the app makes.
	/// </summary>
	[Fact]
	public async Task DoesNotDisposeTheSharedHttpClient()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.Accepted, "{}");
		var httpClient = new HttpClient(handler);
		var client = new BetterStackHttpClient("src-token", httpClient);

		client.Dispose();

		var response = await httpClient.GetAsync(new Uri("https://s1.betterstackdata.com"));
		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
	}

	/// <summary>Configuration comes from the constructor, so this is a no-op.</summary>
	[Fact]
	public void ConfigureDoesNothing()
	{
		var (client, _) = Build();

		client.Configure(null!);
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw exception;
	}

	/// <summary>
	/// Records what was posted and answers with a fixed status.
	///
	/// <para>Nested here rather than FakeBetterStack, because these tests
	/// read the raw request rather than the events in it.</para>
	/// </summary>
	private sealed class StubHttpMessageHandler(
		Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder)
		: HttpMessageHandler
	{
		/// <summary>Every request that reached the handler, in order.</summary>
		public List<HttpRequestMessage> Requests { get; } = [];

		/// <summary>The bodies of those requests, in the same order.</summary>
		public List<string> Bodies { get; } = [];

		public static StubHttpMessageHandler Always(HttpStatusCode status, string body) =>
			new(_ => (status, body));

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			Bodies.Add(request.Content is null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

			var (status, body) = responder(request);

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
				RequestMessage = request,
			};
		}
	}
}
