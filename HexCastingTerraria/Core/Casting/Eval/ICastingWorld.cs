using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.World;

namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 施法环境对「世界」的**只读**访问抽象。
///
/// 为什么要这一层：Core 层**不认识** Terraria 的 Player / NPC / Projectile，
/// 也不该依赖 XNA。但世界图案（`entity_pos/*`、`get_entity_look`、`get_entity_velocity`…）
/// 的逻辑 —— 范围校验、mishap 分支、坐标语义 —— 必须留在 Core 里，
/// 否则离线测试覆盖不到，而这些恰恰是最容易写错的部分。
///
/// 所以：**逻辑在 Core，取值靠本接口**。游戏侧实现见
/// `Content/World/TerrariaCastingWorld.cs`，离线测试用假实现（见 vmtest）。
///
/// ⚠️ 坐标系与单位（**两条都必须遵守，否则会静默出错**）：
///
/// **① 坐标原点**：MC 的 `position()` 是**脚底中心**，而 Terraria 的
/// `Entity.position` 是**左上角**（`Center` 才是中心）。
/// 本接口的 <see cref="FeetPosition"/> / <see cref="EyePosition"/> 一律返回
/// **中心语义**，由游戏侧实现负责换算 —— 换算只写一次，不会各处偏移半个身位。
///
/// **② 单位统一用「图格」**：MC 里 1 格 = 1.0 距离单位，原作所有常量都按格写。
/// 泰拉瑞亚的 1 图格 = 16 像素，所以游戏侧必须把像素**除以**
/// <see cref="HexUnits.PixelsPerTile"/> 再返回。
/// 若直接把像素喂进 VM，`add_motion` 传 1 只会移动 1 像素（肉眼不可见），
/// 而 `entity_pos` 的值会大 16 倍 —— 乘法/除法类图案的结果全部失真。
/// 速度同理：返回**图格/帧**，不是像素/帧。
/// </summary>
public interface ICastingWorld
{
    /// <summary>
    /// 施法者。法术环 / 法术书等「无实体施法」时为 null
    /// —— 源项目里 `env.castingEntity` 同样可为 null，`get_caster` 会因此吐 NullIota。
    /// </summary>
    EntityIota? Caster { get; }

    /// <summary>实体 iota 是否仍指向一个存活实体。索引会因实体死亡而失效，必须校验。</summary>
    bool IsAlive(EntityIota entity);

    /// <summary>
    /// 实体是否在施法范围内。源项目 `assertEntityInRange`。
    /// 范围常量见 <see cref="HexUnits.AmbitRadiusPixels"/>。
    /// </summary>
    bool IsInRange(EntityIota entity);

    /// <summary>脚底中心（对应 MC 的 `entity.position()`）。单位：**图格**。</summary>
    (double X, double Y) FeetPosition(EntityIota entity);

    /// <summary>
    /// 眼睛位置（对应 MC 的 `entity.eyePosition`）。单位：**图格**。
    /// 2D 侧视下「眼高」对应物不明确，实现里取中心即可（见 LOOK_DIRECTION_DESIGN.md）。
    /// </summary>
    (double X, double Y) EyePosition(EntityIota entity);

    /// <summary>
    /// 实体速度，单位：**图格/帧**。
    /// 静止实体返回零向量是**合法**的 —— 调用方负责不要拿它归一化。
    /// </summary>
    (double X, double Y) Velocity(EntityIota entity);

    /// <summary>
    /// 视线方向（单位向量）。
    /// 由 `LookResolver` 保证**永不为 NaN、永不为零向量**（见 `Core/World/LookResolver.cs`）。
    /// </summary>
    (double X, double Y) Look(EntityIota entity);

    /// <summary>
    /// 实体判定箱高度（对应 MC 的 `bbHeight`）。单位：**图格**。
    /// 2D 侧视下仍有意义 —— 它参与 `get_entity_height` 的返回值。
    /// </summary>
    double EntityHeight(EntityIota entity);

    // ── 以下是射线类图案需要的部分 ──────────────────────────────────

