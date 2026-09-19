# Preview 9 docs cleanup hotfix 3

Removed the synthesized sentence shown on many parameter pages:

`时间单位均为节拍。特殊参数自身的单位仍以该条目说明为准。`

The same generated restatement was also removed from the reference overview. Unit information now appears only where the supplied documentation actually states a unit or time basis. The two supplied Markdown source files are unchanged.

Changed files:
- `Assets/Documentation/vsm-reference.json`
- `scripts/dev/build-vsm-reference.py`
- `scripts/dev/check-vsm-reference.py`
- `docs/validation-vsm-reference.json`
- `docs/PREVIEW9_DOCS_CLEANUP_HOTFIX3.md`
- `SOURCE_REVISION.md`
