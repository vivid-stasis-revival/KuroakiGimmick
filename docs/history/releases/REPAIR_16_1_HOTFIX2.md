# v0.1.2 / 16.1 source-branch repair hotfix 2

The original 16.1 incremental ZIP assumed the final 16.0 authoring branch. Some users had the earlier 16.0 candidate, which used `EditorMarker`/`EditorPlacement` and therefore did not contain `TimelineMarker.cs`.

Apply over the current project root, run the repair command once, then build:

```bash
unzip -o ~/Downloads/KuroakiGimmick-v0.1.2-16.1-source-branch-repair-hotfix.2.zip
./APPLY_16_1_REPAIR.command
./scripts/build-mac.sh
```

The repair removes only obsolete source files from the superseded 16.0 authoring prototype. Song/chart files and Assets are not touched.
