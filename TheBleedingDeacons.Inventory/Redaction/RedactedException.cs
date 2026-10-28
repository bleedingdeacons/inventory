// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

namespace TheBleedingDeacons.Inventory.Redaction;

/// <summary>
/// An exception as a sink prints it, with the app's redaction applied: the
/// original's type, message, stack and inner exceptions, as text.
/// </summary>
/// <remarks>
/// Made by <see cref="RedactingSink"/> for sinks only. It is never thrown,
/// and nothing but its text is meant to be read from it.
/// </remarks>
public sealed class RedactedException : Exception
{
	private readonly string _text;

	/// <summary>Initializes a new instance of the <see cref="RedactedException"/> class.</summary>
	/// <param name="original">The exception that was logged.</param>
	/// <param name="redact">The app's redaction.</param>
	public RedactedException(Exception original, Func<string, string> redact)
		: base(Redacted(original, redact, e => e.Message))
	{
		OriginalType = original.GetType().FullName ?? original.GetType().Name;
		_text = redact(original.ToString());
	}

	/// <summary>Gets the full name of the exception's own type.</summary>
	public string OriginalType { get; }

	/// <summary>The original's <see cref="Exception.ToString"/>, redacted.</summary>
	/// <returns>The text a sink prints.</returns>
	public override string ToString() => _text;

	private static string Redacted(Exception original, Func<string, string> redact, Func<Exception, string> part)
	{
		ArgumentNullException.ThrowIfNull(original);
		ArgumentNullException.ThrowIfNull(redact);
		return redact(part(original));
	}
}
