using HexCastingTerraria.Core;
using Microsoft.Xna.Framework;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using HexCastingTerraria.Client;
using HexCastingTerraria.Config;
namespace HexCastingTerraria.Content;

/// <summary>
/// 玩家侧咒术数据。
///
/// 媒质**不存在玩家身上**：与原版一致，全部来自背包里的媒质物品（媒质瓶、紫水晶粉……），
/// 见 <see cref="CollectMediaSources"/>。这里曾经有一个 12 晶体上限的「玩家媒质池」（决策点 D3 未定时的临时方案），
/// 已按原版删除；旧存档里池子的媒质会在进世界时折成一个媒质瓶发还（见 <see cref="OnEnterWorld"/>）。
/// </summary>
public sealed class HexPlayer : ModPlayer
{
    /// <summary>旧存档里「玩家媒质池」的余额，进世界时折成媒质瓶发还。</summary>
    private long _legacyPoolMedia;

    /// <summary>
    /// 背包里所有媒质来源（源项目 scanPlayerForMediaStuff）：媒质瓶按量、媒质材料按整件。
    /// 0..57 = 背包 + 钱币 + 弹药（58 是鼠标上拿着的，不算）。
    /// </summary>
    public System.Collections.Generic.List<MediaSource> CollectMediaSources()
    {
        var list = new System.Collections.Generic.List<MediaSource>();
        for (int i = 0; i < 58 && i < Player.inventory.Length; i++)
        {
            var item = Player.inventory[i];
            if (item is null || item.IsAir || item.stack <= 0) continue;
            if (item.ModItem is Items.MediaFlask flask)
            {
                list.Add(new MediaSource { Slot = i, Priority = MediaPriority.Battery, Stored = flask.Media });
            }
            else if (item.ModItem is Items.MediaMaterial material)
            {
                list.Add(new MediaSource { Slot = i, Priority = material.Priority, UnitValue = material.MediaValue, Count = item.stack });
            }
        }
        return list;
    }

    /// <summary>背包里的媒质总量（HUD 显示用）。</summary>
    public long InventoryMedia()
    {
        long total = 0;
        foreach (var s in CollectMediaSources()) total += s.Total;
        return total;
    }

    /// <summary>背包里所有媒质瓶的（存量, 上限）之和（HUD 的环用）。</summary>
    public (long Stored, long Max) FlaskMedia()
    {
        long stored = 0, max = 0;
        for (int i = 0; i < 58 && i < Player.inventory.Length; i++)
        {
            if (Player.inventory[i]?.ModItem is Items.MediaFlask f) { stored += f.Media; max += f.MaxMedia; }
        }
        return (stored, max);
    }

    /// <summary>
    /// 是否已「启蒙」。
    ///
    /// 重要纠正（来自 CONTENT_INVENTORY.md 第 8 节）：原作**没有**线性研究解锁系统，
    /// 图案可用性靠动作标签（requires_enlightenment 等）判定，而「启蒙」本身是一个成就。
    /// 所以这里不是「科技树进度」，只是一个布尔门槛。
    /// </summary>
    public bool Enlightened { get; set; }

    /// <summary>
    /// 是否失败过一次大法术（原版进度「盲目绘制」y_u_no_cast_angy）。
    /// 原版 PlayerBasedCastEnv.canOvercast 以它为前提：**没失败过大法术就不能过载**。
    /// </summary>
    public bool FailedGreatSpell { get; set; }

    /// <summary>
    /// 拿到过紫水晶（原版进度 root —— 书里大部分条目以它解锁）。
    /// 原版标签 grants_root_advancement = 紫水晶粉 / 紫水晶碎片 / 充能紫水晶；泰拉侧碎片 = 紫晶宝石或模组碎片。
    /// </summary>
    public bool ObtainedAmethyst { get; set; }

    /// <summary>过载后活了下来（原版进度 opened_eyes「睁开双眼」）。</summary>
    public bool Overcasted { get; set; }

    /// <summary>读过的传说篇章（原版 lore/* 进度；读「故事残卷」随机获得一篇）。</summary>
    public System.Collections.Generic.HashSet<string> FoundLore { get; } = new();

    /// <summary>最近一次过载消耗的生命值，供 HUD 显示。</summary>
    public long LastOvercastHealthCost { get; private set; }

