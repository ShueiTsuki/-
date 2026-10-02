# 生成 HexCastingTerraria/Core/Ui/BookContent.Generated.cs —— 咒法学之书的全部内容。
#
# 输入（都是原版，不手写正文）：
#   hexsrc/.../patchouli_books/thehexbook/en_us/{categories,entries}   手册结构（7 分类 / 82 条目）
#   hexsrc_assets/lang/zh_cn.flatten.json5                             官方中文（FallingColors/HexMod v0.11.4）
#   hexsrc_assets/lang/en_us.flatten.json5                             中文缺键时的回退
#
# 泰拉侧的取舍（都写在这里，不藏在生成物里）：
#   - 跳过带 flag 的条目（只在装了别的 MC 模组时才出现：interop / pehkui），interop 分类随之为空、不生成。
#   - secret 条目照生成，标 Secret：没解锁时整条隐藏（Patchouli 的做法），解锁了才出现在目录里。
#   - 物品图标与配方产物映射到本模组 / 泰拉原版物品（见 ITEM_MAP），映射不到就不画图标，**不报错不瞎画**。
#
# 用法：python _tools/gen_book_content.py        （输出路径固定）
import json, os, glob, sys
sys.path.insert(0, os.path.dirname(__file__))
import hexlang

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
# 本仓库（脚本所在的 tmod 目录；在 git worktree 里跑时就是那个 worktree，不会写到别的副本里去）
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
BOOK = os.path.join(ROOT, 'hexsrc/Common/src/main/resources/assets/hexcasting/patchouli_books/thehexbook/en_us')
LANG_ZH = os.path.join(ROOT, 'hexsrc_assets/lang/zh_cn.flatten.json5')
LANG_EN = os.path.join(ROOT, 'hexsrc_assets/lang/en_us.flatten.json5')
OUT = os.path.join(REPO, 'HexCastingTerraria/Core/Ui/BookContent.Generated.cs')
VANILLA = os.path.join(REPO, '_tools/vanilla_ids.json')
MOD_ITEMS_DIR = os.path.join(REPO, 'HexCastingTerraria/Content')

