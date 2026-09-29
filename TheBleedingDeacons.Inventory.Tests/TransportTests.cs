using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.BetterStack;
using TheBleedingDeacons.Inventory.Maui;
using TheBleedingDeacons.Inventory.Sinks;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// The client the shipper makes when it is not given one, and the holding one.
/// </summary>
public sealed class TransportTests
{
	[Fact]
	public void FailsFastAndNamesItself()
	{
		using var client = BetterStackHttp.CreateClient("Register/1.3.0 (Android 14)");

		Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
		Assert.Equal("Register/1.3.0 (Android 14)", client.DefaultRequestHeaders.UserAgent.ToString());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not a (valid agent")]
	public void SendsNoAgentRatherThanABadOne(string? agent)
	{
		using var client = BetterStackHttp.CreateClient(agent);

		Assert.Empty(client.DefaultRequestHeaders.UserAgent);
	}

	[Fact]
	public async Task HoldingAnswersServiceUnavailableAndSendsNothing()
	{
		using var holding = new HoldingHttpClient();
		holding.Configure(null!);

		using var response = await holding.PostAsync("https://holding.invalid/", Stream.Null, CancellationToken.None);

		Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.False(response.IsSuccessStatusCode);
	}
}
