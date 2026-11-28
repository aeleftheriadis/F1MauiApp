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

## 4. Circuit encyclopedia — PARTLY DONE 2026-11-28

* Corner names and DRS zones are not in any open feed. Available open data: geometry (already used), measured lap
  length, race date, winner history (already used).
* **Aerial photograph: DONE, with a measured limit.** The Track screen shows a Commons aerial above the map, and
  only when the file's own title names the venue. That is 12 of the 2026 calendar's 26 circuits: Commons returns
  hand-drawn layouts for several venues (they rank *first* for Montréal), and for Las Vegas it returns a
  photograph of Speed Vegas Motorsports Park, a different circuit in the same city. Numbers in
  `docs/agent/findings.md`; the rule is pinned by `WikimediaImageClientTests`.
* The OpenF1 payload's own `circuit_image` already gives every circuit a track icon, so the photograph is
  decoration rather than information — which is why half the calendar having none is acceptable.
* **Stop if:** corner-level metadata is expected. It would have to be hand-curated, which is a licensing and
  maintenance question before it is a coding one. A curated slug→Commons-category map would also fix the wrong
  venue, and is the same kind of maintenance.

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

## Responsive layout (ADR-017) — DONE 2026-11-22

`Responsive.Panes` gives a two-pane arrangement on a wide window and a stacked column on a phone, applied to
Paddock, Track, Season and Archive; the shell and the pages share one `IsPhone` switch. The arrangement also
follows the **live** width: a `WidthSwitch` per page rebuilds only when the width crosses the 900 pt breakpoint,
verified by the stress run resizing 1500 -> 700 -> 1500 pt with a walk after each crossing.

## Screenshots close most of this gap on desktop

`F1APP_SMOKE=1 F1APP_SHOTS=1` now saves a PNG of every screen to the app cache (`shots/`) as the walk visits it,
so the desktop UI can be looked at on a machine nobody can touch. Eight defects were found and fixed in the first
pass: the wide layout never applied (`WidthSwitch` compared against the first observed width instead of the one
that was built), the Settings theme picker clipped its text (a forced height on WinUI), dates rendered in two
cultures on one screen (`Kup 08 Map` next to `Sun 29 Nov`), the Season hero stretched down an empty column, the
Paddock opened with an empty dossier while its duel lines already named a driver, the 93 px headshot was
fill-cropped to a chin, the race-control strip showed four identical `CLEAR` lines because *CLEAR is itself a
flag value*, and the decade picker was squeezed to its text width.

## Credits and licences shipped in the app

- **Barlow Semi Condensed** — SIL Open Font License 1.1, Copyright 2017 The Barlow Project Authors. Files and
  `OFL.txt` live in `src/F1App.App/Resources/Fonts/`. It is the open substitute for F1's own display face.
- **Formula 1 media** — driver cutouts, team cars, team logos and circuit cards, hotlinked from
  `media.formula1.com` and credited on screen, never stored or redistributed (ADR-019).
- **Wikimedia Commons** — freely licensed portraits, used where F1 media has none, with author and licence
  printed beside the picture.
- **OpenF1** images — track icons and flags shipped in its payloads, credited the same way.

## Real clicks, not just navigation

`F1APP_CLICK=1` sends a **real mouse click** at a pit-wall tab's screen position (`SetCursorPos` +
`mouse_event` on the control's transformed bounds, then `ClientToScreen`). This exists because the reported
NEWS crash did not exist on the synthetic path: calling the handler directly was fine, a pointer click killed
the app in ~5 s. The cause was a hidden `CollectionView` being repopulated, which is now recorded in
`docs/agent/findings.md`.

## Visual checks queued (cannot be answered by a build log)

Pruned 2026-11-28. The Android emulator run answered four of the eight originally listed, so they came off
rather than sitting there implying work that is done:

- ~~Bottom tab bar never drawn~~ — **answered**: five tabs with icons, light theme.
- ~~Icons on device~~ — **answered**: drawn and legible at tab size.
- ~~Light theme overall~~ — **answered**: the app ran in Light; white cards, dark text and red accents read correctly.
- ~~`Surfaces.Row` vs `Card`~~ — **answered**: the More list reads as a list, not a cramped table.
- ~~Paddock placeholder~~ — stale since the dossier landed.