# 原版物品 id（去掉 NBT）→ 泰拉物品。'Mod:类名' = 本模组物品，'Terraria:ItemID 字段名' = 原版物品。
ITEM_MAP = {
    # 咒法学物品
    'hexcasting:focus': 'Mod:Focus', 'hexcasting:spellbook': 'Mod:Spellbook',
    'hexcasting:staff/oak': 'Mod:OakStaff', 'hexcasting:staff/mindsplice': 'Mod:MindspliceStaff',
    'hexcasting:staff/edified': 'Mod:EdifiedStaff', 'hexcasting:staff/quenched': 'Mod:QuenchedStaff',
    'hexcasting:slate': 'Mod:HexSlateItem', 'hexcasting:scroll': 'Mod:ScrollLarge',
    'hexcasting:scroll_small': 'Mod:ScrollSmall', 'hexcasting:scroll_medium': 'Mod:ScrollMedium',
    'hexcasting:quenched_allay': 'Mod:QuenchedAllayItem', 'hexcasting:quenched_allay_shard': 'Mod:QuenchedAllayShard',
    'hexcasting:thought_knot': 'Mod:ThoughtKnot', 'hexcasting:jeweler_hammer': 'Mod:JewelerHammer',
    'hexcasting:lens': 'Mod:ScryingLens', 'hexcasting:abacus': 'Mod:Abacus', 'hexcasting:artifact': 'Mod:Artifact',
    'hexcasting:cypher': 'Mod:Cypher', 'hexcasting:trinket': 'Mod:Trinket', 'hexcasting:battery': 'Mod:MediaFlask',
    'hexcasting:amethyst_dust': 'Mod:AmethystDust', 'hexcasting:charged_amethyst': 'Mod:ChargedAmethyst',
    'hexcasting:akashic_record': 'Mod:AkashicRecordItem', 'hexcasting:akashic_bookshelf': 'Mod:AkashicBookshelfItem',
    'hexcasting:akashic_connector': 'Mod:AkashicLigatureItem', 'hexcasting:edified_log': 'Mod:EdifiedLogItem',
    'hexcasting:edified_planks': 'Mod:EdifiedPlanksItem', 'hexcasting:ancient_scroll_paper': 'Mod:AncientScrollPaperItem',
    'hexcasting:ancient_cypher': 'Mod:Cypher', 'hexcasting:impetus/empty': 'Mod:HexImpetusItem',
    'hexcasting:impetus/rightclick': 'Mod:HexImpetusItem', 'hexcasting:impetus/look': 'Mod:HexImpetusItem',
    'hexcasting:impetus/storedplayer': 'Mod:HexImpetusItem',
    'hexcasting:directrix/empty': 'Mod:HexDirectrixEmptyItem', 'hexcasting:directrix/redstone': 'Mod:HexDirectrixRedstoneItem',
    'hexcasting:directrix/boolean': 'Mod:HexDirectrixBooleanItem', 'hexcasting:lore_fragment': 'Terraria:Book',
    'hexcasting:creative_unlocker': 'Mod:CreativeUnlocker', 'hexcasting:sub_sandwich': 'Mod:SubSandwich',
    'hexcasting:book': 'Mod:HexBookItem',
    # MC 原版物品 → 泰拉的对应物（取「玩家一眼能认出是什么」的那件）
    'minecraft:amethyst_shard': 'Mod:AmethystShard', 'minecraft:amethyst_block': 'Mod:AmethystDustBlockItem',
    'minecraft:bookshelf': 'Terraria:Bookcase', 'minecraft:chain': 'Terraria:Chain', 'minecraft:piston': 'Terraria:Actuator',
    'minecraft:writable_book': 'Terraria:Book', 'minecraft:knowledge_book': 'Terraria:Book',
    'minecraft:wooden_pickaxe': 'Terraria:CopperPickaxe', 'minecraft:stick': 'Terraria:Wood',
    'minecraft:skeleton_skull': 'Terraria:Skull', 'minecraft:wither_skeleton_skull': 'Terraria:Skull',
    'minecraft:shulker_box': 'Terraria:Safe', 'minecraft:red_mushroom': 'Terraria:Mushroom', 'minecraft:quartz': 'Terraria:Diamond',
    'minecraft:purple_candle': 'Terraria:Candle', 'minecraft:potion': 'Terraria:HealingPotion',
    'minecraft:pig_spawn_egg': 'Terraria:PiggyBank', 'minecraft:oak_sign': 'Terraria:Sign', 'minecraft:nether_star': 'Terraria:FallenStar',
    'minecraft:name_tag': 'Terraria:Sign', 'minecraft:music_disc_11': 'Terraria:MusicBoxOverworldDay',
    'minecraft:lodestone': 'Terraria:MagicMirror', 'minecraft:lava_bucket': 'Terraria:LavaBucket',
    'minecraft:item_frame': 'Terraria:ItemFrame', 'minecraft:flint_and_steel': 'Terraria:Torch', 'minecraft:feather': 'Terraria:Feather',
    'minecraft:ender_pearl': 'Terraria:TeleportationPotion', 'minecraft:emerald_block': 'Terraria:EmeraldGemsparkBlock',
    'minecraft:emerald': 'Terraria:Emerald', 'minecraft:elytra': 'Terraria:AngelWings', 'minecraft:comparator': 'Terraria:Timer1Second',
    'minecraft:cobblestone': 'Terraria:StoneBlock', 'minecraft:bundle': 'Terraria:Chest', 'minecraft:blaze_rod': 'Terraria:Hellstone',
    'minecraft:bedrock': 'Terraria:Obsidian', 'minecraft:beacon': 'Terraria:LifeCrystal', 'minecraft:arrow': 'Terraria:WoodenArrow',
    # 状态效果图标（原版用贴图路径当图标）→ 效果相近的物品
    'minecraft:textures/mob_effect/poison.png': 'Terraria:FlaskofPoison',
    'minecraft:textures/mob_effect/nausea.png': 'Terraria:BlackLens',
    'minecraft:textures/mob_effect/levitation.png': 'Terraria:GravitationPotion',
    'minecraft:textures/mob_effect/blindness.png': 'Terraria:Blindfold',
    'minecraft:textures/item/enchanted_book.png': 'Terraria:SpellTome', 'minecraft:smithing_table': 'Terraria:IronAnvil',
    'minecraft:textures/mob_effect/conduit_power.png': 'Terraria:BookofSkulls',
}

# 颜料（染色剂）：原版 dye_colorizer_* / pride_colorizer_* / uuid / default / ancient → 生成的 PigmentXxx 类（_tools/gen_pigments.py）
def _camel(x):
    return ''.join(p.capitalize() for p in x.split('_'))


