using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.Enrichment;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// Exceptions as flat fields, whole chain included.
/// </summary>
public sealed class ExceptionEnricherTests
{
	private static LogEvent Enrich(Exception? exception)
	{
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow,
			LogEventLevel.Error,
			exception,
			new MessageTemplateParser().Parse("It broke"),
			[]);

		new ExceptionEnricher().Enrich(logEvent, new Factory());
		return logEvent;
	}

	private static string Text(LogEvent logEvent, string name) =>
		((ScalarValue)logEvent.Properties[name]).Value?.ToString() ?? string.Empty;

	private static Exception Thrown(Exception exception)
	{
		try
		{
			throw exception;
		}
		catch (Exception caught)
		{
			return caught;
		}
	}

	[Fact]
	public void AddsNothingWithoutAnException() =>
		Assert.Empty(Enrich(null).Properties);

	[Fact]
	public void AddsTheTypeMessageAndStack()
	{
		var logEvent = Enrich(Thrown(new InvalidOperationException("audio focus refused")));

		Assert.Equal(typeof(InvalidOperationException).FullName, Text(logEvent, "ExceptionType"));
		Assert.Equal("audio focus refused", Text(logEvent, "ExceptionMessage"));
		Assert.Contains(nameof(Thrown), Text(logEvent, "ExceptionStackTrace"), StringComparison.Ordinal);
		Assert.False(logEvent.Properties.ContainsKey("ExceptionInnerChain"));
	}

	[Fact]
	public void WalksTheInnerChain()
	{
		var logEvent = Enrich(new HttpRequestException("send failed", new IOException("socket closed", new TimeoutException("too slow"))));
		var chain = Text(logEvent, "ExceptionInnerChain");

		Assert.Contains("[1] System.IO.IOException: socket closed", chain, StringComparison.Ordinal);
		Assert.Contains("[2] System.TimeoutException: too slow", chain, StringComparison.Ordinal);
	}

	/// <summary>
	/// The regression Link fixed and Register still had: an aggregate's branch
	/// that wraps an ordinary exception lost the level that said what went wrong.
	/// </summary>
	[Fact]
	public void KeepsWhatAnAggregatesBranchWraps()
	{
		var logEvent = Enrich(new AggregateException(
			new HttpRequestException("send failed", new TimeoutException("the level that used to vanish"))));
		var chain = Text(logEvent, "ExceptionInnerChain");

		Assert.Contains("[1] System.Net.Http.HttpRequestException: send failed", chain, StringComparison.Ordinal);
		Assert.Contains("[2] System.TimeoutException: the level that used to vanish", chain, StringComparison.Ordinal);
	}

	[Fact]
	public void ListsEveryBranchOfAnAggregate()
	{
		var chain = Text(
			Enrich(new AggregateException(new InvalidOperationException("one"), new InvalidDataException("two"))),
			"ExceptionInnerChain");

		Assert.Contains("one", chain, StringComparison.Ordinal);
		Assert.Contains("two", chain, StringComparison.Ordinal);
	}

	[Fact]
	public void StopsAtTheDepthLimit()
	{
		Exception inner = new InvalidOperationException("bottom");
		for (var i = 0; i < ExceptionEnricher.MaxInnerDepth + 5; i++)
		{
			inner = new InvalidOperationException($"level {i}", inner);
		}

		var chain = Text(Enrich(inner), "ExceptionInnerChain");

		Assert.Contains("inner exception depth limit reached", chain, StringComparison.Ordinal);
		Assert.DoesNotContain("bottom", chain, StringComparison.Ordinal);
	}

	[Fact]
	public void TruncatesALongStackAndSaysSo()
	{
		var stack = string.Join('\n', Enumerable.Range(0, ExceptionEnricher.MaxStackTraceLines + 7).Select(i => $"   at Frame{i}()"));

		var truncated = ExceptionEnricher.TruncateStack(stack);

		Assert.Equal(ExceptionEnricher.MaxStackTraceLines + 1, truncated.Split('\n').Length);
		Assert.EndsWith("(7 more frames truncated)", truncated, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void TruncatesNothingToNothing(string? stack) =>
		Assert.Equal(string.Empty, ExceptionEnricher.TruncateStack(stack));

	/// <summary>An enricher set earlier in the pipeline wins.</summary>
	[Fact]
	public void DoesNotOverwriteAPropertyAlreadyThere()
	{
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow,
			LogEventLevel.Error,
			new InvalidOperationException("x"),
			new MessageTemplateParser().Parse("It broke"),
			[new LogEventProperty("ExceptionType", new ScalarValue("set earlier"))]);

		new ExceptionEnricher().Enrich(logEvent, new Factory());

		Assert.Equal("set earlier", Text(logEvent, "ExceptionType"));
	}

	[Fact]
	public void RejectsNulls()
	{
		Assert.Throws<ArgumentNullException>(() => new ExceptionEnricher().Enrich(null!, new Factory()));
		Assert.Throws<ArgumentNullException>(() => new ExceptionEnricher().Enrich(Enrich(null), null!));
	}

	private sealed class Factory : ILogEventPropertyFactory
	{
		public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
			new(name, new ScalarValue(value));
	}
}
