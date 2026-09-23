# Third-party materials

`Assets/` 中的游戏素材属于游戏《vivid/stasis》及其相应权利人，不公开提供。
以下清单记录本地开发资源与第三方依赖的来源，不表示这些游戏资源可随源码公开分发。

| Component | Version / source | Notice |
|---|---|---|
| .NET | Target .NET 8; historical input records Runtime 8.0.30 / SDK 8.0.424; preview.7 not compiled here | MIT; bundled runtime includes LICENSE.txt and ThirdPartyNotices.txt |
| SDL3 native packages | SDL3-CS 3.4.16 | zlib SDL; MIT package wrapper; notices under ThirdParty |
| Shaderc native | Silk.NET.Shaderc.Native 2.23.0 | Apache-2.0; shaderc, glslang, SPIRV-Tools and SPIRV-Headers notices under ThirdParty |
| SPIRV-Cross native | Silk.NET.SPIRV.Cross.Native 2.23.0 | Apache-2.0 / MIT; included license |
| Silk.NET native packaging | 2.23.0 | MIT; included license |
| NVorbis | 0.10.5 | MIT, included notice |
| StbImageSharp | 2.30.16 | Public domain / MIT; included license |
| StbImageWriteSharp | 1.16.7 | Public domain / MIT; included license; used to encode exported info cards as PNG |
| StbTrueTypeSharp | 1.26.13 | Public domain / MIT; included license; rasterizes the interface font at run time |
| Noto Sans SC | Two static weights shipped as font files under Resources/Fonts | SIL OFL 1.1; ThirdParty/NotoSansSC-OFL.txt |
| Noto Sans CJK SC / legacy Mono atlas | Generated raster artwork only; retired, no longer loaded | SIL OFL 1.1; ThirdParty/NotoCJK-OFL.txt; no font binaries included |
| DejaVu fonts | Former interface atlas; retired, no longer loaded | License in Assets/Fonts/DejaVu-LICENSE.txt |
| Adapted shader code | User-provided VIVIDSTASIS / Custom Gimmicks sources | Original code rights remain with their respective creators |
| Fusion Pixel | 10px monospaced zh_hans, v2026.09.01 | SIL OFL 1.1; original licenses included under ThirdParty |
| Room FX settings | User-exported original Mac game, 7 JSON profiles | Original bytes and hashes under Assets/RoomFX; original rights retained |
| Colour Balance shader | Original GameMaker filter by Xor, user-supplied export | Formula retained with a zero-luminosity guard; original rights retained |
| Old Film shader and noise texture | Xor / GameMaker `_filter_old_film`, read-only export from local vivid/stasis | Original game shader, texture and room parameters kept privately under Assets; no public redistribution |
| Custom star/cover/combo sprites | Read-only UTMT export from local vivid/stasis | Original frames kept privately in Assets/CustomGimmicks; game/source rights remain with their creators |
| Extendnova native sequence | Read-only UTMT export: CG, backgrounds, note skin, HP bar, shader and story text | Kept privately under Assets/Gimmicks/obj_extendnova_gimmick; original rights retained; the game's automatic story-skip patch is not executed by the viewer |
| Shared GameMaker FX noise | User-provided original export, three PNG files | Original bytes retained; hashes and source paths in Assets/GameFX/manifest.json; original rights remain with their creators |
| Shared gameplay sprites | User-supplied export: sp_laneOverlay, sp_holdnote_overlay, pt_diamonddust | Six original PNG files, unchanged; rights remain with their respective creators |
| Kuroaki artwork | User-provided kuroaki.png.zip | Original artwork retained unchanged; ICNS/ICO are format conversions; image rights remain with its creator |
| Note skin | User-provided vsnotes.zip, 12 PNG files | Original image rights remain with their respective creators; files unchanged |
| Custom Gimmicks behavior | User-provided v1.12.7 source and documentation | Reference for VSP/image, VSC, text and custom effect semantics; original patch package is not redistributed here |

ASTELLION song audio, background images and sample charts have been removed. Chart decoding and adapted shader logic remain. Rights to third-party shader code and the user-supplied note skin remain with their respective creators. `Samples/ImageGimmicks` contains original procedural demonstration images.

Primary references: [SDL3](https://wiki.libsdl.org/SDL3/FrontPage), [SDL3 macOS](https://wiki.libsdl.org/SDL3/README-macos), [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS), [.NET runtime](https://github.com/dotnet/runtime), [NVorbis](https://github.com/NVorbis/NVorbis), [StbImageSharp](https://github.com/StbSharp/StbImageSharp), [StbImageWriteSharp](https://github.com/StbSharp/StbImageWriteSharp), [FFmpeg](https://ffmpeg.org/).

Font source: [Fusion Pixel](https://github.com/TakWolf/fusion-pixel-font). Text layout reference: [GameMaker draw_text_ext_transformed](https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Drawing/Text/draw_text_ext_transformed.htm).

The user-exported shared gameplay UI/font pack is kept locally under Assets/GameUI and is not publicly distributed; original rights remain with its creators and SOURCE_SHA256.json records unchanged files. Synthetic testing fixtures are generated temporarily and are not replacement artwork.

本次新增公共 shader：GameMaker / Xor Disk Glow 与 Contrast & Brightness，来自用户提供的原游戏 dump。原始出处与字节哈希保存在 Assets/GimmickExtras/evidence 和 runtime-manifest.json；未附带任何歌曲音频或曲绘。

共享原生对象资源位于 `Assets/Gimmicks`，由用户本机原游戏只读导出，供声明相同对象类型的谱面复用。精灵、shader 的原始权利仍属于其创作者；输入游戏与资源哈希记录在对应对象目录的 `SOURCE_SHA256.json`。原始 posterise shader 另由通用房间 FX 使用；不包含测试曲包的谱面、音乐或曲绘。

The interface is drawn with **Noto Sans SC**, shipped as two static font files under `Resources/Fonts` and rasterized at run time. They are licensed under SIL OFL 1.1, retained independently of the application code; full text and copyright in `ThirdParty/NotoSansSC-OFL.txt`. The family derives from Adobe Source Han Sans and its Reserved Font Name is `Source`, which this project does not use. Upstream: https://github.com/google/fonts (`ofl/notosanssc`).

Earlier releases rasterized the interface from IBM Plex Sans SC into bitmap atlases named Kuroaki UI Sans. Those atlases are no longer loaded; the IBM Plex notice and license are retained in `ThirdParty/IBM-Plex-OFL.txt` for the history of that artwork. Copyright © 2017 IBM Corp. Upstream: https://github.com/IBM/plex.