    /// <summary>
    /// 记录一次过载扣血。
    ///
    /// 供 <see cref="PlayerCastingEnvironment"/> 的媒质支付流程调用 ——
    /// 那条路径自己完成了「池 → 背包物品 → 过载」三段式扣款，
    /// 不再走 <see cref="TrySpendOrOvercast"/>，所以需要在这里登记过载量。
    /// </summary>
    public void NoteOvercast(long healthCost)
    {
        if (healthCost > 0)
        {
            LastOvercastHealthCost = healthCost;
        }
    }

    /// <summary>
    /// 创造模式 / 无限媒质（开发者模式）。
    ///
    /// 对齐源项目：StaffCastEnv 在创造模式下 extractMedia 直接返回 0（不扣任何媒质）。
    /// 打开后所有施法不消耗媒质，也不会触发过载掉血，便于无成本测试法术。
    /// </summary>
    public bool InfiniteMedia { get; set; }

    /// <summary>
    /// 授予「启蒙」（原版进度 enlightenment「获得启迪」）。条件的判定在 <see cref="Core.Media.Overcast.IsEnlightening"/>。
    /// </summary>
    public void GrantEnlightenment()
    {
        if (Enlightened) { return; }
        Enlightened = true;
        if (Player.whoAmI == Main.myPlayer)
        {
            Main.NewText("获得启迪 —— 施放咒术至生命将尽，击碎了屏障。", HexColors.Overcast);
        }
    }

    /// <summary>阿卡夏记录（Ravenmind）：玩家持久化的 iota 存储。Phase 1 接 VM 时使用。</summary>
    public long RavenmindCount { get; set; }

    // ── 哨卫（sentinel/* 图案）─────────────────────────────────────

    /// <summary>
    /// 哨卫：一个**持久**的坐标书签。没放时为 null。
    ///
    /// 为什么要存在玩家身上而不是世界里：源项目就挂在玩家实体上
    /// （`IXplatAbstractions.getSentinel(ServerPlayer)`），
    /// 语义是「我自己的标记」，别人看不见、也不该被别人的哨卫影响。
    ///
    /// 单位是**图格**（Core 的约定），存/取都在这里换算一次，
    /// 避免「存档里是像素、代码里当图格用」这类只有在重启后才暴露的错位。
    /// </summary>
    public Core.Casting.Eval.CastingEnvironment.SentinelState? Sentinel { get; set; }

    // ── 打包法术（craft/* 的产物）────────────────────────────────

    /// <summary>
    /// 打包法术的冷却（tick）。源项目用 MC 的物品冷却，
    /// 泰拉侧没有等价物，所以自己在玩家身上记一个。
    /// </summary>
    public int PackagedCooldown { get; private set; }

    /// <summary>
    /// 法术配色（`colorize` 设置的染料物品类型；0 = 默认色）。
    ///
    /// 它是**纯表现**，但必须进存档：源项目把颜料存在玩家数据里，
    /// 重登回来颜色还在 —— 不然每次上线都要重新染一遍。
    /// </summary>
    public int PigmentDyeType { get; set; }

    public void StartPackagedCooldown(int ticks)
    {
        // 调试开关：打包法术无冷却（连续右键测同一发法术）
        if (HexClientConfig.Instance.NoPackagedCooldown) return;

        if (ticks > PackagedCooldown)
        {
            PackagedCooldown = ticks;
        }
    }

    // ── 咒法飞行（flight 系列）─────────────────────────────────────

    /// <summary>飞行是否生效。</summary>
    public bool FlightActive { get; private set; }

    /// <summary>剩余 tick；< 0 表示不限时。</summary>
    public int FlightTicksLeft { get; private set; } = -1;

    /// <summary>限距飞行的圆心（图格）；半径 < 0 表示不限距。</summary>
    public double FlightOriginX { get; private set; }

    public double FlightOriginY { get; private set; }

    public double FlightRadius { get; private set; } = -1;

    /// <summary>刚起飞这几 tick 不判定「已落地」（Altiora 用）。</summary>
    public int FlightGrace { get; private set; }

    /// <summary>授予飞行。对应源项目 `IXplatAbstractions.setFlight` + `abilities.mayfly = true`。</summary>
    public void GrantFlight(int ticks, double originX, double originY, double radius, int graceTicks)
    {
        bool wasActive = FlightActive;

        FlightActive = true;
        FlightTicksLeft = ticks;
        FlightOriginX = originX;
        FlightOriginY = originY;
        FlightRadius = radius;
        FlightGrace = graceTicks;

        // 起飞音（原版 flight.ambience 是循环音，泰拉这边没有等价物，改成起飞一下）
        if (!wasActive && Player.whoAmI == Main.myPlayer)
        {
            Content.SpellSounds.Play("flight.start");
        }
    }

