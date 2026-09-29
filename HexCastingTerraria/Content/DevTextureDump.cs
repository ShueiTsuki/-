using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Microsoft.Xna.Framework.Graphics;

namespace HexCastingTerraria.Content;

/// <summary>
/// 开发用：把**原版泰拉的 UI 贴图**导出成 PNG，供离线比对。
///
/// ## 为什么需要它
///
/// 做「原版观感」的皮肤时，不能凭记忆去猜泰拉 UI 长什么样 —— 我第一版就是那么做的，
/// 做出来是个"通用深蓝面板"，被一眼看穿。
///
/// 而本地的 `Content/Images/UI/*.xnb` **全是 LZX 压缩**（头部 flags=0x80，202 个文件），
/// 没有解包工具、自己实现 LZX 代价又太大。
/// 但游戏跑起来时这些贴图**本来就是解码好的** —— 所以让模组自己存一份出来最省事，
/// 也最权威：这不是别人转的图，就是游戏实际用的那张。
///
/// ## 怎么触发
///
/// 只在 `_tools\dump_vanilla_ui.flag` **存在**时执行，导完自己把 flag 删掉。
/// 用哨兵文件而不是环境变量：Steam 启动的游戏拿不到我们设的环境变量。
/// 平时玩家不会碰到（没有那个 flag）。
/// </summary>
public sealed class DevTextureDump : ModSystem
{
    /// <summary>哨兵文件路径。存在才导。</summary>
    private const string FlagPath = @"D:\DeepSeekHarness\tmod\_tools\dump_vanilla_ui.flag";

    private const string OutDir = @"D:\DeepSeekHarness\tmod\_tools\vanilla_ui";

    /// <summary>
    /// Terraria 的 Content 目录（用来**列出**真实存在的贴图名）。
    ///
    /// ⚠️ 上一版我**手写了一张贴图名清单**，其中 `Images/UI/Inventory_Back` 在 1.4.5 里根本不存在 ——
    /// 于是 `Request().Value` 抛异常，而它走的是 `LoadAssetWithPotentialAsync`，
    /// 异常在我 try/catch 够不到的地方浮出来，**直接把游戏崩了**（弹了致命错误框）。
    ///
    /// 教训两条：
    ///   ① **不要猜资源名** —— 磁盘上的 .xnb 文件名就是资源名，列出来就是权威清单；
    ///   ② **加载前先 `Main.AssetExists`** —— 用不抛异常的 API 判断存在性，
    ///      而不是靠 try/catch 去接一个可能从别处冒出来的异常。
    /// </summary>
    private const string ContentDir = @"D:\steam\steamapps\common\Terraria\Content";

    /// <summary>要导的目录（相对 Content，用 / 分隔）。</summary>
    private static readonly string[] WantedDirs =
    {
        "Images/UI",
        "Images/UI/Bestiary",
        "Images/Inventory",
        "Images/Menu",
    };

    /// <summary>名字里带这些的优先导（做面板/物品格/按钮最用得上的）。</summary>
    private static readonly string[] Preferred =
    {
        "Inventory", "Back", "Panel", "Button", "Craft", "Slot", "Tab", "Setting", "Toggle",
        "Search", "Close", "Arrow", "Scroll", "Bar", "Box", "Frame", "Menu",
    };

    /// <summary>最多导多少张（避免把整个 Content 拖一遍）。</summary>
    private const int MaxFiles = 400;

    /// <summary>本次会话是否已经尝试过（PostUpdate 每 tick 都会来）。</summary>
    private static bool _tried;

    /// <summary>
    /// 导出时机从 `PostSetupContent` 挪到 `PostUpdate`。
    ///
    /// 为什么：`PostSetupContent` 阶段资源系统还没就绪，实测 461 个贴图**全部加载失败**
    /// （日志：清单 461 个；导出 0，加载异常 461）。等游戏真正跑起来再导才行。
    /// </summary>
    public override void PostUpdateEverything()
    {
        if (_tried) { return; }
        _tried = true;

        try
        {
            if (!File.Exists(FlagPath)) { return; }
            Dump();
        }
        catch (Exception e)
        {
            Mod.Logger.Warn($"[DevTextureDump] 导出失败：{e}");
        }
    }

    private void Dump()
    {
        Directory.CreateDirectory(OutDir);

        // 哨兵**先**改名再干活：上一版是导完才删，中途崩了会变成崩溃循环。最多只尝试一次。
        File.Move(FlagPath, FlagPath + ".done", overwrite: true);

        // 清掉上一轮留下的空文件（上一版先 File.Create 再 SaveAsPng，
        // 保存失败就留下 0 字节垃圾 —— 407 个）
        foreach (var stale in Directory.GetFiles(OutDir, "*.png"))
        {
            if (new FileInfo(stale).Length == 0) { File.Delete(stale); }
        }

        // ① 从磁盘列真实资源名，不猜
        var names = new System.Collections.Generic.List<string>();
        foreach (var dir in WantedDirs)
        {
            var full = Path.Combine(ContentDir, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full)) { continue; }

            foreach (var f in Directory.GetFiles(full, "*.xnb", SearchOption.AllDirectories))
            {
                var rel = f.Substring(Path.Combine(ContentDir, "Images").Length + 1).Replace('\\', '/');
                if (rel.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
                {
                    rel = rel.Substring(0, rel.Length - 4);
                }
                names.Add("Images/" + rel);
            }
        }

        names.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

        int ok = 0, miss = 0, failed = 0;
        foreach (var asset in names)
        {
            if (ok >= MaxFiles) { break; }

            try
            {
                var tex = Main.Assets.Request<Texture2D>(asset, ReLogic.Content.AssetRequestMode.ImmediateLoad)?.Value;
                if (tex is null) { miss++; continue; }

                // ② **先存内存再落盘**：直接 File.Create + SaveAsPng 的话，
                //    保存一失败就留下 0 字节文件，看起来像"导出了"其实全是空的。
                using var ms = new MemoryStream();
                tex.SaveAsPng(ms, tex.Width, tex.Height);
                if (ms.Length == 0) { failed++; continue; }

                var name = asset.Replace("Images/", string.Empty).Replace('/', '_') + ".png";
                File.WriteAllBytes(Path.Combine(OutDir, name), ms.ToArray());
                ok++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        Mod.Logger.Info($"[DevTextureDump] 清单 {names.Count} 个；导出 {ok}，" +
                        $"空/null {miss}，异常 {failed} -> {OutDir}");
    }

    private static int Rank(string asset)
    {
        for (int i = 0; i < Preferred.Length; i++)
        {
            if (asset.Contains(Preferred[i], StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return Preferred.Length;
    }

    public override void Unload()
    {
        _tried = false;
    }
}