for _d in ['white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray',
           'light_gray', 'cyan', 'purple', 'blue', 'brown', 'green', 'red', 'black']:
    ITEM_MAP[f'hexcasting:dye_colorizer_{_d}'] = f'Mod:PigmentDye{_camel(_d)}'
for _p in ['agender', 'aroace', 'aromantic', 'asexual', 'bisexual', 'demiboy', 'demigirl', 'gay',
           'genderfluid', 'genderqueer', 'intersex', 'lesbian', 'nonbinary', 'pansexual', 'plural', 'transgender']:
    ITEM_MAP[f'hexcasting:pride_colorizer_{_p}'] = f'Mod:PigmentPride{_camel(_p)}'
ITEM_MAP.update({'hexcasting:uuid_colorizer': 'Mod:PigmentSoulglimmer', 'hexcasting:default_colorizer': 'Mod:PigmentDefault',
                 'hexcasting:ancient_colorizer': 'Mod:PigmentAncient'})


# ── 「另一只手」→ 泰拉的实际操作（2026-10-01 用户要求）──────────────────────────
# 泰拉人没有副手。移植版把原版的「另一只手」做成 **快捷栏中手持物品右边一格**
# （PlayerCastingEnvironment.PrimarySlots：inventory[(selectedItem + 1) % 10]，第 10 格绕回第 1 格）；
# 原版「手中 / 两只手」= 手持物品 + 它右边一格。官方中文照 MC 写「另一只手 / 副手 / 主手」，
# 玩家照着做不出来，所以生成时把这些句子换成泰拉里的实际操作。**只改文字，不改行为。**
# 统一说法：「快捷栏中手持物品右边一格」。
# 每条「原文片段」都必须在官方中文里命中，没命中（上游改了措辞）就报错退出，免得替换悄悄失效。
# 附属书页的替换表在 gen_addon_book.py 的 ADDON_HAND_WORDING。
HAND_WORDING = [
    # 事故「向量越界 / 实体越界 / 实体免疫」：甩出去的是手持物品和右边一格（PlayerEffects.YeetHeld）
    ('我手中的物品将会掉落并飞向对应',
     '我手持的物品和快捷栏中它右边一格的物品将会掉落并飞向对应'),
    # 事故「物品错误」（MishapBadHeldItem → DropHeldItems：两格一起丢）
    ('如果在手中持有对应物品，则该物品会掉落在地。',
     '如果需要的是快捷栏中手持物品右边一格的物品，则手持物品和这一格的物品都会掉落在地。'),
    # 探知透镜：手持 / 右边一格 / 饰品栏都生效（Client/ScryingOverlay.HasSight）；泰拉不能戴在头上
    ('施法时，在另一只手持有探知透镜就能缩短',
     '施法时，把探知透镜放在快捷栏中手持物品右边一格就能缩短'),
    ('我还可以把它当单片眼镜戴在头上。',
     '我还可以把它当单片眼镜装备在饰品栏里。'),
    # 结念绳
    ('在另一只手持有$(item)结念绳/$时，',
     '把$(item)结念绳/$放在快捷栏中手持物品右边一格时，'),
    # 算盘：拿在手上 ±1，放在右边一格 ±0.1（HexPlayer.SetControls / Abacus.Scroll）
    ('操作方法是潜行时手持算盘滚动滚轮。如果是主手持算盘，',
     '操作方法是潜行时滚动滚轮。如果算盘拿在手上，'),
    ('若是副手持算盘，',
     '若算盘放在快捷栏中手持物品右边一格，'),
    # 法术书：画布开着时滚轮直接翻右边一格的法术书（HexPlayer.SetControls）
    ('时副手手持，并直接用滚轮改变活动页。',
     '时把它放在快捷栏中手持物品右边一格，并直接用滚轮改变活动页。'),
    # 染色剂
    ('可手持之并以另一只手施放',
     '可将其放在快捷栏中手持物品右边一格，再施放'),
    # 读写 iota（PlayerCastingEnvironment.FindHeld）
    ('一般可从另一只手中的物品中读取或写入 iota。',
     '一般可从快捷栏中手持物品右边一格的物品中读取或写入 iota。'),
    ('复制另一只手所持物品中 iota，',
     '复制快捷栏中手持物品右边一格所放物品中的 iota，'),
    ('并将其写入另一只手中的物品中。',
     '并将其写入快捷栏中手持物品右边一格的物品中。'),
    ('而非手中物品',
     '而非快捷栏中手持物品右边一格的物品'),
    ('如果另一只手中物品存有',
     '如果快捷栏中手持物品右边一格的物品存有'),
    ('如果能将 iota 写入另一只手中的物品，',
     '如果能将 iota 写入快捷栏中手持物品右边一格的物品，'),
    ('从我的副手读取 iota，',
     '从快捷栏中手持物品右边一格的物品读取 iota，'),
    # 放置方块：泰拉没有「法杖在副手」这种情况，删掉这一句（TerrariaCastingWorld.FindPlaceableSlot）
    ('若$(l:items/staff)$(item)法杖/$在副手，则会从快捷栏一号位开始搜索。',
     ''),
    # 制作施法物品 / 重新充能 / 清除物品 / 内化染色剂 / 施法者之魅力 / 制作试剂瓶
    ('它们都要求我在另一只手上手持对应的基础物品，',
     '它们都要求我在快捷栏中手持物品右边一格放上对应的基础物品，'),
    ('给另一只手中能装',
     '给快捷栏中手持物品右边一格能装'),
    ('就能给另一只手中物品的',
     '就能给快捷栏中手持物品右边一格那件物品的'),
    ('清除另一只手中写有',
     '清除快捷栏中手持物品右边一格里写有'),
    ('施法时需要在另一只手中持有',
     '施法时需要在快捷栏中手持物品右边一格放有'),
    ('的影响。另一只手持有',
     '的影响。快捷栏中手持物品右边一格放有'),
    ('我需要在另一只手中拿着一个',
     '我需要在快捷栏中手持物品右边一格放一个'),
    # 论物品容器（分拣员之馏化 / 堆叠之馏化能比较的实体，原版 HexItemHolderHandlers）：
    # 原版物品展示框 → 泰拉的物品框（移植版把它做成了实体，见 TerrariaCastingWorld 的「物品框」）；
    # 玩家：主手，空了看副手 → 手持物品，空了看快捷栏中它右边一格
    ('$(li)物品展示框视为放在框中的物品，为空则招致事故',
     '$(li)物品框视为放在框中的物品，为空则招致事故'),
    ('$(li)玩家视为其当前手持的物品，优先选取主手',
     '$(li)玩家视为其当前手持的物品；手上没有东西时，视为快捷栏中手持物品右边一格的物品'),
]

