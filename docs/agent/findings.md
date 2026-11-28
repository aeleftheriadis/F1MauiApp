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
- 2026-11-22 `weather` implemented: one request per session, latest sample only. The formatter (`TrackConditions
  .Describe`) is unit-tested for full, partial and empty field sets so an unpublishable field is left out rather
  than shown as 0; the JSON parse is tested against the provider's documented payload shape.
- 2026-11-22 `session_result` implemented on Results as an **added** OFFICIAL column (points, DNF/DNS/DSQ),
  joined by driver number, rather than replacing the derived table - replacing it would drop sectors, tyre age
  and pit counts that the lap data supplies. An empty array (published-nothing) is a normal answer, tested.
- 2026-11-22 `race_control` implemented on the Pit wall: the four newest messages, verbatim, trimmed at 48 chars.
  Known limitation recorded rather than hidden: with no category filter a busy session can fill all four slots
  with car events, so a yellow flag can scroll out of the strip; sorting flags/safety cars first is the fix.
- 2026-11-22 `starting_grid` implemented: Results resolves the qualifying slot from the same `WeekendSchedule`
  before asking, because the race key answers an empty list (measured). Delta appears only when grid and finish
  are both known, so an unclassified car shows no invented "-0".
- 2026-11-22 responsive layout (ADR-017): `Responsive.Panes` applied to Paddock, Track, Season, Archive. Two
  defects caught by the walk while building it - adding a pane whose parent was the RefreshView ("Grid is already
  a child of RefreshView"), and adding the list grid rather than the RefreshView that owns it. Both verified
  fixed: wide 13/13, phone 23/23, stress (36 theme-switched navigations) 0 exceptions.
- 2026-11-22 `overtakes` implemented on Results ("4 made, 2 lost" appended to OFFICIAL). Deliberately not called
  "on-track passes": the provider's own docs say the list mixes pit-stop/penalty position changes and may be
  incomplete, so the wording follows the data rather than the ideal.
- 2026-11-22 championship swing implemented in the Paddock dossier ("title P3 · 410 pts · +18 this race ·
  2 places gained"). Built on the beta endpoint that had been verified to reconcile with Jolpica. My own test
  expectation was wrong first (I asserted a 5th-to-3rd move as three places); the code was right and the test was
  corrected - the second time this session that an assertion, not the implementation, was the defect.
- 2026-11-22 width-driven arrangement: `WidthSwitch` fires only when the width crosses 900 pt, so a resize storm
  does not rebuild content repeatedly (a rebuild is a full page construction). The stress harness now resizes the
  window 700 -> 1500 pt and walks Season and Paddock after each: 0 exceptions, 0 COMException.
- 2026-11-22 race detail (ADR-018): three Jolpica requests per race on demand, cached as raw payloads. Live probe
  2026 round 20: results 22, qualifying 22, pit stops 30. Qualifying exists from 1994 and pit stops from 2012, so
  an empty section is "not published" - tested with a 1970 race whose optional endpoints 404.
- 2026-11-22 **screenshots**: `F1APP_SHOTS=1` captures a PNG of each screen during the walk. Desktop defects it
  found, all fixed and re-verified by a second screenshot: (1) `WidthSwitch` took the first observed width as its
  baseline, so the wide layout was never applied and the Season hero sat under the list on a 1400 pt window;
  (2) a forced `HeightRequest` on the theme Picker clipped "Dark" to half its height on WinUI; (3) dates used
  `CurrentCulture` in some places and invariant in others, giving "Kup 08 Map" beside "Sun 29 Nov"; (4) the hero
  card stretched down an empty column; (5) Paddock opened with `—` as the dossier while the duel lines already
  described a driver; (6) the provider headshot (93 px) was `AspectFill`-cropped to a chin; (7) race control
  showed four identical `CLEAR IN TRACK SECTOR` lines - `CLEAR` is itself a flag value, so "has a flag" ranked it
  first; (8) the archive decade picker collapsed to its text width.
- Lesson: "constructs and walks" hid all eight. A screenshot is cheap and found what no assertion did.
- 2026-11-22 **crash on the pit-wall NEWS tab** (`0xc000027b` in `CoreMessagingXP.dll`, identical fault offset
  in three user crashes and one reproduction). Root cause: **filling a CollectionView that is not on screen**.
  `RebuildNews()` populated both the News and the Media lists on every refresh; pressing NEWS refreshes the feeds,
  the hidden Media view received its two items, and XAML died inside its input machinery. Bisected in the harness:
  - real mouse click at the tab's screen position (`F1APP_CLICK=1`) reproduces in ~5 s; calling `ShowSection`
    directly does **not** - the crash only exists on the pointer path
  - `ShowSection` split: visibility only, colours only, both = fine; adding `EnsureSectionLoaded` = crash
  - refresh with news only (40 items) = fine; refresh with media only (2 items) = crash
  Fix: `RebuildNews()` fills only the list belonging to the visible section; switching tabs already refills the
  one it reveals. Verified by real clicks on NEWS → TIMING → MEDIA → NEWS: walk completes, 0 Application Error
  events, twice in a row, and a screenshot of the populated NEWS tab.
- Lesson: a synthetic handler call is not a click. Two days of "the walk visits every screen" hid an input-path
  crash that a real pointer event produced immediately.
- Found while bisecting, not fixed: `Themes.Changed` (a static event) and `NewsViewModel.PropertyChanged`
  (a singleton view model) are subscribed by every page constructor and never unsubscribed, so closed pages stay
  reachable and rebuild their content. Recorded as an open item in tasks.md.
- 2026-11-22 **Jolpica rate limit, verified**: the published figures are burst **4 requests/second** and
  sustained **500 requests/hour** (unauthenticated), from `jolpica/jolpica-f1/docs/rate_limits.md`. The app had
  been pacing Jolpica at **120/min (~7 200/hour) in one bucket plus a second 120/min bucket for the champions
  wall**, i.e. roughly fifteen times the published budget - and two buckets defeat the point of a gate, since
  the limit is counted per IP. Now one gate: `ProviderLimits.JolpicaPerMinute = 8` (480/hour) with burst 4,
  injected into every Jolpica client. `ProviderLimitsTests` asserts the arithmetic so the figure cannot drift
  back. Consequence, stated plainly: a first full vault sync (77 seasons) now takes about ten minutes instead
  of one, which is why the Archive reports "syncing archive: N/77 seasons" and the sync resumes where it stops.
- 2026-11-22 **F1 media CDN, measured** (all keyless): driver cutouts at
  `.../common/f1/2026/<team>/<drvid>/2026<team><drvid>right.webp` — 24/24 answered 200 at `w_720` (124 KB);
  team cars `2026<team>carright.webp` — 11/11 200, 20–41 KB, and `w_2000` also 200; team logos
  `2026<team>logowhite.webp` 746 B; circuit cards `fom-website/static-assets/2026/races/card/<slug>.webp` —
  22 of 23 200, **`united-arab-emirates` is 404** (the official calendar page publishes no card for that
  round either). Paths are constructed, not published as a contract: OpenF1's `headshot_url` is a different,
  older scheme (`content/dam/fom-website/drivers/...transform/1col/image.png`, the 93 px crop), so the good
  cutouts exist only in a table of verified ids, with fallbacks.
- 2026-11-28 **Ailoha set up** (CLI 0.2.4, package `Ailoha.Agent.Maui` 0.2.4 via CPM, registered `#if DEBUG`
  after `UseMauiApp`). Working end to end: `agent list`, `diagnose`, `device app`, `ui screenshot`,
  `ui tap <AutomationId>`, `logs`. Proven rather than assumed — `ui tap PitWallSection2` switched the pit wall to
  NEWS and the screenshot showed it.
  **Broken in the CLI, not the app:** `ui tree` and `ui query` both return `[]`, while the agent's own HTTP API
  answers with the full tree (`curl "http://127.0.0.1:11233/api/v1/ui/tree?depth=16"` → Window → AppShell →
  FlyoutItems → PitWallPage → Grids/Labels/Buttons). Passing `--agent-port` explicitly changes nothing, so the
  CLI is the failing layer. Workaround: read the tree over HTTP, address elements by AutomationId.
  **Side effect worth knowing:** this agent serialises a control's `AutomationId` into the tree's `text` field,
  so adding ids to the pit-wall section buttons made `ui query --text "TIMING"` stop matching them. Ids are the
  better handle; text queries simply will not match an element that has one.
- 2026-11-28 **Ailoha, debug-only verified both ways.** Debug: the three Ailoha assemblies are in the output,
  `agent wait` returns the agent on port 11233, `device app` names the app, `ui tap PitWallSection0` and a
  screenshot confirm it drives the app. Release: no Ailoha assembly in the output, the running process opens no
  port, and its stdout never mentions Ailoha. The package reference is conditioned on `Configuration == Debug`
  as well as the registration, so a release build cannot ship the agent even by accident.
  Note for whoever tests this next: `agent list` keeps stale entries after an app exits (it reported 2 agents
  with none running), so it is not evidence either way — the output folder and the process's own listening
  socket are.
- 2026-11-28 **the meeting name was missing from the app, not just from `SessionRequest`.** `WeekendSchedule`
  already had a `MeetingName` field, but the weekend loader filled it with `"LOSAIL 2026"` — a circuit and a year,
  not a race. OpenF1's *sessions* payload carries `meeting_key` but no name; the *meetings* payload carries
  `meeting_name` ("Qatar Grand Prix") and was already cached, so the loader now joins it by key. Consequence:
  the pit wall can show its circuit card, and the catalogue can be keyed on real race names. Names still differ
  per publisher — Jolpica says "Brazilian Grand Prix", OpenF1 says "São Paulo Grand Prix" — so the media catalogue
  accepts both and a test pins that they resolve to the same verified card.
- 2026-11-28 **"phone shell in a desktop window" was not a phone pass.** `F1APP_PHONE=1` switched the shell but
  left the window at ~1400 pt, so the screenshots showed the phone *arrangement* at desktop width and answered
  nothing; the harness even skipped its resize loop in phone mode. With the window at 390x844: Paddock's portrait
  ate a third of the screen, and the Results classification clipped four columns (S1/S2/S3 above all, plus GAP,
  TYRE, PIT and OFFICIAL off-screen) with no way to reach them. Both fixed and re-shot. The WinUI tab bar shows
  two of five tabs plus an overflow at that width — noted, not patched, because Android draws its own bar and is
  the shipping target.
- 2026-11-28 **Ailoha on Android, checked for real** (emulator `pixel_7_-_api_36_0`, API 36 x86_64). Four things
  had to be fixed to get there, and the last two are defects in this project rather than the environment:
  1. `ANDROID_HOME` points at `%LOCALAPPDATA%\Android\Sdk`, which holds **only platform-tools**. The real SDK
     lives at `C:\Program Files (x86)\Android\android-sdk` (build-tools 36/37, platforms 36/37, emulator,
     android-36 system image). Every tool inherits the wrong root, so pass `-p:AndroidSdkDirectory=...` and set
     `ANDROID_SDK_ROOT` for the emulator. This is why the earlier Android build failed with `XA5205 aapt2`.
  2. **My own bug:** the `F1APP_CLICK` real-pointer harness used `Microsoft.UI.Xaml` and `WinRT.Interop`
     unguarded, so the Android TFM did not compile at all (5 errors, all in `AppShell.cs`). Now `#if WINDOWS`.
  3. **Debug APKs use fast deployment**: `adb install` of a Debug APK ships a runtime with no
     `System.Private.CoreLib.dll`, and the app aborts at `host.cc:452` during runtime init with
     `CLR error: Failed to load System.Private.CoreLib.dll`. Build with `-p:EmbedAssembliesIntoApk=true` when
     installing by hand.
  4. **Android chrome was .NET purple** (`#512BD4` from the template's `colors.xml`), which Windows never showed
     because the Shell paints its own bar there. Now the app palette; verified by screenshot.
  Result: `[Ailoha.Agent] HTTP server started on port 9233` in logcat, `adb forward tcp:9233 tcp:9233`, and then
  `ailoha android device app` / `ui screenshot` work against the emulator. The Android tab bar draws **all five
  destinations with icons**, unlike WinUI at 390 pt which collapses to two plus an overflow.

- 2026-11-28 **official live-timing hub, probed between sessions.** `OfficialLiveTimingClient.ObserveAsync` ran for
  20 s at ~09:50 UTC (Qatar weekend, between Practice 2 and Practice 3): negotiate answered, the WebSocket
  connected, the subscribe was accepted, and exactly **one** frame arrived — `{"type":6}`, SignalR's keepalive.
  Nothing else, which is what the type's remarks already claimed but had never been measured with a capture file.
  An 8 s probe caught zero frames (the keepalive is every 15–30 s), so a short probe outside a session proves only
  that the connection works. `tools/live-probe/probe.cs` reproduces this without the UI.

- 2026-11-28 **Archive scroll measured, and the first measurement was worthless.** On the emulator with
  `-gpu swiftshader_indirect` (software) a 20-swipe scroll measured **72.5% janky, p50 65 ms** for the 740-row
  Archive — and **72.88%, p50 81 ms** for the 23-row Season screen. Identical, which means the instrument was the
  bottleneck and the comparison proved nothing. Re-run with `-gpu host` (Intel UHD 630):

  | screen | frames | janky | p50 | p90 |
  | --- | --- | --- | --- | --- |
  | Season, 23 rows | 336 | 9.52% | 31 ms | 46 ms |
  | Archive, 740+ rows | 299 | 24.08% | 34 ms | 57 ms |

  So the long list does cost about 2.5x the jank of a short one, on this hardware. Two honest limits: it is still
  an emulator with an integrated GPU, so a flagship phone will be better and a cheap one worse; and the baseline
  itself (9.5% on 23 rows) is not excellent, so the row template may be worth a look before blaming row count.
  Incremental loading is therefore **not** proven necessary — the numbers say "measurable", not "unusable".
- Lesson worth keeping: an A/B where both sides are equally bad is not evidence. Measure a control before
  concluding a screen is slow.

- 2026-11-28 **Archive row template: fixed height tested, floor not explained.** Each Archive row is seven labels
  inside six nested containers, and its height was a `MinimumHeightRequest`, so every row was measured. Setting a
  fixed 64 pt height (same 20-swipe run, hardware GPU) moved janky from 24.08% to **23.23%** and p90 from 57 ms to
  **48 ms**, p50 34 -> 32 ms. So the change is worth keeping for the tail and does **not** explain the ~9.5%
  baseline a 23-row screen also shows. The remaining suspect is element count per row; testing that means
  redesigning the row, which is a layout change rather than a knob, so it is left as a decision.

- 2026-11-28 **the capture tool now answers the parser question, and proved it can say yes.** `probe.cs` reads
  the capture and reports what `TimingDataParser` makes of it, with three distinct verdicts: real TimingData
  arrived, the hub was silent, or the file is not one JSON object per line. All three were exercised:
  a keepalive-only capture from the hub -> "sent no TimingData"; the unit-test fixture written pretty-printed ->
  "not one JSON object per line" (11 lines, none valid); the same fixture compacted to one line -> "real
  TimingData arrived, 3 standings across 3 drivers", matching what the unit test asserts.
  The middle case is the one worth keeping: without it, a pretty-printed capture would have read exactly like a
  silent hub at 14:30, and the wrong conclusion would have been recorded as fact.
