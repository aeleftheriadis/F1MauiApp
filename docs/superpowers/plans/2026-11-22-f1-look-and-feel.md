# F1.com Look and Feel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every screen of the app read as Formula 1's own product — F1's palette, condensed type, and its driver/car/logo/circuit imagery — without weakening a single honesty rule about data.

**Architecture:** Three layers, built in order. First the media system (URL catalogue, in-memory image cache, fallback resolution) because it is the only part with real logic worth unit tests. Then the tokens and components every screen draws from. Then the thirteen screens, re-skinned onto those components, verified screen by screen with the screenshot harness. The timing tower keeps its single dense grid throughout; only its type, colour and header strip change.

**Tech Stack:** .NET 11 MAUI (net11.0-windows10.0.19041.0 for the dev loop, Android as the shipping target), xUnit, `System.Net.Http`, SIL OFL font assets, F1's keyless media CDN.

**Spec:** `docs/superpowers/specs/2026-11-22-f1-look-and-feel-design.md`

## Global Constraints

- Open, keyless sources only. `media.formula1.com` requires no key (verified 2026-11-22).
- **Images are hotlinked and held in memory only.** Nothing from `media.formula1.com` may be written to disk, cached in `FileSystem.CacheDirectory`, or bundled as an asset.
- Every image carries a visible credit line: `Formula 1 media` for CDN assets; author and licence for Commons assets.
- A URL is either known-good in the catalogue or **not requested at all**. No constructed-and-hoped URLs.
- Palette values: canvas `#15151E`, elevated `#1F1F27`, hairline `#2B2B33`, text `#FFFFFF` / `#949498`, accent `#E10600`.
- Radii: 4 pt cards, 2 pt pills.
- Font: Barlow Semi Condensed (OFL 1.1), files `BarlowSemiCondensed-Regular.ttf`, `-SemiBold.ttf`, `-Bold.ttf`, plus `OFL.txt` beside them in `src/F1App.App/Resources/Fonts/`.
- Numerals stay on the existing monospaced face.
- `dotnet format --verify-no-changes` clean for `F1App.Telemetry` and `F1App.Domain`; app project builds with 0 errors; test suite green; both smoke walks (wide, phone) at 0 `PAGE`/`CRASH`.
- The 2026 grid catalogue is data, not code: extend it by verifying each new entry with one real request.

## Review Focus

- **Timing tower at 22 rows.** A person expects to scan positions, gaps and sectors down the column without re-reading a row twice. If the new type scale or spacing slows that, the row grid must shrink back — card layout is not an option there.
- **A driver whose cutout is not in the catalogue** (a mid-season replacement, a rookie added next year). Expect a real portrait from the OpenF1 headshot or Commons, or a designed initials tile — never a broken box, an empty frame or someone else's face.
- **F1 CDN unavailable or slow** (offline, blocked network, CDN change). Expect the screen to render completely, with fallbacks, and never to block on an image.
- **A race whose slug is not in the catalogue** (a new venue, or a renamed GP). Expect the circuit area to fall back to the existing OpenF1 track icon rather than a blank card.
- **Screen-reader text for every image and credit.** A person using a screen reader must not be told "image" nine times; each graphic needs its own sentence, and the credit must be reachable, not decorative.

---

### Task 1: Font assets and registration

**Files:**
- Create: `src/F1App.App/Resources/Fonts/BarlowSemiCondensed-Regular.ttf`, `-SemiBold.ttf`, `-Bold.ttf`, `OFL.txt`
- Modify: `src/F1App.App/MauiProgram.cs` (font registration block)
- Modify: `src/F1App.App/Theme/Tokens.cs`
- Test: none (asset + registration); verified by the screenshot in Task 9

**Interfaces:**
- Produces: `Tokens.DisplayFont`, `Tokens.BodyFont` (`string`), and `Tokens.FontDisplay` value used by later tasks. Existing `Tokens.MonoFont` stays untouched.

