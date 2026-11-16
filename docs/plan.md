# Remaining work

Ordered by value. Each item states what it is, what it needs, and what would make it not worth doing.
Anything listed here is *not* implemented — the code is the source of truth for what exists.

## 1. Historical session picker (highest value)

Today the replay follows the current weekend or the most recent race. A user cannot say "show me Silverstone
2023".

* **Needs:** OpenF1 `/sessions` filtered by year + circuit; reuse `OpenF1CircuitCatalogue` and
  `OpenF1ReplayLoader`; a picker on the Season or Pit wall screen that calls `SessionRunner.SwitchToAsync`.
* **Cost:** one screen, one service method, no new data source.
* **Stop if:** session switching turns out to need more than a cancel-and-restart (it does not, per ADR-007).

## 2. Archive tab (worm chart, stint matrix, all-time stats)

The prototype's Archive tab.

* **Worm chart:** positions per lap from `/position`, drawn as a SkiaSharp line per driver — the data already
  loads for Results.
* **Stint matrix:** horizontal compound bars; `stints` already loads for Strategy.
* **All-time stats:** wins/poles/podiums per driver from Jolpica, cached in SQLite (same shape as champions).
* **Normalised points toggle:** re-score historical seasons with the current points system — pure arithmetic on
  results, no new source.
* **Stop if:** Jolpica's pagination for career results proves to need more than a handful of requests per driver
  (500/hour budget).

## 3. Paddock depth

The roster and portrait dossiers exist. Missing: teammate head-to-head, season radar, penalty/PU tracker.

* **Head-to-head:** Jolpica qualifying + results for the season → duel counts and average gap. Real, cacheable.
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
* **"On this day":** query historical race dates against today's month/day from the SQLite cache.

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