    /// <summary>结束飞行，并**收回**翅膀（否则玩家会一直飘着）。</summary>
    public void EndFlight()
    {
        if (!FlightActive) return;

        FlightActive = false;
        FlightTicksLeft = -1;
        FlightRadius = -1;
        FlightGrace = 0;

        // 落地音（原版 flight.finish）
        if (Player.whoAmI == Main.myPlayer)
        {
            Content.SpellSounds.Play("flight.finish");
        }

        // 只收回我们自己加的那一份：玩家本来装备着翅膀时 wingsLogic 是装备给的，
        // 直接清零会把他的翅膀一起废掉。
        if (!Player.dead && Player.wingsLogic == HexFlightWingsLogic)
        {
            Player.wingsLogic = 0;
            Player.wingTime = 0f;
        }
    }

    /// <summary>
    /// 我们借用的「翅膀行为编号」。用 1（最基础的翼型，无悬停、无加速）。
    ///
    /// 为什么用翅膀而不是别的方式给飞行：泰拉**没有** MC 的 `abilities.mayfly`
    /// （1.4.5 的 `Player` 里没有 `creativeFly` 这类字段）。
    /// 模组给临时飞行的标准做法就是「占一个 wingsLogic + 灌满 wingTime」，
    /// 游戏自己的飞行物理随后接手 —— 这样手感和真翅膀一致，不需要我们自己写升力。
    /// </summary>
    private const int HexFlightWingsLogic = 1;

    /// <summary>
    /// 每 tick 的飞行维护。**必须放在 PostUpdateMiscEffects**：
    ///
    /// `ResetEffects` 每 tick 会把 `wingsLogic` 重置回「装备给的翅膀」，
    /// 所以我们的写入必须在它之后、且在移动逻辑读取它之前 —— 只有这个钩子同时满足。
    /// </summary>
    public override void PostUpdateMiscEffects()
    {
        if (!FlightActive)
        {
            return;
        }

        // 调试开关：飞行永不结束。
        // 放在所有结束判定之前 —— 测 `flight/range`、`flight/time`、Altiora
        // 这三种飞行的**效果**时，最烦的就是刚起飞就落地了。
        if (HexClientConfig.Instance.InfiniteFlight)
        {
            FlightGrace = 0;
            Player.wingsLogic = HexFlightWingsLogic;
            Player.wingTimeMax = 60 * 60;
            Player.wingTime = 60 * 60;
            return;
        }

        if (Player.dead)
        {
            EndFlight();
            return;
        }

        // Altiora：宽限期过后，一落地/撞墙就结束（源项目 checkPlayerCollision）
        if (FlightGrace > 0)
        {
            FlightGrace--;
        }
        else if (FlightTicksLeft < 0 && FlightRadius < 0 && (Player.velocity.Y == 0f || Player.position == Player.oldPosition))
        {
            // 「不限时不限距」= Altiora：落地即结束
            EndFlight();
            return;
        }

        // 危险度：限距/限时的结束条件，与源项目同一套曲线
        var (cx, cy) = (Player.Center.X / Core.Casting.HexUnits.PixelsPerTile,
                        Player.Center.Y / Core.Casting.HexUnits.PixelsPerTile);
        double dist = FlightRadius >= 0
            ? System.Math.Sqrt((cx - FlightOriginX) * (cx - FlightOriginX) + (cy - FlightOriginY) * (cy - FlightOriginY))
            : 0.0;

        double danger = Core.Casting.Actions.FlightDanger.Compute(dist, FlightRadius, FlightTicksLeft);
        if (danger >= 1.0)
        {
            EndFlight();
            return;
        }

        if (FlightTicksLeft >= 0)
        {
            FlightTicksLeft--;
        }

        // 灌满飞行时间，让游戏自己的翅膀物理接手
        int budget = FlightTicksLeft < 0 ? 60 * 60 : FlightTicksLeft + 1;
        Player.wingsLogic = HexFlightWingsLogic;
        Player.wingTimeMax = budget;
        if (Player.wingTime < 1f)
        {
            Player.wingTime = budget;
        }

        // 预警粒子：越接近结束越偏红。只给本机玩家画，避免联机时每个客户端都重复生成
        if (!Main.dedServ && Player.whoAmI == Main.myPlayer)
        {
            SpawnFlightDust(cx, cy, danger);
        }
    }

