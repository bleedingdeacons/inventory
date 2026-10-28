// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog;
using Serilog.Configuration;

namespace TheBleedingDeacons.Inventory.Redaction;

/// <summary>Adding sinks behind an app's redaction.</summary>
public static class RedactionExtensions
{
	/// <summary>
	/// Adds the sinks <paramref name="configure"/> writes, each behind a
	/// <see cref="RedactingSink"/> when <paramref name="redact"/> is given,
	/// and exactly as written when it is null.
	/// </summary>
	/// <param name="configuration">The pipeline to add to.</param>
	/// <param name="redact">The app's redaction, or null for none.</param>
	/// <param name="configure">Writes the sinks, e.g. <c>wt =&gt; wt.File(...)</c>.</param>
	/// <returns>The pipeline.</returns>
	public static LoggerConfiguration WriteToRedacted(
		this LoggerConfiguration configuration,
		Func<string, string>? redact,
		Action<LoggerSinkConfiguration> configure)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(configure);

		if (redact is null)
		{
			configure(configuration.WriteTo);
			return configuration;
		}

		return configuration.WriteTo.Sink(LoggerSinkConfiguration.Wrap(sink => new RedactingSink(sink, redact), configure));
	}
}
