# Tasks

Open items from docs/plan.md (source of truth for detail and status):

- [x] Plan 1: historical session picker — DONE 2026-11-21, tap a Season round card; build 0w/0e, walk clean
- [x] Plan 2 (core): Archive tab — DONE 2026-11-21. All races since 1950 + all-time win records (ADR-013, race_vault table, live sync verified: 1,147 races / 76 seasons). Still open: worm chart, stint matrix, normalised points.
- [x] Plan 3 (part): teammate head-to-head in the Paddock dossier — DONE 2026-11-22, 111 tests, live data verified. Open: season radar (must not invent axes), PU/penalty tracker (no open source).
- [ ] Plan 4: Circuit encyclopedia (Wikimedia aerials; corner metadata blocked)
- [x] Plan 5 (part): "on this day" — DONE 2026-11-22 on the Archive header, computed from the vault. Open: session notifications (needs platform permission plumbing), what-if championship calculator.
- [ ] Plan 6: live timing — blocked by ADR-009 until first live fixture
- [x] OpenF1 `championship_drivers` — DONE 2026-11-22 (Paddock dossier: title position, points, race swing)
- [x] OpenF1 `overtakes` — DONE 2026-11-22 (Results "N made, M lost")
- [x] OpenF1 `starting_grid` — DONE 2026-11-22 (Results "places ±N", resolved via the qualifying session)
- [x] OpenF1 `race_control` — DONE 2026-11-22 (Pit wall: 4 newest messages). Open: flag-first ordering, Timing tower
- [x] OpenF1 `session_result` — DONE 2026-11-22 (Results OFFICIAL column: points + DNF/DNS/DSQ)
- [x] OpenF1 `weather` — DONE 2026-11-22 on the Pit wall header (latest sample, 1 request/session)
- [ ] OpenF1 unused endpoints (inventory + priorities in docs/plan.md): session_result, race_control, weather,
      starting_grid, overtakes, championship_drivers/teams (beta), team_radio (licensing). Measured available
      keylessly except team_radio's licensing question and starting_grid's qualifying-session key.
- [x] Desktop visual pass via `F1APP_SHOTS=1` screenshots — 8 defects found and fixed 2026-11-22
- [ ] Phone visual pass (screenshots forced with F1APP_PHONE=1 still need a device for gestures/status bar)
- [x] Crash clicking the pit-wall NEWS tab — fixed 2026-11-22 (hidden Media CollectionView was being repopulated)
- [x] F1.com look and feel, phase 1 — palette, Barlow Semi Condensed (OFL), media catalogue, image resolver,
      credited cutouts/cars on Paddock, circuit card on the Season hero (2026-11-22)
- [x] F1.com look and feel, phase 2 (HUD hero) — DONE 2026-11-28. `WeekendSchedule.MeetingName` now carries the
      race's published name (the loader joins the cached meetings payload; it held "LOSAIL 2026" before),
      `SessionRequest.MeetingName` carries it to the replay, and the pit wall shows its circuit card above the
      header, collapsing to nothing when the name is unknown. Verified by screenshot: the Qatar card renders.
      Still open: per-row imagery in the 1 100-row Archive stays deliberately out (SDD ruling).
- [x] Ailoha agent set up (2026-11-28): package + `#if DEBUG` registration, AutomationIds on the pit-wall tabs
      and the season picker, verified by a real tap + screenshot
- [x] Ailoha on Android — VERIFIED 2026-11-28 on the emulator: agent on 9233 inside the device, driven from the
      host through `adb forward`, screenshot confirmed. Fixed on the way: a WinUI-only harness block that broke the
      Android compile, `EmbedAssembliesIntoApk` for hand installs, and the template's purple Android chrome.
      Open: the environment itself — `ANDROID_HOME` points at an empty SDK folder
- [ ] Ailoha CLI 0.2.4 `ui tree` / `ui query` return `[]` against this app while the agent's HTTP API works — retry
      after a CLI update; until then use `curl .../api/v1/ui/tree?depth=16`
- [x] Static event leaks — fixed 2026-11-22 (correction: this line stayed open after the fix). All 15 pages now
      call `Themes.RebuildOnChange`, which subscribes on `Loaded` and releases on `Unloaded`, and the pit-wall news
      handler is bound to the same lifetime. Verified by a Light/Dark theme flip with screenshots.
- [ ] Visual checks queued in docs/plan.md ("Visual checks queued") — icons on device, light-theme contrast,
      status spinner feel, Row vs Card, Archive scroll with 1,158 rows, bottom tab bar, light theme
- [x] Responsive layout for desktop + phone — DONE 2026-11-22 (ADR-017; Paddock, Track, Season, Archive, width-driven via WidthSwitch)
- [~] Plan A: native look-and-feel — DONE: platform navigation (TabBar/phone + flyout/desktop, ADR-014), `Surfaces.Row` flat list rows on Season/Archive/Facts. OPEN: ActivityIndicator progress, RefreshView, haptics, device verification.
- [x] Plan B (detail): per-race classification/qualifying/pit stops — DONE 2026-11-22, on demand (ADR-018)
- [x] Plan B (core): complete archive 1950→ — DONE 2026-11-21, winner-level vault synced + searchable. Still open: full per-race detail screens (classification/qualifying/pit stops).
- [x] Plan C (core): record book — DONE 2026-11-21 on the Archive screen (all-time wins, drivers + constructors). Still open: poles/podiums/fastest-lap records, circuit/season records, milestones, on-this-day.
- [ ] Plan D: replay fallback removal — code done 2026-11-21; open question: remove empty replay screens?