    /// <summary>
    /// 飞行尾迹。对应源项目 `tickDownFlight` 里那三股粒子
    /// （正常色 + 黑 + 红，后两者的数量随危险度上升）。
    /// </summary>
    private void SpawnFlightDust(double cx, double cy, double danger)
    {
        var center = new Microsoft.Xna.Framework.Vector2(
            (float)(cx * Core.Casting.HexUnits.PixelsPerTile),
            (float)(cy * Core.Casting.HexUnits.PixelsPerTile + Player.height * 0.5f));

        var normal = HexPigment.Current;
        int dangerCount = (int)System.Math.Round(5 * danger);
        int okCount = 5 - dangerCount;

        for (int i = 0; i < okCount; i++)
        {
            var d = Dust.NewDustPerfect(center, Terraria.ID.DustID.PurpleTorch,
                new Microsoft.Xna.Framework.Vector2(Main.rand.NextFloat(-0.6f, 0.6f), Main.rand.NextFloat(0.2f, 0.8f)));
            d.noGravity = true;
            d.color = normal;
            d.scale = 0.9f;
        }

        for (int i = 0; i < dangerCount; i++)
        {
            var d = Dust.NewDustPerfect(center, Terraria.ID.DustID.PurpleTorch,
                new Microsoft.Xna.Framework.Vector2(Main.rand.NextFloat(-0.3f, 0.3f), Main.rand.NextFloat(-1.2f, -0.4f)));
            d.noGravity = true;
            d.color = i % 2 == 0
                ? new Microsoft.Xna.Framework.Color(40, 40, 40)
                : new Microsoft.Xna.Framework.Color(200, 40, 40);
            d.scale = 1.1f;
        }
    }

    /// <summary>取玩家身上的 HexPlayer 实例。</summary>
    public static HexPlayer Get(Player player) => player.GetModPlayer<HexPlayer>();

    public override void SaveData(TagCompound tag)
    {
        tag["enlightened"] = Enlightened;
        tag["failedGreatSpell"] = FailedGreatSpell;
        tag["obtainedAmethyst"] = ObtainedAmethyst;
        tag["overcasted"] = Overcasted;
        tag["foundLore"] = new System.Collections.Generic.List<string>(FoundLore);
        tag["ravenmindCount"] = RavenmindCount;
        tag["infiniteMedia"] = InfiniteMedia;
        tag["pigmentDye"] = PigmentDyeType;

        if (Sentinel is { } s)
        {
            tag["sentinelX"] = s.X;
            tag["sentinelY"] = s.Y;
            tag["sentinelGreat"] = s.Great;
        }
    }

    public override void LoadData(TagCompound tag)
    {
        // 旧存档：玩家媒质池的余额（进世界时折成媒质瓶发还）
        _legacyPoolMedia = tag.TryGet("media", out long media) ? System.Math.Max(0, media) : 0;
        if (tag.TryGet("enlightened", out bool enlightened))
        {
            Enlightened = enlightened;
        }
        FailedGreatSpell = tag.GetBool("failedGreatSpell");
        ObtainedAmethyst = tag.GetBool("obtainedAmethyst");
        Overcasted = tag.GetBool("overcasted");
        FoundLore.Clear();
        foreach (var lore in tag.GetList<string>("foundLore")) { FoundLore.Add(lore); }
        if (tag.TryGet("ravenmindCount", out long count))
        {
            RavenmindCount = count;
        }
        if (tag.TryGet("infiniteMedia", out bool infinite))
        {
            InfiniteMedia = infinite;
        }
        if (tag.TryGet("pigmentDye", out int pigment))
        {
            PigmentDyeType = pigment;
        }

        if (tag.TryGet("sentinelX", out double sx) && tag.TryGet("sentinelY", out double sy))
        {
            tag.TryGet("sentinelGreat", out bool great);
            Sentinel = new Core.Casting.Eval.CastingEnvironment.SentinelState(sx, sy, great);
        }
        else
        {
            Sentinel = null;
        }
    }

