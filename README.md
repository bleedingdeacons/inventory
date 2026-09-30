# inventory

[![CI](https://github.com/bleedingdeacons/inventory/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/inventory/actions/workflows/ci.yml)
[![Semgrep](https://github.com/bleedingdeacons/inventory/actions/workflows/semgrep.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/inventory/actions/workflows/semgrep.yml)
[![Coverage Status](https://coveralls.io/repos/github/bleedingdeacons/inventory/badge.svg?branch=main)](https://coveralls.io/github/bleedingdeacons/inventory?branch=main)

Durable logging to [Better Stack](https://betterstack.com/logs) for .NET apps
that spend their lives in pockets and church halls:

- **Disk first.** Every event is written to a buffer file before anything
  is sent. A device with no signal buffers; one killed by the OS ships its
  backlog on the next launch.
- **Its own time.** Each event carries the moment it was logged, so a batch
  that arrives hours late still reads in the right order.
- **Held until told.** An app that learns where to ship only after signing
  in keeps its first minutes and ships them once it knows.
- **Errors at once.** An error goes out immediately rather than on the
  next tick of the timer.
- **Rebuilt in place.** Changing the endpoint or token replaces the
  pipeline without losing anything, including what `ILogger<T>` writes.

It is what Register and Link each had a copy of, taken out and made one
thing. Link's copy had grown the holding buffer, flush-on-error and the
logcat sink; Register's had not. This is Link's, plus a fix neither had.

This README is the specification, argued rather than listed. The
[domain model](specs/domain-model.md) has the vocabulary and the locked
decisions. The [feature files](TheBleedingDeacons.Inventory.Specs/Features)
are the executable half; where this and they disagree, they are what runs.

## In a MAUI app

```csharp
// MauiProgram.CreateMauiApp, first thing:
builder.UseInventory(new InventoryMauiOptions
{
    Application = "Register",
    Environment = "Production",
    DeveloperSinks = BuildInfo.IsDebug,                               // the app's DEBUG, not the library's
    Configure = cfg => cfg.ReadFrom.Configuration(builder.Configuration), // optional
});

// Later — from settings, or from a server after sign-in:
app.Services.GetRequiredService<ILogShipper>().Reconfigure(new BetterStackConfiguration
{
    Endpoint = "s1234567.eu-central-1a.betterstackdata.com",   // a bare hostname is fine
    SourceToken = token,
});
```

`UseInventory` does all of this:

- builds the logger;
- starts **holding**;
- hooks the crash handlers;
- routes `ILogger<T>` through the pipeline;
- registers `ILogShipper`.

Logging is running when it returns, so what goes wrong while the rest of
the app is built is on record.

`Inventory.Maui` has two builds:

- **`net10.0`** serves iOS, Mac Catalyst and Windows. MAUI's own packages
  ship a plain `net10.0` build beside the platform ones, so it works
  through the same Essentials and hosting APIs.
- **`net10.0-android`** adds the logcat sink and the Java-side crash hook.

## On its own

`Inventory` is plain `net10.0` and needs nothing from MAUI. Give it a
factory for your fixed pipeline and a buffer directory:

```csharp
using var shipper = new LogShipper(
    () => new LoggerConfiguration().WriteTo.Console(),
    new InventoryOptions { BufferDirectory = "/var/lib/myapp/log-buffer" });

CrashLogging.Register();
shipper.Reconfigure(null);        // hold
// …
shipper.Reconfigure(betterStack); // ship, held events included
```

[`example/Inventory-cli`](example/Inventory-cli/Program.cs) is exactly
that, runnable.

## Three answers, not two

`Reconfigure` takes the answer to "where do logs go?", and there are
three:

| Given | State | What happens |
| --- | --- | --- |
| `null` | **Holding** | Not told yet. Events go to a small buffer (2 × 1 MB) that is never sent, and wait. |
| a valid configuration | **Shipping** | Disk first, then Better Stack every 5 seconds. What was held goes too. |
| an invalid one | **LocalOnly** | Told not to ship. The Better Stack sink goes and the buffer is deleted, because nothing in it is going anywhere. |

Valid means a source token and an **https** endpoint; a bare hostname gets
`https://` on the way in. An `http://` endpoint is invalid, because the token
travels as a bearer credential on every batch and would go in cleartext.
Link had closed that gap; 0.1.0 reopened it by taking Register's rule, and
0.1.1 closes it again.

The difference between the first and the last is the point:

- **Not told yet** is a device that has not signed in. Its failures are
  the most interesting ones there are.
- **Told not to ship** is an intergroup that has no Better Stack source.

Treating them the same either loses the first or fills a phone with logs
nobody will ever read. Holding is bounded, and wakes only every 5 minutes,
so a device that is never configured costs nearly nothing.

Holding and shipping use **the same buffer files**. That is how held events
come to be shipped: the shipping sink picks up where the holding one left
off, and each event still carries its own timestamp.

## Disk first, with its own time

Better Stack reserves three fields and ignores the rest:

- **`dt`, the timestamp.** Without it Better Stack stamps an event with the
  moment the HTTP request arrived. After an evening offline, a whole
  meeting's log would collapse onto one instant. Serilog's own JSON
  formatter does not write `dt`.
- **`level`.** Serilog's names are mapped: Verbose→TRACE, Information→INFO,
  Warning→WARN.
- **`message`**, rendered.

Everything else is nested under `properties`, so no enricher can shadow the
three. The raw `messageTemplate` travels too, so events of one kind group
together. Batches are NDJSON: one event per line, so one bad row cannot
spoil a batch.

The transport never throws. The durable sink calls it from its background
loop and from its dispose path. An exception from the dispose path lands in
application shutdown; on Windows it showed up at window-destroy time. So:

- **every failure becomes a 599**, which the sink reads as "keep it and try
  later";
- **the cause goes to Serilog's SelfLog**, not to the app.

The HTTP client closes idle connections after 30 seconds. Otherwise the
final flush at shutdown lands on a socket the server has already
half-closed, and on Windows that is WinHttpException 12152.

## Errors ship at once

Nothing is lost by waiting, since the buffer is on disk. What waits is
*arrival*: the shipper runs on a timer, and if the process is killed first
the error waits for the next launch. On a handset, that is the difference
between seeing a fault tonight and whenever the app is next opened. If the
symptom is that messages stopped appearing, nobody opens the app to find
out why.

So any Error or Fatal triggers `ILogShipper.Flush()`:

- **It disposes the pipeline and rebuilds it.** Disposing is what flushes
  the durable sink.
- **It is debounced to one flush per 5 seconds.** One failure is usually
  logged by several layers on its way up.
- **It runs off the logging thread.** Rebuilding a pipeline from inside one
  of its own sinks is a deadlock waiting to happen.

`CrashLogging` covers the three ways a process dies unrecorded:

| Crash | What happens |
| --- | --- |
| Unhandled .NET exception | Logged as Fatal, then `Log.CloseAndFlush()` with a 5-second cap. |
| Java-side Android crash | Logged as Fatal (from `Inventory.Maui`), then `Log.CloseAndFlush()` with a 5-second cap. |
| Unobserved task exception | Logged as Error, then **flushed, not closed**. The app survives these, and closing would leave it running with logging off. |

Every handler swallows its own failures. A logger crash must never replace
the crash being reported.

## Rebuilding without losing a logger

A Serilog logger is fixed when it is built, so a new endpoint or token
means a new pipeline. `LogShipper` does the rebuild in a fixed order:

1. Compose the app's base pipeline plus the right sink, from scratch.
2. Install it as `Log.Logger`.
3. Only then dispose the old one.

That avoids two failures the naive version has:

- **Stacked sinks.** Every event goes out twice, the second time under the
  old token.
- **A leftover shipper** whose timer keeps posting to the old endpoint.

A rebuild that fails leaves the running pipeline alone and logs a warning
through it.

**There was a third failure, in both apps, that nobody had noticed.**
Anything holding a logger holds one pipeline. Microsoft.Extensions.Logging's
Serilog provider, given no logger, binds each `ILogger<T>` to the
`Log.Logger` of the moment it is created, and the logger factory caches it
per category. From the first rebuild on, every `ILogger<T>` made earlier
wrote into a disposed pipeline, silently. In Register and Link that
included the Unity and Freedom clients, and with flush-on-error every error
causes a rebuild.

`CurrentLogger` fixes it. It is a Serilog `ILogger` that writes to whatever
`Log.Logger` is at the moment of each call, carrying its `ForContext`
enrichers with it. `UseInventory` hands it to `AddSerilog`. Use it too for
anything that keeps a logger for the life of the process, in place of a
`Log.ForContext<T>()` stored in a static field. `CurrentLoggerTests` keeps
a test that shows the original failure.

## Settings you are told, not built with

A token in an app's settings file is in every copy of the package, readable
by anyone who unzips it, and replacing it takes a release. A server can
instead:

- hand it only to a device that has signed in;
- refuse a revoked one;
- change or withdraw it at will.

Link is told by Fellowship's `/logging`, and Register by Freedom.

`ShippingSettings` is the part of that which is not the fetching:

```csharp
var settings = new ShippingSettings(new SecureStorageLoggingSettingsStore("link_logging"), shipper);

await settings.ApplyStoredAsync();        // at launch, before the network: ship with what was last told
await settings.ApplyAsync(answer);        // each answer; a no-op when it has not changed
await settings.ForgetAsync();             // sign-out or refusal: forget, and hold again
```

The rules it follows:

- **No answer at all is not a reason to do anything.** Offline, or a
  server having a moment: keep shipping, keep what is held.
- **The answer is stored** so that a phone woken by a push, or opened with
  no signal, ships at once.
- **The store is SecureStorage, not Preferences**, because the token is a
  credential. It can only write to one log source, but whoever holds it
  can fill that source with anything.

The stored JSON is `{"endpoint","sourceToken"}`, what Link wrote before
this library existed, so a handset upgrading keeps what it was told.

## What every event carries

`UseInventory` adds these to every event:

- `Application` and `Environment`, from the options;
- `Platform`, `PlatformVersion`, `DeviceModel` and `DeviceName`;
- `AppVersion`: the file version on Windows, where an unpackaged app's
  `AppInfo.VersionString` is no help;
- `ProcessId`;
- `DeviceLabel`: a user-set label from Preferences `device_label`, the key
  Register, Hand and Link share. Failing that, the machine name on desktop,
  and otherwise maker, model and OS (`LENOVO TB330FU (Android 14)`).
  `Environment.MachineName` says `localhost` on Android, so it tells
  devices apart nowhere that matters.

Anything logged with an exception also gets these flat fields:

- `ExceptionType`, `ExceptionMessage` and `ExceptionStackTrace`, with the
  stack demystified and capped at 40 frames;
- `ExceptionInnerChain`: the whole inner chain, `AggregateException`
  branches included.

Register's copy of the enricher dropped a level under each aggregate
branch, so an `HttpRequestException` hid the `TimeoutException` that said
what went wrong. Link had fixed that, and this is Link's.

## Local files

| Build | Level | File | Kept | Also |
| --- | --- | --- | --- | --- |
| `DeveloperSinks = false` | Information+ | `logs/<app>-.log`, daily | 7 days | — |
| `DeveloperSinks = true` | Debug+ | `logs/<app>-debug-.log`, daily | 21 days | IDE output; console on Windows and Mac; **logcat** on Android |

Both files are opened `shared`, since Link's push service writes to them
too.

Logcat is under the app's own tag (`adb logcat -s Link:V`), not
`app_process64`. That is the runtime's tag, shared with every managed
message on the device.

Not the console sink on a phone either: it sets `Console.ForegroundColor`,
which throws on Android and iOS, so every event would end up in SelfLog.

## Coming from Register or Link

Both apps upgrade in place without dropping anything they were holding:

- **Buffer.** Its directory is `logs/betterstack-buffer` under the app's
  data directory, and its files are named `buffer`, both unchanged. What a
  device held before the upgrade ships after it.
- **Link's stored settings.** Its store key is `link_logging`, unchanged.

What goes from each app:

- `BetterStackLoggerController` and `IBetterStackLoggerController`, now
  `LogShipper` and `ILogShipper`;
- `Support/BetterStackDurable/*` and `ExceptionEnricher`;
- `FlushOnErrorSink`, `LogcatSink` and `HoldingHttpClient`;
- the `SetupSerilog` / `BuildBaseLoggerConfiguration` /
  `ResolveDeviceLabel` / `RegisterGlobalExceptionHandlers` /
  `TryFlushLogs` block in `MauiProgram`;
- the keyed "betterstack" `HttpClient`;
- the app's own `BetterStackConfiguration`.

In Link, `RemoteLogging` shrinks to the fetching; `ShippingSettings` is the
rest.

## Project layout

| Project | Target | What |
| --- | --- | --- |
| `TheBleedingDeacons.Inventory` | net10.0 | Package **Inventory**: `LogShipper`, the Better Stack wire, `CurrentLogger`, `CrashLogging`, `ShippingSettings` |
| `TheBleedingDeacons.Inventory.Maui` | net10.0; net10.0-android | Package **Inventory.Maui**: `UseInventory`, `DeviceLabel`, the SecureStorage store, `LogcatSink` |
| `TheBleedingDeacons.Inventory.Tests` | net10.0 | xUnit v3: the edges and the wire, against real buffer files. Drives the coverage badge. |
| `TheBleedingDeacons.Inventory.Specs` | net10.0 | Reqnroll: the behaviour, in the words of this README. A CI gate, not in coverage. |
| `example/Inventory-cli` | net10.0 | The standalone proof |

**Both test projects run against the real thing:** a real `LogShipper`,
the real durable sink and real buffer files in a temp directory. Only
Better Stack is faked, as `Tests/Support/FakeBetterStack.cs`, which the
Specs project links rather than copies. Both run one test at a time, since
Serilog's `Log.Logger` is one static per process.

## What is not done

- **A remote kill for a noisy device.** Told-not-to-ship is per app, not per
  device.
- **Sampling or rate limits.** A device that logs in a loop fills its
  128 MB of buffer and then drops the oldest.
- **iOS specifics.** An iOS app gets everything but a native log sink; the
  `os_log` equivalent of the logcat sink has not been written.
- **A size cap on the local files**, as opposed to a day count.

## Building and releasing

```bash
dotnet build TheBleedingDeacons.Inventory.sln        # needs the maui-android workload
dotnet run --project TheBleedingDeacons.Inventory.Tests
dotnet run --project TheBleedingDeacons.Inventory.Specs
dotnet format TheBleedingDeacons.Inventory.sln --verify-no-changes
```

Releases are freedom-sharp's:

- `./scripts/release.ps1 0.1.0` bumps both packages' `<Version>`, commits,
  tags `v0.1.0` and pushes.
- The tag runs `release.yml`. It re-runs the gates, then publishes
  **Inventory** and **Inventory.Maui** to GitHub Packages and to a GitHub
  Release.
- It publishes with the workflow's own `GITHUB_TOKEN`, so there is no PAT
  to keep alive.

To install:

1. Add the GitHub Packages feed to `nuget.config` with a token that has
   `read:packages`.
2. Map `Inventory` and `Inventory.*` to it.
3. Reference `Inventory.Maui`, which brings `Inventory` with it.

## License

MIT © The Bleeding Deacons
