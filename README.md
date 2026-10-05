# Emmy

Emmy Miranda is an LLM companion for Final Fantasy XIV. A Dalamud plugin observes its local character and executes bounded actions; a local .NET host owns conversations, memory, model requests and the browser control panel.

**Current release: 0.2.0 preview.** The plugin and host compile. Automated checks exercise the core and conversation flow. Native actions still require acceptance in a real Windows FFXIV client. This is not a completed 1.0 release.

## Start on Windows

1. Download the `Emmy-Windows-preview` artifact from [GitHub Actions](https://github.com/Kabutori/emmy/actions) or run `scripts/build.ps1` with .NET 10 and Dalamud installed.
2. Extract the whole ZIP, run `Install-Plugin.ps1`, and add the printed DLL path under Dalamud's experimental dev plugin locations.
3. Run `Start-Emmy.ps1` under the same Windows user as FFXIV. The local control panel opens at `http://127.0.0.1:17840`.
4. Save a DeepSeek key, test the connection, and permit individual contacts. Start in **Assistiert**.
5. Use `/emmy`, `/emmy web`, `/emmy stop` and `/emmy resume` in game.

Keys are protected with Windows user-bound DPAPI. There is no automatic provider fallback. Movement, travel, native menus, emotes and vision start disabled. The host binds to loopback and requires its local session capability.

## Implemented preview paths

- Say, Tell and Party capture, identity including homeworld, separate private conversation histories and outgoing echo observation.
- Observe, assisted and companion modes; edited one-time drafts; shared cancellation generations and disconnect handling.
- DeepSeek structured replies, inline vision image blocks, typed failures, central request budget and token usage.
- SQLite conversations and editable memories with source, scope and confirmed/derived status; per-person conversation, memory and command rights.
- vnavmesh cancellable local paths, follow distance, target loss, stuck detection and local stop.
- Lifestream teleport, aethernet and world-change requests with destination-state verification.
- Opt-in SelectString and two-button SelectYesno readers, fresh menu signatures and explicitly confirmed selections.
- Ingame control window and bundled dark browser panel; saved meeting points and a finite itinerary API.
- Explicit house-visit recipe with estate/room identity, observed door binding, destination checks, fresh vision and provenance-backed visit memories.
- Offline policy replay, regression executable, repeatable package builds and Windows CI.

## Develop and verify

```powershell
dotnet run --project tests/Emmy.Tests/Emmy.Tests.csproj -c Release
./scripts/build.ps1 -Configuration Release
```

On Linux, the host and regression checks work with .NET 10. Set `EMMY_DEEPSEEK_KEY` in the process environment if live model testing is needed. Windows builds need Dalamud API 15 references; set `DALAMUD_HOME` to the matching reference directory when building outside XIVLauncher.

`/emmy legacy` opens the preserved original chat-only path; `/emmy host` switches back. The new host does not silently import old profiles or databases. They remain separate until a reviewed migration is implemented.

Read [installation](docs/INSTALLATION.md), [acceptance](docs/ACCEPTANCE.md), [architecture](docs/COMPANION-ARCHITECTURE.md) and [remaining work](docs/STATUS.md). The earlier concept and architecture files are historical design references; their checked boxes are not runtime acceptance evidence.

Source provenance: the initial chat-only code comes from `FFXIV/dalamud-emmy`, commit `e61cb75031457b5b12574ebd91546b65f22ef153`. Only the inspected current source snapshot is transferred; the legacy Git history, working data and credentials are excluded.
