# Custom Gimmicks 1.14.2 MPF compatibility

The implementation builds on PR #52 (`925ce55`) and was checked against the user-supplied packages and extracted game code. GameMaker itself was not executed: source-order conclusions below are static evidence, while the regression checks execute K/G's interpreter.

The current update targets upstream [CGMK 1.14.2, commit `3f298e9e60ffd3915d434f59a310a9feaebe704a`](https://github.com/vivid-stasis-revival/Custom-Gimmicks-Mod/commit/3f298e9e60ffd3915d434f59a310a9feaebe704a), checked on 2026-11-14 against its parent 1.14.1 (`0391f24`).

## 1.14.2 changes

- `struct_create`, `struct_get`, `struct_set`, `struct_exists` and `struct_get_names` are SAFE whitelist functions. Structures and nested arrays retain reference identity; keys are case-sensitive strings. Missing fields and `struct_set` return `undefined`. Field access uses functions; struct literals, dot access and struct operators are not supported. New structures, field storage and name arrays share the existing cumulative allocation budget. Name enumeration order is not a portable contract.
- `array_push` mutates its array and returns `undefined`; keep `array_push(a, value);`, not `a = array_push(a, value);`. The 1.14.2 author document still claims the array is returned, contradicting the changelog and [executable whitelist](https://github.com/vivid-stasis-revival/Custom-Gimmicks-Mod/blob/3f298e9e60ffd3915d434f59a310a9feaebe704a/codes/gml_GlobalScript_mpf_white_list_funcs.gml). K/G follows the code. `array_insert` and `array_sort` still return the original array.
- `get_game_var_instance(instance, variable, index)` requires all three arguments. Offline instance support remains limited to the documented `cc` state at index 0.
- Every rebuilt timeline already creates fresh script environments and rediscovers files, including newly added `.vsmpf` files. `REFRESH_ON_EACH_RUN=false` retains variables between ranges within one simulation, but never between rebuilt sessions. Reload the chart or use Save MPF and Preview to rebuild; seeking/loop playback samples the same immutable timeline, and no filesystem watcher is introduced.
- The upstream legacy subtitle fix reads `textcolhex` and converts authored RGB to GameMaker's packed color. K/G now uses that RGB value directly, without the removed `col_convertion` switch. The deleted `InitMisc` registration of `sides` was a duplicate: the callback in `InitSides` remains active.
- Authoring a new `changeskin` also enables `ENABLE_SKIN_CHANGE` in the same undo transaction. The event inspector can enable or disable existing events. In-memory editor configuration and the explicit saved companion take priority over discovered song configs; missing or disabled switches remain inactive on unedited imports. VSM + config and Chart Folder exports preserve the switch.

## Source identity

| Supplied artifact | SHA-256 |
| --- | --- |
| Custom Gimmicks 1.14.zip | `757821e177d8ee352cecfdc0b6d715cb9bdb79767e974c9012abfc9b49aab048` |
| Custom Songs v3.4.1.zip | `d161dbb21e00acfa76f23089d1e7fccabd45bcb411530e59f17643241fdc8c94` |
| CodeEntries/gml_Object_cc_Create_0.gml | `f1d09ab95b17928a18a2b24fbe685d2da78492a1738806b5a9ef63e2175737c4` |
| CodeEntries/gml_Object_cc_Step_1.gml | `444f79b388a839aa37f91064625d607aae565eff25d34cdbb2e78424e014daa0` |

REA archive inspection evidence: `ev_383b41b99f66a966e94318cc7467c5e481b0680098042ac6487e59bfb12c2123` (CGMK), `ev_6bcd1e5bfa0c4a0bb42b576291dad2368d7050bf1fc6191e5307138f84bf9ad9` (Custom Songs). These establish archive identity and inventory, not runtime equivalence. Private package/game source files are not included in this repository.

## Decisions grounded in source

- `custom_mpf_registry.gml` registers GLOBAL programs before difficulty-specific overrides; script names are case-sensitive. `obj_custom_gimmick_Create_0.gml` retains an existing native function when an external script has the same name.
- `custom_mpf_parser.gml` distinguishes operator tokens from strings, validates whitelist/risk/scope at compile time, and keeps script variables shared between ranges. Logical operators preserve the selected operand. Runtime errors are cleared when entering a new range; cumulative K/G resource budgets still apply.
- `codepatches/updateMod.gml` registers due VSM tweens before running MPF. `cc_Step_1.gml` then applies callbacks and chart tweens. Consequently a direct MPF write precedes native tween application, whereas a newly registered MPF tween follows earlier native tweens. An earlier scheduled MPF tween can be overridden by a subsequently registered native tween.
- `get_mod_val` sees the previous tick's final value plus earlier direct writes from the current tick. `mpf_white_list_funcs.gml` schedules callbacks and tweens independently of a script's active range; Finish work can extend beyond that range.
- The supplied documentation and executable GML disagree about native tween precedence and re-enabling scripts after errors. K/G follows the supplied GML on these points.

## Preview boundaries

- The preview clock is fixed at 60 Hz. The game can run at other frame rates. Seeking and parallel rendering reuse immutable samples and do not advance script state.
- Random state is shared by scripts and seedable, but uses a deterministic offline generator rather than GameMaker's global RNG.
- Judgment/combo queries start from a disclosed zero-state baseline. Script judgment changes update shared offline counters; live input and autoplay hits are not simulated. `op_autoplay` is 0 and `gamefps` is 60. `cc.song_speed` reads the chart's configured music-control speed, with callbacks visible on the following tick; `cpos` and `currentms` expose chart time in seconds and milliseconds. Callbacks requiring original game instances still report errors.
- Proxy IDs are checked against the declared count. Each script invocation allows at most 100,000 execution steps, with a 5,000,000-step burst reserve replenished by 1,000 steps per advancing preview tick. Range refreshes and multiple calls within one tick do not replenish that reserve, so ordinary long-running scripts no longer hit a fixed lifetime instruction cap. Cumulative allocations, scheduled work and output samples remain bounded; work after 3600 seconds is reported as outside the preview limit.
- Script errors and discovery failures remain diagnostics. They do not execute arbitrary host code, filesystem functions, or shell commands.
- Saving an editor working VSM retains the original script directory/difficulty. That directory is serialized relative to the project. Chart Folder exports the selected referenced scripts; VSM-only export does not bundle them.

## Verification

`dotnet run -c Release -- --mpf-test` runs the synthetic language, timeline, resource discovery, save/reopen/move and export regressions. The tests use temporary fixtures and no private assets. The 14 complete scripts in the supplied CGMK author documentation were also exercised through `Session --inspect` locally; this validates K/G loading and execution, not a visual comparison against the original game.

The 1.14.2 checks cover struct aliasing/nesting/cycles, missing fields, void returns, argument/type errors, allocation bounds, session resets, script discovery/reload, and skin-change authoring/undo/save/export. With local assets, `--smoke-text-ui` also exercises both skin-event insertion paths and the inspector switch through SDL, then reads GPU pixels for red and blue subtitles in both legacy and extended text formats.
