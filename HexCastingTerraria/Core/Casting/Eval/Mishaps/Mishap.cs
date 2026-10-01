using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

/// <summary>
/// mishap 的上下文：出错的图案与（可能的）图案名。
/// 移植自 at.petrak.hexcasting.api.casting.mishaps.Mishap.Context。
/// </summary>
public sealed class MishapContext
{
    public HexPattern? Pattern { get; }
    public string? Name { get; }

    /// <summary>
    /// 名字的颜色：上游 HexAPI.getActionI18n 给大法术（需要启蒙）金色、其余浅紫；
    /// 数字 / 簿记员（SpecialHandler.getName）浅紫。null = 不上色（上游法术环的 fakeThrowMishap 用方块名）。
    /// </summary>
    public uint? NameColor { get; }

    public MishapContext(HexPattern? pattern, string? name, uint? nameColor = McColors.LightPurple)
    {
        Pattern = pattern;
        Name = name;
        NameColor = nameColor;
    }

    /// <summary>
    /// 上游 PatternIota.lookupAndOperate 的 castedName：注册表里的图案用它的名字（大法术金色），
    /// 数字 / 簿记员用特殊名（「数字之精思：5」「簿记员之策略：v-」），都不是就没有名字。
    /// </summary>
    public static MishapContext Of(HexPattern pattern)
    {
        bool great = PatternRegistry.Match(pattern)?.RequiresEnlightenment == true;
        return new MishapContext(pattern, PatternDisplay.NameOf(pattern), great ? McColors.Gold : McColors.LightPurple);
    }
}

/// <summary>
/// 咒法学里的「错误」。命名沿用原作的 mishap。
/// 移植自 at.petrak.hexcasting.api.casting.mishaps.Mishap。
///
/// 注意：mishap 是**异常**，但它的作用是「反噬」——
/// 由 DoMishap 副作用调用 <see cref="Execute"/> 来修改栈并产生游戏效果。
///
/// 消息一律照上游 hexcasting.mishap.* 的官方中文；里面的 iota、向量、实体名用 <see cref="DisplayTags"/> 的聊天标记
///（聊天栏、探知透镜看原动力、HexDebug 面板都认）。
/// </summary>
public abstract class Mishap : Exception
{
    protected Mishap() { }

    protected Mishap(string message) : base(message) { }

    /// <summary>
    /// 执行实际效果（不只是特效用），可以修改栈。
    /// 对应源项目的 execute。
    /// </summary>
    public abstract void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack);

    /// <summary>解析状态，默认 Errored。少数 mishap 会覆盖（如 ItemTooFarAway 相关）。</summary>
    public virtual ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Errored;

    /// <summary>
    /// 出消息之前看一眼它将要作用的栈（<see cref="CastingVM.LocateMishaps"/> 在 postExecution 之前调）。
    /// 默认什么都不做；MishapInvalidIota 用它找出错参数的下标。
    /// </summary>
    public virtual void LocateIn(IReadOnlyList<Iota> stack) { }

    /// <summary>执行并返回修改后的栈。</summary>
    public List<Iota> ExecuteReturnStack(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        Execute(env, errorCtx, stack);
        return stack;
    }

    /// <summary>错误消息（用于聊天栏显示）。返回 null 表示不显示。</summary>
    protected abstract string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx);

    /// <summary>
    /// 上游 errorMessageWithName：有名字时套 hexcasting.mishap「%s：%s」（名字带颜色），没有就只有消息。
    /// 注意：这里曾经写成「「名字」：消息」，多了一对引号、名字也没上色。
    /// </summary>
    public string? ErrorMessageWithName(CastingEnvironment env, MishapContext errorCtx)
    {
        var msg = ErrorMessage(env, errorCtx);
        if (msg == null)
        {
            return null;
        }
        if (errorCtx.Name == null)
        {
            return msg;
        }
        string name = errorCtx.NameColor is { } c
            ? DisplayTags.Of(DisplayText.Literal(errorCtx.Name, c))
            : errorCtx.Name;
        return $"{name}：{msg}";
    }

    // ── 消息里常用的几样东西（上游 Mishap 的 helper 与各 mishap 的参数）──────────────

    /// <summary>上游 BlockPos.toShortString()：「x, y, z」。方块坐标 = 向量各分量取整（BlockPos.containing）。</summary>
    protected static string BlockPosText(double x, double y, double z)
        => $"{(long)System.Math.Floor(x)}, {(long)System.Math.Floor(y)}, {(long)System.Math.Floor(z)}";

    /// <summary>上游 Vec3Iota.display(位置)：红色的「(x.xx, y.yy, z.zz)」。</summary>
    protected static string VecText(double x, double y, double z) => DisplayTags.Of(new VectorIota(x, y, z));

    /// <summary>
    /// 上游 blockAtPos：那一格方块的名字（getBlockState(pos).block.name，不上色）。
    /// Core 只有坐标，名字向世界要（<see cref="ICastingWorld.BlockNameAt"/>）；世界给不出（离线测试的假世界）时退回那个位置的向量显示。
    /// </summary>
    protected static string BlockNameText(CastingEnvironment env, double x, double y, double z)
        => env.World?.BlockNameAt(x, y) ?? VecText(x, y, z);

    /// <summary>上游 entity.displayName / player.name：实体的名字，不上色；找不到实体时是 hexcasting.spelldata.entity.whoknows。</summary>
    protected static string EntityNameText(EntityIota entity) => IotaDisplay.EntityName?.Invoke(entity) ?? "未知实体";

    /// <summary>上游 entity.displayName.plainCopy().aqua：青色的实体名（和 EntityIota 的显示一样）。</summary>
    protected static string EntityNameAqua(EntityIota entity) => DisplayTags.Of(entity);

    /// <summary>上游 ItemStack.getDisplayName()：方括号括起来的物品名。</summary>
    protected static string ItemNameText(string name) => DisplayTags.Of(DisplayText.Literal($"[{name}]"));
}
