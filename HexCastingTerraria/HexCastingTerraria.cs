using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework.Input;
using Terraria.ID;
using Terraria;
using Terraria.ModLoader;
using HexCastingTerraria.Config;

namespace HexCastingTerraria
{
    /// <summary>
    /// 模组主入口。1.4.5 的 tModLoader 会自动发现 [Mod] 类。
    ///
    /// 原项目：https://github.com/FallingColors/HexMod (MIT)
    /// </summary>
    public class HexCastingTerraria : Mod
    {
        /// <summary>图案注册表的装载结果，供调试与后续阶段使用。</summary>
        public static PatternRegistryLoadResult? PatternLoad { get; private set; }

        /// <summary>
        /// 切换无限媒质（开发者模式）的快捷键。
        /// 默认绑定到 J（字母键，不与泰拉默认操作冲突）。
        /// </summary>
        public static ModKeybind? ToggleInfiniteMediaKey { get; private set; }

        /// <summary>
        /// 手动发放开发者测试包的快捷键（默认 K）。
        ///
        /// 与配置里的「进世界自动发放」互补：那个只在进世界时发一次，
        /// 这个随时可以补 —— 测到一半把法杖扔了、把材料用光了都用它。
        /// </summary>
        public static ModKeybind? GiveDevKitKey { get; private set; }

        /// <summary>开发者面板（法术示例 / 调试开关 / 进度状态），默认 F7。</summary>
        public static ModKeybind? DevPanelKey { get; private set; }

        /// <summary>模组实例，供其它类写日志用。</summary>
        public static HexCastingTerraria? Instance { get; private set; }

        public override void Load()
        {
            Instance = this;
            ToggleInfiniteMediaKey = KeybindLoader.RegisterKeybind(this, "ToggleInfiniteMedia", Keys.J);
            GiveDevKitKey = KeybindLoader.RegisterKeybind(this, "GiveDevKit", Keys.K);
            DevPanelKey = KeybindLoader.RegisterKeybind(this, "DevPanel", Keys.F7);

            // 把服务端配置接进 Core 侧的大法术规则。
            // 用委托而不是值：配置随时可能被改，取的时候再读。
            Core.Casting.GreatTeleportRules.DropsItems = () => HexServerConfig.Instance.GreatTeleportDropsItems;
            Core.Casting.GreatTeleportRules.DropDivisor = () => HexServerConfig.Instance.GreatTeleportDropDivisor;

            // 法术环没有施法者，消息只能落到日志（源项目显示在原动力上方，等做了渲染再改）
            Core.Casting.Circles.CircleMessages.Sink = msg => Logger.Info($"[HexCasting/环] {msg}");

            ConfigureBrainsweepRecipes();

            // Phase 0 自检：用带重叠校验的严格模式装载全部图案数据，
            // 用来体检从源项目提取的 188 条数据是否自洽。
            // 严格模式失败不影响正式注册表（正式装载在下面用非严格模式再做一遍）。
            var strict = PatternRegistry.Load(strictValidation: true);
            PatternLoad = PatternRegistry.Load(strictValidation: false);

            Logger.Info($"[HexCasting] 图案数据自检：共 {strict.Total} 条，严格校验通过 {strict.Loaded} 条，"
                       + $"失败 {strict.StrictValidationFailures.Count} 条");

            foreach (var failure in strict.StrictValidationFailures)
            {
                Logger.Warn($"[HexCasting] 严格校验未通过：{failure}");
            }

            foreach (var dup in PatternLoad.DuplicateSignatures)
            {
                Logger.Warn($"[HexCasting] 签名重复：{dup}");
            }

            Logger.Info($"[HexCasting] 图案注册表已装载 {PatternRegistry.Count} 条"
                       + $"（重复签名 {PatternLoad.DuplicateSignatures.Count} 条）");

            // 存档自检：每种 iota 真写一遍 TagIO 再读回（之前信封直接塞 TagCompound，一存档就崩）
            var (tagTotal, tagFailures) = Content.Net.IotaTag.SelfTest();
            Logger.Info($"[HexCasting] iota 存档自检：共 {tagTotal} 条，失败 {tagFailures.Count} 条");
            foreach (var failure in tagFailures) Logger.Warn($"[HexCasting] iota 存档自检未通过：{failure}");

            // 注册已实现的图案行为（VM 求值用）。
            // 目前只接「常数 + 栈操作」这一批，其余按批增量补齐。
            Core.Casting.Actions.HexActions.RegisterAll();
            Logger.Info($"[HexCasting] 已实现行为的图案：{PatternRegistry.RegisteredActionCount} 条");

            // 附属（ADDONS.md）：开关此时已由 tML 读好；声明全部附属图案，只启用开着的
            Addons.AddonRegistry.Load(this);
        }

        public override void Unload()
        {
            Addons.AddonRegistry.Unload();
            Content.Net.ServerCastState.ClearAll();
            PatternLoad = null;
            ToggleInfiniteMediaKey = null;
            GiveDevKitKey = null;
            DevPanelKey = null;
            PatternRegistry.ClearActions();
        }

