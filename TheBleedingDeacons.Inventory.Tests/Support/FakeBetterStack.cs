using System.Net;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>
/// Better Stack's ingest endpoint, as far as a shipper can tell: records every
/// POST — its URI, its bearer token and each NDJSON line — and answers with
/// whatever status it is told to.
/// </summary>
/// <remarks>
/// Linked into the Specs project too, so both suites stand on the same
/// double. The shipper posts from a background loop, so everything here is
/// locked.
/// </remarks>
public sealed class FakeBetterStack : HttpMessageHandler
{
	private readonly Lock _gate = new();
	private readonly List<Received> _received = [];
	private readonly Queue<HttpStatusCode> _scripted = [];

	/// <summary>Gets or sets the status answered once any scripted ones run out.</summary>
	public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;

	/// <summary>Gets the number of POSTs that reached here, accepted or not.</summary>
	public int Posts
	{
		get
		{
			lock (_gate)
			{
				return _received.Count;
			}
		}
	}

	/// <summary>Gets every event that arrived in a POST that was accepted.</summary>
	public IReadOnlyList<ShippedEvent> Shipped
	{
		get
		{
			lock (_gate)
			{
				return _received.Where(r => r.Accepted).SelectMany(r => r.Events).ToList();
			}
		}
	}

	/// <summary>Gets every POST, in order.</summary>
	public IReadOnlyList<Received> Received
	{
		get
		{
			lock (_gate)
			{
				return _received.ToList();
			}
		}
	}

	/// <summary>Answer the next POSTs with these statuses, in order, then <see cref="Status"/>.</summary>
	public void Then(params HttpStatusCode[] statuses)
	{
		lock (_gate)
		{
			foreach (var status in statuses)
			{
				_scripted.Enqueue(status);
			}
		}
	}

	/// <summary>The events that arrived whose message contains this text.</summary>
	public IReadOnlyList<ShippedEvent> Containing(string text) =>
		Shipped.Where(e => e.Message.Contains(text, StringComparison.Ordinal)).ToList();

	/// <summary>The one POST that carried an event whose message contains this text.</summary>
	public Received PostCarrying(string text) =>
		Received.Single(r => r.Accepted && r.Events.Any(e => e.Message.Contains(text, StringComparison.Ordinal)));

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
		var events = body
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(ShippedEvent.Parse)
			.ToList();

		HttpStatusCode status;
		lock (_gate)
		{
			status = _scripted.Count > 0 ? _scripted.Dequeue() : Status;
			_received.Add(new Received(
				request.RequestUri!,
				request.Headers.Authorization?.Parameter,
				request.Content?.Headers.ContentType?.MediaType,
				events,
				(int)status is >= 200 and < 300));
		}

		return new HttpResponseMessage(status) { RequestMessage = request };
	}
}
