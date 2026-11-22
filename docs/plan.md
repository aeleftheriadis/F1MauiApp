# Remaining work

Ordered by value. Each item states what it is, what it needs, and what would make it not worth doing.
Anything listed here is *not* implemented — the code is the source of truth for what exists.

## 1. Historical session picker (highest value) — DONE 2026-11-21

The Season hub year picker now spans every season OpenF1 publishes (2023 → next year), and tapping a round
card switches the replay to that round's race via `SessionRunner.SwitchToAsync` (the same path Results uses,
`From = race start + 10 min`). Rounds without published telemetry report so instead of switching. No new
data source, no new screen.

## 2. Archive tab — PARTLY DONE 2026-11-21

The Archive screen exists: every WC race since 1950 (search, decade filter) plus the all-time win record
book for drivers and constructors, all from one `race_vault` SQLite table synced with one Jolpica request
per season (ADR-013; measured: 1,147 races, 76 seasons, records match the published numbers).

Still missing from the prototype's Archive tab:

* **Worm chart:** positions per lap from `/position`, drawn as a SkiaSharp line per driver — the endpoint already
  loads for Results, but only the last sample is kept today; a worm needs the whole series.
* **Stint matrix:** horizontal compound bars; `stints` already loads for Strategy. Whether the Archive needs
  it when the Strategy screen has stint strips is a design question, not a data one.
* **Normalised points toggle:** re-score historical seasons with the current points system — needs full
  classifications per race, not just winners; see plan B below.
* **Stop if:** Jolpica's pagination for full results proves to cost more than a handful of requests per
  season above the winner level.

## 3. Paddock depth

The roster and portrait dossiers exist. Missing: teammate head-to-head, season radar, penalty/PU tracker.

* **Head-to-head: DONE 2026-11-21.** The Paddock dossier now carries "vs teammate: races 16-4, average +1.7
  places, qualifying 15-5 over 20 races". Jolpica's season classifications (5 paged requests per season,
  cached in `season_results` for a day) are compared only between the two cars of one constructor, and only
  in races where both were classified - a retirement is excluded rather than counted as a defeat.
* **Radar:** five axes derived from the same tables. Only ship it if the axes are honest — "overtaking" is not in
  the data and should not be invented.
* **PU components and penalty points:** **no open source**. Either leave it out or model it as a "not available"
  panel like team radio.

## 4. Circuit encyclopedia

* Corner names and DRS zones are not in any open feed. Available open data: geometry (already used), measured lap
  length, race date, winner history (already used).
* Wikimedia has aerial photographs of most circuits, CC-licensed — same mechanism as driver portraits (ADR-010).
* **Stop if:** corner-level metadata is expected. It would have to be hand-curated, which is a licensing and
  maintenance question before it is a coding one.

## 5. Weekend tools

* **Session notifications:** local notifications 15 minutes before a session. Needs platform permission plumbing
  (`AlarmManager` / `UNUserNotificationCenter`) and no server.
* **What-if championship calculator:** assign finishing positions to remaining rounds and recompute standings.
  Pure arithmetic on cached results; no new source.
* **"On this day": DONE 2026-11-22.** The Archive list carries a header line such as "ON THIS DAY · 1988 Italian
  Grand Prix — Ayrton Senna", computed from the vault (no request) and hidden on a day with no race. It shows the
  most recent anniversary only; the rest of the matches are in the list below it.

## 6. Live timing (blocked)

Still blocked by ADR-009: the transport works, the payload is unverified. The first live session produces
`live-frames.jsonl`; a parser written against that fixture is the next step, and nothing before it.

## 7. Known gaps that are not features

* **Android cannot be built here** — no SDK build-tools (`XA5205`). CI or a machine with the SDK is required
  before any Android claim.
* **No visual verification.** Every screen is verified as "constructs and renders without throwing"; nobody has
  looked at it on a device. Light theme in particular is contrast-checked but unseen.
* **iOS compiles, never runs.** No Mac or device has been used.
* **Calendar counts disagree** (OpenF1 25 vs Jolpica 23 for 2026) — reconciled in the UI, not in the sources.
* **`Resources/Fonts/OpenSans-*.ttf`** are bundled but unused since the switch to system fonts; removing them is
  a two-file change.
