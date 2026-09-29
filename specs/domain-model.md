# Inventory: domain model

The vocabulary the README, the feature files and the code share, the
decisions that are settled, and what is still open.

## The core idea

A device logs to disk, always, and ships from disk when it knows where to.
Everything else — holding, flushing, rebuilding — follows from keeping those
two apart.

## Ubiquitous language

| Term | Meaning |
| --- | --- |
| **Pipeline** | One built Serilog logger: the app's base sinks and enrichers plus, at most, one Better Stack sink. Never changed once built; replaced. |
| **Base pipeline** | The part that never depends on where to ship: local files, IDE output, logcat, enrichers. Built fresh by a factory on every rebuild. |
| **Rebuild** | Build a new pipeline, install it as `Log.Logger`, then dispose the old one. What `Reconfigure` and `Flush` both do. |
| **Configuration** | Where to ship: a Better Stack endpoint and source token. |
| **Told** | Given a configuration by whatever decides — a settings page, or a server after sign-in. |
| **Holding** | Not told yet. Events go to the buffer and are never sent. |
| **Shipping** | Told, validly. Events go to the buffer and on to Better Stack. |
| **Local only** | Told not to ship. No Better Stack sink, and the buffer deleted. |
| **Buffer** | The durable sink's files on disk, shared by holding and shipping. |
| **Held** | In the buffer while holding. Shipped, with its own timestamp, once shipping. |
| **Flush** | A rebuild that changes nothing, done because disposing a pipeline ships its buffer. Debounced; only while shipping. |
| **Close** | `Log.CloseAndFlush()`. Flushes and leaves logging off. Crash and shutdown paths only. |
| **Current logger** | A logger that writes to whatever `Log.Logger` is at the moment of the call. What survives a rebuild. |
| **Stored answer** | The last configuration an app was told, kept so the next launch ships before reaching the network. |
| **Forget** | Drop the stored answer and hold again. At sign-out, or when the server refuses the device. |

## Locked decisions

- **Three answers, not two.** Null holds, valid ships, invalid drops.
  "Not told yet" and "told not to" are different facts and must not share a
  representation.
- **Holding and shipping share the buffer files.** That is the mechanism by
  which held events ship; it is not an optimisation.
- **Every event carries `dt`.** A late batch must keep its own chronology.
- **The transport never throws.** Failures are a 599 to the sink and a line
  in SelfLog.
- **Flush is a rebuild, not a close.** Logging must survive the event that
  caused the flush.
- **The old pipeline is disposed after the new one is installed.** There is
  no moment with nothing installed.
- **A failed rebuild keeps the running pipeline.**
- **`ILogger<T>` goes through `CurrentLogger`.** A logger bound to one
  pipeline loses everything after the first rebuild.
- **The buffer directory, file prefix and Link's store key are unchanged
  from the apps.** An upgrade ships what the device was already holding.
- **The stored configuration is written by hand as JSON.** A trimmed MAUI
  app switches reflection-based serialisation off.
- **`Inventory.Maui` targets net10.0 and net10.0-android only.** MAUI's
  net10.0 build serves the other heads; only Android adds anything.

## Assumptions still open to change

- **5 seconds** between shipments, **5 minutes** between holding ticks,
  **5 seconds** of flush debounce. Chosen in Register and Link, not
  measured.
- **2 × 1 MB** of holding, **16 × 8 MB** of shipping buffer. Bounded, but
  never tuned against a real device's storage or a real outage.
- **Information and up** in a release build's file. An app can lower it
  through `Configure`, but nothing asks whether it should.
- **One Better Stack source per app.** Nothing yet needs two.
