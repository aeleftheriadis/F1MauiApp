# Architecture decision records

Numbered, append-only. Each records the decision that was made, the evidence behind it, and what it costs.
A decision here is only superseded by a later entry, never edited away.

---

## ADR-001 — Only keyless, anonymous public APIs

**Status:** accepted

**Context.** The app is sideloaded and open source, so it cannot keep a secret. Several attractive F1 data
products (live timing, high-resolution telemetry) are behind paid tiers.

**Decision.** Every data source must be public, free and anonymous: no key, no OAuth, no account, no credit
card. A capability that exists only behind a paid tier is not implemented — it is modelled as a port and left
unfulfilled.

**Evidence.** OpenF1's free tier serves history from 2023 onwards at 30 req/min with no key; real-time needs a
paid credential and an OAuth2 token, and OpenF1's own documentation says a client should never embed one.

**Consequences.** No live timing, no team radio, no onboard video. In exchange there is no credential to leak,
no billing surface, and the app works for anyone who clones it.

---

## ADR-002 — Nothing on screen is generated

**Status:** accepted

**Context.** The prototype for this app simulated 20 cars on a hand-drawn Las Vegas layout.

**Decision.** Delete the simulator. Coordinates, speeds, gaps, positions and pictures all come from payloads.
Where the data is missing the row shows an em dash and the screen says why. A simulated source may exist only
as a clearly labelled offline fixture.

**Evidence.** OpenF1's `/location` returns a real circuit trace: one car's 150 s of GPS closes a 5.31 km loop at
Bahrain (the venue is 5.412 km), and a 30 s window yields 2 380 samples across 20 cars.

**Consequences.** The Track screen cannot draw a circuit until a session is loaded, and the app is useless
before its first successful fetch. Both are visible states, not failures.

---

## ADR-003 — Latest-value telemetry store, not a queue

**Status:** accepted

**Context.** The original design pushed frames into an unbounded `Channel` drained by a fixed-cadence timer.

**Decision.** One slot per driver in `TelemetrySnapshotStore`; the writer keeps the newest sample and rejects
stale sequences; readers take a copy on a render tick.

**Evidence.** Free-tier OpenF1 emits ~3.7 Hz per car, roughly 75 messages a second in total, and a queue
drained at 30 FPS is behind by construction and grows without bound whenever the producer outruns the drain.
Measured cost of a 20-driver read: 309 ns, 0.0009 % of a 33 ms frame budget.

**Consequences.** No ordering guarantees beyond sequence numbers, and no history: charts that need a window
keep their own `RingBuffer`.

---

## ADR-004 — All UI in C#, no XAML

**Status:** accepted

**Context.** MAUI templates default to XAML, and the prototype's visual language is dense custom chrome.

**Decision.** Pages are built in C# with object initialisers and small `Surfaces` helpers. No `.xaml` files
exist in the app project apart from the Windows platform stub.

**Consequences.** A typo in a colour or a property is a compile error, and bindings use compiled paths; there
is no XAML hot reload, so layout iteration needs a rebuild. Theming has no resource dictionary to swap, which
is why ADR-008 exists.

---

## ADR-005 — SkiaSharp `SKCanvasView` (software), pre-rendered geometry

**Status:** accepted

**Context.** Two surfaces are custom-drawn: the circuit map and telemetry traces.

**Decision.** Use `SKCanvasView`, rebuild the path only when a new outline arrives, and keep per-frame work
proportional to the markers rather than the path.

**Evidence.** Microsoft's own documentation states `SKCanvasView` is software-rendered and `SKGLView` is the
GPU one; `SKGLView` has no Mac Catalyst handler. A 340-point outline is rebuilt once per session, not per frame.

**Consequences.** Software rasterisation on mobile — acceptable at this geometry density, and measured
before anything else changes: the trigger to move to `SKGLView` is a frame above 16 ms, not a hunch.

---

## ADR-006 — Hand-written SQL with `Microsoft.Data.Sqlite`, no ORM

**Status:** accepted

**Context.** Three caches: calendar, facts and media metadata. All are read-mostly and rebuildable.

**Decision.** Hand SQL with parameterised statements, schema created on open, one connection per call.

**Consequences.** No model-building cost at startup and no trim/AOT friction from an ORM; migrations are manual
(`PRAGMA user_version`). Every statement is parameterised — there is no string-built SQL anywhere.

---

## ADR-007 — Session-driven replay, one replay at a time

**Status:** accepted

**Context.** Earlier builds pinned the app to a hardcoded 2024 window while the atlas showed 2026.

**Decision.** `SessionRunner` owns one replay. `SwitchToAsync` cancels the current load or stream, clears the
store and starts again under a semaphore, so two quick taps cannot interleave. On start-up a
`DefaultSessionResolver` picks the current weekend's latest finished session, else the most recent completed
race of the season, else the recorded fallback.

