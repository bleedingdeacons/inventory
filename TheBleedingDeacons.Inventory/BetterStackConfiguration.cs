// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Text;
using System.Text.Json;

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// Where to ship logs: a Better Stack ingest endpoint and the source token
/// that writes to it.
/// </summary>
/// <remarks>
/// <para>What an app holds matters as much as whether it is valid. See
/// <see cref="ILogShipper.Reconfigure"/>: no configuration at all means
/// "not told yet" and holds logs on disk, while a configuration that is not
/// valid means "told not to ship" and drops them.</para>
/// </remarks>
public sealed class BetterStackConfiguration
{
	private string _endpoint = string.Empty;

	/// <summary>
	/// Gets or sets the source token Better Stack issued. A credential: it can
	/// only write to one log source, but whoever holds it can fill that source
	/// with anything.
	/// </summary>
	public string SourceToken { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets Better Stack's HTTP ingest endpoint.
	/// </summary>
	/// <remarks>
	/// <para>Normalised on the way in: a value with no scheme gets
	/// <c>https://</c>. That is not cosmetic. Better Stack's dashboard shows the
	/// ingest address as a bare hostname —
	/// <c>sNNNNNN.eu-central-1a.betterstackdata.com</c> — so that is what gets
	/// pasted into configuration, but <see cref="IsValid"/> needs an absolute
	/// URI and <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> refuses a
	/// bare hostname. Without this the configuration reads as invalid and the
	/// app ships nothing at all — silently, because not shipping is a legitimate
	/// state. Register carried exactly that bug until 1.2.2.</para>
	///
	/// <para>Normalised in the setter rather than at the point of use because
	/// the value arrives from JSON, from settings pages and from servers, and a
	/// fix applied at only some of those leaves the trap for the next path
	/// added.</para>
	/// </remarks>
	public string Endpoint
	{
		get => _endpoint;
		set => _endpoint = NormaliseEndpoint(value);
	}

	/// <summary>
	/// Whether this names somewhere logs can actually be shipped: a token, and
	/// an absolute https endpoint.
	/// </summary>
	/// <remarks>
	/// <b>https only.</b> The source token travels as a bearer credential on
	/// every batch, and <c>http://</c> would send it in cleartext without
	/// anybody saying so. Link had closed that; Register's copy, which 0.1.0
	/// was taken from, had not. An <c>http://</c> endpoint therefore reads as
	/// not valid, so a shipper told it drops what it holds rather than ship
	/// under a token anybody on the path can read.
	/// </remarks>
	/// <returns>True when both are present and the endpoint is an absolute https URI.</returns>
	public bool IsValid() =>
		!string.IsNullOrWhiteSpace(SourceToken)
		&& !string.IsNullOrWhiteSpace(Endpoint)
		&& Uri.TryCreate(Endpoint, UriKind.Absolute, out var parsed)
		&& string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

	/// <summary>
	/// A copy fit for a log line: the endpoint as is, the token masked.
	/// </summary>
	/// <returns>A new configuration whose token is <c>***</c>, or empty when there was none.</returns>
	public BetterStackConfiguration ToLogSafe() => new()
	{
		SourceToken = string.IsNullOrEmpty(SourceToken) ? string.Empty : "***",
		Endpoint = Endpoint,
	};

	/// <summary>
	/// Whether two configurations would ship to the same place under the same token.
	/// </summary>
	/// <param name="other">The configuration to compare with; null is never the same.</param>
	/// <returns>True when endpoint and token are both equal, ordinally.</returns>
	public bool SameAs(BetterStackConfiguration? other) =>
		other is not null
		&& string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
		&& string.Equals(SourceToken, other.SourceToken, StringComparison.Ordinal);

	/// <summary>
	/// This configuration as JSON, for a settings store.
	/// </summary>
	/// <remarks>
	/// Written by hand rather than through <see cref="JsonSerializer"/>'s
	/// reflection path, which a trimmed MAUI app switches off. The names are
	/// camelCase — <c>endpoint</c>, <c>sourceToken</c> — because that is what
	/// Link stored under <c>JsonSerializerDefaults.Web</c> before this library
	/// existed, so a handset upgrading keeps what it was told.
	/// </remarks>
	/// <returns>A JSON object with the two fields.</returns>
	public string ToJson()
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("endpoint", Endpoint);
			writer.WriteString("sourceToken", SourceToken);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <summary>
	/// Read a configuration written by <see cref="ToJson"/>, or by Link before it.
	/// </summary>
	/// <param name="json">The stored text.</param>
	/// <returns>The configuration, or null when the text is empty or is not a JSON object.</returns>
	public static BetterStackConfiguration? FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			var configuration = new BetterStackConfiguration();
			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (property.Value.ValueKind != JsonValueKind.String)
				{
					continue;
				}

				// Case-insensitive, as JsonSerializerDefaults.Web reads.
				if (string.Equals(property.Name, "endpoint", StringComparison.OrdinalIgnoreCase))
				{
					configuration.Endpoint = property.Value.GetString() ?? string.Empty;
				}
				else if (string.Equals(property.Name, "sourceToken", StringComparison.OrdinalIgnoreCase))
				{
					configuration.SourceToken = property.Value.GetString() ?? string.Empty;
				}
			}

			return configuration;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	/// Gives a scheme-less endpoint the <c>https://</c> it needs to parse as an
	/// absolute URI. Empty stays empty — that means "not configured", which must
	/// not become a bare <c>https://</c>.
	/// </summary>
	private static string NormaliseEndpoint(string? value)
	{
		var endpoint = (value ?? string.Empty).Trim();

		if (endpoint.Length == 0)
		{
			return string.Empty;
		}

		// "://" rather than a known scheme prefix keeps an explicit http://
		// working, and leaves alone anything carrying a scheme we don't know.
		return endpoint.Contains("://", StringComparison.Ordinal)
			? endpoint
			: "https://" + endpoint;
	}
}