- [ ] **Step 1: Fetch the fonts and their licence**

```bash
cd /c/projects/F1MauiApp/src/F1App.App/Resources/Fonts
B=https://raw.githubusercontent.com/google/fonts/main/ofl/barlowsemicondensed
curl -sL -o BarlowSemiCondensed-Regular.ttf  $B/BarlowSemiCondensed-Regular.ttf
curl -sL -o BarlowSemiCondensed-SemiBold.ttf $B/BarlowSemiCondensed-SemiBold.ttf
curl -sL -o BarlowSemiCondensed-Bold.ttf     $B/BarlowSemiCondensed-Bold.ttf
curl -sL -o OFL.txt                          $B/OFL.txt
```
Expected: four files, each non-empty; `OFL.txt` starts with "Copyright" and names SIL Open Font License 1.1.

- [ ] **Step 2: Register the aliases in `MauiProgram.cs`**

Inside the existing `ConfigureFonts` call, add:

```csharp
fonts.AddFont("BarlowSemiCondensed-Regular.ttf", "BarlowRegular");
fonts.AddFont("BarlowSemiCondensed-SemiBold.ttf", "BarlowSemiBold");
fonts.AddFont("BarlowSemiCondensed-Bold.ttf", "BarlowBold");
```

- [ ] **Step 3: Add the token properties in `Tokens.cs`**

```csharp
/// <summary>The display face: condensed grotesque, the closest open relative of F1's own.</summary>
public const string DisplayFont = "BarlowBold";

/// <summary>Body weight of the same family.</summary>
public const string BodyFont = "BarlowRegular";

/// <summary>Semi-bold, for labels and codes.</summary>
public const string CodeFont = "BarlowSemiBold";
```

- [ ] **Step 4: Verify the build and format**

Run: `RTK_DISABLED=1 dotnet build src/F1App.App/F1App.App.csproj -f net11.0-windows10.0.19041.0 --nologo -v q 2>&1 | grep -cE " error "`
Expected: `0`. A wrong alias makes the app log a font error at runtime, not at build time, so Task 9's screenshot is the real check.

---

### Task 2: Media URL catalogue

**Files:**
- Create: `src/F1App.Telemetry/Media/F1MediaCatalog.cs`
- Test: `tests/F1App.Telemetry.Tests/F1MediaCatalogTests.cs`

**Interfaces:**
- Produces:
  - `public static class F1MediaCatalog`
  - `public static Uri? DriverCutout(string? driverAcronym)`
  - `public static Uri? TeamCar(string? teamName)`
  - `public static Uri? TeamLogo(string? teamName)`
  - `public static Uri? CircuitCard(string? raceSlug)`
  - Each returns `null` when the identifier is absent from the catalogue — callers must treat `null` as "do not request".

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void DriverCutout_ReturnsTheVerifiedUrlForAKnownDriver()
{
    var url = F1MediaCatalog.DriverCutout("ANT");

    Assert.Equal(
        "https://media.formula1.com/image/upload/c_lfill,w_720/q_auto/v1740000001/common/f1/2026/mercedes/andant01/2026mercedesandant01right.webp",
        url?.ToString());
}

[Fact]
public void DriverCutout_ReturnsNullForAnUnknownDriver()
{
    Assert.Null(F1MediaCatalog.DriverCutout("ZZZ"));
    Assert.Null(F1MediaCatalog.DriverCutout(null));
}

[Fact]
public void TeamCar_ReturnsACarCutoutForEveryKnownTeam()
{
    var teams = new[] { "Mercedes", "Red Bull Racing", "Ferrari", "McLaren", "Racing Bulls", "Alpine",
                        "Haas F1 Team", "Audi", "Williams", "Aston Martin", "Cadillac" };

    foreach (var team in teams)
    {
        Assert.NotNull(F1MediaCatalog.TeamCar(team));
        Assert.NotNull(F1MediaCatalog.TeamLogo(team));
    }
}

