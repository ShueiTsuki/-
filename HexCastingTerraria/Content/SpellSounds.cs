using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace HexCastingTerraria.Content;

/// <summary>
/// 咒法学的音效层。
///
/// ## 为什么需要它（这是本项目第二个「静默的整块功能」）
///
/// VM 一直在算 `EvalSound`（`NormalExecute` / `Spell` / `Hermes` / `Thoth` / `Mishap`），
/// 而法杖的 `Item.UseSound` 又是 `null`，注释还写着「施法音效由 EvalSound 负责」——
/// 但**从来没有人播过它**。结果：整个模组施法一声不响，
/// 而且不报错、不掉帧、日志干净。和之前「粒子从来没显示」是同一类错误。
///
/// ## 与原版音效的关系
///
/// 原版有 21 个自定义音效（`HexSounds.java`：`casting.cast.normal/spell/hermes/thoth/fail`、
/// `abacus`、`scroll.scribble`、`flight.ambience`、`spellcircle.find_block` …）。
/// 我们**没有**搬那些 ogg 文件（那是原版的音频资产），而是把每个语义映射到
/// **泰拉自带的音效**上 —— 与 `beep` 用泰拉真实存在的乐器是同一套思路：
/// 宁可少而真，不要多而假。
///
/// ⚠️ 下面这张表是按**音效名字的含义**选的，不是听出来的。它们集中在一个表里，
/// 就是为了以后能一处改完（换音色只需要动 <see cref="Map"/>）。
/// </summary>
internal static class SpellSounds
{
    /// <summary>语义音效名。左边是原版的音效名，右边是我们用的泰拉音效。</summary>
    private static readonly (string Name, SoundStyle Style, float Volume, float Pitch)[] Map =
    {
        // ── 求值音效（每次求值一条图案响一下）──
        // 普通图案：极轻的短音。它一局要响几百次，**必须不吵**。
        ("casting.cast.normal", SoundID.MenuTick, 0.30f, 0.35f),
        // 法术释放：魔法感最强的那个
        ("casting.cast.spell", SoundID.MaxMana, 0.55f, 0.10f),
        // 元求值（eval / for_each / undo）：短促的「翻页」感
        ("casting.cast.hermes", SoundID.Item4, 0.45f, 0.20f),
        // 缩略（thoth）
        ("casting.cast.thoth", SoundID.Item29, 0.45f, 0.00f),
        // 失败：闷响
        ("casting.cast.fail", SoundID.Tink, 0.55f, -0.30f),

        // ── 画图案 ──
        // 起笔：低一点
        ("casting.pattern.start", SoundID.MenuTick, 0.35f, -0.20f),
        // 每走一段：高一点（比起笔亮，形成"描线"的连续感）
        ("casting.pattern.add_segment", SoundID.MenuTick, 0.25f, 0.55f),

        // ── 物品与方块 ──
        ("staff.reset", SoundID.MenuClose, 0.45f, 0.00f),
        ("abacus", SoundID.Tink, 0.45f, 0.30f),
        ("abacus.shake", SoundID.MenuTick, 0.40f, -0.40f),
        ("scroll.scribble", SoundID.Unlock, 0.45f, 0.10f),
        ("scroll.dust", SoundID.Item35, 0.40f, 0.20f),

        // ── 法术环 ──
        // 每走一格：很轻，长环会响几十次
        ("spellcircle.find_block", SoundID.MenuTick, 0.22f, -0.50f),
        ("spellcircle.fail", SoundID.MenuClose, 0.60f, -0.30f),

        // ── 飞行 ──
        ("flight.start", SoundID.Item4, 0.50f, 0.35f),
        ("flight.finish", SoundID.MenuClose, 0.50f, 0.20f),
    };

    /// <summary>取一个语义音效。找不到就返回 null（不静默回落到某个无关音效）。</summary>
    private static SoundStyle? Style(string name)
    {
        foreach (var entry in Map)
        {
            if (entry.Name == name) return entry.Style with { Volume = entry.Volume, Pitch = entry.Pitch };
        }

        return null;
    }

    /// <summary>播放一个语义音效。<paramref name="at"/> 为 null 时是「无位置」的界面音。</summary>
    public static void Play(string name, Vector2? at = null)
    {
        if (Main.dedServ) return;

        var style = Style(name);
        if (style == null) return;

        if (at is { } position)
        {
            SoundEngine.PlaySound(style.Value, position);
        }
        else
        {
            SoundEngine.PlaySound(style.Value);
        }
    }

    /// <summary>把 VM 的求值音效映射成我们的语义音效名。`Nothing` / `Mute` 返回 null。</summary>
    public static string? NameOf(EvalSound sound) => sound switch
    {
        EvalSound.NormalExecute => "casting.cast.normal",
        EvalSound.Spell => "casting.cast.spell",
        EvalSound.Hermes => "casting.cast.hermes",
        EvalSound.Thoth => "casting.cast.thoth",
        EvalSound.Mishap => "casting.cast.fail",
        _ => null,      // Nothing / Mute：不响
    };

    /// <summary>播一次求值音效。</summary>
    public static void PlayEval(EvalSound sound, Vector2? at = null)
    {
        string? name = NameOf(sound);
        if (name != null) Play(name, at);
    }

    // ── 联机 ───────────────────────────────────────────────────────

    /// <summary>
    /// 服务端 → 附近客户端：广播一个语义音效。
    ///
    /// 为什么不直接播：权威求值在服务端，而 `SoundEngine.PlaySound` 在服务端上
    /// 客户端听不到（`Main.dedServ` 为真时它什么都不做）。
    /// 与粒子（`SpellParticles`）同一条思路。
    /// </summary>
    public static void Broadcast(string name, float x, float y)
    {
        if (Main.netMode != NetmodeID.Server) return;

        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)Net.HexMessage.SpellSound);
        packet.Write(name);
        packet.Write(x);
        packet.Write(y);

        const float radiusPx = 120f * Core.Casting.HexUnits.PixelsPerTile;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true }) continue;
            if (System.Math.Abs(other.Center.X - x) > radiusPx) continue;
            if (System.Math.Abs(other.Center.Y - y) > radiusPx) continue;

            packet.Send(i);
        }
    }

    /// <summary>客户端：接收广播来的音效。</summary>
    public static void Receive(System.IO.BinaryReader reader)
    {
        if (Main.dedServ) return;

        string name = reader.ReadString();
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();

        Play(name, new Vector2(x, y));
    }

    /// <summary>播求值音效；联机时由服务端广播。</summary>
    public static void EmitEval(EvalSound sound, Vector2 at)
    {
        string? name = NameOf(sound);
        if (name == null) return;

        if (Main.netMode == NetmodeID.Server)
        {
            Broadcast(name, at.X, at.Y);
        }
        else
        {
            Play(name, at);
        }
    }
}
