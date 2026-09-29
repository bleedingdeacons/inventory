// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

using Serilog;

namespace TheBleedingDeacons.Inventory;

/// <summary>
/// Logs what would otherwise kill the process unrecorded, and gets it to disk
/// on the way down.
/// </summary>
/// <remarks>
/// Logging from a crash path must itself be crash-proof. If <c>Log.Fatal</c>
/// throws — a disposed pipeline, an enricher faulting on this particular
/// exception — the original crash must not be replaced by a logger crash, so
/// every handler here swallows its own failures.
/// </remarks>
public static class CrashLogging
{
	private static int _registered;

	/// <summary>
	/// Hook the process-wide handlers: unhandled exceptions and unobserved task
	/// exceptions. Idempotent — a second call does nothing.
	/// </summary>
	/// <remarks>
	/// Platform hooks, like Android's Java-side exceptions, are the platform
	/// package's; they call <see cref="ReportFatal"/>.
	/// </remarks>
	public static void Register()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			OnUnhandled(args.ExceptionObject, args.IsTerminating);

		TaskScheduler.UnobservedTaskException += (_, args) =>
			OnUnobserved(args.Exception);
	}

	/// <summary>
	/// Log an exception that is about to end the process, then close and flush
	/// with a bounded wait.
	/// </summary>
	/// <param name="exception">What is killing the process.</param>
	/// <param name="messageTemplate">The message to log it under.</param>
	public static void ReportFatal(Exception exception, string messageTemplate)
	{
		try
		{
			Log.Fatal(exception, messageTemplate);
		}
#pragma warning disable CA1031 // Never throw from a crash handler.
		catch (Exception)
#pragma warning restore CA1031
		{
			// The original crash is the story.
		}

		TryFlush();
	}

	/// <summary>
	/// Close and flush every sink with a bounded wait, never throwing.
	/// </summary>
	/// <remarks>
	/// <c>Log.CloseAndFlush()</c> is synchronous with no timeout, so a slow or
	/// unreachable endpoint could hold shutdown for as long as the HTTP client's
	/// timeout. Anything still on disk after the cap ships on the next launch —
	/// which is what the durable buffer is for. For shutdown and crash paths
	/// only: logging is off afterwards. Anywhere the process carries on, use
	/// <see cref="ILogShipper.Flush"/>.
	/// </remarks>
	/// <param name="timeout">How long to wait. Default 5 seconds.</param>
	public static void TryFlush(TimeSpan? timeout = null)
	{
		try
		{
			Task.Run(Log.CloseAndFlush).Wait(timeout ?? TimeSpan.FromSeconds(5));
		}
#pragma warning disable CA1031 // Never throw from a shutdown or crash path.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Nothing to do.
		}
	}

	/// <summary>
	/// The unhandled-exception handler: log it as fatal, then close and flush.
	/// </summary>
	internal static void OnUnhandled(object exceptionObject, bool isTerminating)
	{
		try
		{
			if (exceptionObject is Exception ex)
			{
				Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", isTerminating);
			}
			else
			{
				Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", exceptionObject);
			}
		}
#pragma warning disable CA1031 // Never throw from a crash handler.
		catch (Exception)
#pragma warning restore CA1031
		{
			// The original crash is the story.
		}

		TryFlush();
	}

	/// <summary>
	/// The unobserved-task handler: log it as an error and ship it, leaving
	/// logging running.
	/// </summary>
	internal static void OnUnobserved(Exception exception)
	{
		try
		{
			Log.Error(exception, "Unobserved task exception");
		}
#pragma warning disable CA1031 // Never throw from a crash handler.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Nothing to do.
		}

		// Deliberately not TryFlush. The app survives an unobserved task
		// exception, and CloseAndFlush would leave it running with logging off
		// for the rest of the session — trading one silent failure for a worse
		// one. The shipper's flush sends the event and keeps logging alive.
		try
		{
			LogShipper.Current?.Flush();
		}
#pragma warning disable CA1031 // Never throw from a crash handler.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Nothing to do.
		}
	}
}
