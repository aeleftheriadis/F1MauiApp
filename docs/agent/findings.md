# Findings

Evidence, measurements and dead ends. Newest last.

## 2026-11-21
- `SessionRunner` hardcoded Bahrain-2024 fallback removed (plan D): build 0w/0e; replay now reports
  "no finished session available to replay" when nothing resolves. Full smoke run still pending (previous
  run was aborted mid-command by the user).
- Prototype v4 copied to `docs/design/pitwall-prototype-4.html` as visual reference; 5 main tabs.
- **2026-11-21 Archive vault, measured:** `api.jolpi.ca/ergast/f1/{season}/results/1.json` answers one
  season's races+winners in a single request (verified 1950, 2023, 2026; ~22 KB per season). First live sync
  wrote 989 races (1950-2020) before the smoke-walk timeout killed the process; the second run resumed at
  2021 only and finished at 1,147 races / 76 seasons - the per-season resume path works against the real
  API, not just in tests. All-time wins from the table match the published record (Hamilton 106,
  Schumacher 91, Ferrari 246). The Indy 500 1950-60 was initially kept as-is; the user asked for it removed, so it is now
  filtered at parse time and a v3 migration purges it from synced devices (verified on the device DB: 11
  removed, 8 US GPs at the same circuit kept, 0 duplicates, 1 158 races / 77 seasons).
- 2026-11-21 plan item 1 (historical session picker): tapping a Season round card switches the replay to
  that round's race via the same `SwitchToAsync` path as Results; walk clean.
- 2026-11-21 SessionRunner fallback (plan D) fully verified this time: build 0w/0e, 100/100 tests,
  format clean, iOS 0 errors, 13-destination walk clean (the process stays alive by design after the walk;
  earlier "run=0" was the exception path, not the intended exit).
- 2026-11-21 native navigation (plan A step 1): `Routing.RegisterRoute(route, type)` constructs pages with
  `Activator`, which needs a parameterless constructor - every page takes its view model from DI, so the route
  factory must resolve from the container. Pushing uses the plain route name: `GoToAsync("more/weekend")`
  throws "Relative routing to shell elements is currently not supported". Verified with `F1APP_PHONE=1`:
  desktop walk 13/13, phone walk 23/23, both 0 PAGE/CRASH.
- 2026-11-22 layout defect found by the walk: Season and Results kept their `Picker` in a field and added it
  to content rebuilt on `Themes.Changed`, so MAUI warned "Picker is already a child of VerticalStackLayout".
  Fixed by detaching from the old parent first (`(_picker.Parent as Layout)?.Remove(_picker)`). The phone walk
  now reports 0 such warnings - the desktop walk never rebuilds, which is why this stayed hidden.
- 2026-11-22 dead feed, measured: `https://www.reddit.com/r/formula1/.rss` -> 404; `/r/formula1.rss` -> 429;
  `old.reddit.com/r/formula1/.rss` -> 302. Removed from `NewsFeedReader.All` (10 -> 9 feeds) and from the source
  table in `AGENTS.md`. Smoke run afterwards: 0 "feed could not be read" warnings on either shell.
- 2026-11-22 **the Paddock page showed the literal text "paddock"**: the constructor assigned
  `Content = new Label { Text = "paddock" }` and only a theme change rebuilt it into the real dossier. The smoke
  walk cannot catch this - it detects exceptions, not what a page displays - so this survived several walks.
  Its portrait `Image` and no-photo `Label` had the same re-parenting defect as the pickers (fixed by detaching).
  Lesson for the walk: construction with no exception is not "the screen works"; that is what a device check is for.
- 2026-11-22 Jolpica paging, measured: `limit` is clamped to 100 (`limit=500`, `limit=1000` both answered 100),
  so a 24-race season is 5 requests (479 rows for 2024). `/{season}/results/1.json` answers all races with only
  the P1 result (the archive's cheap shape), which cannot support teammate duels.
- 2026-11-22 two defects the duel tests caught before shipping: integer division turned every fractional average
  position delta into 0 (`-1/2 == 0`), and the duel lookup keyed on a driver id that no OpenF1 three-letter code
  matches (`max_verstappen` does not start with `VER`) - the record now carries the code both sources agree on.
- 2026-11-22 live duel sanity check (2026, 20 rounds): VER 16-4 HAD (+1.7 places, 15-5 grid), NOR 12-8 PIA,
  HAM/LEC 10-10 with deltas of exactly opposite sign - symmetric in both directions, as it must be.
- 2026-11-22 "on this day" needs no new source: the vault already holds every race date since 1950, so the
  lookup is a filter over cached rows. Deliberately excludes the current season (a race run earlier today is
  not an anniversary) and returns empty rather than inventing one.
- 2026-11-22 flyout icons: three defects, all visible only on screen. (1) `ic_results.svg` is a trophy and was
  used by the Championship group, Results and Archive at once - the flyout showed the same cup twice. Fixed by
  giving the trophy its own file (`ic_championship.svg`), drawing `ic_results` as a result sheet and `ic_archive`
  as a box. (2) `ic_paddock.png` was referenced but no SVG existed, so the Paddock slot was empty. (3) Nested
  `Tab` elements carried no icon at all - the flyout draws the group, the tab strip draws the tab, and an unset
  icon is a blank slot, not a fallback; the icons are now set on both. Stroke weight raised 1.6 -> 2 because a
  24 px icon drawn at 16 px loses a 1.6 px stroke.
- 2026-11-22 OpenF1 endpoint probes (meeting 1300, race session 11420, keyless): `weather` 148 rows,
  `race_control` 110, `session_result` 22 (carries **points**), `overtakes` 361, `championship_drivers` 25,
  `championship_teams` 11, `team_radio` 21. Two surprises worth remembering: `starting_grid` is keyed by the
  **qualifying** session (11416 answered 20 rows, race key 11420 answered `{"detail":"No results found."}` - an
  empty list, not an error, so a caller that assumes "no grid" would be silently wrong), and asking for a
  session key that has not happened yet returns the same empty-detail object as a successful empty query.
  Full inventory and priorities: `docs/plan.md` -> "OpenF1 endpoint inventory".
- 2026-11-22 **correction, my own error**: I first reported that OpenF1's `championship_drivers` disagreed with
  Jolpica (11 of 22 drivers differing). That was a wrong comparison, not a data conflict. `points_start` is the
  championship before the race and `points_current` includes it, while Jolpica's snapshot has not yet counted
  that race. Comparing `position_start`/`points_start` with Jolpica: **0 mismatches across 22 drivers**, and 10 of
  11 constructors matched ("Racing Bulls" needs a name map). The OpenF1 delta is therefore usable as a real
  in-race swing. Lesson: when two sources "disagree", check whether the fields describe the same instant before
  concluding the sources conflict.
