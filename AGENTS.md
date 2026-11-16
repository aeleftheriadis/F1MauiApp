# F1 Telemetry companion — project instructions

## Data sources: open APIs only

Every data source must be public, free and anonymous: **no API key, no OAuth, no account, no credit card**. If an endpoint needs any of those, it is not a candidate — the question is settled, not negotiable per feature.

- Rate limits are part of the contract, not an afterthought: one `RequestGate` per provider, because free-tier budgets are per credential.
- Never put a credential in the client. A sideloaded open-source app cannot keep a secret.
- Simulated data is labelled as simulated in the UI **and** in the screen-reader text. No mock may be mistakable for real timing.
- Before adding a source, record base URL, auth requirement, rate limit, coverage and licence/attribution terms. If any of those is unknown, verify it with one real request before writing the adapter — never infer it from a blog post.

### Current sources

| Source | Used for | Auth | Free-tier limit |
| --- | --- | --- | --- |
| OpenF1 (`api.openf1.org/v1`) | meetings, sessions, `location`, `car_data`, `laps`, `intervals`, `stints`, `pit`, `position` — 2023 onwards, including the next season's calendar (verified: 2027 lists 24 circuits, Mar–Dec) | none | 3 req/s, 30 req/min (enforced: a burst of 5 returns 429) |
| Jolpica-F1 (`api.jolpi.ca/ergast/f1`) | calendar, results, standings, champions | none | not published on the API landing page (**unverified**); self-limited to 120/min, burst 4, plus a 24 h cache |

**Measured disagreement:** for 2026 OpenF1 lists 25 race sessions and Jolpica 23; the two Jolpica lacks
are 2026-04-12 and 2026-04-19. Jolpica is the incomplete source here, so **OpenF1 owns the circuit list**
and Jolpica supplies the round number where it agrees. `CalendarReconciler` joins on race date, never on
name ("Sakhir" vs "bahrain"), and the Track screen shows how many meetings the other calendar does not have.

Two Jolpica quirks, both measured: the cross-season filter `driverStandings/1.json` answers **400** — only
`/{season}/driverStandings/1.json` works, so the champions wall costs one request per season — and the
champions are cached for 24 hours rather than fetched per screen.
| News feeds (10, see `NewsSources`) | headlines as links only, never article bodies | none | per-publisher; one browser-shaped User-Agent is required by Reddit |

Feeds verified one by one before being added; `f1technical.net`, `planetf1.com`, `gpblog.com`,
`crash.net` `/rss/news` and the Megaphone/Acast podcast URLs returned 404 and were left out. The
`The Race` podcast feed answers 200 but is 3.5 MB per poll, so the YouTube channel feed (24 KB) is used
for the audio/video affordance instead.

Rate limits were read from the providers' own docs; re-verify before relying on the numbers.

## The one non-open source, and why it is off

`livetiming.formula1.com/signalrcore` is Formula 1's own live-timing hub. Verified: negotiate answers
200 with a connection token, the WebSocket connects, the JSON handshake is acknowledged, without any
key. It is undocumented, F1's terms are unlikely to permit third-party clients, and payload shapes are
unverified outside a live session — so `OfficialLiveTimingClient` only observes and reports, never feeds
the store, and the Settings toggle that enables it defaults to **off**. Nothing in the app depends on it.
`TimingDataParser` reads the community-documented `TimingData.Lines` shape and is unit-tested against a
fixture, but the hub has never been observed sending data from this project, so the parser's output is
reported as a count in Settings and never reaches a screen. Settings captures every received frame to
`live-frames.jsonl` in app data, so the first live session produces the fixture the parser needs.

## Build hygiene

Central package management is on: versions live in `Directory.Packages.props` and no csproj carries a
`Version` attribute. Restore writes `packages.lock.json` per project. `dotnet format --verify-no-changes`
must stay clean for the three non-MAUI projects; the app project cannot be format-verified on a machine
without the Android SDK.

## Screens and what each one rests on

Weekend hub (timetable + phase) · Timing (replay tower) · Track (circuit atlas with live markers only for
the replayed session) · Results (session classification) · Strategy (stint strips) · Compare (two speed
traces) · Facts (world champions) · News (headlines) · Settings (live-feed opt-in).

One replay at a time: picking a session on Results and pressing "Replay this session" switches what the
Timing tower and the Track markers show. Nothing on any screen is generated.

## Where the timing data comes from

`ILiveTelemetrySource` is the only seam between ingest and state. It is fulfilled by
`ReplayTelemetrySource`, which replays a recorded session window loaded by `OpenF1ReplayLoader`:
circuit geometry from one car's GPS lap, positions and car telemetry from the same window. All three
requests are anonymous and inside the free tier (measured: 371 outline points and 2 380 samples for
a 30 s window, ~3.7 s cold).

Nothing on screen is generated. If a coordinate or a speed is not in the payload, it is not drawn, and
a failed load reports the reason instead of falling back to invented data.
