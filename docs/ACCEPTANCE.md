# Acceptance evidence

Automated validation covers identities, tell privacy, typed provider failures, cancellation, budgets, guarded actions, SQLite, two simultaneous tell requests, draft editing/one-time confirmation, finite itinerary progression and the house-visit recipe including wrong estate, absent guest and stop during image analysis. API smoke tests exercise the real host over loopback. DOM checks exercise the real frontend JavaScript. Chromium checks passed on the Windows runner, including contact rights, one-time follow dispatch, Stop, provider/image errors and mobile overflow. Desktop and mobile overview screenshots were inspected. These checks use synthetic bridge telemetry and do not validate native game actions. The local workspace itself cannot launch Chromium.

Compilation uses .NET 10.0.401, Dalamud.NET.Sdk 15.0.0 and downloaded Dalamud build references. The Windows host publishes as self-contained win-x64. These are build checks, not an ingame/Windows runtime acceptance.

Required real-client checks before 1.0:

| Scenario | Required evidence | Current state |
|---|---|---|
| Two rapid tells, same names on different homeworlds | Correct explicit recipients, no history cross-contamination | Core test passed; ingame pending |
| Stop during model, path computation and teleport | No late send, path restart or automatic repeat; measured stop latency | Core test passed; native pending |
| Host process terminates | Local owned controllers abort within heartbeat timeout | API test; native pending |
| Manual keyboard and gamepad takeover | No competing automation after user input | Keyboard implementation; runtime/gamepad pending |
| vnavmesh loaded/missing/busy; multilevel interiors | Correct reachable positions, no wrong height/ownership | Build checked; ingame pending |
| Character changes zone/world while following | Old target reference expires; defined status | Implementation; ingame pending |
| Teleport/aethernet/world change | Observed exact destination and loading lifecycle | Implementation; ingame pending |
| SelectString/SelectYesno reorder, close/reopen, disabled button | Old selection rejected; correct callback in supported client version | Policy tests; native pending |
| Foreground, unfocused, minimized, resized game window | Fresh correct image source; no invalid capture | Build checked; Windows pending |
| Live DeepSeek text and image request | Configured model accepts the actual API format and records usage | Mock contract tested; key required |
| Forget while model request runs | No later recreation or leakage of deleted data | Generation/store tests passed |
| Desktop/mobile web surface | No overflow, all controls usable, status and validation visible | Windows Chromium checks passed; desktop/mobile overview screenshots inspected |
| First complete house visit | Conversation, follow/travel, semantic entry, observed room, confirmed episode | Single-dialog saved-instance recipe and episode regression passed; native evidence/multistage entry pending |

The preview does not ship native live voice, autonomous combat, general quests or economic routines. They remain separately scoped work in STATUS.md. Unrecognized callback families, three-button/hold confirmations and arbitrary commands are not supported.

Evidence: [successful Windows CI run](https://github.com/Kabutori/emmy/actions/runs/37368754964), code commit `cf85f06e617aa6d42c249d79cff4995bb0221784`: **99 checks**, API/DOM/Chromium checks and package creation passed. [Windows preview artifact](https://github.com/Kabutori/emmy/actions/runs/37368754964/artifacts/11368723578) and [browser screenshots](https://github.com/Kabutori/emmy/actions/runs/37368754964/artifacts/11369266299) are attached to that run. Later documentation-only commits do not change that tested code.