Still open, and both are measurements rather than opinions:

- ~~Archive scroll~~ — **measured 2026-11-28** (emulator, hardware GPU): 24.08% janky on 740+ rows against 9.52% on
  a 23-row screen, p50 34 ms. Real but not damning; incremental loading is not proven necessary, and the 9.5%
  floor suggests the row template deserves a look first. Numbers and caveats in `docs/agent/findings.md`.
- **Status bar and pull-to-refresh.** The spinner is bound to `IsBusy` on nine screens and refresh is wired on
  four; whether it appears at the right moments (and does not flash) is a feel question.

~~One known defect, unjudged: icon colour does not follow the theme~~ — **judged and fixed 2026-11-28.** The
baked stroke was `#94A3B8`: **7.07:1 on the dark canvas, 2.37:1 on the light one**, i.e. below the 3:1 that WCAG
1.4.11 asks of a graphical object. A per-theme pair would need thirteen more assets and a swap on
`Themes.Changed`; a single mid-grey covers both, because 3:1 against `#15151E` and against `#F4F6F9` overlap in one
narrow band. `#6B7280` sits in it at **3.75:1 and 4.47:1**. Every icon is now that shade in both themes — a
deliberate flattening, recorded here so nobody "restores" the old tint.

## B. Complete archive: every season, every race — CORE DONE 2026-11-21

**Request.** All seasons from 1950, with all races and their details.

* **Done:** the Archive screen holds every season's races since 1950, each with winner, constructor, circuit,
  country, date, laps and winning time, searchable and decade-filtered; the all-time record book is computed
  from it. One row per race (the P1 result), one request per season (ADR-013).
* **Seasons and races:** the Season hub's year picker now also spans 2023 → next year (every season OpenF1
  publishes). A 1950→ calendar browser is the Archive's job now, and it is done.
* **Race details: DONE 2026-11-22 as on-demand, not a bulk sync.** Tapping a race in the Archive opens it in
  full - classification (position, driver, team, laps, status, points), qualifying (Q1/Q2/Q3) and pit stops
  (lap, stop number, stationary time) - from three Jolpica requests, cached in SQLite so a second open costs
  nothing. The bulk alternative (77 seasons x 24 races x 3 endpoints) stays rejected: it is ~5 500 requests for
  data almost nobody opens, and every race is reachable on demand instead.
* **What the detail screen does not show:** fastest-lap data beyond the classification's `FastestLap` block and
  lap charts. Those need per-lap endpoints at one request per race each and are still open.
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

`car_data` · `drivers` · `intervals` · `laps` · `location` · `pit` · `position` · `race_control` ·
`sessions` · `session_result` · `starting_grid` · `overtakes` · `championship_drivers` ·
`stints` · `weather` · `meetings` (icons, flags, circuit type).

## Documented but unused, with what each would buy

| Endpoint | Measured (session 11420) | What it would enable | Cost / risk |
| --- | --- | --- | --- |
| ~~`weather`~~ **done 2026-11-22** | **148 rows** (air/track temp, humidity, pressure, rainfall, wind) | The Pit wall header now shows "air 27.8 °C · track 52.5 °C · 58% humidity · wind 2.4 m/s · dry" from the session's latest sample | One request per session; the latest sample is what a screen shows, so the history is fetched and discarded. Extending it to Timing is still open |
| ~~`race_control`~~ **done 2026-11-22** | **110 rows** (SessionStatus, CarEvent, Flag, SafetyCar, DRS; message, lap, scope, sector) | The Pit wall shows the four most recent messages above the table ("lap 61 · SafetyCar · SAFETY CAR DEPLOYED"), verbatim and trimmed | One request per session. Not yet filtered by category, so a busy session can fill all four slots with car events - a flag-first sort is the obvious next step |
| ~~`session_result`~~ **done 2026-11-22** | **22 rows** (position, duration, gap, laps, **points**, dnf/dns/dsq) | Results now carries an OFFICIAL column: points and DNF/DNS/DSQ from the FIA classification, joined to the derived table by driver number | Additive, not a replacement - the derived table keeps sectors, tyres and pits. Published a few minutes after the session, so the column is empty until then and the status line says so |
| ~~`overtakes`~~ **done 2026-11-22** | **361 rows** (overtaker, overtaken, position, date) | The Results OFFICIAL column ends with "4 made, 2 lost" per driver | Counted as overtakes, never "on-track passes": the provider mixes in pit-stop and penalty position changes, and warns the list may be incomplete |
| ~~`starting_grid`~~ **done 2026-11-22** | **empty for the race (11420), 20 rows for qualifying (11416)** | Results appends "places +2" to the OFFICIAL column: grid vs finish, only where both are known | **Keyed by the qualifying session, not the race** - the Results screen resolves the sibling qualifying slot from the weekend schedule before asking. A race weekend with no qualifying session gets no delta at all |
| ~~`championship_drivers`~~ **done 2026-11-22** | **25 rows** (position/points start → current) | The Paddock dossier shows "title P3 · 410 pts · +18 this race · 2 places gained" | Beta; race sessions only, so other sessions leave the line empty. Verified to reconcile with Jolpica (0 mismatches on 22 drivers when compared like-for-like) |
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

