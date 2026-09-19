# v0.1.2 / 16.2

基线：16.1 clean source + theme-layer hotfix.1 + compile-hotfix.2 + flicker-hotfix.3 + zorder-hotfix.4。
新增图片对象、画布操作、姿态和动画分组、路径续接、替换资源和 CPU/IO 自测。原 Assets/字体、对象定义、窗口核心与扩展文件保持不变；Graphics 仅新增复用图像纹理缓存的 authoring accessor，现有渲染代码文件未改。

未完成 C# 编译和原生 UI 验证。验证结果见 docs/VALIDATION_16_2.json。

---

## 历史

# 源码修订 v0.1.2 / 16.1

基于最后交付的 v0.1.2 / 16.0。新增布局与缩放、图片拖入、VSP 文档状态、图片资源保存和导出联动。
版本标识、应用元信息和 macOS/Windows 发布目录更新至 16.1。
未修改原 Assets、字体图集、Graphics 渲染源码和窗口扩展代码。

本次验证边界与结果见 `docs/VALIDATION_16_1.json`。未执行 C# 编译/自测及原生运行。

---

## 历史修订记录

# v0.1.2 / 16.0

Base: conversation archive `KuroakiGimmick-v0.1.1-editor-preview.9-docs-cleanup-hotfix.3-source.zip`.
Requested previous release label: v0.1.1 / 15.5.
No separately supplied, modified 15.5 source tree was available; do not overwrite local custom changes without a backup.

Changes: chart export, custom gimmick authoring, E markers, documentation context-menu insertion, higher-contrast UI.
Source delivery only; no C# compilation or native execution was performed in this environment.
See docs/EXPORT_AUTHORING_16.md and docs/VALIDATION_16.json.


16.1 theme hotfix: track-help cards suppress layout grips; Settings adds Nekomiya/Scarlet/Kuroaki UI themes.
