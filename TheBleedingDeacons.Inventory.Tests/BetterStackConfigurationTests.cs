namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// Where to ship, and the three things about it that have gone wrong before:
/// a bare hostname read as invalid, a token leaking into a log line, and a
/// device losing what it was told when the storage format changed.
/// </summary>
public sealed class BetterStackConfigurationTests
{
	private static BetterStackConfiguration Config(string endpoint = "s1.eu-central-1a.betterstackdata.com", string token = "src-token") =>
		new() { Endpoint = endpoint, SourceToken = token };

	/// <summary>
	/// Better Stack's dashboard shows a bare hostname. Pasted as it is, it must
	/// still ship — Register's dev builds shipped nothing for months over this.
	/// </summary>
	[Fact]
	public void GivesABareHostnameHttps()
	{
		var config = Config("  s1.eu-central-1a.betterstackdata.com ");

		Assert.Equal("https://s1.eu-central-1a.betterstackdata.com", config.Endpoint);
		Assert.True(config.IsValid());
	}

	[Theory]
	[InlineData("http://localhost:8080")]
	[InlineData("https://in.logs.example")]
	[InlineData("ftp://nope.example")]
	public void LeavesAnExplicitSchemeAlone(string endpoint) =>
		Assert.Equal(endpoint, Config(endpoint).Endpoint);

	/// <summary>Empty means "not configured"; it must not become a bare "https://".</summary>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void LeavesEmptyEmpty(string? endpoint) =>
		Assert.Equal(string.Empty, new BetterStackConfiguration { Endpoint = endpoint! }.Endpoint);

	[Theory]
	[InlineData("https://in.logs.example", "t", true)]
	[InlineData("s123456.betterstackdata.com", "t", true)]
	[InlineData("https://in.logs.example", "", false)]
	[InlineData("https://in.logs.example", "  ", false)]
	[InlineData("", "t", false)]
	[InlineData("ftp://in.logs.example", "t", false)]
	[InlineData("https://", "t", false)]
	public void IsValidOnlyWithATokenAndAnHttpsEndpoint(string endpoint, string token, bool valid) =>
		Assert.Equal(valid, Config(endpoint, token).IsValid());

	/// <summary>
	/// The token is a bearer credential on every batch; http would send it in
	/// cleartext. Link had closed this, and 0.1.0 reopened it.
	/// </summary>
	[Theory]
	[InlineData("http://in.logs.example")]
	[InlineData("http://localhost:9000")]
	public void RefusesToSendTheTokenInCleartext(string endpoint) =>
		Assert.False(Config(endpoint).IsValid());

	[Fact]
	public void MasksTheTokenForLogging()
	{
		var safe = Config().ToLogSafe();

		Assert.Equal("***", safe.SourceToken);
		Assert.Equal("https://s1.eu-central-1a.betterstackdata.com", safe.Endpoint);
		Assert.Equal(string.Empty, Config(token: string.Empty).ToLogSafe().SourceToken);
	}

	[Fact]
	public void IsTheSameOnlyWithTheSameEndpointAndToken()
	{
		Assert.True(Config().SameAs(Config()));
		Assert.False(Config().SameAs(Config(token: "other")));
		Assert.False(Config().SameAs(Config("https://elsewhere.example")));
		Assert.False(Config().SameAs(null));
	}

	[Fact]
	public void RoundTripsThroughJson()
	{
		var back = BetterStackConfiguration.FromJson(Config().ToJson())!;

		Assert.True(back.SameAs(Config()));
	}

	/// <summary>
	/// What Link stored with <c>JsonSerializerDefaults.Web</c> before this
	/// library existed. A handset upgrading must keep what it was told.
	/// </summary>
	[Fact]
	public void ReadsWhatLinkStoredBeforeIt()
	{
		var stored = """{"sourceToken":"src-token","endpoint":"https://s1.eu-central-1a.betterstackdata.com"}""";

		Assert.True(BetterStackConfiguration.FromJson(stored)!.SameAs(Config()));
	}

	[Fact]
	public void ReadsNamesInAnyCase() =>
		Assert.True(BetterStackConfiguration.FromJson("""{"SourceToken":"src-token","ENDPOINT":"s1.eu-central-1a.betterstackdata.com"}""")!.SameAs(Config()));

	[Fact]
	public void IgnoresFieldsItDoesNotKnowAndValuesThatAreNotText()
	{
		var read = BetterStackConfiguration.FromJson("""{"endpoint":42,"sourceToken":"t","extra":"x"}""")!;

		Assert.Equal(string.Empty, read.Endpoint);
		Assert.Equal("t", read.SourceToken);
	}

	/// <summary>
	/// Unreadable storage reads as "not told yet", which holds logs rather than
	/// losing them.
	/// </summary>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not json")]
	[InlineData("[1,2]")]
	[InlineData("\"a string\"")]
	public void ReadsNothingFromWhatIsNotAnObject(string? json) =>
		Assert.Null(BetterStackConfiguration.FromJson(json));

	[Fact]
	public void EscapesAwkwardTextInJson()
	{
		var config = Config("https://in.logs.example/?a=\"b\"", "to\\ken\n");

		Assert.True(BetterStackConfiguration.FromJson(config.ToJson())!.SameAs(config));
	}
}
