# Implementation status

The repository now contains a runnable **0.2.0 companion preview**, not a final 1.0 release. Compilation and automated checks do not prove native actions in a real game client.

| Area | Implemented | Outstanding |
|---|---|---|
| Chat | Say/Tell/Party, world-bound identities, histories, drafts, one-time dispatch and echo checks | Ingame delivery acceptance, broader social/relevance calibration, manually formatted outgoing tells |
| Provider | DeepSeek text/vision format, JSON plan envelope, cancellation, typed errors, request budget, usage | Live key/model check, tool-call capability probing, streaming, provider variants in host |
| Control | Modes, generations, local stop, heartbeat loss and basic keyboard takeover | Gamepad/remapped-input takeover, stop latency measurements and competing-plugin acceptance |
| Memory | SQLite, provenance, private scopes, candidate facts, edit/confirm/delete/forget | Session consolidation, groups UI, contradictions/version history, reviewed legacy-data import |
| Vision | Foreground central crop, metadata, near-blank rejection, explicit analysis and model-requested capture continuation | Actual capture acceptance, visual-to-object projection, event-trigger calibration, minimized-window alternatives |
| Navigation | vnavmesh cancellable path, follow, move, distance, target loss/stuck stop | Zone/height/flying acceptance, target reacquisition and richer autonomous route plans |
| Travel | Lifestream teleport/aethernet/world change and destination checks | Unlock/cost catalog and assisted route editor |
| Menus | SelectString and two-button SelectYesno snapshots and explicit signature-checked selection | Native callback and house-recipe acceptance, multistage house/NPC dialogs, semantic purchase adapters and economic result checks |
| UI | Ingame mode/stop/link, local panel, contacts, memory, model, abilities, places and diagnostics | Rich ingame editing, onboarding migration preview, settings search and product polish |
| Tasks | Finite itinerary API, confirmed-result progression, instance-bound meeting points, explicit house-visit recipe, observation episode and ordered browser task editor | Natural-language arbitrary multistep planning, saved reusable task templates and task discovery |
| Diagnostics | Event/result journal, state export, offline action-policy replay | Full deterministic conversation/model replay and imported recording UI |
| Voice | Separate operator text channel prepared in contracts | Audio capture, native live-audio provider, interruption, output routing and devices |
| Fachmodule | Optional integration points documented | Questing, combat, retainers, shopping and demonstration learning |

The house-visit recipe approaches a saved entrance, interacts with an observed door, matches an explicitly configured entry prompt/option, verifies the exact saved estate/room instance, moves to its saved position and analyzes a fresh frame. Confirmed visit facts and derived room descriptions have separate provenance. It requires previously observed entrance/interior places and a single supported entry confirmation; arbitrary natural-language discovery and multistage entry dialogs remain pending. The complete companion scenario still needs real-client acceptance. See ACCEPTANCE.md.

The imported historical documents remain useful context. Their implementation claims, including older checked-off risk mitigations, must be interpreted through this status matrix.
