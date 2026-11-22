# F1.com look and feel, inside the existing app

2026-11-22 · design

## Intent

The app should read as Formula 1's own product. Today it is a HUD: near-black, monospaced
numerals, one red reserved for live state. That language suits a timing tower and suits nothing
else — the Season, Archive, Paddock and Facts screens look like a telemetry tool wearing a
calendar.

The goal is to adopt **F1.com's visual language** — its palette, its type rhythm, its use of
imagery — without adopting its dishonesty: no invented data, no mock assets, no "none happened"
where the source says "not published".

## What was measured before designing

All URLs below were requested today against `media.formula1.com` with no API key.

| Asset | Pattern | Result |
| --- | --- | --- |
| Driver cutout, transparent, right-facing | `/image/upload/c_lfill,w_720/q_auto/v1740000001/common/f1/2026/<team>/<drvid>/2026<team><drvid>right.webp` | 200, 124 KB |
| Team car cutout | `/image/upload/c_lfill,w_720/q_auto/v1740000001/common/f1/2026/<team>/2026<team>carright.webp` | 200 for **all 11** 2026 teams, 20–41 KB; `w_2000` also 200 |
| Team logo, white | `/image/upload/c_lfill,w_256/q_auto/v1740000001/common/f1/2026/<team>/2026<team>logowhite.webp` | 200, 746 B |
| Circuit card, 16:9 | `/image/upload/c_lfill,w_720/q_auto/v1740000001/fom-website/static-assets/2026/races/card/<slug>.webp` | 200, 39 KB (`w_1296` also 200) |

Two facts that shape the design:

1. **OpenF1 does not ship these.** Its `drivers.headshot_url` is a legacy
   `content/dam/fom-website/drivers/...transform/1col/image.png` path — the 93 px crop the Paddock
   currently shows. The cutouts require **constructing** CDN paths, which is a verified guess, not a
   published contract. Every constructed URL therefore needs a fallback chain.
2. The media host is F1's own CDN. These are **F1's copyrighted assets**, used as F1 publishes them.

## Goals

- Every screen carries F1's visual language: near-black canvas, F1 red as the single accent,
  condensed uppercase headings, 4–6 pt corners, hairline dividers, generous space.
- Driver cutouts, team cars, team logos and circuit cards appear where F1.com shows them.
- The timing tower, track map and pit wall adopt the type, colour and header treatment **without
  losing scan speed** at 22 rows.
- Every image carries a visible credit line.

## Non-goals

- No scraping of f1.com HTML pages. Only CDN media URLs, which need no key.
- No images bundled, downloaded, resized and stored, or redistributed. Nothing is written to disk.
- No mock or generated artwork, ever. A missing image shows a designed fallback, not something that
  could be mistaken for real data.

## Anti-goals

- **If the timing tower becomes slower to read** — gaps, sectors, tyre age — the change has failed
  no matter how it looks. Density wins over airiness on that one screen.
- **If any credit line is dropped**, the change has failed: the credit is the condition of use.
- If a constructed URL 404s and the app shows a broken image or an empty box, the change has failed.

## Constraints

- Open, keyless sources only; `media.formula1.com` answered 200 with no key (measured today).
- **Memory-only image cache.** A bounded LRU keyed by URL, held for the session. No disk, no
  `FileSystem.CacheDirectory`, nothing to redistribute.
- Constructed URLs must fall back: cutout → OpenF1 headshot → Commons → initials on a team-coloured
  tile. Each step is a real source, and the fallback is visible, not silent.
- `dotnet format --verify-no-changes` stays clean for Telemetry and Domain; tests stay green; the
  smoke walk stays at 0 `PAGE`/`CRASH`.

## Approaches considered

**A. Palette and type only.** Re-skin the existing layouts with F1's colours and type. Cheapest and
lowest risk, but it addresses nothing that actually makes F1.com look like F1.com: the imagery and
the card composition do that work.

**B. Data screens only.** Season, Archive, Paddock, Results and Facts adopt F1.com components; the
HUD screens keep their current language. Honest split — F1.com's look where F1.com has an
equivalent page — and it protects the tower by leaving it alone.

**C. Full re-skin including the HUD.** *(chosen)* Every screen, the timing tower and the track map
included. Chosen by the user. It is the largest blast radius of the three: the HUD screens are dense
by necessity and F1.com has no equivalent to copy, so their treatment is an invention that must be
judged by scanning speed, not by resemblance.

## Design

### 1. Tokens

`Theme/Tokens.cs` and `Theme/Palette.cs` are reworked, not extended with a second system:

- Canvas `#15151E`, elevated surface `#1F1F27`, hairline `#2B2B33`, text `#FFFFFF` / `#949498`.
- Accent: F1 red `#E10600`, used for live state, selection and the section chevron — nothing else.
- Type: headings uppercase, 700 weight, tracking tightened; numerals keep the mono face so timing
  columns still align.
