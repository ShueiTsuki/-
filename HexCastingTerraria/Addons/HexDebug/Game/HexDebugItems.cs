using System.Collections.Generic;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Config;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Eval;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>贴图（上游 jar 里的 16×16 放大两倍，_tools/gen_hexdebug_art.py 生成）。</summary>
internal static class HexDebugArt
{
    public const string Dir = "HexCastingTerraria/Addons/HexDebug/Assets/";

    public static Texture2D Get(string name)
        => ModContent.Request<Texture2D>(Dir + name, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;

    /// <summary>在背包格子里按本体贴图的位置与缩放叠一层。</summary>
    public static void Overlay(SpriteBatch sb, string name, Vector2 position, Rectangle frame, Color drawColor, Vector2 origin, float scale)
        => sb.Draw(Get(name), position, frame, drawColor, 0f, origin, scale, SpriteEffects.None, 0f);
}

/// <summary>线程号（淬灵的潜行 + Ctrl + 滚轮切换，上游 rotateThreadId：夹在 0 到上限之间，不绕回）。</summary>
internal static class ThreadIds
{
    public static int Max => HexAddonsConfig.Instance.HexDebugOptions.MaxDebugThreads;

    public static int Rotate(int threadId, bool increase) => System.Math.Clamp(threadId + (increase ? 1 : -1), 0, Max - 1);
}

/// <summary>
/// 调试杖 / 淬灵调试杖（上游 items/DebuggerItem.kt）：和造物一样封着一段咒术（也能直接调试另一只手里的列表），
/// 每用一次按步进模式走一步；潜行 + 滚轮换步进模式，淬灵的再加 Ctrl 换线程（可以同时调试好几段）。
/// </summary>
public abstract class DebuggerItemBase : ItemPackagedSpell, IShiftScrollable
{
    public abstract bool IsQuenched { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled("hexdebug");

    /// <summary>上游 canDrawMediaFromInventory = true、breakAfterDepletion = false、冷却用造物的冷却。</summary>
    public override PackagedSpellKind Kind => PackagedSpellKind.Artifact;

    public override string CraftKey => IsQuenched ? "hexdebug:quenched_debugger" : "hexdebug:debugger";

    public override bool UsesStateArt => false;

    public override string Texture => HexDebugArt.Dir + (IsQuenched ? "QuenchedDebugger" : "Debugger");

    public StepMode Mode { get; private set; } = StepMode.Continue;

    /// <summary>普通调试杖固定线程 0；淬灵的可切换。</summary>
    public int ThreadId { get; private set; }

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = IsQuenched ? ItemRarityID.LightRed : ItemRarityID.Green;
    }

    /// <summary>空的也能用：调试另一只手里的列表。</summary>
    public override bool CanUseItem(Player player) => true;

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;
        int slot = player.selectedItem;
        HexDebugNet.ToServer(HexDebugNet.Msg.UseDebugger, w => w.Write((byte)slot));
        return true;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        if (IsQuenched)
        {
            HexDebugClient.Threads.TryGetValue(ThreadId, out var v);
            tooltips.Add(new TooltipLine(Mod, "HexDebugThread", HexDebugText.Thread(ThreadId, v?.Name)) { OverrideColor = Color.Gray });
        }
        base.ModifyTooltips(tooltips);
    }

    // 上游：只潜行 = 换步进模式；潜行 + Ctrl = 换线程（只有淬灵的）
    public bool CanShiftScroll(bool ctrl) => !ctrl || IsQuenched;

    public string? ShiftScroll(Player player, bool increase, bool ctrl)
    {
        if (ctrl)
        {
            ThreadId = ThreadIds.Rotate(ThreadId, increase);
            HexDebugClient.Threads.TryGetValue(ThreadId, out var v);
            return HexDebugText.Thread(ThreadId, v?.Name);
        }
        int n = System.Enum.GetValues<StepMode>().Length;
        Mode = (StepMode)((((int)Mode + (increase ? 1 : -1)) % n + n) % n);
        return HexDebugText.StepMode(Mode);
    }

    // 上游的物品模型：本体 + 封着咒术的叠层 + 调试中的叠层 + 步进模式图标
    public override void PostDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        bool debugging = HexDebugClient.IsDebugging(ThreadId);
        if (!IsEmpty) HexDebugArt.Overlay(sb, "Overlay_HasHex", position, frame, drawColor, origin, scale);
        if (debugging) HexDebugArt.Overlay(sb, "Overlay_Debugging", position, frame, drawColor, origin, scale);
        HexDebugArt.Overlay(sb, "Step_" + (debugging ? "debugging" : "not_debugging") + "_" + Mode.ToString().ToLowerInvariant(),
            position, frame, drawColor, origin, scale);
    }

    public override void SaveData(TagCompound tag)
    {
        base.SaveData(tag);
        tag["hexdebug_step_mode"] = (int)Mode;
        tag["hexdebug_thread_id"] = ThreadId;
    }

    public override void LoadData(TagCompound tag)
    {
        base.LoadData(tag);
        Mode = (StepMode)System.Math.Clamp(tag.GetInt("hexdebug_step_mode"), 0, System.Enum.GetValues<StepMode>().Length - 1);
        ThreadId = IsQuenched ? System.Math.Max(0, tag.GetInt("hexdebug_thread_id")) : 0;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        base.NetSend(writer);
        writer.Write((byte)Mode);
        writer.Write((byte)ThreadId);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        base.NetReceive(reader);
        Mode = (StepMode)reader.ReadByte();
        ThreadId = reader.ReadByte();
    }
}

