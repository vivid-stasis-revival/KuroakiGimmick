# KuroakiGimmick v0.1.2 / 16.2

## 更新

基于 16.1 clean source，并合入 Nekomiya/Scarlet/Kuroaki 主题、编译修复、说明卡 hover 与固定绘制层级的四个热修。增量包会一并补齐这些源文件，无需依次重装旧热修。它不包含任何系统字体文件，也不改动已有字体图集。

先退出旧程序、备份自己的源码改动，在已有的 16.1 工程根目录覆盖更新包，然后执行：

```bash
bash scripts/build-mac.sh
```

Windows 使用 `scripts/build-windows.ps1`。产物和应用标识为 v0.1.2 / 16.2；文件夹仍叫 v0.1.1 不影响实际版本。16.0 混合分支应先修复为 16.1，或直接使用本次完整源码包在新目录构建。不要把旧分支源码额外复制到完整源码内。


详细操作见 docs/IMAGE_OBJECTS_16_2.md。

升级至 16.2 后，不要再运行旧的 16.1 source-branch repair 脚本，以免覆盖新版源码。
