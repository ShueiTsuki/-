using System.Collections.Generic;
using System.Linq;
using System.Text;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// 指令 <c>/hexParse</c>（上游 hooks/HexParseCommands.java + commands/*）。子指令照搬上游：
/// <code>
/// /hexParse &lt;代码&gt; [重命名]                  解析代码写进手上的核心 / 念珠 / 法术书当前页
/// /hexParse read | read_signatures | read_hexbug | share
/// /hexParse clipboard | clipboard_angles | clipboard_hexpattern [重命名]
/// /hexParse mind_stack peek | push &lt;代码&gt; | push_clipboard
/// /hexParse macro | dialect  list | define &lt;名&gt; &lt;内容…&gt; | remove &lt;名&gt; | (macro) define_clipboard &lt;名&gt;
/// /hexParse conflict [list [短名]] | set &lt;短名&gt; &lt;长 id&gt;     （单人或房主）
/// /hexParse lehmer &lt;数…&gt;  /  donate &lt;粉数&gt;  /  learn_great
/// /hexParse unlock_great unlockAll | lockAll | unlock &lt;id&gt; | lock &lt;id&gt;   （单人或房主）
/// </code>
/// 参数的写法同上游（Brigadier 的 string）：带空格就用双引号括起来，引号里可以用 \" 和 \\。
/// 在**本人客户端**执行（物品、剪贴板、宏都在客户端）；改世界表的子指令联机时发给服务端，由服务端检查房主再执行。
/// </summary>
public sealed class HexParseCommand : AddonCommand
{
    public override string AddonId => "hexparse";

    public override CommandType Type => CommandType.Chat;

    public override string Command => "hexParse";

    public override string Usage =>
        "/hexParse <代码> [重命名]  |  read / read_signatures / read_hexbug / share  |  clipboard [重命名]\n"
        + "mind_stack peek|push <代码>|push_clipboard  |  macro / dialect list|define|remove  |  conflict  |  lehmer  |  donate  |  learn_great  |  unlock_great";

