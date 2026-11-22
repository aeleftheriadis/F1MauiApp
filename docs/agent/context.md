# F1 Maui App — agent context

Entry point. Offline-first F1 companion, pure C# MAUI UI, open APIs only (OpenF1, Jolpica, Wikimedia Commons, RSS).
Data-source rules and per-source limits live in `AGENTS.md` (project root); architecture in `docs/architecture.md`,
decisions in `docs/adr.md`, the value-ordered work list in `docs/plan.md` (that file is the execution ledger:
items are marked done as they land).

- Plan execution happens directly on `master` in this tree (user's standing choice); commits only on explicit request.
- Build gate: `dotnet build src/F1App.App/F1App.App.csproj -f net11.0-windows10.0.19041.0` → 0 warnings/0 errors.
- Smoke: run the built exe, then read the app log walk (`walk -> ... complete`).
- `docs/design/pitwall-prototype-4.html` — visual reference (5 tabs: Season, Pit wall, Paddock, Archive, Media).