- Radii: 4 pt for cards, 2 pt for pills. F1.com is squarer than this app currently is.

### 2. Components (`Theme/Surfaces.cs`)

Four primitives, built once and reused by every screen:

- `HeroCard` — circuit card image, round number, GP name, date strip. Used by Season, Archive and
  the top of the pit wall.
- `DriverRow` — cutout, three-letter code, surname, team logo, right-aligned value column. This is
  the standings table's row and the Paddock's roster row.
- `TeamCard` — car cutout over the team colour, team name, drivers.
- `CreditedImage` — an image plus its credit line, and the fallback chain behind it.

### 3. Screens

- **Season** — round cards with circuit art, "NEXT" hero card.
- **Archive** — race rows gain the circuit card thumbnail; record book keeps the type.
- **Paddock** — full-body cutout, team car below it, team logo beside the code.
- **Results** — standings table in `DriverRow` form.
- **Facts** — champions wall with cutouts.
- **Timing / Track / Pit wall** — F1 type and colour, `HeroCard` header strip, then the dense grid.
  No card layout for the rows: the tower keeps its single grid so 22 rows stay scannable, and the
  hero strip is what carries the F1 identity.

### 4. Image system

- `Telemetry/Media/F1MediaUrls.cs` — builds cutout / car / logo / circuit-card URLs from team slug,
  driver id and race slug; returns `null` when the slug is unknown rather than guessing.
- `Telemetry/Media/F1ImageCache.cs` — bounded LRU, memory only, keyed by URL, reports hit rate so the
  cost is measurable.
- `Telemetry/Media/F1ImageSource.cs` — resolves a driver's image: cutout → OpenF1 `headshot_url` →
  Commons → nothing. Same for a team: car → logo → nothing.

### 5. The identifier catalogue (my call, stated)

A CDN URL needs identifiers the app's feeds do not carry: `andant01`, `redbullracing`,
`las-vegas`. F1's driver ids are **not derivable** — `georus01`, `lewham01`, `chalec01` and
`maxver01` show no rule — so building them from a name would be a guess dressed as code.

Decision: one static catalogue, `Telemetry/Media/F1MediaCatalog.cs`, holding the 2026 grid's driver
ids, the 11 team slugs and the race slugs. Every entry is verified by a real request when it is
added, and the file says so. Anything not in the catalogue resolves to `null` and takes the fallback
chain — no guessed URL is ever requested. New season: extend the table, verify, and the fallbacks
already cover the gap while it is missing.

This keeps the honesty rule intact: a URL is either known-good or not requested at all.

### 6. Credits

One line under each image, in the existing credit style:
`Portrait: Formula 1 media · Car: Formula 1 media · Circuit: Formula 1 media`.
Commons-sourced images keep their author and licence, as they already do.

## Typography

F1.com's display face is `Formula1 Display Web` — a tight, squarish grotesque, and **not licensed for this
app**. The open substitute is **Barlow Semi Condensed** (SIL Open Font License 1.1, Jeremy Tribby): the same
condensed grotesque proportions, flat terminals and a squarish `R`, shipped as static TTFs so no variable-font
support is needed on Android. Bold and SemiBold only — two weights, not nine.

Verified today against `google/fonts` (all 200): `BarlowSemiCondensed-Bold.ttf` 112 KB,
`BarlowSemiCondensed-SemiBold.ttf` 110 KB, and its `OFL.txt` (4.4 KB). The OFL file ships beside the fonts,
as the licence requires, and the credit goes in `docs/plan.md` with the rest of the attribution list.

Role split: **Barlow Semi Condensed 700/600** for headings, labels and codes; **Barlow Semi Condensed 400**
for body text; the existing monospaced face stays for numerals, because tabular alignment matters more than
typographic consistency in the timing tower. OpenSans stays in the project but loses its heading role.

## What is left to the builder

- Exact grid geometry per screen, decided against screenshots, not in the abstract.
- Which screens get a car cutout versus a circuit card.
- How much of the tower's current table survives contact with the new type scale — measured by
  scanning a 22-row screenshot, then adjusted.
- Whether the F1 type rhythm needs a condensed face; today the app ships no font asset, and adding
  one has a licence cost that has not been accepted. — **settled: Barlow Semi Condensed, OFL, above.**

## Verification

1. Screenshot every screen before the change and after (`F1APP_SHOTS=1`), and compare them by looking.
2. Tests: URL builder (known slug → exact URL; unknown slug → null), cache (evicts, never writes to
   disk), fallback order (cutout 404 → headshot).
3. `dotnet format` clean; full test suite; both smoke walks with real clicks at 0 `PAGE`/`CRASH`.
4. Read the 22-row timing screenshot and confirm the row order and gaps are still scannable at a
   glance — the anti-goal, checked by eye and stated plainly.
5. Font licence: `OFL.txt` present beside the TTFs, and the app credits Barlow Semi Condensed in
   `docs/plan.md`. A missing licence file is a shipping blocker, not a nit.
