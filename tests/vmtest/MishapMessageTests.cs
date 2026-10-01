using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

/// <summary>
/// 事故消息照上游官方中文（hexcasting/lang/zh_cn.json 的 hexcasting.mishap.*）。
/// 期望值逐条按上游的 key 与参数拼出来；比纯文字时先 DisplayTags.Strip 去掉聊天标记。
/// 「另一只手」按移植版的说法写成「快捷栏中手持物品右边一格」。
/// </summary>
static class MishapMessageTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("事故消息：" + name, ok, detail);

    static PatternIota P(string id)
        => new(PatternRegistry.All.First(d => d.Id == id).Prototype);

    /// <summary>跑一串 iota，返回最后一条事故消息（带标记）。</summary>
    static (string? Raw, string Plain, CastOutcome Result) Cast(TestEnv env, Iota[] stack, params Iota[] iotas)
    {
        var img = new CastingImage(stack);
        var r = new CastingVM(img, env).QueueExecute(img, iotas);
        string? raw = env.MishapMessages.LastOrDefault();
        return (raw, raw is null ? "" : DisplayTags.Strip(raw), r);
    }

    static string Msg(Mishap m, TestEnv? env = null)
        => DisplayTags.Strip(m.ErrorMessageWithName(env ?? new TestEnv(), new MishapContext(null, null)) ?? "");

    public static void Run()
    {
        System.Console.WriteLine("=== 事故消息照原版官方中文 ===");

        // ── 名字前缀：上游 hexcasting.mishap「%s：%s」，名字浅紫（大法术金色），没有「」 ──
        {
            var (raw, plain, _) = Cast(new TestEnv(), System.Array.Empty<Iota>(), P("hexcasting:add"));
            Check("参数不够、空栈：no_args，名字前缀浅紫",
                plain == "加法之馏化：本应接受大于等于2个参数，而实际为空栈"
                && raw!.StartsWith("[hext/FF55FF:加法之馏化]：") && !raw.Contains('「'), raw);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(1) }, P("hexcasting:add"));
            Check("参数不够：not_enough_args「而实际栈高度为1」", plain == "加法之馏化：本应接受大于等于2个参数，而实际栈高度为1", plain);
        }
        {
            var (raw, _, _) = Cast(new TestEnv(), System.Array.Empty<Iota>(), P("hexcasting:teleport/great"));
            Check("大法术的名字是金色（上游 getActionI18n isGreat → GOLD）", raw?.StartsWith("[hext/FFAA00:卓越传送]：") == true, raw);
        }
        {
            HexPattern.TryFromAngles("a", HexDir.SouthEast, out var v, out _);   // 簿记员之策略「v」
            var (_, plain, _) = Cast(new TestEnv(), System.Array.Empty<Iota>(), new PatternIota(v!));
            Check("簿记员之策略的事故也带名字（上游 SpecialHandler.getName）",
                plain == "簿记员之策略：v：本应接受大于等于1个参数，而实际为空栈", plain);
        }
        {
            var env = new TestEnv { Enlightened = false };
            var (_, _, r) = Cast(env, System.Array.Empty<Iota>(), P("hexcasting:teleport/great"));
            Check("未启蒙：没有事故消息，「法术没起效」单独发给施法者（上游 sendSystemMessage）",
                env.MishapMessages.Count == 1 && env.MishapMessages[0] is null
                && env.CasterMessages.SequenceEqual(new[] { "奇怪，法术没起效……也许我还不够熟练？" }),
                string.Join(" | ", env.MishapMessages) + " / " + string.Join(" | ", env.CasterMessages));
        }

        // ── 参数不对：invalid_value，下标是离栈顶多远，说法照 invalid_value.* ──
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(5), new DoubleIota(1), new DoubleIota(2) }, P("hexcasting:if"));
            Check("最深的参数错：下标 2（以前消息里永远是 0）",
                plain == "占卜师之提整：本应在栈下标为2处接受一个布尔值，而实际接受了一个数：5.00", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(1) }, P("hexcasting:splat"));
            Check("「一个列表」（以前是「list」「列表」）", plain == "群体之拆解：本应在栈下标为0处接受一个列表，而实际接受了一个数：1.00", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(9), new DoubleIota(5) }, P("hexcasting:last_n_list"));
            Check("群体之策略个数过大：int.positive.less.equal「一个小于等于1的正整数」",
                plain == "群体之策略：本应在栈下标为0处接受一个小于等于1的正整数，而实际接受了一个数：5.00", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(world: new FakeWorld()),
                new Iota[] { new VectorIota(1, 1), new DoubleIota(20), new DoubleIota(3) }, P("hexcasting:beep"));
            Check("乐器编号越界：getPositiveIntUnder「一个小于14的正整数」，下标 1",
                plain == "弹奏音符：本应在栈下标为1处接受一个小于14的正整数，而实际接受了一个数：20.00", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(world: new FakeWorld()),
                new Iota[] { new EntityIota(EntityIota.EntityKind.Player, 0), new VectorIota(10, 20) }, P("hexcasting:brainsweep"));
            Check("剥离意识给了玩家：getMob「一个生物」（不是「排斥此生物的意识」）",
                plain == "剥离意识：本应在栈下标为1处接受一个生物，而实际接受了一个实体：未知实体", plain);
        }
        Check("带数字的说法：整数原样、小数照 Java Double.toString",
            InvalidValue.DoubleBetween(-1.0, 1.0) == "一个介于-1.0和1.0之间的数"
            && InvalidValue.DoublePositiveLessEqual(10.0) == "一个小于等于10.0的正数"
            && InvalidValue.IntBetween(-3, 3) == "一个介于-3和3之间的整数"
            && InvalidValue.JavaDouble(0.5) == "0.5",
            InvalidValue.DoubleBetween(-1.0, 1.0));
        {
            var (_, plain, r) = Cast(new TestEnv(), new Iota[] { new DoubleIota(1), BooleanIota.True, new DoubleIota(3) }, P("hexcasting:construct_vec"));
            Check("向量之提整参数不对：上游是运算符，invalid_operator_args.many 列出全部参数并全换成垃圾",
                plain == "向量之提整：在栈下标为0到2处获取到3个意外iota：1.00, True, 3.00"
                && r.Image.Stack.Count == 3 && r.Image.Stack.All(i => i is GarbageIota), plain);
        }

        // ── 除以零：divide_by_zero.*，0 说成「零」，向量按整个向量报 ──
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(1), new DoubleIota(0) }, P("hexcasting:div"));
            Check("1 ÷ 0：「试图用零除1.00」", plain == "除法之馏化：试图用零除1.00", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new VectorIota(1, 2, 3), new DoubleIota(0) }, P("hexcasting:div"));
            Check("向量 ÷ 0：报整个向量（上游 OperatorVec3Delegating 接住再抛）", plain == "除法之馏化：试图用零除(1.00, 2.00, 3.00)", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(), new Iota[] { new DoubleIota(-8), new DoubleIota(0.5) }, P("hexcasting:pow"));
            Check("负数开方：divide_by_zero.exponent", plain == "乘方之馏化：试图计算-8.00的0.50", plain);
        }
        Check("对数 / 正切：logarithm、tan 的正弦余弦说法",
            Msg(MishapDivideByZero.Of(0.0, 2.0, "logarithm")) == "试图计算零以2.00为底的对数"
            && Msg(MishapDivideByZero.Tan(1.5)) == "试图用1.50的余弦除1.50的正弦",
            Msg(MishapDivideByZero.Tan(1.5)));

        // ── 位置、方块 ──
        {
            var world = new FakeWorld { RangeCheck = (x, y) => x < 50 };
            var (raw, plain, _) = Cast(new TestEnv(world: world),
                new Iota[] { new VectorIota(100, 0), new DoubleIota(1), new DoubleIota(3) }, P("hexcasting:beep"));
            Check("超出范围：location_too_far，位置用向量显示（红色）",
                plain == "弹奏音符：(100.00, 0.00, 0.00)超出影响范围" && raw!.Contains("[hext/FF5555:(100.00, 0.00, 0.00)]"), raw);
        }
        {
            var world = new FakeWorld();
            world.BlockNames[(4, 4)] = "泥土块";
            var (_, plain, _) = Cast(new TestEnv(world: world), new Iota[] { new VectorIota(4.5, 4.5) }, P("hexcasting:edify"));
            Check("不是树苗：bad_block，位置 toShortString、最后是方块名",
                plain == "启迪树苗：本应在4, 4, 0处接受一个树苗，而实际接受了泥土块", plain);
        }
        {
            var (_, plain, _) = Cast(new TestEnv(world: new FakeWorld()), new Iota[] { new VectorIota(4.5, 4.5) }, P("hexcasting:edify"));
            Check("世界给不出方块名：退回那个位置的向量显示",
                plain == "启迪树苗：本应在4, 4, 0处接受一个树苗，而实际接受了(4.50, 4.50, 0.00)", plain);
        }
        Check("位置不可用：location_out_of_world / too_close_to_out / forbidden",
            Msg(new MishapBadLocation(1, 2, MishapBadLocation.OutOfWorld)) == "(1.00, 2.00, 0.00)不在此世界内"
            && Msg(new MishapBadLocation(1, 2, MishapBadLocation.TooCloseToOut)) == "(1.00, 2.00, 0.00)离世界边界太近了"
            && Msg(new MishapBadLocation(1, 2, MishapBadLocation.Forbidden, 3)) == "(1.00, 2.00, 3.00)并未对你开放",
            Msg(new MishapBadLocation(1, 2, MishapBadLocation.Forbidden, 3)));
        Check("没有阿卡夏记录：「x, y, z处无阿卡夏记录」", Msg(new MishapNoAkashicRecord(3.5, -2.5, 0)) == "3, -3, 0处无阿卡夏记录",
            Msg(new MishapNoAkashicRecord(3.5, -2.5, 0)));

        // ── 手上的东西：bad_item.offhand / no_item.offhand ──
        {
            var (_, plain, _) = Cast(new TestEnv(), System.Array.Empty<Iota>(), P("hexcasting:read"));
            Check("手上没有载体：「而实际无对应物品」",
                plain == "书吏之精思：需要在快捷栏中手持物品右边一格放有一个可以读出iota的地方，而实际无对应物品", plain);
        }
        {
            var env = new TestEnv { HasStorage = true, HeldStorageDesc = new ItemStackInfo("核心", 1) };
            var (_, plain, _) = Cast(env, System.Array.Empty<Iota>(), P("hexcasting:read"));
            Check("空载体：报出那件物品「而实际放有1个[核心]」",
                plain == "书吏之精思：需要在快捷栏中手持物品右边一格放有一个可以读出iota的地方，而实际放有1个[核心]", plain);
        }
        {
            var env = new TestEnv { HasStorage = true, HeldCanWrite = _ => false, HeldStorageDesc = new ItemStackInfo("念珠", 1) };
            var (_, plain, _) = Cast(env, new Iota[] { new DoubleIota(3) }, P("hexcasting:write"));
            Check("载体不收：bad_item.iota.readonly「一个能够接受3.00的地方」",
                plain == "书吏之策略：需要在快捷栏中手持物品右边一格放有一个能够接受3.00的地方，而实际放有1个[念珠]", plain);
        }
        {
            var world = new FakeWorld();
            var (_, plain, _) = Cast(new TestEnv(world: world),
                new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 4), new ListIota(new List<Iota>()) }, P("hexcasting:craft/cypher"));
            Check("制作杂件、手上没有空杂件：报物品名「杂件」（以前是「一张空的符纸」）",
                plain == "制作杂件：需要在快捷栏中手持物品右边一格放有杂件，而实际无对应物品", plain);
        }
        {
            var world = new FakeWorld();
            world.ItemStacks[4] = new ItemStackInfo("泥土块", 3);
            var env = new TestEnv(world: world) { HeldRechargeRoom = 100 };
            var (_, plain, _) = Cast(env, new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 4) }, P("hexcasting:recharge"));
            Check("掉落物不含媒质：bad_item「需要含有媒质的物品，而实际持有3个[泥土块]」",
                plain == "重新充能：需要含有媒质的物品，而实际持有3个[泥土块]", plain);
        }

        // ── 实体：名字走显示挂钩，不露出 Npc#3 这类内部编号 ──
        var oldName = IotaDisplay.EntityName;
        try
        {
            IotaDisplay.EntityName = e => e.Target == EntityIota.EntityKind.Player ? $"玩家{e.Index}" : "史莱姆";
            var slime = new EntityIota(EntityIota.EntityKind.Npc, 3);
            string immune = new MishapImmuneEntity(slime).ErrorMessageWithName(new TestEnv(), new MishapContext(null, null))!;
            Check("免疫：immune_entity「无法影响到史莱姆」，名字青色、没有内部编号",
                DisplayTags.Strip(immune) == "无法影响到史莱姆" && immune.Contains("[hext/55FFFF:史莱姆]") && !immune.Contains("Npc"), immune);
            Check("实体太远：entity_too_far「史莱姆超出影响范围」",
                Msg(new MishapEntityTooFarAway(slime)) == "史莱姆超出影响范围", Msg(new MishapEntityTooFarAway(slime)));

            var caster = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld { Caster = caster };
            var env = new TestEnv(world: world) { HasStorage = true };
            var (_, plain, _) = Cast(env, new Iota[] { new EntityIota(EntityIota.EntityKind.Player, 1) }, P("hexcasting:write"));
            Check("写别人的真名：others_name「试图侵犯玩家1的灵魂的隐私」", plain == "书吏之策略：试图侵犯玩家1的灵魂的隐私", plain);
            Check("写自己的真名：others_name.self（是不是自己按环境的施法者判断）",
                Msg(new MishapOthersName(caster), new TestEnv(world: world)) == "试图随意泄露我自己真名的秘密");
        }
        finally
        {
            IotaDisplay.EntityName = oldName;
        }

        // ── 其余固定说法 ──
        Check("固定说法：stack_size / eval_too_much / needs_parens / no_spell_circle / bad_caster / already_brainswept",
            Msg(new MishapStackSize()) == "超出了栈的大小上限"
            && Msg(new MishapEvalTooMuch()) == "运行了过多图案"
            && Msg(new MishapNeedsParens()) == "在绘制反思前未先绘制内省"
            && Msg(new MishapNoSpellCircle()) == "需在法术环上执行"
            && Msg(new MishapBadCaster()) == "试图运行的图案需要强大的意识才能承受"
            && Msg(new MishapAlreadyBrainswept(new EntityIota(EntityIota.EntityKind.Npc, 1))) == "此意识已被使用");
        Check("媒质不够：hexcasting.message.cant_overcast", Msg(new MishapNotEnoughMedia(10)) == "这个咒术需求的媒质量比我有的还多……我应该再算几遍。");
        Check("快捷栏里没有方块：bad_item.hotbar", Msg(new MishapLackingHotbarItem(Wanted.Placeable)) == "需要在快捷栏里放有一个可放置的物品");
        Check("内部异常：unknown「抛出异常（类名: 消息）。这是模组中的漏洞。」",
            Msg(new MishapInternalException(new System.ArgumentException("count is negative: -1")))
                == "抛出异常（System.ArgumentException: count is negative: -1）。这是模组中的漏洞。");
        {
            HexPattern? junk = null;
            foreach (var sig in new[] { "qeqeqe", "eqeqeqe", "qqeqqeqq", "eeqeeqee", "qwqwqwqwqa" })
            {
                if (HexPattern.TryFromAngles(sig, HexDir.East, out var cand, out _) && cand != null
                    && PatternRegistry.Match(cand) == null && !SpecialPatterns.TryNumber(sig, out _)
                    && !SpecialPatterns.TryMask(cand, out _)) { junk = cand; break; }
            }
            var (raw, plain, _) = Cast(new TestEnv(), System.Array.Empty<Iota>(), new PatternIota(junk!));
            Check("无效图案：invalid_pattern「图案（小图）不对应任何操作」，没有名字前缀",
                plain.StartsWith("图案HexPattern[") && plain.EndsWith("]不对应任何操作") && raw!.Contains("[hexp/") && !plain.Contains("识别到"),
                raw);
        }
    }
}