    /// <summary>
    /// 某个世界坐标（**图格单位**）是否在施法范围内。
    /// 对应源项目的 `assertVecInRange` / `isVecInRange`。
    /// 注意这是「点」的范围校验，与 <see cref="IsInRange"/>（按实体）不同。
    /// </summary>
    bool IsVecInRange(double x, double y);

    /// <summary>
    /// 某图格是否**实心**（阻挡射线）。
    /// 对应 MC 的 `ClipContext.Block.COLLIDER` —— 只算有碰撞的方块，不算液体/草。
    /// </summary>
    bool IsTileSolid(int tileX, int tileY);

    /// <summary>
    /// 取给定区域（**图格单位**）内所有实体的判定箱，供
    /// <see cref="SegmentSweep"/> 做线段扫掠。
    ///
    /// 之所以由世界侧提供候选集：Core 层不知道有哪些实体、
    /// 也不知道 `Main.npc` 之类的容器；而扫掠本身是纯几何，留在 Core 里可测。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<EntityBox> EntitiesInArea(
        double minX, double minY, double maxX, double maxY);

    // ── 以下是写入类图案需要的部分（会改变世界状态）────────────────

    /// <summary>
    /// 坐标是否位于世界内。对应源项目 `isVecInWorld`。
    /// 用来阻止把实体传送到世界边界之外（那会让它永久掉出地图）。
    /// </summary>
    bool IsVecInWorld(double x, double y);

    /// <summary>
    /// 给实体施加一次推力。单位：**图格/帧**（与 <see cref="Velocity"/> 一致）。
    /// 对应源项目 `Entity.push`。
    /// </summary>
    void ApplyMotion(EntityIota entity, double mx, double my);

    /// <summary>
    /// 把实体瞬移一段位移（相对当前位置）。单位：**图格**。
    ///
    /// 由世界侧负责「不把实体塞进墙里」的处理 ——
    /// Core 层看不到图格碰撞，只能给出位移量。
    /// </summary>
    void TeleportBy(EntityIota entity, double dx, double dy);

    /// <summary>
    /// 大传送的代价：按距离的概率把**施法者自己**的物品震落在地。
    /// 对应源项目 `OpTeleport` 的 `doesGreaterTeleportSplatItems` 分支。
    ///
    /// 只有「施法者本人」会掉东西 —— 传送别的实体不掉。
    /// 是否启用由服务端配置决定（<see cref="HexCastingTerraria.Config.HexServerConfig.GreatTeleportDropsItems"/>）。
    /// </summary>
    /// <param name="entity">被传送的实体。</param>
    /// <param name="distanceTiles">传送距离（图格）。越远掉得越多。</param>
    void ScatterInventory(EntityIota entity, double distanceTiles);

    // ── 以下是阿卡夏记录（以图案为键的存储）──────────────────────

    /// <summary>
    /// 该坐标（图格单位）是否是**可用的阿卡夏记录方块**。
    ///
    /// 与「查不到键」是两回事，必须分开：
    ///   - 不是记录方块 → 报 MishapNoAkashicRecord（玩家指错地方了）
    ///   - 是记录方块但没有该键 → 返回空值（正常情况，记录本来就是慢慢写的）
    /// 合并成一种结果会让玩家分不清「指错了」和「还没写」。
    /// </summary>
    bool IsAkashicRecord(double x, double y);

    /// <summary>按图案查记录。返回 null 表示没有该键。</summary>
    Iota? LookupAkashic(double x, double y, Math.HexPattern key);

    /// <summary>按图案写入记录。位置不是记录方块时静默忽略。</summary>
    void WriteAkashic(double x, double y, Math.HexPattern key, Iota value);

    // ── 以下是区域查询（`zone_entity` 等图案需要）────────────────

    /// <summary>
    /// 查询以 (x, y) 为中心、给定半径内的实体。
    ///
    /// 返回的集合由**世界侧**负责：先按范围与存活过滤，再按距离升序排列。
    /// 排序必须在世界侧做 —— Core 层拿不到距离信息，而源项目要求结果按距离排序。
    /// </summary>
    /// <param name="filter">筛选种类（动物/怪物/物品/玩家/活物）。</param>
    /// <param name="negate">是否取反。</param>
    /// <param name="x">中心（图格）。</param>
    /// <param name="y">中心（图格）。</param>
    /// <param name="radius">半径（图格）。</param>
    System.Collections.Generic.IReadOnlyList<EntityIota> QueryEntities(
        Actions.ZoneEntityFilter filter, bool negate, double x, double y, double radius);

    /// <summary>
    /// 给实体施加药水效果。
    /// 对应源项目 `LivingEntity.addEffect`，效果种类的映射由世界侧负责。
    /// </summary>
    /// <param name="entity">目标。</param>
    /// <param name="effect">效果种类。</param>
    /// <param name="ticks">持续 tick 数。</param>
    /// <param name="potency">效力（1 起）。泰拉的 buff 等级是 1 起，与 MC 的 amplifier+1 一致。</param>
    void ApplyPotion(EntityIota entity, Actions.PotionEffectKind effect, int ticks, int potency);

    /// <summary>
    /// 取该坐标上最近的实体（不筛选种类）。没有则返回 null。
    /// 对应源项目 `get_entity`（判定盒 `pos ± 0.5`）。
    /// </summary>
    EntityIota? QueryNearestEntity(double x, double y);

    /// <summary>
    /// 是否有实体的**眼睛位置**恰好落在该坐标上。
    ///
    /// 专为 `explode` 的「眼位坑」而设：源项目发现爆炸正好落在实体眼位时
    /// **不会造成伤害**，所以那种情况下要把爆炸点微移一下。
    /// </summary>
    bool HasEntityEyeExactlyAt(double x, double y);

    /// <summary>
    /// 制造一次爆炸。对应源项目 `Level.explode`。
    /// </summary>
    /// <param name="x">位置（图格）。</param>
    /// <param name="y">位置（图格）。</param>
    /// <param name="strength">强度（已 clamp 到 0~10）。</param>
    /// <param name="fire">是否留下火焰。</param>
    void Explode(double x, double y, double strength, bool fire);

    /// <summary>
    /// 不能被闪现 / 传送（源项目 tag hexcasting:cannot_teleport → MishapImmuneEntity）。
    /// 泰拉侧：Boss 及其身体部件。默认 false。
    /// </summary>
    bool IsTeleportImmune(EntityIota entity) => false;

    /// <summary>快捷栏里有可放置的方块（源项目 OpPlaceBlock 找不到就 MishapLackingHotbarItem）。默认 true。</summary>
    bool HasPlaceableInHotbar() => true;

    // ── mishap 用到的世界效果（默认不做；泰拉世界里实现）─────────────

    /// <summary>MishapBadBlock：0.25 强度的小爆炸，**不破坏方块**（原版 ExplosionInteraction.NONE）。</summary>
    void MishapExplosion(double x, double y) { }

    /// <summary>MishapBadItem：把那个掉落物往上弹（原版 deltaMovement.y += 0.75）。</summary>
    void MishapLaunchItem(EntityIota item) { }

    /// <summary>MishapBadBrainsweep / MishapAlreadyBrainswept：伤害（kill=true 时直接杀死）那个生物。</summary>
    void MishapHurtEntity(EntityIota entity, bool kill) { }

    // ── 以下是方块操作（conjure/break/place）──────────────────────

    /// <summary>该格是否可以被替换（空气、草、水这类）。对应源项目 `canBeReplaced`。</summary>
    bool IsReplaceable(double x, double y);

    /// <summary>
    /// 凭空造出一块召唤方块 / 光源，并登记存活时间。
    /// 对应源项目 `conjured_block` / `conjured_light`。
    /// </summary>
    void ConjureBlock(double x, double y, bool light);

    /// <summary>
    /// 该格的方块是否「廉价可挖」（草、花、树叶这类）。
    /// 源项目用方块标签区分，廉价方块的挖掘消耗只有 1/12。
    /// </summary>
    bool IsCheapToBreak(double x, double y);

    /// <summary>挖掉该格的方块。返回是否真的挖掉了。</summary>
    bool BreakBlockAt(double x, double y);

    /// <summary>
    /// 该格**现在**能不能被挖掉；不能时把原因写进 <paramref name="reason"/>。
    ///
    /// 为什么必须单独有这一条：<see cref="BreakBlockAt"/> 是在法术**真正施放时**才跑的，
    /// 它返回的 false 没有任何人看 —— 玩家看到的是「施法成功」，
    /// 然后方块纹丝不动，也拿不到任何提示。这正是「破坏魔法没反应」的成因。
    /// 把判定提前到 `break_block` 的 execute 阶段，失败就能变成一条明确的 mishap。
    ///
    /// 给了默认实现只是为了不打断测试里的假世界（它们不关心可挖性）；
    /// 真实世界必须覆写，否则等于把这条反馈又丢了。
    /// </summary>
    bool CanBreakBlockAt(double x, double y, out string reason)
    {
        reason = string.Empty;
        return true;
    }

    // ── 以下是世界效果（天气/火/水/雷电/催熟）────────────────────

    /// <summary>
    /// 改变天气。持续时长在 [minMinutes, maxMinutes] 分钟内**随机**（源项目同）。
    /// </summary>
    void SetRain(bool rain, int minMinutes, int maxMinutes);

    /// <summary>点燃一个实体。</summary>
    void IgniteEntity(EntityIota entity);

    /// <summary>点燃一个位置。泰拉没有火焰方块，实现为「烧这一格附近的实体 + 撒粒子」。</summary>
    void IgniteAt(double x, double y);

    /// <summary>扑灭一片区域的火焰，最多 <paramref name="maxCount"/> 格（泛洪）。</summary>
    void ExtinguishAt(double x, double y, int maxCount);

    /// <summary>造出一格水。</summary>
    void CreateWaterAt(double x, double y);

    /// <summary>抽干一片水域，最多 <paramref name="maxCount"/> 格（泛洪）。</summary>
    void DestroyWaterAt(double x, double y, int maxCount);

    /// <summary>召下一道闪电。</summary>
    void SpawnLightning(double x, double y);

    /// <summary>催熟该位置附近的植物。</summary>
    void ApplyBonemeal(double x, double y);

    /// <summary>[0, 1) 的随机数。对应源项目 `env.world.random.nextDouble()`。</summary>
    double NextDouble();

    // ── 以下是比较类图案（compare_*）────────────────────────────

    /// <summary>两个实体是不是**同类**（不是「同一个」）。对应源项目 `entityA.type == entityB.type`。</summary>
    bool IsSameEntityType(EntityIota a, EntityIota b);

    /// <summary>
    /// 两个位置的方块是不是同一种。
    /// <paramref name="exact"/> 为真时比**完整状态**（含朝向/帧/油漆），否则只比方块种类。
    /// </summary>
    bool CompareBlocks(double x1, double y1, double x2, double y2, bool exact);

    /// <summary>
    /// 两个物品实体是不是同一种物品。
    /// <paramref name="exact"/> 为真时比**完整物品**（含前缀等），否则只比物品 ID。
    /// </summary>
    bool CompareItems(EntityIota a, EntityIota b, bool exact);

    // ── 以下是实体身上的数据载体（read/entity 与 write/entity）────

    /// <summary>
    /// 这个实体身上是否有一个 iota 载体。
    /// 对应源项目 `IXplatAbstractions.findDataHolder(entity) != null`。
    ///
    /// 泰拉侧的对应物是**掉在地上、本身就是载体的物品**（聚念核心、念珠、卷轴）。
    /// 源项目那边还能读物品展示框、盔甲架之类，泰拉没有等价实体。
    /// </summary>
    bool IsEntityIotaHolder(EntityIota entity);

    /// <summary>该实体身上的载体是否可写。对应源项目 `dataHolder.writeable()`。</summary>
    bool IsEntityIotaWritable(EntityIota entity);

    /// <summary>读出实体身上载体里的 iota。null = 载体是空的（或根本不是载体）。</summary>
    Iota? ReadEntityIota(EntityIota entity);

    /// <summary>写入实体身上的载体。返回是否写成功。</summary>
    bool WriteEntityIota(EntityIota entity, Iota value);

    // ── 以下是单点法术的落地（beep / create_lava / edify / place_block / recharge）──

    /// <summary>
    /// 在某处播放一个音符。移植自源项目 `OpBeep`（那边发 `MsgBeepS2C` 给 128 格内的玩家）。
    /// <paramref name="instrument"/> 是乐器编号，<paramref name="note"/> 是 0~24 的音高。
    /// </summary>
    void Beep(double x, double y, int instrument, int note);

    /// <summary>造出一格岩浆。移植自源项目 `OpCreateFluid` 的岩浆实例。</summary>
    void CreateLavaAt(double x, double y);

    /// <summary>该格是不是树苗（`edify` 的前置条件）。对应源项目 `BlockTags.SAPLINGS`。</summary>
    bool IsSaplingAt(double x, double y);

    /// <summary>把该格的树苗催成一棵树。移植自源项目 `OpEdifySapling`。返回是否真的长出来了。</summary>
    bool GrowTreeAt(double x, double y);

    /// <summary>
    /// 从施法者背包里拿一件**可放置**的物品放到该格。
    /// 移植自源项目 `OpPlaceBlock`。返回是否放上了（放不上就是被什么拦住了）。
    /// </summary>
    bool PlaceBlockAt(double x, double y);

    /// <summary>
    /// 地上这个掉落物一共能出多少媒质（源项目 withdrawMedia(-1, simulate=true)）。不是媒质物品 → 0。
    /// <paramref name="forBattery"/>：只算「能用来造媒质瓶 / 打包法术」的媒质（源项目 drainForBatteries：
    /// 粉、碎片、充能紫水晶这类可以，媒质瓶本身不行）。
    /// </summary>
    long ItemEntityMedia(EntityIota itemEntity, bool forBattery);

    /// <summary>
    /// 从地上这个掉落物抽媒质（源项目 extractMedia(stack, cost, drainForBatteries)）：
    /// <paramref name="cost"/> &lt; 0 = 全部抽干；堆叠物品按整件扣（可能多于 cost），媒质瓶按量扣。
    /// 抽空了掉落物就消失。返回实际抽出的量。
    /// </summary>
    long DrainItemEntity(EntityIota itemEntity, long cost, bool forBattery);

    // ── 以下是咒法飞行（flight 系列）──────────────────────────────

    /// <summary>向上弹一下（`flight` 的第一步）。源项目是 `push(0, 1.5, 0)`。</summary>
    void LaunchUp(EntityIota target);

    /// <summary>
    /// 授予咒法飞行。
    ///
    /// <paramref name="ticks"/> &lt; 0 = 不限时；<paramref name="radius"/> &lt; 0 = 不限距；
    /// <paramref name="graceTicks"/> &gt; 0 表示「刚起飞这几 tick 不算落地」（Altiora 用）。
    ///
    /// 只有玩家能飞 —— 对其它实体是空操作（源项目的 `getPlayer` 就是这么限定的）。
    /// </summary>
    void GrantFlight(EntityIota target, int ticks, double originX, double originY, double radius, int graceTicks);

    /// <summary>该实体当前有没有咒法飞行（含 Altiora 与限时/限距两种）。`flight/can_fly` 用。</summary>
    bool HasHexFlight(EntityIota target);

    // ── 以下是脑叶切除（brainsweep）────────────────────────────────

    /// <summary>该位置能不能被改动（世界内 + 不在受保护区域）。对应源项目 `canEditBlockAt`。</summary>
    bool CanEditAt(double x, double y);

    /// <summary>该位置的方块类型；-1 表示空气。对应源项目取 `BlockState` 的用途。</summary>
    int TileTypeAt(double x, double y);

    /// <summary>
    /// 实体的「种类编号」，用于配方匹配。
    /// 泰拉侧：NPC 用 `netID`，城镇 NPC 用负数码（见 <see cref="Actions.BrainsweepRules.TownNpcSpecies"/>）。
    /// </summary>
    int EntitySpeciesOf(EntityIota entity);

    /// <summary>这个实体能不能被脑叶切除（源项目用 `NO_BRAINSWEEPING` 标签排除）。</summary>
    bool IsBrainsweepable(EntityIota entity);

    /// <summary>这个实体是不是已经被切除过了。对应源项目 `isBrainswept`。</summary>
    bool IsBrainswept(EntityIota entity);

    /// <summary>执行脑叶切除：换掉方块、处理掉生物、掉落产物。</summary>
    void Brainsweep(double x, double y, EntityIota target, Actions.BrainsweepRecipe recipe);
}
