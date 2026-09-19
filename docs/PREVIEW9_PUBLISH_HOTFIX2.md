# preview.9 publish hotfix 2

修复 `scripts/publish-common.sh` 在发布阶段仍复制旧版 `docs/VSM_REFERENCE_PREVIEW8.md` 的问题。
preview.9 改为随发行包复制 `docs/DOCS_UI_PREVIEW9.md`。

该修复不修改 C#、渲染、VSM/VSP/VSV、字体或 ExtCustomGimmick 内容。
