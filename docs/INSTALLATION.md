# Windows installation

Requirements: FFXIV through XIVLauncher with Dalamud API 15, a separate character/client if Emmy should stand beside the operator, and a DeepSeek API key for model requests. No Docker or external database is required. The self-contained host includes its .NET runtime.

Extract the complete preview ZIP. Run `Install-Plugin.ps1` in PowerShell. Add the emitted DLL path in `/xlsettings` → Experimental → Dev Plugin Locations. Load Dalamud Emmy. Run `Start-Emmy.ps1`; it starts the matching local host and opens its authenticated control panel. Both processes must use the same Windows account.

The web panel uses `http://127.0.0.1:17840`. Open it through the start helper or `/emmy web`; opening the address alone does not grant access. The local capability rotates when the host restarts. Keys are entered in Emmy & Modelle and protected by Windows DPAPI, never returned through the API. Start with a provider connection test and assisted mode. Native integrations are disabled until explicitly selected.

Allow conversation and memory separately for each observed player. Allow commands only for people who should be able to direct Emmy. Character background such as Cecile's role does not confer technical permissions. Unknown homeworlds never become tell recipients.

Install vnavmesh if using local movement and Lifestream if using travel. Emmy checks their IPC readiness; incompatible or absent plugins leave those abilities unavailable. No dependency plugin is downloaded, bundled or enabled automatically. Other controllers must not own the same movement or menu resources.

Commands:

| Command | Effect |
|---|---|
| `/emmy` | Opens the ingame control window |
| `/emmy web` | Opens authenticated local control panel |
| `/emmy stop` or `/emmy pause` | Immediately stops locally owned work and invalidates host plans |
| `/emmy resume` | Returns to assisted mode |
| `/emmy legacy` | Opens the original chat-only mode, assisted by default |
| `/emmy host` | Returns to the companion host |

Moving with WASD or arrow keys stops Emmy's owned controller when no text input is active. This is an initial takeover detector, not a universal controller/gamepad detector. Always keep `/emmy stop` accessible. On missing host heartbeats, the plugin stops its own controller without waiting for a model call.

Data live in `%LOCALAPPDATA%/Emmy`: SQLite, the local capability and protected key. The old Dalamud profile database is retained separately. Forget removes the selected person scope from active host data and invalidates open contexts; exports and backups are separate copies. Vision captures the central scene crop of the foreground client, excluding the usual edge/lower HUD. Move private chat panels out of that crop before image use; arbitrary relocated overlays cannot be identified automatically. Minimized, unfocused and near-blank captures are rejected.

Update by stopping the host, unloading the dev plugin, replacing the extracted package and restarting both. Keep a backup of the data directory. Builds refuse to package failed or stale output. The native integrations are a preview and need the [ingame acceptance matrix](ACCEPTANCE.md).