    /// <summary>
    /// 进入世界时自动发一把开发者法杖（仅调试期）。
    ///
    /// 理由：调试阶段不该被「先刷 20 木头 5 紫晶再找工作台」卡住。
    /// 正式发布前应改为依赖合成配方或研究解锁。
    /// </summary>
    public override void OnEnterWorld()
    {
        // 服务端：清掉这个 whoAmI 的施法状态。
        // 【不清会出无声的 bug】泰拉用 whoAmI 索引标识玩家，索引会被下一个进服的人复用 ——
        // 新玩家会继承上一个玩家的栈，画第一条图案时接在别人的数据上，而且完全不报错。
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Net.ServerCastState.Clear(Player.whoAmI);
        }
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        // 旧存档里「玩家媒质池」的余额 → 一个同样多的媒质瓶（池子已按原版删除）
        if (_legacyPoolMedia > 0)
        {
            var flask = new Item(ModContent.ItemType<Items.MediaFlask>());
            (flask.ModItem as Items.MediaFlask)!.SetMedia(_legacyPoolMedia, _legacyPoolMedia);
            Player.QuickSpawnItem(Player.GetSource_Misc("HexLegacyMedia"), flask);
            Main.NewText($"咒法学：媒质不再存在玩家身上（与原版一致），原来的 {MediaConstants.Format(_legacyPoolMedia)} 媒质已装进一个媒质瓶还给你。", HexColors.Media);
            _legacyPoolMedia = 0;
        }

        // 咒法学之书：**开局必带**（用户要求）。
        // 它是唯一教人「图案怎么画」的东西，没有它新玩家会完全无从下手。
        // 丢了可以用配方补（10 木板 + 10 稻草）。
        if (!Player.HasItem(ModContent.ItemType<Items.HexBookItem>()))
        {
            Player.QuickSpawnItem(Player.GetSource_Misc("HexBook"),
                ModContent.ItemType<Items.HexBookItem>(), 1);
        }

        // 开发者测试包（配置开关；默认关）
        if (HexClientConfig.Instance.GiveDevKitOnEnter)
        {
            Items.DevKit.Give(Player);
            Main.NewText("已发放开发者测试包（可在 设置 → 模组配置 里关闭）", HexColors.Media);
        }

        var staff = new Item(ModContent.ItemType<Items.DevStaff>());

        // 已经有了就不再发，避免反复进出世界刷一堆
        if (Player.HasItem(staff.type))
        {
            return;
        }

