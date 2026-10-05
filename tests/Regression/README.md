Run the CPU regression suite (no game assets, GPU or additional test packages needed):

```sh
dotnet run --project tests/Regression -c Release
dotnet run --project tests/Regression -c Release -- --scale
```

The score oracle implements the previous expanded judgement algorithm. Checks cover exact hit payloads, tick boundaries, fixed/variable BPM, reverse seeks, rolling scores and simultaneous judgements. Scale mode reports synthetic note/index/score construction, managed allocations, retained memory and magnet queries at 10k/100k/500k/1M notes, plus synchronized and staggered long holds with billions of logical judgements. These timings exclude audio, texture loading and GPU rendering.

Issue checks also cover the supplied LR Extra Gimmicks callback/colour contract, zero Custom mod weights, dependency-only Chart Folder exports, staged image names, save/reopen/export, unchanged backup locations and the Windows hidden flag (when run on Windows).

`ScoreState` retains explicit heads/tails, arithmetic tick streams and at most 4097 rolling checkpoints. Identical tick streams are counted together; intervals in which both score displays provably remain below their current targets can be skipped exactly. Other intervals use an ordered merge, so pathological sparse interleaving can still cost time proportional to the judgements visited, without storing them all. Effect generation scales with the hits in the visible effect lifetime.

`NoteIndex` uses a start-time search and subtree maximum end times, preserving crossing holds and source draw order. Its future cutoff is conservative for positive/negative scroll, offsets, wave and boost. Zero scroll retains future candidates because those notes may actually be visible. No note-count or simultaneous-note limit was lowered.

With the local game assets, also run the real SDL/Metal UI checks:

```sh
dotnet run -c Release -- --smoke-startup --out /tmp/kuroaki-startup.ppm
dotnet run -c Release -- Samples/EditorDemo/demo.sgv.json --smoke-text-ui --out /tmp/kuroaki-text.ppm --overwrite
```

The UI checks exercise tips drawing, small windows, input blocking, recent-file loading, cue insertion from the playhead/active marker and undo/redo.
