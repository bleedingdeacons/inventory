// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Enrichment;

/// <summary>
/// Adds an event's exception as flat, top-level properties —
/// <c>ExceptionType</c>, <c>ExceptionMessage</c>, <c>ExceptionStackTrace</c>
/// and <c>ExceptionInnerChain</c> — for aggregators like Better Stack that
/// filter on fields rather than dig into nested objects.
/// </summary>
/// <remarks>
/// Walks the whole inner-exception chain, <see cref="AggregateException"/>
/// branches included, so the context behind the outermost wrapper is not lost,
/// and demystifies stack traces so async and iterator frames read like the
/// source that produced them.
/// </remarks>
public sealed class ExceptionEnricher : ILogEventEnricher
{
	/// <summary>
	/// Async state-machine boilerplate typically eats the first 5–10 frames, so
	/// an earlier cap of 5 was effectively no useful frames at all. 40 shows the
	/// real call chain in deep async, LINQ and EF stacks while still bounding
	/// the payload.
	/// </summary>
	internal const int MaxStackTraceLines = 40;

	/// <summary>
	/// A guard against cycles. The inner chain is normally a tree, but
	/// flattening plus custom exception types have been known to loop.
	/// </summary>
	internal const int MaxInnerDepth = 10;

	/// <inheritdoc/>
	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		ArgumentNullException.ThrowIfNull(logEvent);
		ArgumentNullException.ThrowIfNull(propertyFactory);

		var ex = logEvent.Exception;
		if (ex == null)
		{
			return;
		}

		var demystified = ex.Demystify();

		logEvent.AddPropertyIfAbsent(
			propertyFactory.CreateProperty("ExceptionType", ex.GetType().FullName ?? ex.GetType().Name));

		logEvent.AddPropertyIfAbsent(
			propertyFactory.CreateProperty("ExceptionMessage", ex.Message));

		logEvent.AddPropertyIfAbsent(
			propertyFactory.CreateProperty("ExceptionStackTrace", TruncateStack(demystified.StackTrace)));

		var innerChain = BuildInnerChain(ex);
		if (!string.IsNullOrEmpty(innerChain))
		{
			logEvent.AddPropertyIfAbsent(
				propertyFactory.CreateProperty("ExceptionInnerChain", innerChain));
		}
	}

	/// <summary>
	/// A stack trace cut to <see cref="MaxStackTraceLines"/>, saying how much was cut.
	/// </summary>
	internal static string TruncateStack(string? stack)
	{
		if (string.IsNullOrEmpty(stack))
		{
			return string.Empty;
		}

		var lines = stack.Split('\n');
		if (lines.Length <= MaxStackTraceLines)
		{
			return stack;
		}

		return string.Join('\n', lines.Take(MaxStackTraceLines))
			+ string.Create(CultureInfo.InvariantCulture, $"\n  ... ({lines.Length - MaxStackTraceLines} more frames truncated)");
	}

	/// <summary>
	/// Every inner exception — and, for an <see cref="AggregateException"/>,
	/// every entry in <see cref="AggregateException.InnerExceptions"/> — as one
	/// string, each prefixed with its depth so it reads as a flat field.
	/// </summary>
	internal static string BuildInnerChain(Exception root)
	{
		var sb = new StringBuilder();
		AppendInners(sb, root, depth: 1, visited: new HashSet<Exception>(ReferenceEqualityComparer.Instance));
		return sb.ToString().TrimEnd();
	}

	private static void AppendInners(StringBuilder sb, Exception current, int depth, HashSet<Exception> visited)
	{
		if (depth > MaxInnerDepth)
		{
			sb.Append('[').Append(depth).AppendLine("] ... (inner exception depth limit reached)");
			return;
		}

		if (current is AggregateException agg)
		{
			foreach (var inner in agg.Flatten().InnerExceptions)
			{
				if (!visited.Add(inner))
				{
					continue;
				}

				AppendOne(sb, inner, depth);

				// The branch itself, not its inner exception. AppendInners
				// appends what is *underneath* what it is given, so handing it
				// the child started at the grandchild and dropped a level.
				// Flatten() only unwraps nested AggregateExceptions, so a branch
				// wrapping an ordinary exception — an HttpRequestException around
				// a TimeoutException, say — lost the part that said what went
				// wrong. Fixed in Link; Register's copy still had it.
				AppendInners(sb, inner, depth + 1, visited);
			}

			return;
		}

		var next = current.InnerException;
		if (next == null || !visited.Add(next))
		{
			return;
		}

		AppendOne(sb, next, depth);
		AppendInners(sb, next, depth + 1, visited);
	}

	private static void AppendOne(StringBuilder sb, Exception ex, int depth)
	{
		sb.Append('[').Append(depth).Append("] ")
			.Append(ex.GetType().FullName ?? ex.GetType().Name)
			.Append(": ")
			.AppendLine(ex.Message);

		var stack = ex.Demystify().StackTrace;
		if (!string.IsNullOrEmpty(stack))
		{
			sb.AppendLine(TruncateStack(stack));
		}
	}
}