* **App identity** is still the template default (`com.companyname.f1mauiapp`, data folder "User Name"). Pin the
  real id before anyone else runs the Windows build.

---

# Requested additions (not implemented yet)

These came from review of the prototype iterations. Each states what exists today, so nothing here reads as
already done.

## A. Native look and feel — IN PROGRESS (steps 1–2 done 2026-11-21)

**Request.** The current screens read as a custom HUD, not as iOS/Android apps. The dark pit-wall styling was
translated literally; native conventions were not.

**Done**

1. **Navigation is platform-native.** Phones get a `TabBar` with five thumb-reachable destinations (Season,
   Pit wall, Archive, Paddock, More); the other nine screens are pushed routes from a More list that is built
   from `AppShell.Secondary`, so no second list to keep in step. Desktop keeps the flyout. `ServicesRouteFactory`
   makes routed pages come from the container (the default `Activator` path needs a parameterless constructor).
   `F1APP_PHONE=1` forces the phone shell on a desktop build, because an unexercised branch is an unverified one:
   the smoke walk now covers 13 destinations on desktop and 23 on phone (5 tabs + 9 pushes + returns), both clean.
2. **`Surfaces.Row`** replaces cards in list rows: full width, no radius, no shadow, hairline separator, 44 pt
   minimum. Applied to the three primary lists (Season rounds, Archive races/records, Facts champions). Cards are
   kept where content is genuinely a group — hero cards and the pit wall.

3. **`Surfaces.StatusBar`** — one status row, spinner bound to the view model's `IsBusy` plus the sentence.
   Applied to Archive, Facts, News, Season, Results, Strategy, Track, Weekend and Paddock. Compare and the
   Pit wall keep their own (both compute synchronously, so there is nothing to spin).
4. **`RefreshView`** — pull-to-refresh on Archive, Facts, News and Season, bound so the spinner *is* `IsBusy`
   and the pull runs the same load path as the toolbar item. Results, Strategy, Track and Paddock have no
   pull-to-refresh yet (Results reloads from its picker, the others load once per selection).
5. **Haptics** on the two actions that matter: switching the replayed session, and opening a More row. Desktop
   and emulators ignore it.

**Still to do**

6. **Verify on a device.** Every claim above is "constructs and walks", not "looks native". That gap is the
   remaining blocker, and no build log can close it.

## Visual checks queued (cannot be answered by a build log)

These are recorded so they are checked once, on a device, instead of being re-discovered as bugs. Each one is
something I could only verify as "the file exists and the build accepts it".

- **Flyout and tab icons.** 13 icons build and are distinct, but nobody has seen them drawn. Open questions:
  do they read at 16 px, are they legible on the light-theme chrome (the strokes are a fixed `#94A3B8` grey on
  `#FFFFFF`, about 2.6:1 - likely too faint), and is each motif right for its screen.
- **Icon colour does not follow the theme.** The SVG stroke is baked at build time, so light and dark share one
  shade. If it looks wrong, the fix is a per-theme pair of icons swapped on `Themes.Changed`, not a tint.
- **Paddock placeholder.** The page used to display the literal text "paddock" until a theme change; that is
  fixed, but the real dossier has never been looked at.
- **Status bar and pull-to-refresh.** The spinner is bound to `IsBusy` on nine screens; refresh is wired on four.
  Whether the spinner appears at the right moments (and does not flash) is a feel question.
- **`Surfaces.Row` vs `Card`.** List rows were flattened; whether the hairline separators and 44 pt height read
  as a native list, or as a cramped table, is only answerable by eye.
- **Archive scroll.** 1,158 rows in one `CollectionView`. MAUI virtualises, but nobody has scrolled it on a
  phone; if it stutters the answer is incremental loading, not a smaller vault.
- **Bottom tab bar.** Never drawn — the phone shell is only exercised through `F1APP_PHONE=1` on Windows. Tab
  labels for five destinations, and whether "More" is discoverable, need a phone.
