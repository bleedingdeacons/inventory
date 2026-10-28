using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Inventory.Enrichment;
using TheBleedingDeacons.Inventory.Redaction;
using TheBleedingDeacons.Inventory.Tests.Support;

namespace TheBleedingDeacons.Inventory.Tests;

/// <summary>
/// An app's redaction, applied to everything a sink writes: the properties,
/// including those Inventory's own enrichers add, and the exception's text.
/// </summary>
public sealed class RedactionTests
{
	// Stands in for an app's redaction: "secret" becomes "[x]". Returns the
	// same instance when there is nothing to replace, as a redaction must.
	private static string Redact(string text) =>
		text.Contains("secret", StringComparison.Ordinal) ? text.Replace("secret", "[x]", StringComparison.Ordinal) : text;

	private static LogEvent Event(string template, Exception? exception = null, params (string Name, LogEventPropertyValue Value)[] properties) =>
		new(
			DateTimeOffset.UnixEpoch,
			LogEventLevel.Error,
			exception,
			new MessageTemplateParser().Parse(template),
			properties.Select(p => new LogEventProperty(p.Name, p.Value)));

	[Fact]
	public void TheEnricherRedactsAPropertyAndSoTheRenderedMessage()
	{
		var logEvent = Event("Sent to {To}", properties: ("To", new ScalarValue("the secret one")));

		new RedactingEnricher(Redact).Enrich(logEvent, null!);

		Assert.Equal("Sent to \"the [x] one\"", logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
	}

	[Fact]
	public void TheEnricherRedactsInsideListsObjectsAndDictionaries()
	{
		var logEvent = Event(
			"{List} {Object} {Map}",
			properties:
			[
				("List", new SequenceValue([new ScalarValue("a secret"), new ScalarValue(3)])),
				("Object", new StructureValue([new LogEventProperty("To", new ScalarValue("secret"))], "Mail")),
				("Map", new DictionaryValue([new(new ScalarValue("secret key"), new ScalarValue("secret value"))])),
			]);

		new RedactingEnricher(Redact).Enrich(logEvent, null!);

		var rendered = logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture);
		Assert.DoesNotContain("secret", rendered, StringComparison.Ordinal);
		Assert.Equal("Mail", ((StructureValue)logEvent.Properties["Object"]).TypeTag);
	}

	[Fact]
	public void TheEnricherRedactsAnObjectThatPrintsTheSecret()
	{
		var logEvent = Event("{Thing}", properties: ("Thing", new ScalarValue(new Printed("my secret"))));

		new RedactingEnricher(Redact).Enrich(logEvent, null!);

		Assert.Equal("\"my [x]\"", logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
	}

	[Fact]
	public void TheEnricherLeavesOtherValuesAsTheyWere()
	{
		var number = new ScalarValue(42);
		var plain = new ScalarValue("nothing to hide");
		var logEvent = Event("{N} {S}", properties: [("N", number), ("S", plain)]);

		new RedactingEnricher(Redact).Enrich(logEvent, null!);

		Assert.Same(number, logEvent.Properties["N"]);
		Assert.Same(plain, logEvent.Properties["S"]);
	}

	/// <summary>
	/// The reason it runs last: what the exception enricher lifts out of an
	/// exception would otherwise reach the sinks as it was.
	/// </summary>
	[Fact]
	public void RunAfterTheExceptionEnricherItRedactsWhatThatAdded()
	{
		var logEvent = Event("Failed", new InvalidOperationException("the secret was refused", new IOException("inner secret")));

		new ExceptionEnricher().Enrich(logEvent, new Factory());
		new RedactingEnricher(Redact).Enrich(logEvent, null!);

		Assert.Equal("the [x] was refused", ((ScalarValue)logEvent.Properties["ExceptionMessage"]).Value);
		Assert.DoesNotContain("secret", ((ScalarValue)logEvent.Properties["ExceptionInnerChain"]).Value as string, StringComparison.Ordinal);
	}

	[Fact]
	public void TheSinkHandsOnTheExceptionAsRedactedTextWithItsTypeAndStack()
	{
		var collected = new CollectingSink();
		Exception thrown;
		try
		{
			throw new InvalidOperationException("the secret was refused");
		}
		catch (InvalidOperationException ex)
		{
			thrown = ex;
		}

		new RedactingSink(collected, Redact).Emit(Event("Failed", thrown));

		var exception = Assert.IsType<RedactedException>(Assert.Single(collected.Events).Exception);
		var text = exception.ToString();
		Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
		Assert.Contains("System.InvalidOperationException: the [x] was refused", text, StringComparison.Ordinal);
		Assert.Contains(nameof(TheSinkHandsOnTheExceptionAsRedactedTextWithItsTypeAndStack), text, StringComparison.Ordinal);
		Assert.Equal("System.InvalidOperationException", exception.OriginalType);
		Assert.Equal("the [x] was refused", exception.Message);
	}

	[Fact]
	public void TheSinkPassesAnEventWithoutAnExceptionStraightThrough()
	{
		var collected = new CollectingSink();
		var logEvent = Event("Nothing went wrong");

		new RedactingSink(collected, Redact).Emit(logEvent);

		Assert.Same(logEvent, Assert.Single(collected.Events));
	}

	[Fact]
	public void TheSinkKeepsTheEventsProperties()
	{
		var collected = new CollectingSink();

		new RedactingSink(collected, Redact).Emit(Event("{Id}", new InvalidOperationException("x"), ("Id", new ScalarValue(7))));

		Assert.Equal(7, ((ScalarValue)Assert.Single(collected.Events).Properties["Id"]).Value);
	}

	[Fact]
	public void WithoutARedactionTheSinksAreWrittenAsTheyWere()
	{
		var collected = new CollectingSink();
		using var logger = new LoggerConfiguration()
			.WriteToRedacted(null, wt => wt.Sink(collected))
			.CreateLogger();

		logger.Error(new InvalidOperationException("a secret"), "Failed");

		Assert.IsType<InvalidOperationException>(Assert.Single(collected.Events).Exception);
	}

	[Fact]
	public void WithARedactionEverySinkWrittenGetsTheRedactedException()
	{
		var first = new CollectingSink();
		var second = new CollectingSink();
		using var logger = new LoggerConfiguration()
			.WriteToRedacted(Redact, wt =>
			{
				wt.Sink(first);
				wt.Sink(second);
			})
			.CreateLogger();

		logger.Error(new InvalidOperationException("a secret"), "Failed");

		Assert.DoesNotContain("secret", Assert.Single(first.Events).Exception!.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("secret", Assert.Single(second.Events).Exception!.ToString(), StringComparison.Ordinal);
	}

	private sealed class Printed(string text)
	{
		public override string ToString() => text;
	}

	private sealed class Factory : Serilog.Core.ILogEventPropertyFactory
	{
		public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
			new(name, new ScalarValue(value));
	}
}
