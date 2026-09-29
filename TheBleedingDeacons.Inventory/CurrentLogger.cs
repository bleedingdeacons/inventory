// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Serilog;
using Serilog.Core;
using Serilog.Core.Enrichers;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// A Serilog <see cref="ILogger"/> that writes to whatever <c>Log.Logger</c> is
/// <i>at the moment of each call</i>, not whatever it was when this was made.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Anything that holds on to a logger holds on to
/// one pipeline. <c>Log.ForContext&lt;T&gt;()</c> binds to the logger of the
/// moment, and so does Microsoft.Extensions.Logging's Serilog provider: with no
/// logger passed, each <c>ILogger&lt;T&gt;</c> it creates captures
/// <c>Log.Logger</c> once, and the logger factory caches it for the category.
/// A <see cref="LogShipper"/> replaces and disposes the pipeline on every
/// reconfigure and every flush, so every such logger made before then goes on
/// writing into a disposed pipeline — silently. In Register and Link that was
/// everything logged through <c>ILogger&lt;T&gt;</c>, the Unity and Freedom
/// clients among them, from the first rebuild onwards.</para>
///
/// <para>Hand this to <c>AddSerilog</c> — <c>UseInventory</c> does — and to
/// anything else that keeps a logger for the life of the process. Context added
/// through <c>ForContext</c> travels with it as enrichers and is applied on
/// every write, before the live pipeline's own.</para>
/// </remarks>
public sealed class CurrentLogger : ILogger
{
	private readonly ILogEventEnricher[] _enrichers;

	private CurrentLogger(ILogEventEnricher[] enrichers) => _enrichers = enrichers;

	/// <summary>
	/// Gets the logger with no context of its own.
	/// </summary>
	public static ILogger Instance { get; } = new CurrentLogger([]);

	/// <summary>
	/// A logger that adds one more enricher to every event.
	/// </summary>
	/// <param name="enricher">The enricher.</param>
	/// <returns>A new logger; this one is unchanged.</returns>
	public ILogger ForContext(ILogEventEnricher enricher)
	{
		ArgumentNullException.ThrowIfNull(enricher);
		return new CurrentLogger([.. _enrichers, enricher]);
	}

	/// <summary>
	/// A logger that adds these enrichers to every event.
	/// </summary>
	/// <param name="enrichers">The enrichers.</param>
	/// <returns>A new logger; this one is unchanged.</returns>
	public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
	{
		ArgumentNullException.ThrowIfNull(enrichers);
		return new CurrentLogger([.. _enrichers, .. enrichers]);
	}

	/// <summary>
	/// A logger that adds one property to every event.
	/// </summary>
	/// <param name="propertyName">The property's name.</param>
	/// <param name="value">Its value.</param>
	/// <param name="destructureObjects">Whether to capture its structure rather than its string.</param>
	/// <returns>A new logger; this one is unchanged.</returns>
	public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false) =>
		ForContext(new PropertyEnricher(propertyName, value, destructureObjects));

	/// <summary>
	/// Whether the live pipeline would write an event at this level.
	/// </summary>
	/// <param name="level">The level.</param>
	/// <returns>What the live pipeline says.</returns>
	public bool IsEnabled(LogEventLevel level) => Log.Logger.IsEnabled(level);

	/// <summary>
	/// Apply this logger's context, then write to the live pipeline.
	/// </summary>
	/// <param name="logEvent">The event.</param>
	public void Write(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		var target = Log.Logger;

		if (_enrichers.Length != 0)
		{
			var factory = new BindingPropertyFactory(target);
			foreach (var enricher in _enrichers)
			{
				enricher.Enrich(logEvent, factory);
			}
		}

		target.Write(logEvent);
	}

	/// <summary>
	/// Bind a template and its values, as the live pipeline would.
	/// </summary>
	/// <param name="messageTemplate">The template.</param>
	/// <param name="propertyValues">The values.</param>
	/// <param name="parsedTemplate">The parsed template.</param>
	/// <param name="boundProperties">The bound properties.</param>
	/// <returns>Whether binding succeeded.</returns>
	public bool BindMessageTemplate(
		string messageTemplate,
		object?[]? propertyValues,
		[NotNullWhen(true)] out MessageTemplate? parsedTemplate,
		[NotNullWhen(true)] out IEnumerable<LogEventProperty>? boundProperties) =>
		Log.Logger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);

	/// <summary>
	/// Bind a property, as the live pipeline would.
	/// </summary>
	/// <param name="propertyName">The name.</param>
	/// <param name="value">The value.</param>
	/// <param name="destructureObjects">Whether to capture structure.</param>
	/// <param name="property">The bound property.</param>
	/// <returns>Whether binding succeeded.</returns>
	public bool BindProperty(
		string? propertyName,
		object? value,
		bool destructureObjects,
		[NotNullWhen(true)] out LogEventProperty? property) =>
		Log.Logger.BindProperty(propertyName, value, destructureObjects, out property);

	/// <summary>
	/// Creates properties through a logger's own binding, so an enricher's values
	/// are captured with the live pipeline's destructuring policies.
	/// </summary>
	private sealed class BindingPropertyFactory(ILogger target) : ILogEventPropertyFactory
	{
		public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
			target.BindProperty(name, value, destructureObjects, out var property)
				? property
				: new LogEventProperty(name, new ScalarValue(value));
	}
}
