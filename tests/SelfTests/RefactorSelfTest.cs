using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// SDL_GPU 结构重构新增的 CPU 回归：目录兼容、数据验证、回调寿命和同深度顺序。
/// 不创建窗口，不加载 native GPU 库；通过 --self-test 在真实 .NET 运行时执行。
/// </summary>
public static class RefactorSelfTest
{
    /// <summary>
    /// 全程只用临时目录和内存谱面，验证的是托管层的行为；通过不代表 native ABI 或 GPU 渲染正确，那两项由 GpuSelfTest 和源码契约脚本分别负责。
    /// </summary>
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Refactor regression: " + description);
            }
            checks++;
            Console.WriteLine("PASS " + description);
        }

        bool Rejects<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        string root = Path.Combine(Path.GetTempPath(), "kuroaki-r19-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 模拟"新程序 + 旧 Assets 目录"：用户只覆盖了可执行文件，没更新 Assets 里新增的散装 catalog。
            // 此时必须回退到内嵌数据照常工作，而不是因为少了一个文件就报错或把房间配错。
            string emptyAssets = Path.Combine(root, "old-assets");
            Directory.CreateDirectory(emptyAssets);
            Check(RoomAssociations.PresetFor("Scarlet Death", emptyAssets) == "scarletdeath",
                "room catalog falls back to embedded data when the new loose file is missing");
            // 反过来，没有登记过的曲名要老老实实留在普通 gameplay 房间，不能猜一个最像的特殊房间给它。
            Check(RoomAssociations.PresetFor("Unrelated chart", emptyAssets) == "gameplay",
                "unknown song remains on the ordinary gameplay room");
            var jackets = BundledCatalog.Read<Dictionary<string, string[]>>(
                "Catalog/jackets.json", "KuroakiGimmick.JacketCatalog.json", emptyAssets);
            // 11 是 plaudite 在内嵌 jacket 索引里的实际条目数，用来确认回退读到的是完整数据而不是空表。
            Check(jackets.TryGetValue("plaudite", out var names) && names.Length == 11,
                "legacy indexed jackets remain available without the new loose catalog");

            // 关键的不对称：文件"缺失"才回退内嵌；文件"存在但坏了"必须抛出去让用户看见。
            // 否则用户改崩了自己的 catalog 却看到一切正常，永远查不出为什么改动没生效。
            Directory.CreateDirectory(Path.Combine(emptyAssets, "Catalog"));
            string roomCatalog = Path.Combine(emptyAssets, "Catalog", "room-associations.json");
            File.WriteAllText(roomCatalog, "{broken-json");
            Check(Rejects<JsonException>(() => RoomAssociations.PresetFor("Scarlet Death", emptyAssets)),
                "corrupt external catalog is reported, not silently replaced by embedded defaults");
            File.Delete(roomCatalog);
            // 回退分支同样要走路径包含检查：不能靠"反正会回退到内嵌"就放任 ../ 逃出 Assets 目录。
            Check(Rejects<InvalidDataException>(() => BundledCatalog.Read<Dictionary<string, string[]>>(
                "../outside.json", "KuroakiGimmick.JacketCatalog.json", emptyAssets)),
                "unsafe external catalog path cannot bypass containment through fallback");

            // 随程序分发的粒子图只是借来当 sprite 素材，和这个对象的身份无关。
            File.Copy(Path.Combine(Paths.Assets, "GameCommon", "pt_diamonddust_0.png"), Path.Combine(root, "shape.png"));
            const string objectName = "obj_ordering_fixture";
            // 三个绘制项刻意共用 Depth 260，逼着同深度的先后只能由 SortOrder 决定；
            // 绘制寿命 .3875 与下面 CallbackLifetimes 的 .4 也刻意取不同值，用来区分"画面寿命"和"时间轴尾巴"。
            var definition = new GimmickDefinition
            {
                ObjectName = objectName,
                RoomPreset = "none",
                Sprites = new()
                {
                    ["shape"] = new() { Width = 9, Height = 9, OriginX = 4, OriginY = 4, Frames = ["shape.png"] }
                },
                Callbacks = new()
                {
                    ["pulse"] = [new()
                    {
                        Sprite = "shape", Stage = GimmickStages.BeforePlayfield,
                        Lifetime = .3875, Depth = 260, SortOrder = 0
                    }],
                    ["bar"] =
                    [
                        new() { Sprite = "shape", Lifetime = .1, Depth = 260, SortOrder = 1,
                            Stage = GimmickStages.BeforePlayfield, EventValue = 1, LatestOnly = true },
                        new() { Sprite = "shape", Lifetime = .1, Depth = 260, SortOrder = 2,
                            Stage = GimmickStages.BeforePlayfield, EventValue = 2, LatestOnly = true }
                    ]
                },
                CallbackLifetimes = new() { ["pulse"] = .4 }
            };
            string manifest = Path.Combine(root, "gimmick-object.json");
            File.WriteAllText(manifest, AppJson.Serialize(definition, ViewerProject.Json));
            string chartPath = Path.Combine(root, "fixture.vsc");
            File.WriteAllText(chartPath, "");
            var chart = new Chart { ObjectName = objectName };
            // 120 BPM 下这三条分别落在 0、.01、.02 秒；声明顺序（bar=2、bar=1、pulse）故意和渲染顺序（0,1,2）相反。
            chart.Mods.Add(new(0, 0, "linear", 0, 2, "bar", -1, 0));
            chart.Mods.Add(new(.02, 0, "linear", 0, 1, "bar", -1, 1));
            chart.Mods.Add(new(.04, 0, "linear", 0, 1, "pulse", -1, 2));
            var project = new ViewerProject { Chart = chartPath, Bpm = 120, RoomPreset = "none" };
            var profile = NativeGimmickProfile.Load(project, chart);
            Check(profile.Data != null && profile.Issues.Count == 0, "ordering fixture loads through the generic object pipeline");
            var timeline = new Timeline(chart, project, 0, native: profile);
            // .42 = 最后一条事件的 .02 秒 + 元数据声明的回调寿命 .4。时间轴尾巴按元数据算，
            // 这样即使绘制早就消失，音频/进度条也不会提前判定演出结束。
            Check(Math.Abs(timeline.End - .42) < 1e-12, "callback tail retains .4 seconds even when its drawing exits earlier");
            var scheduler = new GimmickDrawScheduler(profile.Data!);
            var before = scheduler.At(timeline, .03, GimmickStages.BeforePlayfield, true);
            Check(before.Select(item => item.Drawing.SortOrder).SequenceEqual(new[] { 0, 1, 2 }),
                "equal-depth ordering is explicit, independent of callback creation time");
            // 反过来：.41 秒时绘制早已过期（.02 + .3875 = .4075），不能因为时间轴尾巴到 .42 就把画面也拖长。
            Check(scheduler.At(timeline, .41, GimmickStages.BeforePlayfield, true).Count == 0,
                "visual lifetime does not extend just because the timeline tail is longer");
            // 往回跳必须得到完全相同的结果（含 EventIndex），否则拖动进度条来回走会让演出错位。
            Check(scheduler.At(timeline, .03, GimmickStages.BeforePlayfield, true).SequenceEqual(before),
                "backward seek restores the same ordering and event indices");
            Check(scheduler.At(timeline, .03, GimmickStages.Gui, true).Count == 0,
                "an object cannot leak draw commands into another composition stage");

            // 元数据寿命短于它自己绘制项的寿命属于自相矛盾，必须在校验阶段拒绝，
            // 而不是运行时把画面截断一半。
            definition.CallbackLifetimes["pulse"] = .1;
            Check(Rejects<InvalidDataException>(() => GimmickValidation.Validate(definition)),
                "metadata cannot truncate callback lifetime before its visible drawings");

            // 命令行解析阶段只做路径解析：audio 指向一个不存在的文件，此时也不许去解码它，更不许顺手建 Session。
            // 否则用 --audio 覆盖路径的用法会在覆盖生效之前就先失败。
            string projectPath = Path.Combine(root, "paths.sgv.json");
            File.WriteAllText(projectPath, AppJson.Serialize(new ViewerProject
            {
                Chart = "fixture.vsc", Audio = "not-loaded.ogg", GimmickDefinition = "gimmick-object.json"
            }, ViewerProject.Json));
            var parsed = Session.ReadProject(projectPath, out string? origin);
            Check(origin == projectPath && parsed.Audio == Path.Combine(root, "not-loaded.ogg"),
                "project parsing resolves paths without loading audio or building a Session");
            // 相对路径一律以工程文件所在目录为基准，不能用进程当前目录 —— 从别处双击打开工程时两者并不相同。
            Check(parsed.GimmickDefinition == manifest && parsed.Chart == chartPath,
                "object definition and chart references use the project directory, not process cwd");
        }
        finally
        {
            Directory.Delete(root, true);
        }
        return checks;
    }
}
