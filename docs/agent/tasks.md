# Tasks

Open items from docs/plan.md (source of truth for detail and status):

- [x] Plan 1: historical session picker — DONE 2026-11-21, tap a Season round card; build 0w/0e, walk clean
- [x] Plan 2 (core): Archive tab — DONE 2026-11-21. All races since 1950 + all-time win records (ADR-013, race_vault table, live sync verified: 1,147 races / 76 seasons). Still open: worm chart, stint matrix, normalised points.
- [x] Plan 3 (part): teammate head-to-head in the Paddock dossier — DONE 2026-11-22, 111 tests, live data verified. Open: season radar (must not invent axes), PU/penalty tracker (no open source).
- [ ] Plan 4: Circuit encyclopedia (Wikimedia aerials; corner metadata blocked)
- [x] Plan 5 (part): "on this day" — DONE 2026-11-22 on the Archive header, computed from the vault. Open: session notifications (needs platform permission plumbing), what-if championship calculator.
- [ ] Plan 6: live timing — blocked by ADR-009 until first live fixture
- [ ] OpenF1 unused endpoints (inventory + priorities in docs/plan.md): session_result, race_control, weather,
      starting_grid, overtakes, championship_drivers/teams (beta), team_radio (licensing). Measured available
      keylessly except team_radio's licensing question and starting_grid's qualifying-session key.
- [ ] Visual checks queued in docs/plan.md ("Visual checks queued") — icons on device, light-theme contrast,
      status spinner feel, Row vs Card, Archive scroll with 1,158 rows, bottom tab bar, light theme
- [~] Plan A: native look-and-feel — DONE: platform navigation (TabBar/phone + flyout/desktop, ADR-014), `Surfaces.Row` flat list rows on Season/Archive/Facts. OPEN: ActivityIndicator progress, RefreshView, haptics, device verification.
- [x] Plan B (core): complete archive 1950→ — DONE 2026-11-21, winner-level vault synced + searchable. Still open: full per-race detail screens (classification/qualifying/pit stops).
- [x] Plan C (core): record book — DONE 2026-11-21 on the Archive screen (all-time wins, drivers + constructors). Still open: poles/podiums/fastest-lap records, circuit/season records, milestones, on-this-day.
- [ ] Plan D: replay fallback removal — code done 2026-11-21; open question: remove empty replay screens?
