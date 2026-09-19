namespace KuroakiGimmick.Core;

/// <summary>
/// 基础解析、资源契约和确定性逻辑的进程内测试；测试使用临时资源，不覆盖用户谱面。
/// </summary>
public static class SelfTest
{
    /// <summary>
    /// 顺序跑完全部检查并返回通过数为 0 的退出码；任一断言失败立即抛异常中止，不收集失败列表。
    /// 所有临时产物建在系统临时目录下的随机子目录，finally 中整体删除，不写入用户谱面目录。
    /// </summary>
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string description)
        {
            if (!ok)
            {
                throw new Exception("Test failed: " + description);
            }
            checks++;
            Console.WriteLine("PASS " + description);
        }
        // 冷启动必须不依赖任何随程序分发的歌曲资源，否则裸安装的编辑器打不开。
        Check(Session.Empty().IsEmpty, "startup needs no bundled song assets");
        // 4→4.2 秒 @30 fps 恰好 6 帧；区间长度先量化再取整，不能让浮点误差凭空多出一帧。
        Check(new ExportOptions("unused.mp4", 4, 4.2, 30).FrameCount == 6, "decimal export range does not gain a frame from floating-point rounding");
        // 故意用带小数起点的 viewport：预览区必须落在整数物理像素上，缩放系数也必须是整数，
        // 否则 320x180 逻辑空间的像素画会被半像素采样糊掉。
        var viewport = new Graphics.Rect(240.25f, 175.25f, 878, 390);
        var retina = Graphics.Canvas.PreviewRect(viewport, 2, 2, true);
        Check(retina.W * 2 == 1280 && retina.H * 2 == 720 && retina.X * 2 == MathF.Round(retina.X * 2)
            && retina.Y * 2 == MathF.Round(retina.Y * 2), "Retina preview uses an integer physical pixel scale and origin");
        var desktop = Graphics.Canvas.PreviewRect(viewport, 1, 1, true);
        Check(desktop.W == 640 && desktop.H == 360, "desktop preview uses an integer physical pixel scale");
        var small = Graphics.Canvas.PreviewRect(new(0, 0, 200, 100), 1, 1, true);
        Check(small.W <= 200.0001f && small.H <= 100.0001f, "small preview fits without overflowing its panel");
        // 关掉整数缩放开关后 FIT 模式仍须保留：它铺满分数尺寸的 viewport，与整数像素模式两者不混用。
        var fit = Graphics.Canvas.PreviewRect(viewport, 2, 2, false);
        Check(Math.Abs(fit.H - 390) < .0001f && fit.W > retina.W, "FIT remains available for filling a fractional viewport");
        string dir = Path.Combine(Path.GetTempPath(), "sgv-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 当前 UTMT 紧凑版音符导出：source_sprites 记录的是完整的
            // 107x7 GameMaker bumper 帧。L/M/R 侧标是有意保留的帧内像素，
            // 必须原样存活；bumper_mine 是独立的 45x7 sprite。
            string noteAssets = Path.Combine(dir, "note-assets"), noteV1 = Path.Combine(noteAssets, "Notes");
            Directory.CreateDirectory(noteV1);
            foreach (string name in new[]
            {
                "chip.png",
                "chip_mine.png",
                "hold_head.png",
                "hold_body_L.png",
                "hold_body_R.png",
                "bumper_L.png",
                "bumper_M.png",
                "bumper_R.png",
                "bumper_mine.png",
                "judge_bumper_L.png",
                "judge_bumper_M.png",
                "judge_bumper_R.png"
            })
            {
                File.WriteAllBytes(Path.Combine(noteV1, name), [0]);
            }
            File.WriteAllText(Path.Combine(noteV1, "manifest.json"), """
            {
              "format":"vividstasis-default-notes-v1",
              "source_sprites":{
                "sp_note_chip_normal":{"width":22,"height":7,"origin_x":11,"origin_y":3},
                "sp_note_chip_mine_normal":{"width":22,"height":7,"origin_x":11,"origin_y":3},
                "sp_note_hold_normal":{"width":22,"height":1,"origin_x":11,"origin_y":0},
                "sp_note_bumper_normal":{"width":107,"height":7,"origin_x":53,"origin_y":3},
                "sp_note_bumper_timing_normal":{"width":107,"height":7,"origin_x":53,"origin_y":3},
                "sp_note_bumper_mine_normal":{"width":45,"height":7,"origin_x":22,"origin_y":3}
              },
              "files":[
                {"file":"chip.png","source_sprite":"sp_note_chip_normal","source_frame":0},
                {"file":"chip_mine.png","source_sprite":"sp_note_chip_mine_normal","source_frame":0},
                {"file":"hold_head.png","source_sprite":"sp_note_chip_normal","source_frame":0},
                {"file":"hold_body_L.png","source_sprite":"sp_note_hold_normal","source_frame":0},
                {"file":"hold_body_R.png","source_sprite":"sp_note_hold_normal","source_frame":1},
                {"file":"bumper_L.png","source_sprite":"sp_note_bumper_normal","source_frame":0},
                {"file":"bumper_M.png","source_sprite":"sp_note_bumper_normal","source_frame":1},
                {"file":"bumper_R.png","source_sprite":"sp_note_bumper_normal","source_frame":2},
                {"file":"bumper_mine.png","source_sprite":"sp_note_bumper_mine_normal","source_frame":0},
                {"file":"judge_bumper_L.png","source_sprite":"sp_note_bumper_timing_normal","source_frame":0},
                {"file":"judge_bumper_M.png","source_sprite":"sp_note_bumper_timing_normal","source_frame":1},
                {"file":"judge_bumper_R.png","source_sprite":"sp_note_bumper_timing_normal","source_frame":2}
              ],
              "warnings":["compact chip frame 1 omitted"]
            }
            """);
            var noteProfile = NoteSkinProfile.Load(noteAssets);
            // 皮肤取值器返回可空是给 changeskin 的换肤皮肤用的（那几套有意不画某些音符）。
            // 0 号皮肤的十七个键由 Validate 保证齐全，所以这一段全部 ! 掉。
            var compactBumper = noteProfile.Bumper(0) !;
            Check(noteProfile.Format == "vividstasis-default-notes-v1" && compactBumper.Width == 107 && compactBumper.LocalX == -53,
                "compact note dump preserves the full 107x7 bumper frame and GameMaker origin");
            Check(Math.Abs(compactBumper.UvX) < 1e-6 && Math.Abs(compactBumper.UvW - 1) < 1e-6,
                "compact note bumper keeps the complete L/M/R frame instead of cropping side markers");
            Check(noteProfile.BumperMine(2) !.Width == 45 && noteProfile.HoldBody(2) !.Height == 1,
                "compact note dump preserves per-resource geometry");
            Check(noteProfile.Chip(3) !.File.EndsWith("chip.png") && noteProfile.Warnings.Count == 1,
                "compact v1 explicitly aliases its missing right-side chip frame");
            // 完整 v2 原始素材导出：原始裁剪块由 TargetX/Y 定位在旋转前的
            // sprite 包围盒内。精确帧目录优先于已安装的紧凑版 Notes 目录，
            // 两者同时存在时不回退到紧凑版。
            string full = Path.Combine(noteAssets, "NoteSkinFull"), exact = Path.Combine(full, "NotesExact"), meta = Path.Combine(full, "metadata");
            Directory.CreateDirectory(exact);
            Directory.CreateDirectory(meta);
            string[] exactNames =
            {
                "chip_L", "chip_R", "chip_mine_L", "chip_mine_R", "hold_head_L", "hold_head_R", "hold_body_L", "hold_body_R", "bumper_L",
                    "bumper_M", "bumper_R", "bumper_mine_L", "bumper_mine_M", "bumper_mine_R", "judge_bumper_L", "judge_bumper_M", "judge_bumper_R"
            };
            foreach (string name in exactNames)
            {
                File.WriteAllBytes(Path.Combine(exact, name + ".png"), [0]);
            }
            var exactRows = exactNames.Select((name, i) => new
            {
                file = name + ".png",
                source_sprite = name == "bumper_L" ? "sp_note_bumper_normal" : "dummy_" + i,
                source_frame = name == "bumper_L" ? 0 : i,
                width = 130,
                height = 43,
                target_size = new[]
                {
                    name.StartsWith("bumper") || name.StartsWith("judge_bumper") ? 45 : 22,
                    7
                },
                bounding_size = new[]
                {
                    name == "bumper_L" ? 107 : (name.StartsWith("bumper") || name.StartsWith("judge_bumper") ? 45 : 22),
                    7
                }
            });
            File.WriteAllText(Path.Combine(exact, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                format = "vividstasis-default-note-skin-exact-lanes-v2",
                files = exactRows
            }));
            File.WriteAllText(Path.Combine(meta, "sprites.json"), "[{\"name\":\"sp_note_bumper_normal\",\"origin_x\":53,\"origin_y\":3}]");
            File.WriteAllText(Path.Combine(meta, "frames.json"),
                "[{\"sprite\":\"sp_note_bumper_normal\",\"frame\":0,\"target\":{\"x\":24,\"y\":0,\"width\":52,\"height\":7}}]");
            var exactProfile = NoteSkinProfile.Load(noteAssets);
            Check(exactProfile.ExactLaneFrames && exactProfile.Bumper(0) !.Width == 52 && exactProfile.Bumper(0) !.LocalX == -29,
                "exact v2 restores TargetX and sprite origin for raw atlas crop");
            // 只有一套皮肤的旧 dump 必须照旧可用：changeskin 的任何取值都折回正常皮肤，
            // 没有 hold_end 键时尾帽退回头帧 —— 原版的正常皮肤本来就是拿 chip 帧当尾帽的。
            Check(exactProfile.SkinNames.Count == 1 && exactProfile.Chip(0, 3) == exactProfile.Chip(0)
                && exactProfile.HoldEnd(0) == exactProfile.HoldHead(0),
                "single-skin dump ignores changeskin and aliases the hold tail cap onto the head frame");
            // 多套皮肤：换肤皮肤自带的键用自己的图，表里写成 sp_empty 的键在导出时就没有对应文件，
            // 取不到时必须返回 null（什么都不画），而不是偷偷退回正常皮肤那张。
            string multi = Path.Combine(noteAssets, "NoteSkinFull", "NotesExact"), alt = Path.Combine(multi, "stopmotion");
            Directory.CreateDirectory(alt);
            File.WriteAllBytes(Path.Combine(alt, "chip_L.png"), [0]);
            var multiRows = exactRows.Cast<object>().Append(new
            {
                file = "stopmotion/chip_L.png",
                skin = 1,
                source_sprite = "sp_note_chip_stopmotion",
                source_frame = 0,
                width = 20,
                height = 7,
                target_size = new[]
                {
                    20,
                    7
                },
                bounding_size = new[]
                {
                    20,
                    7
                }
            });
            File.WriteAllText(Path.Combine(multi, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                format = "vividstasis-default-note-skin-exact-lanes-v2",
                skins = new[]
                {
                    new
                    {
                        index = 0,
                        name = "normal"
                    },
                    new
                    {
                        index = 1,
                        name = "stopmotion"
                    }
                },
                files = multiRows
            }));
            var skinned = NoteSkinProfile.Load(noteAssets);
            Check(skinned.SkinNames.Count == 2 && skinned.Chip(0, 1) !.File.EndsWith("chip_L.png")
                && skinned.Chip(0, 1) !.Width == 20 && skinned.Chip(0) !.Width == 22,
                "changeskin picks the alternate skin's own frame instead of the default one");
            Check(skinned.ChipMine(0, 1) == null && skinned.Bumper(1, 1) == null,
                "a skin that declares no mines or middle bumper draws nothing rather than falling back");
            Check(skinned.Chip(0, 2) == skinned.Chip(0) && skinned.Chip(0, -1) == skinned.Chip(0),
                "changeskin values the pack does not provide fall back to the default skin");
            // 逐字段验证设置往返：分辨率、音符对齐、两个方向的延迟、音量、字体都要原值回来。
            // 字体在磁盘上存的是原版资源名（Monaco → fnt_phosphor），改这个映射会让老配置读出错误字体。
            var settingsProject = new ViewerProject
            {
                RenderWidth = 3840,
                NoteAlignment = 1,
                AudioDelayMs = 120,
                VisualDelayMs = -35,
                PreviewVolume = .35,
                GameUiFont = ViewerSettings.MonacoFont
            };
            string settingsFile = Path.Combine(dir, "settings.json");
            new ViewerSettings().Save(settingsProject, settingsFile);
            var restored = new ViewerProject();
            ViewerSettings.Load(settingsFile).Apply(restored);
            Check(restored.RenderWidth == 3840 && restored.NoteAlignment == 1 && restored.AudioDelayMs == 120 && restored.VisualDelayMs == -35
                && restored.PreviewVolume == .35, "viewer settings preserve resolution, alignment, volume and both delays");
            Check(restored.GameUiFont == "fnt_phosphor", "Monaco font choice survives settings save and reload");
            Check(new ViewerSettings().GameUiFont == "fnt_monacovs" && ViewerSettings.FontResource("Default") == "fnt_monacovs"
                && ViewerSettings.FontResource("Monaco") == "fnt_phosphor", "Default / Monaco follow the original game option mapping");
            // 缺字段的旧配置不得被当成"显式选择"：未写的字段保留默认，写了的字段照常生效。
            File.WriteAllText(settingsFile, "{\"RenderWidth\":640}");
            ViewerSettings.Load(settingsFile).Apply(restored);
            Check(restored.GameUiFont == "fnt_monacovs" && restored.RenderWidth == 640,
                "older settings without a font keep Default and existing resolution");
            Check(!File.Exists(settingsFile + ".tmp"), "settings save completes its atomic file replacement");
            // 音频延迟换算成采样帧：48 kHz 下 100 ms = 4800 帧，96000 帧即 2 秒 PCM。
            // 正延迟把播放头推到零点之前（负帧号表示先补静音），负延迟从更靠后的采样开始；
            // 末尾必须钳位，绝不能读过 PCM 尾部。
            Check(Transport.AudioFrame(0, 100, 48000, 96000) == -4800, "positive audio delay queues silence before song zero");
            Check(Transport.AudioFrame(.5, 100, 48000, 96000) == 19200, "seek with positive audio delay resumes from an earlier sample");
            Check(Transport.AudioFrame(0, -100, 48000, 96000) == 4800, "negative audio delay starts from a later sample");
            Check(Transport.AudioFrame(4, -100, 48000, 96000) == 96000, "audio delay never reads past the end of PCM");
            // 手工拼一段 VSC1 字节流，逐个标记走遍解码器分支：VSC 头 → 192 音符段（160 开音符，
            // 162 类型 / 163 轨道 / 164 f32 毫秒时间 / 166 扩展块中用 182 写 id=1 的 f32 结束时间）
            // → 224 效果段（229 对象名、228 proxy 数、226 事件段里的 233：拍、时长、ease、from、to、id、proxy）
            // → 255 收尾。源数值单位是毫秒，解析后转秒，所以 1000f/2000f 对应 Time=1、End=2。
            string binary = Path.Combine(dir, "synthetic.vsb");
            using (var writer = new BinaryWriter(File.Create(binary)))
            {
                writer.Write(new byte[]
                {
                    86,
                    83,
                    67,
                    1,
                    0,
                    192,
                    160,
                    162,
                    2,
                    163,
                    1,
                    164
                });
                writer.Write(1000f);
                writer.Write(new byte[]
                {
                    166,
                    182,
                    1
                });
                writer.Write(2000f);
                writer.Write(new byte[]
                {
                    167,
                    161,
                    193,
                    224,
                    229
                });
                writer.Write(System.Text.Encoding.UTF8.GetBytes("obj_astellion_gimmick\0"));
                writer.Write(new byte[]
                {
                    228,
                    1,
                    226,
                    233
                });
                writer.Write(8f);
                writer.Write(4f);
                writer.Write((byte) 1);
                writer.Write(0f);
                writer.Write(1f);
                writer.Write((byte) 129);
                writer.Write((sbyte) - 1);
                writer.Write(new byte[]
                {
                    227,
                    225,
                    255
                });
            }
            var sample = Session.Load(binary);
            Check(sample.Chart.Notes.Single() is { Time: 1, End: 2, Type: 2, Lane: 1 }, "VSB hold and absolute millisecond end");
            Check(sample.Chart.ObjectName == "obj_astellion_gimmick" && sample.Chart.Mods.Single().Name == "gray",
                "ASTELLION binary mod IDs without song assets");
            Check(sample.Timeline.Bpm.BpmAtBeat(0) == 174, "ASTELLION effect BPM is 174 without bundled resources");
            // 事件从第 8 拍起持续 4 拍，第 10 拍正好是中点，0→1 的 tween 必须读出 .5。
            double middle = sample.Timeline.Bpm.Time(10);
            Check(Math.Abs(sample.Timeline.Get("gray", middle) - .5) < 1e-8, "VSB tween evaluates between event endpoints");
            // Timeline 是有状态的顺序推进器：先跳到很靠后的时间再跳回来，必须重建出完全相同的值，
            // 不能残留上一次推进的结果，否则拖动进度条会看到与顺放不一致的画面。
            _ = sample.Timeline.Get("gray", 180);
            Check(Math.Abs(sample.Timeline.Get("gray", middle) - .5) < 1e-8, "backward seek restores identical tween state");
            // 零点、分数拍、以及长时间累积的几个采样点：beat→time→beat 往返误差必须 <1e-9，
            // 否则长曲尾部的演出会相对音符整体漂移。
            foreach (var second in new[]
            {
                0.0,
                4.75,
                32,
                100,
                192
            })
            {
                Check(Math.Abs(sample.Timeline.Bpm.Time(sample.Timeline.Bpm.Beat(second)) - second) < 1e-9, "beat/time roundtrip " + second);
            }
            // 区间语法是 start:end:step —— 0:4:2 展开成第 0/2/4 拍三条事件，连同另外两行共 5 条。
            // EaseLinear 是原版写法的别名，必须与 linear 等价；`_` 是哨兵，表示沿用事件开始瞬间的当前值。
            string vsm = Path.Combine(dir, "range.vsm");
            File.WriteAllText(vsm, "!obj:obj_base_gimmick\n!proxies:1\n0:4:2,1,EaseLinear,_,10,prx,0\n0,0,linear,_,1,pra,0\n4,0,linear,_,_,prx,0\n");
            var chart = new Chart();
            VsmReader.ReplaceMods(chart, vsm);
            Check(chart.Mods.Count == 5 && chart.Diagnostics.Count == 0, "VSM range expansion and Ease aliases");
            var t2 = new Timeline(chart, new ViewerProject
            {
                Bpm = 60
            }, 10);
            Check(Math.Abs(t2.Get("prx", .5, 0) - 5) < 1e-9, "sentinel resolves at event start");
            Check(t2.Get("prx", 4, 0) == 10, "to-sentinel retains current value");
            // 原版 GML 导出会写出 327.6.6 这种多余小数后缀：取前缀 327.6 并只记一条提示（非错误）；
            // 而完全没有数字前缀的行仍要报错——不能靠悄悄丢弃未知行假装兼容。数值字段还要认 0xFF 十六进制。
            File.WriteAllText(vsm, "327.6.6,0,linear,0,25,textY_67,-1\n1:2:.5,0,linear,0,0xFF,prx,-1\nnot-a-beat,0,linear,0,1,prx,-1\n");
            var prefixChart = new Chart();
            VsmReader.ReplaceMods(prefixChart, vsm);
            Check(prefixChart.Mods[0] is { Beat: 327.6, To: 25, Name: "textY_67" }, "GML-style numeric prefix retains 327.6.6 event at 327.6");
            Check(prefixChart.Mods.Count == 4 && prefixChart.Mods.Skip(1).All(e => e.To == 255),
                "colon ranges and hexadecimal numeric fields remain distinct");
            Check(prefixChart.Diagnostics.Any(d => !d.Error && d.Line == 1) && prefixChart.Diagnostics.Count(d => d.Error) == 1,
                "numeric suffix gets a notice; missing numeric prefix still errors");
            // 步长 0 会死循环、上亿次展开会吃光内存：必须在展开之前就拒绝并记诊断，一条事件都不产出。
            File.WriteAllText(vsm, "0:10:0,1,linear,0,1,prx,0\n0:999999999:1,1,linear,0,1,prx,0\n");
            var bad = new Chart();
            VsmReader.ReplaceMods(bad, vsm);
            Check(bad.Diagnostics.Count == 2 && bad.Mods.Count == 0, "invalid loops terminate without expansion");
            // 截断的 VSB 必须抛 InvalidDataException，而不是安静地返回半张谱面。
            string truncated = Path.Combine(dir, "broken.vsb");
            File.WriteAllBytes(truncated, [86, 83, 67, 1, 0, 192, 160, 164, 0]);
            bool rejected = false;
            try
            {
                VsbReader.Load(truncated);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, "truncated VSB is rejected");
            var variable = new Chart();
            variable.Notes.Add(new(2, 3, 0, 2, new Dictionary<int, object>
            {
                {
                    1,
                    120d
                }
            }));
            // 基准 60 BPM、100 ms offset，再叠一个 BPM 变化音符：Time 与 Beat 必须互为逆运算
            // （这里 4 拍 ↔ 3.1 秒）。只验单向会漏掉 offset 被加两次或在变化点少算一段的 bug。
            var map = new BpmMap(variable, 60, 100);
            Check(Math.Abs(map.Time(4) - 3.1) < 1e-9 && Math.Abs(map.Beat(3.1) - 4) < 1e-9, "BPM change and offset roundtrip");
            // 工程文件存的是相对路径：存盘再读回后，引用的谱面路径必须还原成同一个绝对路径。
            var proj = Path.Combine(dir, "saved.sgv.json");
            sample.Save(proj);
            var reload = Session.Load(proj);
            Check(reload.Chart.Mods.Count == 1 && reload.Project.Chart == sample.Project.Chart, "relative-path project roundtrip");
            // 拿随程序分发的示例工程做一次端到端加载：GameUI manifest 必须在统一的 Assets 目录下被发现；
            // 4 个绘制项共用 2 个贴图文件（共享纹理条）；示例本身故意没有封面，诊断里要留下 jacket 提示但不能升级成错误。
            var imageDemo = Session.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "ImageGimmicks", "ImageGimmicks.sgv.json"));
            Check(imageDemo.GameUi.Ready && imageDemo.GameUi.Manifest == Path.Combine(Paths.Assets, "GameUI", "game-ui.gameui.json"),
                "bundled GameUI is discovered in the unified Assets directory");
            Check(imageDemo.Images.Items.Count == 4 && imageDemo.Images.Files.Count == 2, "VSP layers and shared texture strips");
            Check(!imageDemo.Chart.Diagnostics.Any(d => d.Error) && imageDemo.Chart.Diagnostics.Any(d => d.Source == "jacket")
                && imageDemo.Fx.Find("glow") != null, "image demo reports missing cover and loads original particle glow");
            // 动画帧序号与位置互相独立：M31/M32 是矩阵的平移分量，推进帧不应牵动位移。
            var pose = imageDemo.Images.At(imageDemo.Timeline, 2).First(x => x.Image.Id == "left");
            Check(pose.Pose.Frame == 4 && pose.Pose.Matrix.M31 == 65 && pose.Pose.Matrix.M32 == 70, "image frame index and independent position");
            // 中间跳到第 10 秒再跳回第 2 秒，取到的必须是同一个值：图像 seek 不能依赖推进历史。
            var same = imageDemo.Images.At(imageDemo.Timeline, 2).First(x => x.Image.Id == "left");
            _ = imageDemo.Images.At(imageDemo.Timeline, 10).ToArray();
            Check(same == imageDemo.Images.At(imageDemo.Timeline, 2).First(x => x.Image.Id == "left"), "image seek is deterministic");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Samples", "ImageGimmicks", "grid.png"), Path.Combine(dir, "grid.png"));
            // 缺图不能让整份 VSP 作废：报出出错的源行号（第 6 行），其余合法声明照常加载。
            File.WriteAllText(Path.Combine(dir, "GLOBAL.vsp"),
                "#Layer\nmain,0\n#Image\nmain:\nstatic,shared,grid.png,0\nstatic,missing,no-such-image.png,0\n");
            var imageChart = new Chart();
            var images = CustomImages.Load(new ViewerProject
            {
                Gimmick = vsm
            }, imageChart);
            Check(images.Items.Count == 1 && imageChart.Diagnostics.Single().Line == 6,
                "missing image reports line and preserves valid declarations");
            // 难度同名 VSP 一旦存在就整体覆盖 GLOBAL.vsp，不做合并；这里的 3 帧无法整除贴图宽度，
            // 必须拒绝并说明原因，而不是按错误的帧宽切出一堆歪掉的 UV。
            File.WriteAllText(Path.Combine(dir, "range.vsp"), "#Layer\nmain,0\n#Image\nmain:\nanimated,strip,grid.png,0,3\n");
            var imageBad = new Chart();
            var badImages = CustomImages.Load(new ViewerProject
            {
                Gimmick = vsm
            }, imageBad);
            Check(badImages.Items.Count == 0 && imageBad.Diagnostics.Single().Message.Contains("divide evenly"),
                "difficulty VSP overrides GLOBAL and rejects invalid frame geometry");
            // 搭一份完整的歌曲目录 fixture：VSC 音符 + VSM 演出 + info.json + 文本轨道。
            // VSC 第 5 行（327.6.6）是故意写坏的，用来验证"坏行只丢自己、后续音符照收"。
            string song = Path.Combine(dir, "Song");
            Directory.CreateDirectory(song);
            File.WriteAllText(Path.Combine(song, "FINALE.vsc"),
                "0,3,0,b:174|t:0|v:undefined|s:undefined\n1000,2,1,2500\n1500,0,3\n1900,8,1\n327.6.6,0,0\n2000,6,0\n");
            File.WriteAllText(Path.Combine(song, "FINALE.vsm"),
                "!obj:obj_custom_gimmick\n0,0,linear,0,0,uialpha,-1\n0,0,linear,0,1,bgalph,-1\n4,2,linear,0,1,textalp_named,-1\n");
            File.WriteAllText(Path.Combine(song, "info.json"), "{\"name\":\"Fixture\",\"bpm_display\":\"174\"}");
            File.WriteAllText(Path.Combine(song, "FINALE_text_named.txt"), "0,{n}\n4,Hello{comma}{t}世界\n8,done\n");
            // 打开 VSM 要自动关联同名 VSC；BPM 取自 VSC 首行与 info.json，绝不能回退到 120 的默认值。
            var vsc = Session.Load(Path.Combine(song, "FINALE.vsm"));
            Check(vsc.Project.Chart?.EndsWith("FINALE.vsc") == true && vsc.Chart.Notes.Count == 5,
                "opening VSM auto-associates VSC; malformed row does not discard later notes");
            Check(vsc.Timeline.Bpm.BpmAtBeat(0) == 174 && Math.Abs(vsc.Timeline.Bpm.Time(174) - 60) < 1e-9,
                "VSC BPM and song info prevent fallback to 120 BPM");
            Check(vsc.Chart.Notes.First(n => n.Type == 2).End == 2.5, "VSC hold end uses absolute milliseconds");
            // 文本轨道用 {n}/{comma}/{t} 这套转义：逗号是字段分隔符，只能靠 {comma} 写入；内容按 UTF-8 读。
            Check(vsc.Texts.Tracks.Single().At(4) == "Hello,\t世界", "text supports named IDs, UTF-8 and escape markers");
            _ = vsc.Texts.Tracks.Single().At(100);
            Check(vsc.Texts.Tracks.Single().At(0) == "\n", "text seek restores earlier cue");
            Check(Math.Abs(vsc.Timeline.Get("textalp_named", vsc.Timeline.Bpm.Time(5)) - .5) < 1e-9, "text parameters share beat-based easing");
            Check(vsc.Timeline.Get("uialpha", 1) == 0 && vsc.Timeline.Get("bgalph", 1) == 1, "HUD and lane visibility remain independent");
            // 原版灰色判定区有自己的默认 alpha，不跟随 uialpha/bgalph：把它并进 HUD 开关会让判定区跟着消失。
            Check(vsc.Timeline.Get("holdoverlayalpha", 1) == 1, "original gray judgment area has independent default alpha");
            // 尘埃粒子密度对齐原版 High 档：每秒 30 次生成、每次两颗（位置相同的一对），第一秒共 60 颗。
            // 帧号、速度上限和存活时间也照抄原版逻辑，改这些常数会直接改变演出观感。
            var dust = vsc.Timeline.Particles.Where(p => p.Time <= 1 + 1e-8).ToArray();
            Check(dust.Length == 60 && dust.Chunk(2).All(pair => pair[0].Time == pair[1].Time && pair[0].X == pair[1].X
                && pair[0].Y == pair[1].Y), "source particle density: 30 pairs/second at the default High setting");
            Check(dust.All(p => p.Frame is >= 0 and < 4 && Math.Abs(p.Vx) <= 1.5 && Math.Abs(p.Vy) <= 1.5 && p.Death <= p.Time + 2),
                "original particle frame, velocity and lifetime bounds");
            // 粒子带随机量，但必须由确定性种子驱动：重建一条新 Timeline 要逐个粒子完全相同，
            // 否则导出的视频和预览里看到的不是同一段演出。
            Check(new Timeline(vsc.Chart, vsc.Project, vsc.Timeline.End).Particles.SequenceEqual(vsc.Timeline.Particles),
                "particle seek/export rebuild is deterministic");
            Check(Session.Load(song).Project.Chart == vsc.Project.Chart, "song folder selects an available difficulty");
            Check(Session.Empty().Attach(Path.Combine(song, "FINALE.vsm")).Chart.Notes.Count == 5, "attach to empty workspace also associates chart");
            Check(vsc.Chart.Diagnostics.Count(d => d.Error) == 1 && vsc.Chart.Diagnostics.Single(d => d.Error).Line == 5,
                "malformed VSC reports precise source line");
            // 旧式单文本声明（FINALE_text.txt）优先级高于具名轨道，Id 为空串；这是源加载器的既定顺序，不要改成合并。
            File.WriteAllText(Path.Combine(song, "FINALE_text.txt"), "0,legacy\n");
            Check(Session.Load(song).Texts.Tracks.Single().Id == "", "legacy single-text declaration has source-loader precedence");
            File.WriteAllText(Path.Combine(song, "cgmk_config.json"), "{\"ENABLE_TEXT\":false}");
            Check(Session.Load(song).Texts.Tracks.Count == 0, "custom config can disable text");
            string cover = Path.Combine(song, "jacket.jpg"), cover2 = Path.Combine(song, "jacket[2].png");
            // 解码器按字节内容识别格式，与扩展名无关（这里把 PNG 存成 .jpg）。
            File.Copy(Path.Combine(dir, "grid.png"), cover);
            File.Copy(cover, cover2);
            File.WriteAllText(Path.Combine(song, "cgmk_config.json"), "{\"JACKET_MANAGE_MODE\":\"custom\"}");
            var covered = Session.Load(song);
            Check(covered.Jackets.DefaultPath == cover && covered.Jackets.Mode == "custom" && covered.Jackets.Count == 3,
                "jacket auto-discovery and sparse custom index array");
            // 封面索引沿用原版的"向上取整 + 取模（负数回绕）"语义：Count=3 时 .01→1、3→0、-.01→2。
            // 换成 floor 或 C# 默认的负数取模都会选错封面。
            Check(covered.Jackets.Index(.01) == 1 && covered.Jackets.Index(3) == 0 && covered.Jackets.Index(-.01) == 2,
                "source jacket ceil/modulo/negative-index selection");
            // custom 模式的索引数组是稀疏的：缺的槽位就让它缺着，不能悄悄拿别的封面顶上。
            Check(!covered.Jackets.Files.ContainsKey(1) && covered.Jackets.Files[2] == cover2,
                "missing custom jacket slots do not silently use another cover");
            var attached = covered.Attach(cover2);
            attached.Save(proj);
            Check(Session.Load(proj).Project.Jacket == cover2, "manually attached jacket survives relative-path project save/reload");
            // plaudite 模式固定 12 个槽位，本地封面落在 11 号槽（0 时刻显示的就是它）；
            // 10 号是 ASTELLION 的官方封面，既不随程序分发也不凭空生成。
            File.WriteAllText(Path.Combine(song, "cgmk_config.json"), "{\"JACKET_MANAGE_MODE\":\"plaudite\"}");
            var plaudite = Session.Load(song);
            Check(plaudite.Jackets.Count == 12 && plaudite.Jackets.At(plaudite.Timeline, 0) == cover, "plaudite starts on local cover slot 11");
            Check(!plaudite.Jackets.Files.ContainsKey(10), "ASTELLION cover is never bundled or invented");
            // 瞬时爆发照搬原版的两颗一组生成调用：循环次数向上取整后每组两颗（25→50 颗、1.1→4 颗）。
            // 粒子从屏幕上方 y=-10 出发，速度取 pburstspeed，寿命是源码里固定的 alpha 衰减时长。
            // "重复模式"里的 25 是毫秒倒计时间隔而非 25 组粒子：按倒计时 tick 发射，也不该为此报 repeated mode 诊断。
            var burstChart = new Chart
            {
                ObjectName = "obj_custom_gimmick"
            };
            burstChart.Mods.Add(new(1, 0, "linear", 1, 25, "pburstspeed", -1, 0));
            burstChart.Mods.Add(new(1, 0, "linear", 0, 25, "plaudite_pburst", -1, 1));
            burstChart.Mods.Add(new(2, 0, "linear", 0, 1.1, "plaudite_pburst", -1, 2));
            burstChart.Mods.Add(new(3, 0, "linear", 1, 25, "plaudite_pburst", -1, 3));
            var bursts = new Timeline(burstChart, new ViewerProject
            {
                Bpm = 60
            }, 5);
            Check(bursts.Particles.Count(p => p.Burst && p.Time == 1) == 50 && bursts.Particles.Count(p => p.Burst && p.Time == 2) == 4,
                "instant burst repeats the original two-particle spawn call, including fractional loop counts");
            Check(bursts.Particles.Where(p => p.Burst).All(p => p.Y == -10 && p.Vx == 0 && p.Vy >= 0 && p.Vy <= 18.75f && p.Death <= p.Time + 2),
                "burst starts above the screen, uses pburstspeed and the source fixed alpha lifetime");
            Check(!burstChart.Diagnostics.Any(d => d.Message.Contains("repeated mode")) && bursts.Particles.Count(p => p.Burst && p.Time > 3 && p.Time < 3.025) == 2,
                "25 ms repeated burst emits on the source countdown tick, not as 25 particle groups");
            // `!key:value` 形式的元数据要原样留在 Metadata 里，不参与效果事件解析，也不因为不认识就报诊断。
            File.WriteAllText(vsm, "!obj:obj_custom_gimmick\n!0:0:8\n!author:fixture\n0,0,linear,0,1,prx,-1\n");
            var metadata = new Chart();
            VsmReader.ReplaceMods(metadata, vsm);
            Check(metadata.Metadata["0"] == "0:8" && metadata.Metadata["author"] == "fixture" && metadata.Mods.Count == 1
                && !metadata.Diagnostics.Any(), "arbitrary VSM metadata is retained separately from effect events");
            // 左右爆发的组数按四舍五入取整（2.4→2 组、3→3 组），每组两颗；X=130/190 是原版左右发射点，
            // 粒子向外飞、4 秒淡出，并受全局运动力影响。
            var sideChart = new Chart();
            sideChart.Mods.Add(new(0, 0, "linear", 0, 2.4, "pburstleft", -1, 0));
            sideChart.Mods.Add(new(0, 0, "linear", 0, 3, "pburstright", -1, 1));
            var side = new Timeline(sideChart, new ViewerProject(), 1);
            Check(side.Particles.Count == 10 && side.Particles.Count(p => p.X == 130) == 4 && side.Particles.Count(p => p.X == 190) == 6,
                "left/right callbacks use rounded group counts and two particles per group");
            Check(side.Particles.All(p => p.Life == 4 && p.FollowPower && p.Burst && p.Vy == 0 && p.Y is >= -50 and <= 230
                && (p.X == 130 ? p.Vx <= 0 : p.Vx >= 0)),
                "side particles use source origin, outward speed, four-second fade and global motion powers");
            // 无音频的预览时长要延长到最后一颗粒子消亡为止，否则末尾演出会被硬截断。
            Check(side.End == 4 && side.Particles.All(p => p.Death <= 4), "last side burst extends a silent preview through its lifetime");
            // 把发射速度归零、再加满 particlexpower：粒子只剩全局风力驱动，会被吹出界。
            // 出界销毁的时刻必须是记住的既成事实，重复求值不能得到不同结果。
            sideChart.Mods.Add(new(0, 0, "linear", 0, 0, "pburstspeed", -1, 2));
            sideChart.Mods.Add(new(0, 0, "linear", 5, 5, "particlexpower", -1, 3));
            var wind = new Timeline(sideChart, new ViewerProject(), 1);
            Check(wind.Particles.All(p => p.Death < 1), "side particles remember their first wind-driven out-of-bounds destruction");
            var motionChart = new Chart
            {
                ObjectName = "obj_custom_gimmick"
            };
            motionChart.Mods.Add(new(0, 0, "linear", 0, 12, "xoffsetind0", -1, 0));
            motionChart.Mods.Add(new(0, 0, "linear", 0, 50, "yoffsetind0", -1, 1));
            var motion = new Timeline(motionChart, new ViewerProject
            {
                ScrollSpeed = 1
            }, 2);
            Check(NoteMotion.X(motion, 1, 0, 100) == 12 && NoteMotion.X(motion, 1, 1, 100) == 0,
                "per-lane horizontal motion does not move adjacent lanes");
            // 逐轨道偏移只作用于自己那条轨道，单位是像素，数值口径对齐原版 csmNoteMods。
            Check(NoteMotion.Y(motion, 1, 0, 100) == 139 && NoteMotion.Y(motion, 1, 1, 100) == 134,
                "per-lane vertical offset matches csmNoteMods in pixels");
            // 这些不是随手挑的常数，而是原版的初始值：没有事件覆盖时读到的必须就是它们。
            Check(motion.Get("boost_timeind0", 0) == 300 && motion.Get("twr4", 0) == .4 && motion.Get("sinp", 0) == 1,
                "custom shader and per-lane defaults match source initialization");
            motionChart.Mods.Add(new(0, 0, "linear", 0, 0, "recolor", -1, 2));
            var recolored = new Timeline(motionChart, new ViewerProject(), 2);
            // recolor 回调随机挑一个色相，但必须是确定的：同一会话内不同时刻读到同一个值，seek 不会重新掷色。
            Check(recolored.Get("curcolor", 1) is >= 0 and <= 255 && recolored.Get("curcolor", 1) == recolored.Get("curcolor", .1),
                "recolor callback creates a deterministic hue retained across seeks");
            // freeze 不是 tween 而是闭锁：原版给它注册了 start/end 回调，于是 updateMods 走
            // CreateChartCallback，两个回调写入的都是事件起点的毫秒数（或清零），from/to 只当开关看。
            // 所以区间中途读到的必须是原值，而不是 1→0 的插值结果。
            var freezeChart = new Chart();
            freezeChart.Mods.Add(new(2, 4, "linear", 1, 0, "freeze", -1, 0));
            var frozen = new Timeline(freezeChart, new ViewerProject
            {
                Bpm = 60
            }, 8);
            Check(frozen.Get("freeze", 1.9) == 0 && frozen.Get("freeze", 2) == 2000 && frozen.Get("freeze", 4) == 2000
                && frozen.Get("freeze", 5.9) == 2000 && frozen.Get("freeze", 6) == 0,
                "freeze latches the event start in milliseconds instead of tweening between from and to");
            // 后一条事件的起点会排在前一条的结束回调之前，装进轨道前必须整体排序，否则二分查找读到的是错的那一段。
            freezeChart.Mods.Add(new(3, 0, "linear", 0, 0, "freeze", -1, 1));
            var released = new Timeline(freezeChart, new ViewerProject
            {
                Bpm = 60
            }, 8);
            Check(released.Get("freeze", 2.5) == 2000 && released.Get("freeze", 3) == 0 && released.Get("freeze", 5) == 0,
                "a later freeze release sorts ahead of the earlier event's end callback");
            // drawdist 在 6767 里没有任何实时读取点，登记它只是别报成未知 mod，同时如实说明它没有效果。
            var distChart = new Chart();
            distChart.Mods.Add(new(0, 0, "linear", 0, 500, "drawdist", -1, 0));
            var dist = new Timeline(distChart, new ViewerProject
            {
                Bpm = 60
            }, 1);
            Check(dist.Get("drawdist", 1) == 500 && !distChart.Diagnostics.Any(d => d.Message.Contains("Unsupported mod: drawdist"))
                && distChart.Diagnostics.Any(d => d.Message.StartsWith("drawdist:")),
                "drawdist is evaluated but reported as having no reader in the original build");
            // FX profile 必须按它声明的 shader 类型加载并随工程存盘；depth 决定覆盖层顺序。
            string fxfile = Path.Combine(song, "fixture.fx.json");
            File.WriteAllText(fxfile,
                "{\"layers\":[{\"name\":\"FX_contrast\",\"filter\":\"_filter_colourise\",\"depth\":-200,\"parameters\":{\"g_Intensity\":0,\"g_TintCol\":[1,1,1,1]}}]}");
            var withFx = Session.Load(song).Attach(fxfile);
            withFx.Save(proj);
            Check(withFx.Fx.Find("FX_contrast")?.Filter == "_filter_colourise" && Session.Load(proj).Project.FxProfile == fxfile,
                "original FX profile loads with its declared shader type and survives save/reload");
            File.WriteAllText(fxfile,
                "{\"layers\":[{\"name\":\"FX_chroma\",\"filter\":\"_filter_hue\",\"parameters\":{\"g_HueShift\":0,\"g_HueSaturation\":1}}]}");
            // 层名与 filter 类型对不上时直接拒绝该层并报 binding 错误，不能拿另一个效果凑数——
            // 悄悄替换会让预览显示出谱面里根本没有的画面。
            var wrongFx = GameFxProfile.Load(new ViewerProject
            {
                Chart = Path.Combine(song, "FINALE.vsc"),
                FxProfile = fxfile
            }, motionChart);
            Check(wrongFx.Find("FX_chroma") == null && motionChart.Diagnostics.Any(d => d.Error && d.Message.Contains("binding")),
                "wrong room filter type is rejected instead of substituting an unrelated effect");
            // 逐个校验内置房间 profile：跳过 auto/none 这两个不对应实际房间的伪预设。
            // 每个预设都必须零错误加载、来源标记为 bundled；chroma 层只在 plaudite / gameplay 下可见，
            // 这是原版静态房间与运行时房间的区别，不能统一成"都显示"。
            foreach (string preset in GameFxProfile.Presets.Where(p => p is not ("auto" or "none")))
            {
                var c = new Chart
                {
                    ObjectName = "obj_custom_gimmick"
                };
                var fx = GameFxProfile.Load(new ViewerProject
                {
                    Chart = Path.Combine(song, "FINALE.vsc"),
                    RoomPreset = preset
                }, c);
                Check(!c.Diagnostics.Any(d => d.Error) && fx.Source == "bundled" && fx.Find("FX_contrast")?.Filter == "_filter_colourise",
                    "original room profile validates: " + preset);
                Check(fx.Find("FX_chroma")?.Visible == (preset is "plaudite" or "gameplay"),
                    "static / runtime chroma visibility retained: " + preset);
            }
            // Scarlet Death 用的是真正的 colour balance shader，阴影色调固定为纯红 (1,0,0)：
            // 这些数值来自原版房间，不是近似的调色方案。
            var selectedProject = new ViewerProject
            {
                Chart = Path.Combine(song, "FINALE.vsc"),
                RoomPreset = "scene_gameplay_scarletdeath"
            };
            var selectedFx = GameFxProfile.Load(selectedProject, new Chart());
            Check(selectedFx.Find("FX_red")?.Filter == "_filter_colour_balance" && selectedFx.Find("FX_red")!.Vector("g_ColourBalanceShadows",
                3).SequenceEqual(new double[]
            {
                1,
                0,
                0
            }), "Scarlet Death preserves the actual colour-balance shader and shadow tones");
            new Session(selectedProject).Save(proj);
            Check(Session.Load(proj).Project.RoomPreset == "scene_gameplay_scarletdeath", "selected room preset survives project save/reload");
            Check(GameFxProfile.Load(new ViewerProject
            {
                Chart = selectedProject.Chart,
                RoomPreset = "none"
            }, new Chart()).Layers.Count == 0, "NONE disables bundled room profiles");
            // 认不出来的自定义房间要回退到 scene_gameplay，并显式标记 AutoFallback + 留下诊断：
            // 回退结果不能被当成"已确认的房间关联"展示给作者。
            var autoChart = new Chart
            {
                ObjectName = "obj_custom_gimmick"
            };
            var autoFx = GameFxProfile.Load(new ViewerProject
            {
                Gimmick = Path.Combine(dir, "unknown-room.vsm")
            }, autoChart);
            Check(autoFx.AutoFallback && autoFx.Room == "scene_gameplay"
                && autoChart.Diagnostics.Any(d => d.Message.Contains("room association")),
                "unknown custom room is reported as a fallback rather than claimed as verified");
            // 逐轨道 alpha 的命名与默认值取自 Custom Gimmicks 的实际代码补丁，默认全不透明。
            Check(ModCatalog.Supported.Contains("notealpind6") && ModCatalog.Default("notealpind6", -1) == 1,
                "per-lane alpha follows the actual Custom Gimmicks code patch");
            // 每一种缓动的端点都必须精确落在 0 和 1（中间值只要求有限）：
            // 端点漂移会让每段 tween 结束时跳一下，尤其是首尾相接的连续事件。
            foreach (var ease in "linear inSine outSine inOutSine inQuad outQuad inOutQuad inCubic outCubic inOutCubic inQuart outQuart inOutQuart inQuint outQuint inOutQuint inExpo outExpo inOutExpo inCirc outCirc inOutCirc inBack outBack inOutBack inElastic outElastic inOutElastic inBounce outBounce inOutBounce".Split(' '))
            {
                Check(Easings.Eval(ease, 0) == 0 && Easings.Eval(ease, 1) == 1 && double.IsFinite(Easings.Eval(ease, .37)),
                    "easing endpoints: " + ease);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        // 其余自测套件在临时目录清理之后再跑，各自管理自己的 fixture；返回值累加进总检查数。
        checks += VsmBeatRangeSelfTest.Run();
        checks += CustomFxSelfTest.Run();
        checks += ObjectProfileSelfTest.Run();
        checks += LegacyCompatibilitySelfTest.Run();
        checks += RefactorSelfTest.Run();
        Console.WriteLine($"{checks} checks passed.");
        return 0;
    }
}