[Fact]
public void CircuitCard_KnownRaceSlug_ReturnsTheCard()
{
    Assert.EndsWith("/races/card/qatar.webp", F1MediaCatalog.CircuitCard("qatar")?.ToString());
}

[Fact]
public void CircuitCard_UnknownSlug_ReturnsNull()
{
    Assert.Null(F1MediaCatalog.CircuitCard("never-heard-of-it"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter F1MediaCatalogTests`
Expected: build error — `F1MediaCatalog` does not exist.

- [ ] **Step 3: Implement the catalogue**

A `FrozenDictionary` from driver acronym to `(teamSlug, driverId)`, from team name to slug, and a `HashSet` of race slugs. Populate the driver map with the 24 entries verified on 2026-11-22 (ANT→`mercedes/andant01`, VER→`redbullracing/maxver01`, HAM→`ferrari/lewham01`, RUS→`mercedes/georus01`, NOR→`mclaren/lannor01`, LEC→`ferrari/chalec01`, PIA→`mclaren/oscpia01`, HAD→`redbullracing/isahad01`, LAW→`racingbulls/lialaw01`, GAS→`alpine/piegas01`, LIN→`racingbulls/arvlin01`, COL→`alpine/fracol01`, BEA→`haasf1team/olibea01`, BOR→`audi/gabbor01`, HUL→`audi/nichul01`, SAI→`williams/carsai01`, OCO→`haasf1team/estoco01`, ALO→`astonmartin/feralo01`, ALB→`williams/alealb01`, TSU→`racingbulls/yuktsu01`, HIR→`haasf1team/ryohir01`, STR→`astonmartin/lanstr01`, BOT→`cadillac/valbot01`, PER→`cadillac/serper01`, CAM→`haasf1team/rafcam01`). The file's XML doc states the verification date and that an unknown key returns `null` on purpose.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter F1MediaCatalogTests`
Expected: PASS, 5 tests.

---

### Task 3: In-memory image cache

**Files:**
- Create: `src/F1App.App/Services/ImageMemoryCache.cs`
- Test: `tests/F1App.Telemetry.Tests/ImageMemoryCacheTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public sealed class ImageMemoryCache(int capacity)`
  - `public bool TryGet(string key, out byte[] bytes)`
  - `public void Set(string key, byte[] bytes)`
  - `public int Count { get; }`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Set_ThenTryGet_ReturnsTheBytes()
{
    var cache = new ImageMemoryCache(2);
    cache.Set("a", [1, 2, 3]);

    Assert.True(cache.TryGet("a", out var bytes));
    Assert.Equal([1, 2, 3], bytes);
}

[Fact]
public void Set_BeyondCapacity_EvictsTheOldestEntry()
{
    var cache = new ImageMemoryCache(2);
    cache.Set("a", [1]);
    cache.Set("b", [2]);
    cache.Set("c", [3]);

    Assert.False(cache.TryGet("a", out _));
    Assert.True(cache.TryGet("c", out _));
    Assert.Equal(2, cache.Count);
}

[Fact]
public void TryGet_MissingKey_ReturnsFalse()
{
    Assert.False(new ImageMemoryCache(4).TryGet("nope", out _));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter ImageMemoryCacheTests`
Expected: build error, `ImageMemoryCache` not found.

- [ ] **Step 3: Implement it**

`LinkedList<string>` for recency order plus a `Dictionary<string, (LinkedListNode<string> Node, byte[] Bytes)>`, a `Lock`, and eviction from the tail when `Count > capacity`. Constructor throws on `capacity < 1`. No file I/O anywhere in the file — the class doc says why (the media licence is hotlink-only).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter ImageMemoryCacheTests`
Expected: PASS, 3 tests.

---

### Task 4: Image source with the fallback chain

**Files:**
- Create: `src/F1App.App/Services/F1ImageSource.cs`
- Modify: `src/F1App.App/MauiProgram.cs` (register the cache and the source)
- Test: `tests/F1App.Telemetry.Tests/F1ImageSourceTests.cs`

**Interfaces:**
- Consumes: `F1MediaCatalog` (Task 2), `ImageMemoryCache` (Task 3).
- Produces:
  - `public sealed class F1ImageSource(HttpClient http, ImageMemoryCache cache)`
  - `public Task<ImageSource?> DriverAsync(string acronym, Uri? openF1Headshot, Uri? commonsImage, CancellationToken ct)`
  - The chain is exactly the spec's: cutout → OpenF1 headshot → Commons → `null` (initials tile).
  - `public Task<ImageSource?> TeamCarAsync(string teamName, CancellationToken ct)`
  - `public Task<ImageSource?> CircuitAsync(string? raceSlug, string? circuitShortName, CancellationToken ct)`
  - Returns `null` when every source is unavailable — the caller draws a designed fallback.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task DriverAsync_CutoutReturns404_FallsBackToTheOpenF1Headshot()
{
    var handler = new StubHttpHandler();
    handler.Map(F1MediaCatalog.DriverCutout("ANT")!.ToString()!, 404);
    handler.Map("https://media.formula1.com/headshot.png", 200, [0x89, 0x50]);

    var source = new F1ImageSource(new HttpClient(handler), new ImageMemoryCache(8));
    var result = await source.DriverAsync(
        "ANT", new Uri("https://media.formula1.com/headshot.png"), null, CancellationToken.None);

    Assert.NotNull(result);
    Assert.StartsWith("https://media.formula1.com/headshot.png", ((UriImageSource)result!).Uri.ToString());
}

[Fact]
public async Task DriverAsync_EverythingUnavailable_ReturnsNull()
{
    var handler = new StubHttpHandler();
    handler.Map(F1MediaCatalog.DriverCutout("ZZZ")?.ToString() ?? "unused", 404);

    var source = new F1ImageSource(new HttpClient(handler), new ImageMemoryCache(8));

    Assert.Null(await source.DriverAsync("ZZZ", null, null, CancellationToken.None));
}

[Fact]
public async Task DriverAsync_Success_IsServedFromTheMemoryCacheTheSecondTime()
{
    var handler = new StubHttpHandler();
    handler.Map(F1MediaCatalog.DriverCutout("ANT")!.ToString()!, 200, [0x01]);

    var cache = new ImageMemoryCache(8);
    var source = new F1ImageSource(new HttpClient(handler), cache);
    var url = new Uri(F1MediaCatalog.DriverCutout("ANT")!.ToString()!);

    await source.DriverAsync("ANT", null, null, CancellationToken.None);
    await source.DriverAsync("ANT", null, null, CancellationToken.None);

    Assert.Equal(1, handler.RequestCount);
    Assert.True(cache.TryGet(url.ToString(), out _));
}
```

Reuse the existing `StubHttpHandler` pattern from `tests/F1App.Telemetry.Tests/JolpicaClientTests.cs`: add `Map(string url, int status, byte[]? body = null)` and a `RequestCount`.

- [ ] **Step 2: Run it and watch it fail**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter F1ImageSourceTests`
Expected: build error, `F1ImageSource` not found.

- [ ] **Step 3: Implement it**

Commons is the existing `WikimediaImageClient`'s URL, passed in by the caller so this class does no
Commons-specific work. Probe with `HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)`; on 200 and an `image/*` content type, cache the bytes and return `ImageSource.FromUri(url)`; on any failure or non-2xx, try the next source; when the chain is exhausted return `null`. Never throw: a missing picture is not an error the user should see. Registered as a singleton.

- [ ] **Step 4: Run the tests**

Run: `RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q --filter F1ImageSourceTests`
Expected: PASS, 3 tests.

---

### Task 5: Tokens and palette

**Files:**
- Modify: `src/F1App.App/Theme/Palette.cs`, `src/F1App.App/Theme/Tokens.cs`

**Interfaces:**
- Produces: unchanged public shape (`Palette` properties, `Tokens` constants) with new values. No new API — every screen already consumes these.

- [ ] **Step 1: Set the palette values**

`Palette` dark: `Background #15151E`, `Surface #1F1F27`, `SurfaceMuted #24242D`, `Stroke #2B2B33`, `TextPrimary #FFFFFF`, `TextSecondary #949498`, `Live #E10600`. Light palette keeps its existing relationships but moves to the same red.

- [ ] **Step 2: Set the type and radius tokens**

`FontCaption 12`, `FontBody 15`, `FontSubtitle 17`, `FontTitle 20`, `FontDisplay 28` stay; add `RadiusCard 4`, `RadiusPill 2`. Headings become uppercase at the call sites in Task 6, not by string-casing inside tokens.

- [ ] **Step 3: Build and screenshot**

Run the smoke walk with `F1APP_SMOKE=1 F1APP_SHOTS=1`, then open `shots/pitwall.png`.
Expected: the app still renders, colours shifted, no `PAGE`/`CRASH` in the log. Nothing should be unreadable — contrast against `#15151E` for `#949498` is the one to check by eye.

---

### Task 6: Components

**Files:**
- Modify: `src/F1App.App/Theme/Surfaces.cs`
- Create: `src/F1App.App/Theme/F1Components.cs`

**Interfaces:**
- Consumes: `Tokens`, `Themes.Current`, `F1ImageSource` (Task 4).
- Produces:
  - `public static View HeroCard(string round, string title, string subtitle, Uri? image, string credit)`
  - `public static View DriverRow(string code, string name, string team, string value, Uri? cutout, Uri? logo)`
  - `public static View TeamCard(string team, Uri? car, Uri? logo, string credit)`
  - `public static View CreditedImage(ImageSource? source, string credit, string description)`

- [ ] **Step 1: Build `CreditedImage` first**

An `Image` plus a `Label` in a `VerticalStackLayout`; the label is `Formula 1 media`, 10 pt, `TextSecondary`; `SemanticProperties.Description` is set from `description` on the card and the credit label stays readable to a screen reader. When `source` is `null`, render the initials tile instead — a `Border` on the team colour with the three-letter code centred.

- [ ] **Step 2: Build `DriverRow`, `TeamCard`, `HeroCard`**

`DriverRow`: fixed 56 pt height, cutout 40 pt wide on the left, code in `CodeFont`, name in `BodyFont`, logo 16 pt, value right-aligned in the mono face. `HeroCard`: 16:9 image with the round/date strip over it, 4 pt radius. `TeamCard`: car cutout on a `SurfaceMuted` panel.

- [ ] **Step 3: Compile**

Run: `RTK_DISABLED=1 dotnet build src/F1App.App/F1App.App.csproj -f net11.0-windows10.0.19041.0 --nologo -v q 2>&1 | grep -cE " error "`
Expected: `0`.

---

### Task 7: Paddock (the reference screen)

**Files:**
- Modify: `src/F1App.App/Features/Paddock/PaddockPage.cs`, `PaddockViewModel.cs`

**Interfaces:**
- Consumes: `F1ImageSource.DriverAsync`, `TeamCarAsync` (Task 4); `DriverRow`, `TeamCard`, `CreditedImage` (Task 6).

- [ ] **Step 1: Replace the roster and dossier**

Roster rows become `DriverRow`. The dossier shows the full-body cutout at the top, the team car below it, the team logo beside the code, and the credit lines under each image.

- [ ] **Step 2: Make the fallback visible in the view model**

`WikimediaImageClient` already resolves a Commons portrait; pass its URL as the third argument.
`PaddockViewModel` gains `DriverImage` / `CarImage` (`ImageSource?`) and keeps `PortraitUrl` for the credit test. When `DriverAsync` returns `null`, `DriverImage` stays `null` and the page draws the initials tile.

- [ ] **Step 3: Screenshot and look**

Run: `F1APP_SMOKE=1 F1APP_SHOTS=1` and open `shots/paddock.png`.
Expected: a real cutout for Norris, a car cutout, two credit lines, no empty frames. If the cutout fails to load, the initials tile shows the code `NOR` — and that is what gets investigated, not shipped.

---

### Task 8: Season, Archive, Results, Facts

**Files:**
- Modify: `Features/Season/SeasonHubPage.cs`, `Features/Archive/ArchivePage.cs`, `Features/Results/ResultsPage.cs`, `Features/Facts/FactsPage.cs` and their view models

- [ ] **Step 1: Season** — round cards become `HeroCard` with circuit art; the next race is the largest card.
- [ ] **Step 2: Archive** — each race row gains a circuit card thumbnail 64×36; the record book keeps its type.
- [ ] **Step 3: Results** — the classification becomes `DriverRow` rows with cutouts and team logos.
- [ ] **Step 4: Facts** — champion rows gain cutouts where the catalogue has the code, initials tiles where it does not.
- [ ] **Step 5: Screenshot all four and read them**

Run: `F1APP_SMOKE=1 F1APP_SHOTS=1`, then open `season.png`, `archive.png`, `results.png`, `facts.png`, and fix what looks wrong (this is the step that found eight defects last time).

---

### Task 9: The HUD screens

**Files:**
- Modify: `Features/PitWall/PitWallPage.cs`, `Features/TrackMap/TrackMapPage.cs`, `Features/Strategy/StrategyPage.cs`, `Features/Timing/*`, `Features/Compare/ComparePage.cs`

- [ ] **Step 1: Header strip only** — the tower gains a `HeroCard` strip (circuit art, session, lap counter) above the existing grid. The grid itself keeps its columns and 22-row density.
- [ ] **Step 2: Type and colour** — headings to `DisplayFont` uppercase, labels to `CodeFont`, accent to `#E10600`. No layout change inside the grid.
- [ ] **Step 3: Screenshot the tower and judge it**

Run: `F1APP_SMOKE=1 F1APP_SHOTS=1` and open `pitwall.png`, `timing.png`.
Expected: 22 rows visible, positions and gaps still in vertical columns, nothing wrapped. Write one line in `docs/agent/findings.md` saying whether the tower still scans, and what was changed if it did not.

---

### Task 10: Documentation, credits and the full gate

**Files:**
- Modify: `docs/plan.md` (attribution list), `docs/agent/context.md`, `docs/agent/tasks.md`, `AGENTS.md` (image section), `docs/adr.md` (new ADR for the media decision)

- [ ] **Step 1: Record the licence and the credits** — Barlow Semi Condensed OFL 1.1 with the file path; F1 media credit wording; the memory-only rule; the catalogue's verification date.
- [ ] **Step 2: ADR** — "F1 media assets: hotlinked, credited, never stored", with the fallback chain and what it costs (breaks if F1 changes paths; screens depend on a remote CDN).
- [ ] **Step 3: Full gate**

```bash
RTK_DISABLED=1 dotnet test tests/F1App.Telemetry.Tests/F1App.Telemetry.Tests.csproj --nologo -v q
RTK_DISABLED=1 dotnet format src/F1App.Telemetry/F1App.Telemetry.csproj --verify-no-changes --no-restore
F1APP_SMOKE=1 F1APP_CLICK=1 F1APP_SHOTS=1 ./F1App.App.exe   # wide, then F1APP_PHONE=1 for the phone walk
```
Expected: all tests pass, format clean, both walks complete with 0 `PAGE`/`CRASH`, and a screenshot set that has been read screen by screen.