1. ~~`session_result` on Results~~ - **done 2026-11-22** as an added column (points + classified status) rather
   than a replacement; fully replacing the derived table is still open and would lose sectors/tyres/pits.
2. ~~`race_control` on Pit wall and Timing~~ - **done on the Pit wall** 2026-11-22; the Timing tower does not show
   it yet, and the strip does not yet prioritise flags over car events.
3. ~~`weather` on Timing~~ - **done on the Pit wall** 2026-11-22; extending the same line to the Timing tower is
   the remaining half.
4. ~~`starting_grid` (+ `session_result`) for gained/lost places~~ - **done 2026-11-22**, keyed off the sibling
   qualifying session as measured.
5. ~~`overtakes` for a Facts record~~ - **done 2026-11-22** on Results; then `championship_*`, which **is** usable: the start snapshot matches Jolpica
   exactly, so a delta (points_current - points_start) is a real in-race swing and not a competing truth.
6. `team_radio` - last, and only if the licensing question is answered; it is F1-hosted audio, not open data.

## E. Dossiers: drivers, teams, cars, pit stops, regulations — PLANNED 2026-11-28, NOT STARTED

**Request (2026-11-28).** More stats and detail about drivers and teams; fastest pit stops; details of each car;
how the cars changed through a season; the FIA regulations. Images only where the licence allows. Research and
UI/UX are part of the work, not a preface to it. **It starts on its own branch, after research** — nothing here is
begun, and this section exists so the research happens before the code.

### What is open data, what needs research, and what is probably impossible

Measured 2026-11-28 against the two providers the app already uses. Nothing below is planned on an unverified
source: a field whose source is not confirmed does not get a screen slot.

| Ask | Source | Verdict |
| --- | --- | --- |
| Driver career numbers: starts, wins, points, titles, per-season line | Jolpica `results` + `driverStandings` (both in use) | **Computable** from a paced pass over the seasons the vault already syncs. |
| Poles, podiums, fastest laps | Jolpica `qualifying` + `results` per season | **Computable, not yet verified**: ~77 seasons × 1–2 requests ≈ 150, paced at 8/min ≈ 20 min, resumable, cached like the vault. Jolpica returned **502 to every path** on 2026-11-28, so the exact payload was not confirmed — that is the first thing to do when it is back. |
| Team history: constructors' titles, wins, eras | Jolpica standings + the vault's winner/constructor columns | **Computable**, with one trap: the API has no name lineage (Toro Rosso → AlphaTauri → RB, Jordan → Force India → Racing Point → Aston Martin). Without a curated alias table the counts are silently split across names and therefore wrong. The table is data to maintain, not code. |
| Fastest pit stops | Jolpica `pitstops` (has `duration`; already consumed by the race-detail screen) and, 2023+, OpenF1 `pit` (`pit_duration`) | **Available now.** Per race, the fastest stop is a min over rows already fetched. A season leaderboard is one request per race; note that pre-2011 stop durations are largely absent, so any all-time figure must say "since 2011" or show nothing. |
| Car details: chassis, engine, gearbox, designer, weight | **neither API has any of it** | **Research required, may not exist in licit form — and if it does not, the car screen is not built.** Candidates to test: Wikidata items per car (CC0, machine-readable) and Wikipedia infoboxes (CC BY-SA, but scraping an infobox is a brittle non-API). F1.com car pages are all rights reserved — link out only. Expect gaps: the plan is a car section that shows the fields a source backs and says so when the list is short. |
| Mid-season changes and upgrades | **no open source** | **Almost certainly impossible, and therefore no screen.** Press releases are copyrighted and secondary summaries are unreliable; a timeline assembled from memory would be invented data. If the research pass finds no citable feed, this ask ends there — no screen, and the research document says why. |
| FIA regulations | fia.com document library: PDFs, no key, no account | **Free to read, not free to copy.** Titles, dates and links are fine; a short quoted excerpt with attribution at most. Reproducing rule text, or screenshots of it, is out. |

