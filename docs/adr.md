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

## ADR-013 — The archive is one request per season, persisted per season, and its records are computed

**Status:** accepted

**Context.** "All seasons, all races" reads as thousands of requests: Jolpica's per-race results endpoints
would cost ~7 400 requests for 1950→today. The user asked for the full archive on keyless sources, and
Jolpica self-budgets to bursts of four.

**Decision.** The vault syncs with one request per season: `/{season}/results/1.json` answers with the whole
season's races and their first-place results (measured against the live endpoint for 1950, 2023 and 2026).
Rows are written per season into `race_vault`, so an interrupted sync resumes at the first missing season
instead of starting over; past seasons are fetched once, the current season refreshes when a day old. The
record book (all-time wins by driver and constructor) is computed from the cached rows, never fetched.

**Evidence.** First live sync filled 989 races (1950-2020) before the process was stopped; the second run
added only the missing seasons, ending at 1,147 races across 76 seasons with the all-time win counts matching
the published record (Hamilton 106, Schumacher 91; Ferrari 246). Unit tests pin the same behaviour: parse
shape, per-season resume, failure reporting, and record computation.

**Correction (same day).** The Indianapolis 500 is championship-scoring 1950-1960 and Jolpica lists it among
each season's races; it is not a Grand Prix, so the vault now drops it at parse time (`circuitId ==
"indianapolis"` **and** season <= 1960, because the 2000-2007 US GP shares that circuit id) and a v3 schema
migration deletes the eleven rows an already-synced device holds. Measured after the migration: 11 rows
removed, the 8 US GPs kept, 0 duplicates, 1 158 races across 77 seasons.

**Consequences.** The first open of the Archive screen is the only one that pays for the network (~78
requests under a minute at burst four); every later open reads SQLite. A season that fails is skipped and
reported, not retried until the next open. "All details" per race (full classification, qualifying, pit
stops) is *not* what this stores - one row per race carries the winner; anything else stays on the Results
and Strategy screens, which have per-session data OpenF1 only publishes from 2023.

## ADR-014 — Platform navigation per idiom: bottom tabs on phones, flyout on desktop

**Status:** accepted

**Context.** Every screen was a `FlyoutItem` group holding one `Tab`, which on a phone is a hamburger menu -
a pattern iOS and Android users read as a web app, not a native one. The prototype that set the visual target
has five primary tabs.

**Decision.** `DeviceInfo.Idiom == Phone` selects a `TabBar` with five destinations (Season, Pit wall, Archive,
Paddock, More); the remaining nine screens are `Routing.RegisterRoute` entries pushed from `MorePage`, which is
generated from one table (`AppShell.Secondary`). Desktop and Mac keep the flyout, which is the desktop idiom for
a dozen screens. Routed pages are created by `ServicesRouteFactory` from the container, because the default
`Activator` path requires a parameterless constructor and every page takes its view model from DI.

**Evidence.** `F1APP_PHONE=1` forces the phone shell inside a desktop build, so the walk exercises it: 5 tabs and
9 pushed routes, 23 navigations, zero page failures. The desktop walk covers its 13 destinations, also clean.
The first attempt pushed `more/{route}` and every route failed with "Relative routing to shell elements is not
currently supported" - a registered route is pushed by its plain name, which the walk then verified.

**Consequences.** Two navigation wirings to keep, but one page set, one route table and one walk. A screen added
to `Secondary` appears in the More list automatically; a screen added as a tab needs a deliberate decision, which
is the point. The phone shell has not been seen on a phone - the forced-shell walk proves wiring, not appearance.

## ADR-015 — Teammate duels come from paged season classifications, counted only where both cars were classified

**Status:** accepted

**Context.** "Teammate head-to-head" is the first Paddock stat that is not a photo or a roster row, and it needs
every race of a season at driver level. Measured: `/{season}/results.json` returns 479 rows for 2024, and
Jolpica clamps `limit` to 100 (asking 500 or 1000 still answers 100), so a season is five paged requests.
`/{season}/results/1.json` - the archive's endpoint - carries only the winner, so it cannot answer this.

**Decision.** A `JolpicaSeasonResultsClient` pages the season and the rows land in `season_results` with a
24-hour TTL; `TeammateDuelService` computes duels from those rows. A pair is only compared when both cars of
one constructor finished with a numeric position, and the result is emitted both ways round so either driver can
be shown. Average position delta is a double division from the driver's own point of view (positive = ahead).

**Evidence.** Live run: 440 classifications cached for 2026. The computed numbers are symmetric and plausible -
Verstappen 16-4 over Hadjar with +1.7 places and 15-5 on grid; Norris 12-8 over Piastri; Hamilton/Leclerc 10-10
with deltas of exactly opposite sign. Two of these tests failed first and found real defects: integer division
reported every fractional average as 0, and the duel was keyed on a driver id that no open-f1 driver code
matches (both fixed).

**Consequences.** Five requests per season, once a day - far inside the self-imposed budget. A season whose page
fails is reported as unavailable rather than counted over the pages that succeeded: half a season would be a
wrong number, not a smaller one. Qualifying comparison uses the grid, which is the only qualifying data in the
classifications payload; real Q1/Q2/Q3 gaps would need the qualifying endpoint at one request per race.

## ADR-016 — Provider-supplied images come first, with the licence stated on screen

**Status:** accepted

**Context.** The OpenF1 payloads carry image URLs the app was ignoring: `drivers.headshot_url` (21 of 22 drivers
in the 2026 Las Vegas session), `meetings.circuit_image` and `meetings.country_flag`. Until now the Paddock
searched Commons for a portrait and the Track had no circuit artwork at all. Those URLs are Formula 1 media,
which is not an open licence - unlike every other source in this project.

**Decision.** The provider's headshot is shown first and labelled "Portrait: Formula 1 media, via OpenF1";
Commons stays the fallback and keeps its author-and-licence caption. The Track header shows the country flag
and the official track-layout icon from `meetings` (one extra request per season, best effort), credited
"track icon and flag: Formula 1 media, via OpenF1". The Commons aerial photograph stays on the same screen
with its full CC attribution.

**Evidence.** All three URL classes answered `200 image/png` with no key: headshot 93x93 (11 KB), circuit icon
960x720 (17 KB), flag 1000x563 (2.4 KB). `meetings?year=2026` returned 27 meetings, all with a circuit image.
The catalogue's extra request is one per season and is ignored when it fails, so a picture never blocks a map.

**Consequences.** Images are displayed as the provider serves them - never copied, cropped or bundled - and
every screen that shows them says whose they are. This is the one place where "open APIs" does not mean "open
licence", and AGENTS.md records that explicitly rather than letting the rule quietly mean less.
