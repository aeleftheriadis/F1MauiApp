# Architecture

A cross-platform Formula 1 companion: historical timing, real circuit geometry, strategy, champions and
news, built from keyless public APIs. This document describes what the code is, not what it should become —
the backlog lives in `plan.md`, the reasoning in `adr.md`.

## Layers

```
src/F1App.Domain/        net11.0   pure records + closed hierarchies, no IO, no framework
src/F1App.Telemetry/     net11.0   providers (OpenF1, Jolpica, Wikimedia, RSS), state, caches
src/F1App.App/           MAUI      pages, view models, SkiaSharp surfaces, DI, theming
tests/F1App.Telemetry.Tests        xUnit over Domain + Telemetry
```

Dependencies point inward: `App → Telemetry → Domain`, and Domain references nothing. Every network call,
every database read and every parse lives in Telemetry, so the whole data path is unit-testable without a
device.

## Data flow

```
OpenF1 ─┐                    ┌─> TelemetrySnapshotStore ─> UI timer (15–30 Hz) ─> pages
        ├─> RequestGate ─> provider clients ─┤
Jolpica ┘   (per provider)                   └─> SQLite caches ─> pages
```

* **One gate per provider.** Free-tier budgets are per credential, so OpenF1, Jolpica, Wikimedia and the news
  feeds each own a `RequestGate` (token bucket: burst, then a sustained rate). A 429 from any provider
  penalises its own gate and returns `RateLimited`; nothing retries in a loop.
* **`FetchResult<T>` is a union** (`T | HttpFailure | RateLimited`). Providers cannot invent a fourth outcome
  and callers cannot ignore a failure — every `switch` over it is exhaustive.
* **The telemetry store is latest-value, not a queue.** One slot per driver, newest sample wins, stale
  sequences rejected. A queue drained at a fixed cadence accumulates latency without bound; a snapshot at
  most one frame old cannot.
* **Replay is paced by the samples' own timestamps**, so the consumer cannot tell it from a live feed.

## Screens

| Screen | Rests on |
| --- | --- |
| Season | Jolpica calendar via SQLite-backed `IRaceCalendarStore` |
| Weekend | OpenF1 `/sessions` for the current meeting + `WeekendPhase` closed hierarchy |
| Pit wall | classification (OpenF1 laps/position/intervals/stints/pit/drivers) + live replay samples |
| Timing | replay samples only |
| Track | circuit atlas, geometry from one car's recorded GPS lap |
| Results | six OpenF1 endpoints folded into `SessionTiming` |
| Strategy | `stints` + `drivers` |
| Compare | replay samples, speed against elapsed time |
| Paddock | session driver list + Wikimedia Commons portraits |
| Facts | Jolpica champions, persisted |
| News | ten public RSS/Atom feeds |
| Settings | theme, live-feed opt-in, diagnostics |

## State that survives a restart

| Table | Contents | Why persisted |
| --- | --- | --- |
| `races` | calendar per season | a screen that needs the network to open is not offline-first |
| `champions` | season champions | historical, changes once a year |
| `circuit_winners` | winners per circuit | historical, changes once a year |
| `media` | resolved Commons image + attribution | one request per subject, ever |

`Preferences` holds the theme mode and the live-feed opt-in.

## Theming

`Palettes.Dark` / `Palettes.Light` are the only sources of colour; `Themes.Current` answers with the palette
for the effective mode and `Themes.Changed` asks open screens to rebuild from `BuildContent()`. That is the
C# equivalent of a resource-dictionary swap, chosen because every screen here is built in code. Both
palettes are contrast-checked (light: primary 16.5:1, secondary 7.0:1, live-red text 6.4:1).

## Diagnostics

`Services/Diagnostics.cs` writes `logs/app.log` in app data and installs handlers for unhandled exceptions and
unobserved task exceptions; every page catch records what it swallowed. `F1APP_SMOKE=1` walks every
destination once and logs any page that throws — that is how the last three defects were found, including a
start-up failure that never reached a managed handler.

## Data sources and their limits

| Source | Used for | Constraint |
| --- | --- | --- |
| OpenF1 | sessions, location, car data, laps, intervals, stints, pit, position, drivers | anonymous; 3 req/s, 30 req/min (enforced); historical only — live needs a paid credential, which the project forbids |
| Jolpica | calendar, champions, circuit winners | anonymous; limit unpublished, self-limited to 120/min + burst 4, cached 24 h |
| Wikimedia Commons | driver photographs | anonymous; CC licences, attribution displayed |
| 10 RSS/Atom feeds | headlines as links | per publisher; Reddit needs a browser-shaped User-Agent |

Measured disagreements are recorded rather than papered over: for 2026 OpenF1 lists 25 race sessions and
Jolpica 23 (the two Jolpica lacks are 2026-04-12 and 2026-04-19), so OpenF1 owns the circuit list and
Jolpica supplies round numbers where it agrees. `CalendarReconciler` joins on date, never on name.

## Build

Central package management (`Directory.Packages.props`), lock files per project, warnings as errors,
`dotnet format --verify-no-changes` clean on the three non-MAUI projects. Verified targets:

```
dotnet build src/F1App.App -f net11.0-windows10.0.19041.0     # 0 warnings, 0 errors
dotnet build src/F1App.App -f net11.0-ios                    # 0 errors
dotnet test  tests/F1App.Telemetry.Tests                     # 92 passed
```

The Android target cannot be built on this machine (no SDK build-tools, `XA5205`).