- **Light theme overall.** Contrast-checked on paper, unseen in practice.

**Reference:** `docs/design/pitwall-prototype-4.html` (5 tabs: Season, Pit wall, Paddock, Archive, Media) is the
visual target; native conventions win where the two disagree.

## B. Complete archive: every season, every race — CORE DONE 2026-11-21

**Request.** All seasons from 1950, with all races and their details.

* **Done:** the Archive screen holds every season's races since 1950, each with winner, constructor, circuit,
  country, date, laps and winning time, searchable and decade-filtered; the all-time record book is computed
  from it. One row per race (the P1 result), one request per season (ADR-013).
* **Seasons and races:** the Season hub's year picker now also spans 2023 → next year (every season OpenF1
  publishes). A 1950→ calendar browser is the Archive's job now, and it is done.
* **Race details (still open):** full classifications, qualifying, pit stops and fastest laps come from
  `/{season}/{round}/results.json` etc. Per-race requests multiply fast (24 races × 4 endpoints ×
  77 seasons ≈ 7 400 requests) — this needs an explicit one-off sync with progress, and the per-round screens
  to show it. Not built; the vault stores the winner row only.
* **What "all details" cannot mean:** lap-by-lap telemetry (`car_data`, `location`) exists only from 2023 (OpenF1).
  For 1950–2022 the archive can show classification, grid, points, fastest lap and pit stops — not telemetry.
  Any screen implying otherwise would be fabricating.
* **Known gap:** Jolpica's own 2026 calendar is two races short of OpenF1's (measured: 2026-04-12, 2026-04-19).

## C. Dedicated Facts section — RECORD BOOK DONE 2026-11-21

**Request.** A separate section for F1 facts, not a single champions list.

Today `FactsPage` shows the champions wall (Jolpica standings, persisted). The Archive screen now carries the
record book (all-time wins per driver and per constructor, computed from the vault). Still planned, all
computed from cached data:

* Deeper records: poles, podiums, fastest laps, starts — needs full classifications, not winners only.
* Circuit records: most wins at each venue (data already fetched for the Track screen).
* Season records: biggest points margin, most wins in a season, rookie seasons.
* Milestones and streaks; ~~"on this day"~~ (done - it lives on the Archive, where the vault already is).
* All-time tables with a normalised-points toggle, so pre-2010 seasons compare sensibly.

## D. Replay: no hardcoded fallback

**Done in this change.** `SessionRunner` no longer falls back to a hardcoded Bahrain 2024 window. When nothing
resolves, the replay does not start and the dependent screens report "no finished session available to replay".

**Open question for the user:** if the replay cannot be loaded, should the replay-only screens (Timing, Compare,
and the live columns of Pit wall) be *removed from navigation entirely*, or kept and allowed to show their empty
state? Removing them is a five-screen deletion and needs an explicit yes.

---

# OpenF1 endpoint inventory — every documented feature, none of it built yet

Source: <https://openf1.org/docs/> (read 2026-11-22). Probes below were made against the live API with the
2026 Las Vegas meeting (`meeting_key` 1300; race session 11420, qualifying 11416) - keyless, no account.

## Used today

`car_data` · `drivers` · `intervals` · `laps` · `location` · `pit` · `position` · `sessions` · `stints`
(plus `meetings`, added for the circuit icons and flags).

## Documented but unused, with what each would buy

