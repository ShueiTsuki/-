using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 上游 mixin/iota/* + mixin_interface/NestedCounter：显示 iota 时
///   - 嵌套列表按深度换颜色（深紫、金、深蓝、深红、深绿、深青，循环）——「[ , ]」跟着列表的颜色走；
///   - 列表里的内省 / 反思按配对深度换颜色（青、浅紫、绿、黄、蓝，循环），同一对颜色相同；不在列表里的不改；
///   - 注释 iota 两边不加逗号（这条不受开关影响）。
/// 计数器是显示时的全局状态，和上游一样：列表开始显示 +1、显示完 -1，回到最外层时括号计数清零。
/// </summary>
public sealed class NestedDisplay : IIotaDisplayDecorator
{
    private static readonly uint[] ListColors =
        { McColors.DarkPurple, McColors.Gold, McColors.DarkBlue, McColors.DarkRed, McColors.DarkGreen, McColors.DarkAqua };

    private static readonly uint[] ParenColors =
        { McColors.Aqua, McColors.LightPurple, McColors.Green, McColors.Yellow, McColors.Blue };

    private int _nested = -1;
    private int _parens = -1;

    private static bool On => HexParseSettings.Current.ShowColorfulNested;

    public void BeforeList(ListIota list)
    {
        if (!On) return;
        _nested++;
    }

    public void AfterList(ListIota list, DisplayText shown)
    {
        if (!On) return;
        if (_nested < 0) return;   // 上游这里也写着「but why?」
        shown.WithColor(ListColors[_nested % ListColors.Length]);
        _nested = System.Math.Max(_nested - 1, -1);
        if (_nested == -1) _parens = -1;
    }

    public void AfterPattern(PatternIota pattern, DisplayText shown)
    {
        if (!On) return;
        string sig = pattern.Pattern.AnglesSignature();
        bool left = sig == "qqq", right = sig == "eee";
        if (!left && !right) return;
        if (left && _nested >= 0) _parens++;
        if (_parens < 0) return;
        shown.WithColor(ParenColors[_parens % ParenColors.Length]);
        if (right && _nested >= 0) _parens = System.Math.Max(_parens - 1, -1);
    }

    /// <summary>上游 MixinListIotaDisplay：相邻两个里有一个是注释就不加逗号。</summary>
    public bool DropComma(Iota a, Iota b) => a is CommentIota || b is CommentIota;
}