        // 注意：1.4.5 起 QuickSpawnItem 的签名变了（官方迁移指南标注 "changed since 1.4.4"）。
        // 可用的重载是 (IEntitySource, Item) / (IEntitySource, Item, GetItemSettings)，
        // 以及 (IEntitySource, int type, int stack)。
        Player.QuickSpawnItem(Player.GetSource_Misc("HexCastingDevStaff"), staff);
    }


    /// <summary>
    /// 是否戴着探术透镜（饰品）。
    ///
    /// 每帧由 `ScryingLens.UpdateAccessory` 置位、`ResetEffects` 清空 ——
    /// 与泰拉所有饰品的惯例一致。
    /// 纯表现，不做联机同步：透镜只影响**戴的人**看到什么。
    /// </summary>
    public bool ScryingLensEquipped { get; set; }

    public override void ResetEffects()
    {
        // 饰品标记每帧重置（泰拉饰品的标准写法）
        ScryingLensEquipped = false;

        // 媒质不在 ResetEffects 里清空——它是持久资源，不是每帧状态。

        // 打包法术的冷却逐 tick 递减（ResetEffects 每 tick 跑一次）
        if (PackagedCooldown > 0)
        {
            PackagedCooldown--;
        }
    }

    /// <summary>
    /// 画布打开时压制物品栏与玩家输入。
    ///
    /// 踩过的两个坑：
    ///  1. 只在 PostUpdateInput 里设 Main.playerInventory = false 是无效的 ——
    ///     泰拉的 Esc 处理在输入阶段把它重新切成 true，覆盖掉我们的值。
    ///  2. Main.blockInput 只屏蔽**鼠标**，不屏蔽键盘，所以拦不住 Esc。
    ///
    /// 因此必须在「本帧最后一次」玩家回调里强制关闭。ModPlayer.PostUpdate 正合适：
    /// 它在 HandleInput 与 Update 之后执行，之后本帧不会再有人改 Main.playerInventory。
    /// </summary>
    public override void PostUpdate()
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        ResolveLook();

        if (!ObtainedAmethyst
            && (Player.HasItem(Terraria.ID.ItemID.Amethyst)
                || Player.HasItem(ModContent.ItemType<Items.AmethystDust>())
                || Player.HasItem(ModContent.ItemType<Items.AmethystShard>())
                || Player.HasItem(ModContent.ItemType<Items.ChargedAmethyst>())))
        {
            ObtainedAmethyst = true;
        }

        bool canvasOpen = Client.HexCanvasState.Canvas.IsOpen;
        bool bookOpen = Client.HexCanvasState.Book.IsOpen;

        if (canvasOpen || bookOpen)
        {
            Main.playerInventory = false;
            Main.blockInput = true;
            Client.HexCanvasState.BlockedInput = true;

            // 清除悬停提示：画布/书里鼠标不应显示生物/世界物品/墓碑等信息。
            // 这些值在 Player.Update（本回调之前）被 vanilla 设置，此处清空后，
            // 本帧的绘制阶段就不会再显示提示气泡/物品 tooltip。
            Main.hoverItemName = "";
            Main.mouseText = false;
            Main.signHover = -1;
            Main.HoverItem.TurnToAir();
        }
        else if (Client.HexCanvasState.BlockedInput)
        {
            // ⚠️ 兜底：两个界面都没开，就**绝不允许**还处于输入压制状态。
            //
            // 这段是「打开书后无法移动」那个 bug 的根治：之前解除压制只写在一条分支里，
            // 只要有任何一条路径忘了清（菜单、死亡、传送、界面被别的东西关掉…），
            // 玩家就会永久卡住，而且不报错、日志干净。
            // 现在把「谁开的界面谁负责清」换成「最后一道回调统一保证一致」。
            Main.blockInput = false;
            Client.HexCanvasState.BlockedInput = false;
        }
    }

    /// <summary>
    /// 玩家当前视线方向（单位向量）。
    ///
    /// **画布关闭时** = 实时鼠标方向（泰拉玩家的「瞄准」就是鼠标）。
    /// **画布打开时** = <see cref="FrozenAim"/>，即**打开画布那一刻**的方向。
    ///
    /// 为什么必须冻结：画布开着的时候鼠标在**画图案**，
    /// 它不可能同时表示「法术朝哪打」。原版也是这个语义 ——
    /// MC 的 `GuiSpellcasting : Screen`，打开 Screen 期间玩家朝向被冻结，
    /// 所以 `get_entity_look` 拿到的是「你掏出法杖那一刻面朝的方向」。
    /// 见 LOOK_DIRECTION_DESIGN.md 第八节。
    /// </summary>
    public Vector2 Look { get; private set; } = new(1f, 0f);

    /// <summary>
    /// 画布打开瞬间冻结的瞄准方向（单位向量）。
    /// 为 null 表示当时没取到有效方向（例如鼠标正压在角色身上）。
    /// </summary>
    public Vector2? FrozenAim { get; private set; }

    /// <summary>上一帧画布是否开着，用于检测「刚打开」这个跳变。</summary>
    private bool _canvasWasOpen;

    /// <summary>
    /// 把当前鼠标方向冻结为本次施法的瞄准方向。
    ///
    /// 取不到有效方向时置 null 而不是退化成 (1,0)：
    /// 那会让「鼠标压在角色身上」变成「朝右打」，是**静默的**错误朝向。
    /// 置 null 后 <see cref="ResolveLook"/> 会走后续层（速度/上次朝向），行为可预期。
    /// </summary>
    public void FreezeAimFromMouse()
    {
        var delta = Main.MouseWorld - Player.Center;

        // 像素平方阈值：鼠标几乎压在角色中心时方向没有意义
        if (delta.LengthSquared() < 4f)
        {
            FrozenAim = null;
            return;
        }

        var n = Vector2.Normalize(delta);
        FrozenAim = new Vector2(
            Core.Casting.HexMathUtil.FixNaN(n.X),
            Core.Casting.HexMathUtil.FixNaN(n.Y));
    }

    /// <summary>清除冻结的瞄准（画布关闭时调用，恢复实时鼠标）。</summary>
    public void ClearFrozenAim() => FrozenAim = null;

    /// <summary>
    /// 每帧解析一次视线。优先级见 <see cref="Core.World.LookResolver"/>。
    ///
    /// 画布开/关两种模式：
    ///   - 关：第 1 层用**实时鼠标**
    ///   - 开：第 1 层用**打开画布那一刻冻结的方向**
    /// </summary>
    private void ResolveLook()
    {
        bool canvasOpen = Client.HexCanvasState.Canvas.IsOpen;

        // 检测「画布刚打开」的跳变并冻结瞄准。
        // 放在这里而不是让 OpenCanvas 去调，是为了**不漏**：
        // 任何打开画布的路径都会经过本方法，不会出现「忘了冻结」这种失误。
        if (canvasOpen && !_canvasWasOpen)
        {
            FreezeAimFromMouse();
        }
        else if (!canvasOpen && _canvasWasOpen)
        {
            ClearFrozenAim();
        }

        _canvasWasOpen = canvasOpen;

        bool hasAim = !Main.gameMenu && !Main.dedServ;
        Vector2 aimPoint = Main.MouseWorld;

        if (canvasOpen)
        {
            if (FrozenAim is { } frozen)
            {
                // 构造一个沿冻结方向的瞄点，让第 1 层算出该方向。
                // 距离取一个图格即可，反正会被归一化。
                aimPoint = Player.Center + frozen * Core.Casting.HexUnits.PixelsPerTile;
            }
            else
            {
                // 没冻结到方向 -> 放弃第 1 层，交给速度/缓存层
                hasAim = false;
            }
        }

        var (x, y) = Core.World.LookResolver.Resolve(new Core.World.LookInput
        {
            HasAim = hasAim,
            AimX = aimPoint.X,
            AimY = aimPoint.Y,
            SelfX = Player.Center.X,
            SelfY = Player.Center.Y,
            VelX = Player.velocity.X,
            VelY = Player.velocity.Y,
            HasTarget = false,
            PrevX = Look.X,
            PrevY = Look.Y,
            Direction = Player.direction,
        });

        Look = new Vector2(x, y);
    }

    /// <summary>
    /// 画布打开时禁止使用任何物品。
    ///
    /// 必要性：Main.blockInput 只屏蔽鼠标输入，挡不住物品使用逻辑，
    /// 结果画布开着时右键会同时触发法杖的 UseItem（把画布关掉）与其它物品。
    /// 这里直接否决使用，画布内右键就只剩「撤销」一个语义。
    /// </summary>
    public override bool CanUseItem(Item item)
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return true;
        }

        // 画布或书开着时不使用任何物品：Main.blockInput 只屏蔽鼠标输入，
        // 挡不住物品使用逻辑，不拦的话点书页会顺手把手上的东西用掉/扔出去。
        if (Client.HexCanvasState.Canvas.IsOpen || Client.HexCanvasState.Book.IsOpen)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 玩家死亡时关闭画布并解除输入阻塞。
    ///
    /// 否则会出现「死了还在画」：画布是纯客户端 UI，玩家倒下后依然开着，
    /// 而且 Main.blockInput 被我们置为 true，复活后会出现无法操作的情况。
    /// </summary>
    public override void UpdateDead()
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        ForceCloseCanvas("死亡");
    }


    /// <summary>复活时再兜一次，确保输入被恢复。</summary>
    public override void OnRespawn()
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        ForceCloseCanvas("复活");
    }

    /// <summary>强制关闭画布与书，并恢复输入状态。</summary>
    private static void ForceCloseCanvas(string reason)
    {
        var state = Client.HexCanvasState.Canvas;
        var book = Client.HexCanvasState.Book;

        // 书也要关 —— 死着还能翻书，而且 blockInput 会一直是 true
        book.Close();

        if (!state.IsOpen && !Client.HexCanvasState.BlockedInput)
        {
            return;
        }

        state.Close();
        Main.blockInput = false;
        Main.playerInventory = false;
        Client.HexCanvasState.BlockedInput = false;
        Client.HexCanvasState.SetMessage(null);
    }

    /// <summary>
    /// 切换无限媒质（会话级开关，与设置里的配置项是「或」关系）。
    /// 配置项是持久设置，这个是临时快捷键，两者任一为真即生效。
    /// </summary>
    public void ToggleInfiniteMedia()
    {
        InfiniteMedia = !InfiniteMedia;

        if (Player.whoAmI == Main.myPlayer)
        {
            bool effective = InfiniteMedia || HexClientConfig.Instance.InfiniteMedia;
            Main.NewText(
                effective ? "无限媒质：开（施法不消耗）" : "无限媒质：关",
                HexColors.Overcast);
        }
    }
}