public sealed class Debugger : DebuggerItemBase
{
    public override bool IsQuenched => false;

    // 上游 recipes/debugger.json（有序合成 " CC"/" UC"/"L  "）：造物 + 充能紫水晶 ×3 + 金锭
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<Artifact>()
            .AddIngredient<ChargedAmethyst>(3)
            .AddIngredient(ItemID.GoldBar)
            .AddTile(TileID.Anvils)
            .Register();
    }
}

public sealed class QuenchedDebugger : DebuggerItemBase
{
    public override bool IsQuenched => true;

    // 上游 recipes/quenched_debugger.json（flyswatter_quenching：保留原物品的数据）：调试杖 + 淬灵晶碎片 ×4
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<Debugger>()
            .AddIngredient<QuenchedAllayShard>(4)
            .AddTile(TileID.WorkBenches)
            .AddOnCraftCallback((r, item, consumed, dest) => QuenchingRecipe.CopyFrom(item, consumed, ModContent.ItemType<Debugger>()))
            .Register();
    }
}

/// <summary>上游 FlyswatterQuenchingShapedRecipe：淬灵时把原物品里的东西（咒术、媒质、模式等）原样带过去。</summary>
internal static class QuenchingRecipe
{
    public static void CopyFrom(Item item, System.Collections.Generic.List<Item> consumedItems, int sourceType)
    {
        foreach (var c in consumedItems)
        {
            if (c.type != sourceType || c.ModItem is not { } source) continue;
            var tag = new TagCompound();
            source.SaveData(tag);
            item.ModItem.LoadData(tag);
            return;
        }
    }
}

/// <summary>
/// 运行杖 / 淬灵运行杖（上游 items/EvaluatorItem.kt）：一根法杖，画的图案在调试杖当前（已暂停的）线程的栈上跑；
/// 潜行使用先复原到画第一个图案之前。淬灵的潜行 + Ctrl + 滚轮换线程。
/// </summary>
public abstract class EvaluatorItemBase : AddonItem, IShiftScrollable
{
    public override string AddonId => "hexdebug";

    public abstract bool IsQuenched { get; }

    public override string Texture => HexDebugArt.Dir + (IsQuenched ? "QuenchedEvaluator" : "Evaluator");

    public int ThreadId { get; private set; }

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.autoReuse = false;
        Item.useTurn = true;
        Item.maxStack = 1;
        Item.noMelee = true;
        Item.rare = IsQuenched ? ItemRarityID.LightRed : ItemRarityID.Green;
        Item.value = Item.sellPrice(silver: 40);
    }

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;
        bool shift = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
                     || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
        if (shift && HexDebugClient.Threads.TryGetValue(ThreadId, out var v) && v.EvaluatorModified)
        {
            Content.SpellSounds.Play("staff.reset", player.Center);
        }
        int thread = ThreadId;
        HexDebugNet.ToServer(HexDebugNet.Msg.EvalOpen, w =>
        {
            w.Write((byte)thread);
            w.Write(shift);
        });
        return true;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        if (!IsQuenched) return;
        HexDebugClient.Threads.TryGetValue(ThreadId, out var v);
        tooltips.Add(new TooltipLine(Mod, "HexDebugThread", HexDebugText.Thread(ThreadId, v?.Name)) { OverrideColor = Color.Gray });
    }

    // 上游：只有淬灵的、只有潜行 + Ctrl
    public bool CanShiftScroll(bool ctrl) => ctrl && IsQuenched;

    public string? ShiftScroll(Player player, bool increase, bool ctrl)
    {
        ThreadId = ThreadIds.Rotate(ThreadId, increase);
        HexDebugClient.Threads.TryGetValue(ThreadId, out var v);
        return HexDebugText.Thread(ThreadId, v?.Name);
    }

    public override void PostDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        if (HexDebugClient.Threads.TryGetValue(ThreadId, out var v) && v.EvaluatorModified)
        {
            HexDebugArt.Overlay(sb, "Overlay_Modified", position, frame, drawColor, origin, scale);
        }
    }

    public override void SaveData(TagCompound tag) => tag["hexdebug_thread_id"] = ThreadId;

    public override void LoadData(TagCompound tag) => ThreadId = IsQuenched ? System.Math.Max(0, tag.GetInt("hexdebug_thread_id")) : 0;

    public override void NetSend(System.IO.BinaryWriter writer) => writer.Write((byte)ThreadId);

    public override void NetReceive(System.IO.BinaryReader reader) => ThreadId = reader.ReadByte();
}

public sealed class Evaluator : EvaluatorItemBase
{
    public override bool IsQuenched => false;

    // 上游 recipes/evaluator.json：板岩方块 ×2 + 充能紫水晶 ×3（法杖的做法，所以用工作台）
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(Content.Items.HexRecipeGroups.SlateBlocks, 2)
            .AddIngredient<ChargedAmethyst>(3)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

public sealed class QuenchedEvaluator : EvaluatorItemBase
{
    public override bool IsQuenched => true;

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<Evaluator>()
            .AddIngredient<QuenchedAllayShard>(4)
            .AddTile(TileID.WorkBenches)
            .AddOnCraftCallback((r, item, consumed, dest) => QuenchingRecipe.CopyFrom(item, consumed, ModContent.ItemType<Evaluator>()))
            .Register();
    }
}