    public override string Description => "HexParse：代码文本与 iota 列表互转";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        var player = caller.Player;
        if (player == null) return;
        var r = new ArgReader(input.Trim().Length > Command.Length + 1 ? input.Trim().Substring(Command.Length + 1) : string.Empty);
        if (r.AtEnd)
        {
            Say(Usage);
            return;
        }
        try
        {
            Dispatch(player, r);
        }
        catch (HexParseException e)
        {
            HexParseHost.Show(e.Message, HexParseMessageKind.Error);
        }
    }

    private void Dispatch(Player player, ArgReader r)
    {
        string head = r.PeekWord();
        switch (head)
        {
            case "read": r.ReadWord(); HexParseIO.DisplayCode(HexParseIO.ReadHeld(player, false, IotaWriter.ReadDefault)); return;
            case "read_signatures": r.ReadWord(); HexParseIO.DisplayCode(HexParseIO.ReadHeld(player, true, IotaWriter.ReadDefault)); return;
            case "read_hexbug": r.ReadWord(); HexParseIO.DisplayCode(HexParseIO.ReadHeld(player, false, IotaWriter.ReadHexbug)); return;
            case "share": r.ReadWord(); Share(player); return;
            case "clipboard": r.ReadWord(); HexParseIO.HandleClipboard(player, ClipboardMode.Default, r.ReadStringOrNull()); return;
            case "clipboard_angles": r.ReadWord(); HexParseIO.HandleClipboard(player, ClipboardMode.AnglesOnly, r.ReadStringOrNull()); return;
            case "clipboard_hexpattern": r.ReadWord(); HexParseIO.HandleClipboard(player, ClipboardMode.HexPatternFormat, r.ReadStringOrNull()); return;
            case "mind_stack": r.ReadWord(); MindStack(player, r); return;
            case "macro": r.ReadWord(); Macro(r, isMacro: true); return;
            case "dialect": r.ReadWord(); Macro(r, isMacro: false); return;
            case "conflict": r.ReadWord(); Conflict(r); return;
            case "lehmer": r.ReadWord(); Lehmer(r.ReadGreedy()); return;
            case "donate": r.ReadWord(); Donate(player, r.ReadWord()); return;
            case "learn_great": r.ReadWord(); LearnGreat(player); return;
            case "unlock_great": r.ReadWord(); UnlockGreat(r); return;
        }
        // 默认：/hexParse <代码> [重命名]
        string code = r.ReadString();
        string? rename = r.ReadStringOrNull();
        HexParseIO.WriteHeld(player, p => p.ParseCode(code), rename);
    }

    private static void Say(string text) => HexParseHost.Show(text, HexParseMessageKind.Info);

    // ── share ──────────────────────────────────────────────────────

    private static void Share(Player player)
    {
        string? code = HexParseIO.ReadHeld(player, false, IotaWriter.ReadDefault);
        var iota = HexParseIO.HeldIO(player)?.Read();
        if (code == null || iota == null) return;
        // 上游「%s分享了：%s （点击复制）」；泰拉聊天不能点，代码直接附在后面
        string text = $"{player.name}分享了：{DisplayTags.Of(iota)} （{code}）";
        if (Main.netMode == NetmodeID.MultiplayerClient) HexParseNet.SendShare(text);
        else Say(text);
    }

    // ── mind_stack ─────────────────────────────────────────────────

    private static void MindStack(Player player, ArgReader r)
    {
        switch (r.ReadWord())
        {
            case "peek":
            {
                var stack = Client.HexVmState.Stack;
                Iota top = stack.Count == 0 ? NullIota.Instance : stack[stack.Count - 1];
                HexParseIO.DisplayCode(new HexParseHost(player).NewWriter().Write(top, false, IotaWriter.ReadDefault));
                return;
            }
            case "push":
            {
                string code = r.ReadString();
                var host = new HexParseHost(player);
                var parser = host.NewParser();
                var list = parser.ParseCode(code);
                HexParseIO.ChargeCost(player, parser.TotalCost);
                HexParseIO.PushMind(player, list);
                return;
            }
            case "push_clipboard":
                HexParseIO.HandleClipboard(player, ClipboardMode.PushMind, null);
                return;
            default:
                Say("/hexParse mind_stack peek | push <代码> | push_clipboard");
                return;
        }
    }

    // ── macro / dialect ────────────────────────────────────────────

    private static void Macro(ArgReader r, bool isMacro)
    {
        string what = isMacro ? "宏" : "别名";
        switch (r.ReadWord())
        {
            case "list":
            {
                var entries = HexParseMacros.List(isMacro);
                Say($"共{entries.Count}个{what}：");
                foreach (var kv in entries) Say($"{kv.Key} = {kv.Value}");
                return;
            }
            case "define":
            {
                string? key = CheckKey(r.ReadString(), isMacro);
                if (key == null) return;
                string value = r.ReadGreedy();
                DefineMacro(key, value);
                return;
            }
            case "remove":
            {
                string? key = CheckKey(r.ReadString(), isMacro);
                if (key == null) return;
                HexParseMacros.Remove(key);
                Say($"移除了 {key} 的定义。");
                return;
            }
            case "define_clipboard" when isMacro:
            {
                string? key = CheckKey(r.ReadString(), true);
                if (key != null) HexParseIO.HandleClipboard(Main.LocalPlayer, ClipboardMode.MacroDefine, key);
                return;
            }
            default:
                Say($"/hexParse {(isMacro ? "macro" : "dialect")} list | define <名> <内容> | remove <名>{(isMacro ? " | define_clipboard <名>" : "")}");
                return;
        }
    }

    /// <summary>上游 wrapCheckMacroKey：宏名自动补 #；别名不许以 # 开头。</summary>
    private static string? CheckKey(string key, bool isMacro)
    {
        if (isMacro == HexParseMacros.IsMacro(key)) return key;
        if (isMacro) return "#" + key;
        HexParseHost.Show($"别名不可以'#'开头（{key}）", HexParseMessageKind.Error);
        return null;
    }

    /// <summary>上游 generalModify(define) + MacroManager.modifyMacro。</summary>
    internal static void DefineMacro(string key, string value)
    {
        if (HexParseMacros.WouldExceedLimit(key))
        {
            HexParseHost.Show("宏/别名数量超过限制，添加失败", HexParseMessageKind.Error);
            return;
        }
        if (key.Length > HexParseMacros.MaxSingleSize)
        {
            HexParseHost.Show("宏/别名取名过长，添加失败", HexParseMessageKind.Error);
            return;
        }
        if (value.Length > HexParseMacros.MaxSingleSize)
        {
            HexParseHost.Show($"宏过长；移除了末尾的{value.Length - HexParseMacros.MaxSingleSize}个字符", HexParseMessageKind.Warning);
            value = value.Substring(0, HexParseMacros.MaxSingleSize);
        }
        HexParseMacros.Define(key, value);
        Say($"定义 {key} = {value}.");
    }

    // ── conflict ───────────────────────────────────────────────────

    private static bool CanEditWorld() => Main.netMode == NetmodeID.SinglePlayer || Main.countsAsHostForGameplay[Main.myPlayer];

    private static void Conflict(ArgReader r)
    {
        if (!CanEditWorld())
        {
            HexParseHost.Show("只有单人游戏或房主能用这个子指令", HexParseMessageKind.Error);
            return;
        }
        var names = HexParseHost.Names();
        string sub = r.AtEnd ? "list" : r.ReadWord();
        if (sub == "list" && r.AtEnd)
        {
            Say("所有含冲突图案名称：");
            foreach (var name in names.ShortNameWithConflicts)
                Say($"- \"{name}\" （共 {names.AllPointed[name].Count} 个图案）");
            return;
        }
        if (sub == "list")
        {
            string name = PatternNames.ShortOf(r.ReadString());
            if (!names.ActiveShortName.TryGetValue(name, out var current))
            {
                HexParseHost.Show("无效名称", HexParseMessageKind.Error);
                return;
            }
            Say($"名称 \"{name}\" 下的所有冲突图案（当前指向 {current}）：");
            foreach (var id in names.AllPointed[name]) Say($"- \"{id}\" ({DisplayOf(id)})");
            return;
        }
        if (sub == "set")
        {
            string name = PatternNames.ShortOf(r.ReadString());
            string id = r.ReadString();
            if (Main.netMode == NetmodeID.MultiplayerClient) HexParseNet.SendWorldOp(HexParseNet.Op.ConflictSet, name, id);
            else Say(ApplyWorldOp(Main.LocalPlayer, HexParseNet.Op.ConflictSet, name, id));
            return;
        }
        Say("/hexParse conflict [list [短名]] | set <短名> <长 id>");
    }

    /// <summary>上游 getPatternDisplay：没解锁的大法术显示 ???。</summary>
    private static string DisplayOf(string longId)
    {
        var def = PatternRegistry.FindById(longId);
        if (def == null) return "NULL";
        if (PatternRegistry.IsPerWorld(def) && !HexParseWorld.IsUnlocked(longId)) return "???";
        return def.DisplayName();
    }

    // ── lehmer / donate / learn_great / unlock_great ─────────────────

    private static void Lehmer(string input)
    {
        var orders = new List<int>();
        foreach (var seg in input.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(seg, out int n)) throw new HexParseException($"未知符号：{seg}");
            orders.Add(n);
            if (orders.Count > 20) throw new HexParseException("代码过长（20）");
        }
        HexParseIO.DisplayCode(IotaWriter.Lehmer(orders).ToString());
    }

    /// <summary>上游 CommandDonate：向自然捐赠 amount 个紫水晶粉的媒质（从背包扣，扣不起就过载）。</summary>
    private static void Donate(Player player, string amountText)
    {
        if (!long.TryParse(amountText, out long amount) || amount < 1)
        {
            Say("/hexParse donate <数量>");
            return;
        }
        HexParseIO.ChargeCost(player, amount * MediaConstants.DustUnit);
    }

    private static void LearnGreat(Player player)
    {
        if (HexParseSettings.Current.ParseGreatSpells != HexParseSettings.GreatMode.ByScroll)
        {
            Say("当前设置不需要学习卓越法术");
            return;
        }
        if (Main.netMode == NetmodeID.MultiplayerClient) HexParseNet.SendWorldOp(HexParseNet.Op.Learn);
        else Say(ApplyWorldOp(player, HexParseNet.Op.Learn, "", ""));
    }

    private static void UnlockGreat(ArgReader r)
    {
        if (!CanEditWorld() || HexParseSettings.Current.ParseGreatSpells != HexParseSettings.GreatMode.ByScroll)
        {
            HexParseHost.Show("只有单人游戏或房主、且「解析卓越法术」为「按古卷解锁」时能用", HexParseMessageKind.Error);
            return;
        }
        (HexParseNet.Op op, string id) = r.ReadWord() switch
        {
            "unlockAll" => (HexParseNet.Op.UnlockAll, ""),
            "lockAll" => (HexParseNet.Op.LockAll, ""),
            "unlock" => (HexParseNet.Op.Unlock, r.ReadString()),
            "lock" => (HexParseNet.Op.Lock, r.ReadString()),
            _ => ((HexParseNet.Op)255, ""),
        };
        if ((byte)op == 255)
        {
            Say("/hexParse unlock_great unlockAll | lockAll | unlock <图案 id> | lock <图案 id>");
            return;
        }
        if (Main.netMode == NetmodeID.MultiplayerClient) HexParseNet.SendWorldOp(op, id);
        else Say(ApplyWorldOp(Main.LocalPlayer, op, id, ""));
    }

    /// <summary>
    /// 改世界表（单人直接执行；联机在服务端执行，执行前再查一次权限）。返回给请求者的消息。
    /// 学大法术人人可用；其余只许房主。
    /// </summary>
    internal static string ApplyWorldOp(Player player, HexParseNet.Op op, string a, string b)
    {
        bool host = Main.netMode == NetmodeID.SinglePlayer || Main.countsAsHostForGameplay[player.whoAmI];
        if (op != HexParseNet.Op.Learn && !host) return "只有房主能用这个子指令";
        string result;
        switch (op)
        {
            case HexParseNet.Op.Learn:
            {
                var learned = HexParseActions.LearnFromHeld(player);
                result = DisplayTags.Of(new ListIota(learned));
                break;
            }
            case HexParseNet.Op.UnlockAll: result = $"解锁了{HexParseWorld.UnlockAll()}种卓越图案"; break;
            case HexParseNet.Op.LockAll: result = $"锁定了{HexParseWorld.LockAll()}种卓越图案"; break;
            case HexParseNet.Op.Unlock:
            {
                string id = a.Contains(':') ? a : HexParseHost.Names().ActiveLongName(a);   // 没写命名空间 = 按短名找
                HexParseWorld.Unlock(id);
                result = $"解锁了{id}";
                break;
            }
            case HexParseNet.Op.Lock:
            {
                string id = a.Contains(':') ? a : HexParseHost.Names().ActiveLongName(a);   // 没写命名空间 = 按短名找
                HexParseWorld.Lock(id);
                result = $"锁定了{id}";
                break;
            }
            case HexParseNet.Op.ConflictSet:
            {
                var names = HexParseHost.Names();
                names.RedirectShortName(a, b);   // 不合法会抛 HexParseException
                HexParseWorld.SetShortName(a, b);
                result = $"短名称 \"{a}\" 已设置为图案 \"{b}\" ({DisplayOf(b)})。";
                break;
            }
            default:
                return "未知操作";
        }
        HexParseWorld.Sync();
        return result;
    }

    // ── 参数读取（上游 Brigadier 的 word / string / greedyString）─────────

    private sealed class ArgReader
    {
        private readonly string _s;
        private int _i;

        public ArgReader(string s) => _s = s;

        public bool AtEnd
        {
            get
            {
                SkipSpace();
                return _i >= _s.Length;
            }
        }

        private void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        public string PeekWord()
        {
            int save = _i;
            string w = ReadWord();
            _i = save;
            return w;
        }

        public string ReadWord()
        {
            SkipSpace();
            int start = _i;
            while (_i < _s.Length && !char.IsWhiteSpace(_s[_i])) _i++;
            return _s.Substring(start, _i - start);
        }

        /// <summary>Brigadier string()：引号括起来的（支持 \" \\ 转义），或者一个不含空白的词。</summary>
        public string ReadString()
        {
            SkipSpace();
            if (_i >= _s.Length) throw new HexParseException("缺少参数");
            char q = _s[_i];
            if (q != '"' && q != '\'') return ReadWord();
            _i++;
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '\\' && _i < _s.Length && (_s[_i] == q || _s[_i] == '\\')) { sb.Append(_s[_i++]); continue; }
                if (c == q) return sb.ToString();
                sb.Append(c);
            }
            throw new HexParseException("引号没有闭合");
        }

        public string? ReadStringOrNull() => AtEnd ? null : ReadString();

        /// <summary>Brigadier greedyString()：剩下的全部。</summary>
        public string ReadGreedy()
        {
            SkipSpace();
            string rest = _s.Substring(_i);
            _i = _s.Length;
            return rest;
        }
    }
}
