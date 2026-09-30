using HexCastingTerraria.Core.Media;

/// <summary>
/// 颜料（染色剂）的取色，逐条对原版 ColorProvider / ADPigment.morphBetweenColors / ItemUUIDPigment。
/// </summary>
static class PigmentTests
{
    public static void Run(CheckFn check)
    {
        Console.WriteLine("\n=== ⑨ 颜料（原版 ColorProvider）===");

        check("35 种颜料：染料 16 + 骄傲旗 16 + 空无 / 远古 / 灵魂闪光", Pigments.All.Count == 35
            && Pigments.All.Count(p => p.Id.StartsWith("dye_")) == 16 && Pigments.All.Count(p => p.Id.StartsWith("pride_")) == 16,
            $"{Pigments.All.Count}");

        // 纯色：MC DyeColor.textColor；空无 = 0xab65eb（没用过颜料时的默认色）
        check("纯色颜料与时间、位置无关", Pigments.Color("dye_red", Guid.Empty, 0, 0) == 0xFF0000
            && Pigments.Color("dye_red", Guid.Empty, 12345, 0.1f) == 0xFF0000
            && Pigments.Color("default", Guid.Empty, 777, 0) == 0xab65eb);
        check("未知 id 退回默认颜料（原版 FrozenPigment 读不出时用 DEFAULT）",
            Pigments.Color("no_such_pigment", Guid.Empty, 0, 0) == 0xab65eb);

        // 纯黑亮度 < 0.05：叠一层暗色色轮（原版 MINIMUM_LUMINANCE_COLOR_WHEEL），不会真的画成看不见的黑
        int black = Pigments.Color("dye_black", Guid.Empty, 0, 0);
        check("黑色颜料加上最低亮度（色轮第一格 0x200000）", black == 0x200000, $"0x{black:x6}");

        // morphBetweenColors：整数格点上正好是表里的颜色；半格处三次缓动 t = 0.5
        var gay = Pigments.Find("pride_gay")!;
        int at0 = Pigments.Color("pride_gay", Guid.Empty, 0, 0);
        int at1 = Pigments.Color("pride_gay", Guid.Empty, gay.Period / gay.Colors.Length, 0);
        check("渐变：第 k 格正好是第 k 种颜色（400 刻一圈、5 色）",
            at0 == gay.Colors[0] && at1 == gay.Colors[1], $"0x{at0:x6} 0x{at1:x6}");
        int mid = Pigments.Morph(new[] { 0x000000, 0xFEFEFE }, 0.25f, 0);   // 2 色、0.25 圈 = 第 0 格的一半
        check("渐变：半格处缓入缓出取中值", mid == 0x7F7F7F, $"0x{mid:x6}");
        check("渐变：位置偏移（posDot）让同一时刻不同位置颜色不同",
            Pigments.Color("pride_gay", Guid.Empty, 50, 0) != Pigments.Color("pride_gay", Guid.Empty, 50, 0.1f));
        check("渐变：一圈之后回到起点（positiveModulo）",
            Pigments.Color("pride_gay", Guid.Empty, 400, 0) == at0 && Pigments.Color("pride_gay", Guid.Empty, -400, 0) == at0);

        // java.util.Random：已知输出（new Random(0).nextFloat() = 0.73096776，new Random(42).nextFloat() = 0.7275637）
        float r0 = new Pigments.JavaRandom(0).NextFloat(), r42 = new Pigments.JavaRandom(42).NextFloat();
        check("java.util.Random 移植与 Java 输出一致", MathF.Abs(r0 - 0.73096776f) < 1e-7 && MathF.Abs(r42 - 0.7275637f) < 1e-7,
            $"{r0:0.00000000} {r42:0.00000000}");

        // MC Mth.hsvToRgb
        check("HSV → RGB（MC Mth.hsvToRgb）", Pigments.HsvToRgb(0, 1, 1) == 0xFF0000
            && Pigments.HsvToRgb(1f / 3, 1, 1) == 0x00FF00 && Pigments.HsvToRgb(0.5f, 0, 0.5f) == 0x7F7F7F);

        // 灵魂闪光：同一个人永远同一对颜色，不同的人不同
        var a = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var b = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var ca = Pigments.OwnerColors(a);
        check("灵魂闪光：颜色由主人决定（同人同色、异人异色、两种颜色）",
            ca.Length == 2 && ca.SequenceEqual(Pigments.OwnerColors(a)) && !ca.SequenceEqual(Pigments.OwnerColors(b)));
        check("灵魂闪光：按主人取色，而不是固定颜色",
            Pigments.Color("uuid", a, 0, 0) == ca[0] || Pigments.Color("uuid", a, 0, 0) != Pigments.Color("uuid", b, 0, 0));
    }
}