| Endpoint | Measured (session 11420) | What it would enable | Cost / risk |
| --- | --- | --- | --- |
| `weather` | **148 rows** (air/track temp, humidity, pressure, rainfall, wind) | A real track-conditions strip on Timing and Pit wall; the rain flag changes strategy reading | One request per session; fields are per-minute so it must be sampled, not replayed frame by frame |
| `race_control` | **110 rows** (SessionStatus, CarEvent, Flag, SafetyCar, DRS; message, lap, scope, sector) | A race-control feed: yellow/SC/VSC, investigations, penalties - the single biggest missing context on every timing screen | One request per session; needs a category filter or the feed buries the flags |
| `session_result` | **22 rows** (position, duration, gap, laps, **points**, dnf/dns/dsq) | An official classification for Results instead of one derived from the last position sample; points power any standings arithmetic | One request per session; published "a few minutes after" official results, so a live session has none |
| `overtakes` | **361 rows** (overtaker, overtaken, position, date) | "Overtakes of the race" on Results or Archive; a count per driver for the Facts record book | Docs warn the data "may be incomplete"; 361 rows per race is fine to cache |
| `starting_grid` | **empty for the race (11420), 20 rows for qualifying (11416)** | Grid vs finish delta - the cheapest honest "gained/lost places" stat | **Keyed by the qualifying session, not the race** - measured, and it returns an empty list (not an error) if asked with the race key |
| `championship_drivers` (beta) | **25 rows** (position/points start → current) | In-race championship swings: "Verstappen gains 2 places in the title fight" | Beta; race sessions only. **Verified to reconcile**: `position_start`/`points_start` matched Jolpica's standings on all 22 shared drivers (0 mismatches). `*_current` includes the race itself, so the two only agree like-for-like |
| `championship_teams` (beta) | **11 rows** | Same for constructors | Same as above; **verified**: 10 of 11 teams matched Jolpica exactly (the 11th, "Racing Bulls", needs a name map) |
| `team_radio` | **21 rows** (recording_url per driver) | Radio clips on Pit wall | Recordings are hosted on `livetiming.formula1.com` - the same host as the SignalR hub ADR-009 keeps off, so the licensing question is the same and it is **not** an open asset |
| `meetings` | **27 rows for 2026** (`circuit_image`, `country_flag`, `circuit_type`, `gmt_offset`, `is_cancelled`) | Already used for icons and flags; still unused: `is_cancelled` (a cancelled round should not read as "upcoming") and `gmt_offset` | Free - one request per season |

## Cross-cutting capabilities the docs describe and this app does not use

- **Attribute filtering with operators.** Any field can be filtered, e.g. `speed>=315`, `position<=3`,
  `stop_duration<2.3`. Today every call fetches whole sets and filters in memory; pushing the predicate to the
  provider would cut payloads substantially on `car_data`, `laps` and `position`.
- **Time-range filtering on any endpoint.** `date>=...&date<...` on sessions and laps would make "all sessions
  in September 2023" one request.
- **CSV output** (`csv=true`). Not wanted in this app - JSON with source-generated serializers is the contract -
  but worth recording so nobody adds a CSV parser later thinking it is required.
- **Real-time data requires a paid subscription** (the docs say so plainly). That closes the last hope of a
  keyless live feed and agrees with ADR-009: nothing in this app may depend on live timing.
- **`latest` as a magic key** (`session_key=latest`, `meeting_key=latest`) exists. The app resolves "current"
  from its own clock and cache instead, which is testable; switching to `latest` would make the same call
  behave differently on different days.

## Verification of the claims in this table (2026-11-22)

The first pass compared OpenF1's `points_current` with Jolpica's standings and found 11 of 22 drivers differing -
which looked like two sources disagreeing about the championship. That comparison was wrong: `points_start` is
the table *before* the race, `points_current` includes it, and Jolpica's snapshot did not yet have that race.
Comparing like-for-like, all 22 shared drivers matched to the point, and 10 of 11 constructors matched (the
eleventh only because "Racing Bulls" needs a name map). So the beta endpoints are internally consistent with the
standings the app already shows, and the delta between the two fields is the in-race swing itself.

## If this list is worked through, the order that pays first

1. `session_result` on Results - replaces a derived classification with the official one, and brings points.
2. `race_control` on Pit wall and Timing - flags and safety cars are the context every timing column lacks.
3. `weather` on Timing - conditions explain lap-time drift that currently looks like noise.
4. `starting_grid` (+ `session_result`) for gained/lost places, keyed off the qualifying session.
5. `overtakes` for a Facts record; then `championship_*`, which **is** usable: the start snapshot matches Jolpica
   exactly, so a delta (points_current - points_start) is a real in-race swing and not a competing truth.
6. `team_radio` - last, and only if the licensing question is answered; it is F1-hosted audio, not open data.