        /// <summary>
        /// 把脑叶切除配方注入 Core。
        ///
        /// 为什么要注入而不是写死在 Core 里：配方里的方块/生物 ID 是**泰拉的数据**，
        /// 而 Core 不许引用 Terraria（那会让 400+ 条离线用例全部编译不过）。
        /// 匹配与代价的逻辑在 Core（可离线测），具体数值在这里给。
        ///
        /// 配方逐条对照源项目 `HexplatRecipes.java`，差异见
        /// <see cref="Core.Casting.Actions.BrainsweepRules"/> 的表格。
        /// </summary>
        private void ConfigureBrainsweepRecipes()
        {
            const long crystal10 = 10 * Core.Media.MediaConstants.CrystalUnit;
            const long crystal1 = Core.Media.MediaConstants.CrystalUnit;
            const int none = -1;

            int dustBlock = ModContent.TileType<Content.Tiles.AmethystDustBlock>();
            int geodeCore = ModContent.TileType<Content.Tiles.GeodeCore>();
            int emptyDirectrix = ModContent.TileType<Content.Tiles.HexDirectrixEmpty>();
            int redstoneDirectrix = ModContent.TileType<Content.Tiles.HexDirectrixRedstone>();
            int booleanDirectrix = ModContent.TileType<Content.Tiles.HexDirectrixBoolean>();
            int akashicRecord = ModContent.TileType<Content.Tiles.AkashicRecord>();
            int emptyImpetus = ModContent.TileType<Content.Tiles.HexImpetusEmpty>();
            int quenchedAllay = ModContent.TileType<Content.Tiles.QuenchedAllay>();

            int AnyNpc = Core.Casting.Actions.BrainsweepRules.AnySpecies;
            int wizard = Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.Wizard);
            int demolitionist = Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.Demolitionist);
            int dyeTrader = Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.DyeTrader);

            Core.Casting.Actions.BrainsweepRules.Configure(new[]
            {
                // 原版：紫水晶块 + 村民 → 母岩
                new Core.Casting.Actions.BrainsweepRecipe(dustBlock, AnyNpc, geodeCore, none, crystal10),

                // 原版：空导线 + 石匠/牧羊人 → 红石导线 / 布尔导线（职业已映射到泰拉的城镇 NPC）
                new Core.Casting.Actions.BrainsweepRecipe(emptyDirectrix, demolitionist, redstoneDirectrix, none, crystal10),
                new Core.Casting.Actions.BrainsweepRecipe(emptyDirectrix, dyeTrader, booleanDirectrix, none, crystal10),

                // 原版：空白促动石 + 工具匠 / 制箭师 / 牧师 → 三种促动石（2 级村民，1000000 媒质）
                // 泰拉：工具匠 → 哥布林工匠（修改工具的人）、制箭师 → 军火商（远程武器与弹药）、牧师 → 护士（治疗者）
                new Core.Casting.Actions.BrainsweepRecipe(emptyImpetus,
                    Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.GoblinTinkerer),
                    ModContent.TileType<Content.Tiles.HexImpetus>(), none, crystal10),
                new Core.Casting.Actions.BrainsweepRecipe(emptyImpetus,
                    Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.ArmsDealer),
                    ModContent.TileType<Content.Tiles.HexImpetusLook>(), none, crystal10),
                new Core.Casting.Actions.BrainsweepRecipe(emptyImpetus,
                    Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(NPCID.Nurse),
                    ModContent.TileType<Content.Tiles.HexImpetusRedstone>(), none, crystal10),

                // 原版：阿卡夏系带 + 图书管理员 → 记录方块（泰拉没有「系带」，改用粉块）
                new Core.Casting.Actions.BrainsweepRecipe(dustBlock, wizard, akashicRecord, none, crystal10),

                // 原版：紫水晶块 + 悦灵 → **淬灵块**（方块，敲掉掉 2~4 片碎片），1 晶体。
                // 泰拉的悦灵 = **小精灵（Pixie）**：神圣地的精灵，肉后才有 —— 用户按进度特意定的，淬灵线整条落在肉后
                //（与法术环「肉后 · 启蒙」同一档）。按 netID 匹配（它不是城镇 NPC）。
                // 这里曾经写成 TownNpcSpecies(粉妖精)：城镇 NPC 编码配小动物，**这条配方永远匹配不上**；产物也错成了碎片物品。
                new Core.Casting.Actions.BrainsweepRecipe(dustBlock, NPCID.Pixie, quenchedAllay, none, crystal1),
            });

            Logger.Info($"[HexCasting] 脑叶切除配方：{Core.Casting.Actions.BrainsweepRules.Recipes.Count} 条");
        }

        /// <summary>
        /// 网络包处理。
        ///
        /// 分界很清楚：
        ///   - 单机（`netMode == SinglePlayer`）走客户端本地求值，**不经过这里**
        ///   - 联机时客户端只上报「我画了什么」，一切求值在服务端做
        ///
        /// 之所以把这条路径做成**附加**而不是替换：单机路径是经过离线测试验证的，
        /// 而联机路径无法在离线环境验证 —— 隔离能让已验证的部分不受影响。
        /// </summary>
        public override void HandlePacket(System.IO.BinaryReader reader, int whoAmI)
        {
            var msg = (Content.Net.HexMessage)reader.ReadByte();

            switch (msg)
            {
                case Content.Net.HexMessage.CastPattern:
                    if (Main.netMode != NetmodeID.Server)
                    {
                        return;   // 只有服务端处理
                    }
                    HandleCastPattern(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.DirectrixFacing:
                    if (Main.netMode != NetmodeID.Server) return;
                    Content.Net.HexNetSync.HandleDirectrixFacing(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.StartCircle:
                    if (Main.netMode != NetmodeID.Server) return;
                    Content.Net.HexNetSync.HandleStartCircle(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.SlatePattern:
                    if (Main.netMode != NetmodeID.Server) return;
                    Content.Net.HexNetSync.HandleSlatePattern(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.SlateNormal:
                    if (Main.netMode != NetmodeID.Server) return;
                    Content.Net.HexNetSync.HandleSlateNormal(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.ResetCast:
                    if (Main.netMode != NetmodeID.Server)
                    {
                        return;
                    }
                    Content.Net.ServerCastState.Clear(whoAmI);
                    break;

                case Content.Net.HexMessage.StackSync:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Client.HexVmState.ApplySyncedStack(reader);
                    break;

                case Content.Net.HexMessage.SpellVisual:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Client.HexClientSystem.ReceiveSpellVisual(reader);
                    break;

                case Content.Net.HexMessage.Beep:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandleBeep(reader);
                    break;

                case Content.Net.HexMessage.CastPackaged:
                    if (Main.netMode != NetmodeID.Server)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandleCastPackaged(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.CircleCursor:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandleCircleCursor(reader);
                    break;

                case Content.Net.HexMessage.WallScroll:
                    if (Main.netMode != NetmodeID.Server)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandleWallScroll(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.SpellSound:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.SpellSounds.Receive(reader);
                    break;

                case Content.Net.HexMessage.PlayerMotion:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandlePlayerMotion(reader);
                    break;

                case Content.Net.HexMessage.PlayerBuff:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.Net.HexNetSync.HandlePlayerBuff(reader);
                    break;

                case Content.Net.HexMessage.OwnerEffect:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.PlayerEffects.Handle(reader);
                    break;

                case Content.Net.HexMessage.ImpetusAction:
                    if (Main.netMode != NetmodeID.Server) return;
                    Content.Tiles.HexImpetusEntity.Handle(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.Addon:
                    Addons.AddonRegistry.HandlePacket(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.TripWire:
                    Content.Tiles.EdifiedWiring.Receive(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.PlayerState:
                    Content.HexPlayer.HandleState(reader, whoAmI);
                    break;

                case Content.Net.HexMessage.SpellParticles:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        return;
                    }
                    Content.SpellVisuals.Receive(reader);
                    break;
            }
        }

        /// <summary>
        /// 服务端处理「客户端画完一条图案」。
        ///
        /// 三件事，顺序不能变：
        ///   ① 权威求值（会扣媒质、会真的施法）
        ///   ② 回给该玩家当前栈（HUD）
        ///   ③ 广播瞄准点给附近的人（纯表现，失败不影响求值）
        /// </summary>
        private void HandleCastPattern(System.IO.BinaryReader reader, int whoAmI)
        {
            var pattern = Content.Net.IotaWire.ReadPattern(reader);
            float aimX = reader.ReadSingle();
            float aimY = reader.ReadSingle();

            if (pattern == null)
            {
                Logger.Warn($"[HexCasting] 收到来自 {whoAmI} 的非法图案数据，已忽略");
                return;
            }

            if (whoAmI < 0 || whoAmI >= Main.maxPlayers)
            {
                return;
            }

            var player = Main.player[whoAmI];
            if (player is not { active: true })
            {
                return;
            }

            // ① 权威求值
            var (stack, resolution) = Content.Net.ServerCastState.EvaluatePattern(player, pattern);

            // ② 回传栈状态
            Content.Net.ServerCastState.SendStackSync(whoAmI, stack, resolution);

            // ③ 广播瞄准点（只给附近玩家，避免全服刷屏）
            var visual = GetPacket();
            visual.Write((byte)Content.Net.HexMessage.SpellVisual);
            visual.Write((byte)whoAmI);
            visual.Write(aimX);
            visual.Write(aimY);

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                var other = Main.player[i];
                if (other is not { active: true } || i == whoAmI) continue;
                if (System.Math.Abs(other.Center.X - player.Center.X) > 4000f) continue;
                if (System.Math.Abs(other.Center.Y - player.Center.Y) > 4000f) continue;
                visual.Send(i);
            }
        }
    }
}