# 生成时实际生效的替换表（gen_addon_book.py 会接上附属自己的表）与命中计数
WORDING = list(HAND_WORDING)
_wording_hits = {}


def reword(s):
    """把书页正文里的「另一只手」类说法换成泰拉的实际操作（见 HAND_WORDING）。"""
    for old, new in WORDING:
        if old in s:
            _wording_hits[old] = _wording_hits.get(old, 0) + s.count(old)
            s = s.replace(old, new)
    return s


def check_wording(table):
    """表里每条原文都得命中过；没命中说明上游措辞变了，替换悄悄失效 —— 直接报错。"""
    miss = [old for old, _ in table if old not in _wording_hits]
    if miss:
        sys.exit('「另一只手」替换表里这些原文没有命中（官方中文改了？）：' + ' | '.join(miss))
    print(f'  「另一只手」改写：{len(table)} 条，命中 {sum(_wording_hits.get(old, 0) for old, _ in table)} 处')


# 药水在原版里靠 NBT 区分（minecraft:potion{Potion:"hexcasting:enlarge_grid"}）：原版的明晰 / 蒙翳药水对应本模组的两种药水。
# 延长 / 加强的版本移植版没做，图标都用基础款。
POTION_MAP = {'hexcasting:enlarge_grid': 'Mod:EnlargeGridPotion', 'hexcasting:shrink_grid': 'Mod:ShrinkGridPotion'}


def item_key(src):
    if not src:
        return ''
    first = src.split(',')[0].strip()
    for potion, item in POTION_MAP.items():
        if '"' + potion in first:
            return item
    base = first.split('{')[0].strip()
    return ITEM_MAP.get(base, '')


def recipe_item(recipe):
    """配方 id（hexcasting:focus / hexcasting:staff/oak …）→ 产物物品键。"""
    if not recipe:
        return ''
    ns, path = recipe.split(':', 1) if ':' in recipe else ('hexcasting', recipe)
    if path.startswith('brainsweep/') or path.startswith('decompose_'):
        return ''
    return item_key(ns + ':' + path)