**Evidence.** On 2026-11-15 the current meeting (Las Vegas) had no finished session, so the resolver selected
Interlagos 2026-11-08 (session 11412) — verified live.

**Consequences.** Screens that show replay data are coupled to one current session; comparing two sessions at
once is not possible.

---

## ADR-008 — Theming by palette swap, not `AppThemeBinding`

**Status:** accepted

**Context.** ADR-004 removed resource dictionaries, so `AppThemeBinding`/`DynamicResource` have nothing to bind
to. Users are expected to want a light theme in daylight.

**Decision.** `Palettes.Dark` and `Palettes.Light` hold every colour under role names; `Themes.Current` resolves
the active palette from `UserAppTheme` (or the OS, or a saved preference) and `Themes.Changed` tells screens to
rebuild from `BuildContent()`.

**Evidence.** Contrast is computed, not eyeballed — light: primary 16.5:1, secondary 7.0:1, live-red text 6.4:1,
sector colours 7.1:1 and 5.0:1; dark: 18.8/7.7/6.5. Android declares `ConfigChanges.UiMode`, so an OS switch
does not restart the activity.

**Consequences.** A theme switch rebuilds the visible screen and loses scroll position. In exchange there is no
stale-colour class of bug, because nothing caches a colour on an element.

---

## ADR-009 — Formula 1's SignalR hub is observe-only, off by default

**Status:** accepted

**Context.** There is exactly one keyless live-timing endpoint: `livetiming.formula1.com/signalrcore`. It is
undocumented, F1's terms are unlikely to bless a third-party client, and its payload shapes are unverifiable
outside a session.

**Decision.** Wrap it in `OfficialLiveTimingClient`, which connects, subscribes and **reports what it saw**. It
never feeds the store. The Settings toggle defaults to off. Received frames are captured to
`live-frames.jsonl` so the first live session produces the fixture the parser needs.

**Evidence.** Negotiate answers 200 with a connection token, the WebSocket connects, the handshake is
acknowledged; 15 s of subscription produced zero frames off-session.

**Consequences.** The one interesting non-open source exists but is inert. `TimingDataParser` is unit-tested
against a hand-built fixture and its output is reported as a count, never rendered.

---

## ADR-010 — Freely licensed media from Wikimedia Commons, with attribution

**Status:** accepted

**Context.** The prototype's paddock is built on driver portraits, helmet galleries and car photography. Only
the first has a source that fits ADR-001.

**Decision.** Query Commons through its public API for portraits, cache the resolved URL with its author and
licence in SQLite, display the attribution under every picture, and state plainly when nothing free exists.

**Evidence.** A query for "Lewis Hamilton 2024" returns `File:Lewis Hamilton 2024 Chinese GP.jpg`, CC BY-SA 4.0,
with the author; the API needs no key. Results are not reliably recent (a plain "Max Verstappen" query returned
a 2017 photo), so the current year is tried first and the file title is shown alongside the attribution.

**Consequences.** Helmet galleries, livery studio shots and corner photograph galleries are not implemented;
those panels say so. Commons itself gets one request per subject, ever.

---

## ADR-011 — Lazy page construction for sections with heavy dependencies

**Status:** accepted

**Context.** Adding the paddock page as a constructor dependency of `App` broke start-up. Two separate causes
were found: an unregistered `Func<T>` factory (DI threw before any managed handler could log), and a missing
`RequestGate` registration that only surfaced when the page was first navigated to.

**Decision.** Destinations whose pages touch media or caches are added to the shell as a `DataTemplate` over a
DI-backed factory, so the page is built on first navigation. Start-up constructs only what the first screen needs.

**Consequences.** A broken section can no longer take down the shell, and start-up is measurably cheaper. The
cost is that a failure appears when the tab is opened rather than at launch — which the `F1APP_SMOKE` walk
covers.

---

## ADR-012 — Failures are logged to a file, and swallowed exceptions are recorded

**Status:** accepted

**Context.** An exception that reached the user left no trace, because `AddDebug()` only writes to the debugger.

**Decision.** `Diagnostics` installs a file logger plus handlers for unhandled and unobserved exceptions, writing
`logs/app.log`. Every page `catch` records the exception with its screen name. `F1APP_SMOKE=1` walks all
destinations and logs per-page failures.

**Evidence.** Three defects were located this way: a `NullReferenceException` inside
`SemanticScreenReader.Announce` on unpackaged WinUI (which re-threw from a catch block and became an unobserved
task exception), a font probe erroring on every label, and the two DI failures in ADR-011.

**Consequences.** The app writes to disk on the user's device; the log holds no personal data, only provider
status, counts and stack traces.
