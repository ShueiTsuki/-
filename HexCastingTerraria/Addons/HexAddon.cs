using System.Collections.Generic;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons;

/// <summary>附属跑在哪一侧：决定它的开关放在服务端还是客户端配置（ADDONS.md「开关的位置」）。</summary>
public enum AddonSide
{
    /// <summary>有物品 / 方块 / 图案 / 指令，两端都要有：服务端开关，需要重载。</summary>
    Both,

    /// <summary>纯客户端界面：客户端开关，随时改。</summary>
    Client,
}

/// <summary>
/// 一个附属的入口。每个附属在 <c>Addons/&lt;名&gt;/Game/&lt;名&gt;Addon.cs</c> 里写一个子类，
/// 并在 <see cref="AddonRegistry"/> 里登记一行。
///
/// 规矩（check_arch 断言）：<see cref="Id"/> = addon.json 的 id = 图案命名空间；
/// <see cref="Side"/> = addon.json 的 side；开关读哪份配置由 Side 决定。
/// </summary>
public abstract class HexAddon
{
    /// <summary>附属 id，也是图案 / iota 种类的命名空间，例如 "hexparse"。</summary>
    public abstract string Id { get; }

    /// <summary>显示名（上游的名字）。</summary>
    public abstract string Name { get; }

    public abstract AddonSide Side { get; }

    /// <summary>开关是否打开（读配置）。关着 = 没装：内容不加载、图案不识别、书里不出现。</summary>
    public abstract bool IsEnabled { get; }

    /// <summary>
    /// 这个附属的全部图案（静态数据）。**不管开没开都会声明** ——
    /// 每个世界的大法术笔顺要避开它们（见 PatternRegistry「附属的图案」）。
    /// </summary>
    public virtual IEnumerable<PatternData> Patterns => System.Array.Empty<PatternData>();

    /// <summary>开着时，模组加载阶段调用：登记 iota 种类、图案行为等。</summary>
    public virtual void OnLoad(Mod mod) { }

    /// <summary>开着时，模组卸载阶段调用：撤掉 OnLoad 登记的东西。</summary>
    public virtual void OnUnload() { }

    /// <summary>开着时，建书的时候调用：把附属的分类 / 条目加进咒法学之书。</summary>
    public virtual void AddBookContent(BookDocument book) { }

    /// <summary>收到这个附属的联机消息（<see cref="GetPacket"/> 发的；附属 id 已经读掉了）。</summary>
    public virtual void HandlePacket(System.IO.BinaryReader reader, int whoAmI) { }

    /// <summary>开一个这个附属的联机包：已经写好「附属消息 + 附属 id」，接着写自己的内容再 Send。</summary>
    public ModPacket GetPacket()
    {
        var packet = HexCastingTerraria.Instance!.GetPacket();
        packet.Write((byte)Content.Net.HexMessage.Addon);
        packet.Write(Id);
        return packet;
    }
}