def load_lang():
    zh, en = hexlang.load(LANG_ZH), hexlang.load(LANG_EN)
    missing = []

    def t(key):
        if not key:
            return ''
        if key in zh:
            return zh[key]
        if key in en:
            missing.append(key)
            return en[key]
        # 不是本地化键（原文就是文本）
        return key
    return t, missing


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '') + '"'


def main():
    t, missing = load_lang()
    vanilla = json.load(open(VANILLA, encoding='utf-8'))['ItemID']
    mod_classes = set()
    for f in glob.glob(os.path.join(MOD_ITEMS_DIR, '**/*.cs'), recursive=True):
        for line in open(f, encoding='utf-8-sig'):
            if 'class ' in line:
                mod_classes.add(line.split('class ', 1)[1].split()[0].split(':')[0].strip())
    for v in list(ITEM_MAP.values()) + list(POTION_MAP.values()):
        kind, name = v.split(':')
        ok = name in vanilla if kind == 'Terraria' else name in mod_classes
        if not ok:
            sys.exit(f'ITEM_MAP 指向不存在的物品：{v}')

    cats = []
    for f in sorted(glob.glob(os.path.join(BOOK, 'categories/**/*.json'), recursive=True)):
        c = json.load(open(f, encoding='utf-8'))
        c['id'] = os.path.relpath(f, os.path.join(BOOK, 'categories')).replace(os.sep, '/')[:-5]
        c['entries'] = []
        cats.append(c)
    by_id = {c['id']: c for c in cats}

    skipped = []
    for f in sorted(glob.glob(os.path.join(BOOK, 'entries/**/*.json'), recursive=True)):
        e = json.load(open(f, encoding='utf-8'))
        rel = os.path.relpath(f, os.path.join(BOOK, 'entries')).replace('\\', '/')[:-5]
        if 'flag' in e:
            skipped.append(rel)
            continue
        e['id'] = rel
        by_id[e['category'].split(':', 1)[1]]['entries'].append(e)

    unmapped = set()
    out = []
    w = out.append
    w('// <auto-generated>')
    w('//     本文件由 _tools/gen_book_content.py 生成，**不要手改**：改生成器，然后重新跑脚本。')
    w('//     内容：原版咒法学手册（FallingColors/HexMod v0.11.4，MIT）的结构 + 官方简体中文正文。')
    w('// </auto-generated>')
    w('using System.Collections.Generic;')
    w('')
    w('namespace HexCastingTerraria.Core.Ui;')
    w('')
    w('public static partial class BookContent')
    w('{')
    w('    public static BookDocument Create()')
    w('    {')
    w(f'        var doc = new BookDocument {{ Id = "thehexbook", TitleKey = "item.hexcasting.book", DisplayTitle = {cs(t("item.hexcasting.book"))}, LandingText = {cs(t("hexcasting.landing"))} }};')
    w('        BookCategory c;')
    w('        BookEntry e;')
    w('        BookPage p;')

    n_entries = n_pages = 0
    # 父分类先于子分类（运行时按 ParentId 挂接，顺序只影响可读性）
    for c in sorted(cats, key=lambda c: ('parent' in c, c.get('sortnum', 0))):
        if not c['entries']:
            continue
        icon = item_key(c.get('icon'))
        if not icon:
            unmapped.add(c.get('icon'))
        w('')
        w(f'        c = new BookCategory {{ Id = {cs(c["id"])}, NameKey = {cs(c["name"])}, DisplayName = {cs(t(c["name"]))}, '
          f'IconItem = {cs(icon)}, DescriptionKey = {cs(c["description"])}, DisplayDescription = {cs(t(c["description"]))}, SortNum = {c.get("sortnum", 0)}, '
          f'ParentId = {cs(c.get("parent", "").split(":", 1)[-1])} }};')
        w('        doc.Categories.Add(c);')
        # 原版排序：priority 在前，再按 sortnum，再按名字
        for e in sorted(c['entries'], key=lambda e: (not e.get('priority', False), e.get('sortnum', 0), t(e['name']))):
            n_entries += 1
            icon = item_key(e.get('icon'))
            if not icon:
                unmapped.add(e.get('icon'))
            color = int(e['entry_color'], 16) if 'entry_color' in e else -1
            w(f'        e = new BookEntry {{ Id = {cs(e["id"])}, CategoryId = {cs(c["id"])}, NameKey = {cs(e["name"])}, '
              f'DisplayName = {cs(t(e["name"]))}, IconItem = {cs(icon)}, SortNum = {e.get("sortnum", 0)}, '
              f'Advancement = {cs(e.get("advancement", ""))}, EntryColor = {color}, Priority = {str(e.get("priority", False)).lower()}, '
              f'Secret = {str(e.get("secret", False)).lower()} }};')
            w('        c.Entries.Add(e);')
            for pg in e['pages']:
                if isinstance(pg, str):
                    pg = {'type': 'patchouli:text', 'text': pg}
                n_pages += 1
                emit_page(w, pg, t, unmapped)
                w('        e.Pages.Add(p);')

    w('')
    w('        doc.RebuildIndex();')
    w('        return doc;')
    w('    }')
    w('}')
    check_wording(HAND_WORDING)
    open(OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')

    print(f'书本内容：{sum(1 for c in cats if c["entries"])} 分类 / {n_entries} 条目 / {n_pages} 页 -> {OUT}')
    print(f'  跳过（flag）：{", ".join(skipped)}')
    if missing:
        print(f'  中文缺失、用英文回退的键：{len(missing)} 个，如 {missing[:5]}')
    if unmapped:
        print(f'  没有泰拉对应物、不画图标：{sorted(x for x in unmapped if x)}')


def emit_page(w, pg, t, unmapped):
    typ = pg['type']
    kind = {
        'patchouli:text': 'Text', 'patchouli:link': 'Text', 'patchouli:image': 'Empty',  # image：原版是 MC 世界截图，泰拉侧换成装饰页（保留标题）
       
        'patchouli:crafting': 'Crafting', 'hexcasting:crafting_multi': 'Crafting',
        'patchouli:spotlight': 'Spotlight', 'patchouli:empty': 'Empty',
        'hexcasting:pattern': 'Pattern', 'hexcasting:manual_pattern': 'Pattern',
        'hexcasting:manual_pattern_nosig': 'Pattern', 'hexcasting:brainsweep': 'Brainsweep',
    }[typ]
    title = pg.get('title') or pg.get('heading') or ''
    header = pg.get('header', '')
    if typ == 'hexcasting:pattern' and not header:
        header = 'hexcasting.action.' + pg['op_id']
    text = t(pg.get('text', ''))
    if typ == 'patchouli:link':
        text = text + '$(br2)' + t(pg.get('link_text', ''))
    text = reword(text)
    props = [f'Kind = BookPageKind.{kind}']
    if title or header:
        props.append(f'Title = {cs(t(title or header))}')
    if text:
        props.append(f'Text = {cs(text)}')
    if 'op_id' in pg:
        props.append(f'PatternId = {cs(pg["op_id"])}')
    if pg.get('anchor'):
        props.append(f'Anchor = {cs(pg["anchor"])}')
    if typ in ('hexcasting:pattern', 'hexcasting:manual_pattern'):
        props.append(f'Input = {cs(pg.get("input", ""))}')
        props.append(f'Output = {cs(pg.get("output", ""))}')
    if typ == 'patchouli:crafting':
        props.append(f'RecipeItem = {cs(recipe_item(pg.get("recipe", "")))}')
    if typ == 'hexcasting:crafting_multi':
        props.append('CycleRecipes = true')
    if typ == 'patchouli:spotlight':
        icon = item_key(pg.get('item', ''))
        if not icon:
            unmapped.add(pg.get('item'))
        props.append(f'IconItem = {cs(icon)}')
    w(f'        p = new BookPage {{ {", ".join(props)} }};')
    recipes = []
    if typ == 'patchouli:crafting' and pg.get('recipe2'):
        recipes = [pg['recipe'], pg['recipe2']]
    if typ == 'hexcasting:crafting_multi':
        recipes = pg.get('recipes', [])
    for r in recipes:
        ri = recipe_item(r)
        if ri:
            w(f'        p.RecipeItems.Add({cs(ri)});')
    pats = pg.get('patterns')
    if isinstance(pats, dict):
        pats = [pats]
    for pt in pats or []:
        w(f'        p.Patterns.Add(new PagePattern {{ StartDir = {cs(pt["startdir"])}, Signature = {cs(pt["signature"])}, '
          f'Q = {pt.get("q", 0)}, R = {pt.get("r", 0)} }});')


if __name__ == '__main__':
    main()