### Images, and what each licence actually permits

Four classes, and only three of them can be shown:

1. **Formula 1 media**, from OpenF1 payloads (`headshot_url`, `circuit_image`, `country_flag`): hotlinked, credited
   on screen, never copied, cropped or stored (ADR-010, in use since 2026-11-22).
2. **Wikimedia Commons** (CC BY / CC BY-SA): author and licence on screen, memory only (ADR-019). Verified
   2026-11-28: thumbnails now come from `thumb.wikimedia.org`, which answers 403 to an agentless request, so the
   image must be fetched by the app and rendered from memory, never bound as a bare URL.
3. **Wikidata images**: the same Commons files, so the same rule — resolve to the Commons file and show its author.
4. **FIA and team press material**: **no reuse.** Not a logo, not a regulation page screenshot. Link out.

### UI/UX (designed later, on that branch)

The shape follows what exists rather than a new vocabulary: `Surfaces.Row` for factual lines, `HeroCard` for the
subject, `DriverRow` for lists, the credited-image block for artwork, ADR-017's two arrangements for desktop and
phone, and the palette tokens for colour. The screens this asks for:

- **Driver page** — the Paddock dossier grown up: career table, per-season line, team history, and the records it
  holds, all computed from the vault.
- **Team page** — from the constructor card: titles, wins, seasons, driver line, and a car section that lists only
  the specifications a verified source states.
- **Pit-stop leaderboard** — fastest stop per race (already computed on Results), plus a season and archive board,
  always labelled with the era limitation.
- **Regulations reading list** — titles, dates, links out; no reproduction. It exists only if the fia.com document
  library serves those links without a key, which the research pass checks.

**A screen is only built when research found data for it (decision 2026-11-28).** No data, no screen — not a screen
that explains its own emptiness. So the car section appears only if the research pass verifies car specifications
against a citable source; the mid-season-changes screen does not appear at all unless a citable feed exists, and
that is the expected outcome. Inside a screen that *does* exist, a missing field is simply not drawn; it is never
filled with a plausible-looking guess.

### Order, when this starts on its own branch

1. **Research pass first**, as a background agent per the `research` skill, writing
   `docs/research/2026-11-28-dossiers.md`: one row per field with source URL, licence, a real sample request, and a
   verified/unverified flag. No field gets built from an unverified row.
2. **Vault pass** for poles, podiums, fastest laps and the constructor alias table — blocked until Jolpica answers.
3. **Pit-stop boards** from data the app already fetches: no new source, no new licence.
4. **Car and team pages last**, because they are the ones most likely to have no licit source at all.

Skills to use on that branch: `research` (the pass above), `brainstorming` → `writing-plans` (spec before code),
`plan-ui-change` (decomposing the new screens), `ui-ux-pro-max` (visual decisions), `code-testing` (parsers and
queries), and `dotnet-breaking-changes` if the domain records for drivers and teams have to be split.

**Stop if:** the dataset behind a screen has no citable open source — then that screen is dropped, not stubbed.
No curated-from-memory facts, no scraped infoboxes, no regulation text, and no placeholder screens for data that
does not exist. The research document is what records the drop, so the decision is visible later rather than
looking like something nobody got round to.
