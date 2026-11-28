# F1 Maui App — agent context

Entry point. Offline-first F1 companion, pure C# MAUI UI, open APIs only (OpenF1, Jolpica, Wikimedia Commons, RSS).
Data-source rules and per-source limits live in `AGENTS.md` (project root); architecture in `docs/architecture.md`,
decisions in `docs/adr.md`, the value-ordered work list in `docs/plan.md` (that file is the execution ledger:
items are marked done as they land).

- Plan execution happens directly on `master` in this tree (user's standing choice); commits only on explicit request.
- Build gate: `dotnet build src/F1App.App/F1App.App.csproj -f net11.0-windows10.0.19041.0` → 0 warnings/0 errors.
- Smoke: run the built exe, then read the app log walk (`walk -> ... complete`).
- `docs/design/pitwall-prototype-4.html` — visual reference (5 tabs: Season, Pit wall, Paddock, Archive, Media).

## Ailoha (dev-only UI agent)

`ailoha` inspects the running app from a terminal: screenshots, taps, logs, network, device info.

- CLI at `~/.ailoha/bin/ailoha` (0.2.4); skills in `.claude/skills/ailoha-*`.
- App side: `Ailoha.Agent.Maui` 0.2.4 (version in `Directory.Packages.props`), referenced **only when
  `Configuration == Debug`** and registered with `builder.AddMauiDevFlowAgent(...)` inside `#if DEBUG` in
  `MauiProgram.cs`, placed **after** `UseMauiApp`. Verified 2026-11-28: Debug output carries
  `Ailoha.Agent.Core.dll` / `Ailoha.Agent.Maui.dll` / `Ailoha.Logging.dll` and the agent answers on 11233;
  Release output carries none of them and the running release process opens no agent port.
- Controls carry `AutomationId`s (`PitWallSection0..3`, `SeasonYearPicker`) because text is localisable and ids
  are not — and because this agent reports an id-bearing control's `text` as its id, so text queries no longer
  match it.
- Known CLI defect: `ui tree` / `ui query` return `[]` for this app; the agent's HTTP API works
  (`curl "http://127.0.0.1:11233/api/v1/ui/tree?depth=16"`).
