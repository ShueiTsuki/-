# 模组配置的文字：每个配置项、标题、枚举值在中文和英文里都要有，而且键要对得上 tML 实际查的键。
#
# 为什么要常驻（2026-10-01 用户截图发现）：配置文字文件名 {语言}_Mods.HexCastingTerraria.Configs.hjson
# 已经带了前缀 Mods.HexCastingTerraria.Configs，文件里却又包了一层 Mods: { HexCastingTerraria: { Configs: ...，
# 于是所有键都多了一截、一个都对不上 —— 游戏里整个配置页退回英文默认名（Show Media Ring…），而没有任何检查会红。
#
# tML 查的键（ConfigManager.GetDefaultLocalizationKey）：
#   配置类        Mods.<模组>.Configs.<类名>.DisplayName（以及 .Headers.<标题名>）
#   成员          Mods.<模组>.Configs.<声明它的类名>.<成员名>.Label / .Tooltip
#   嵌套类 / 枚举 Mods.<模组>.Configs.<类型名>.Tooltip；枚举值 <枚举名>.<值名>.Label
# 退出码 0 = 中英文都齐全。
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(ROOT, 'HexCastingTerraria')
LOC = os.path.join(MOD, 'Localization')


def hjson_keys(path):
    """极简 hjson 读取：只认本项目用到的写法（name: { 开块、} 收块、key: value 一行一个）。"""
    text = open(path, encoding='utf-8-sig').read()
    keys, stack = set(), []
    for raw in text.split('\n'):
        line = raw.strip()
        if not line or line.startswith('#') or line.startswith('//'):
            continue
        m = re.match(r'^([\w.@-]+):\s*\{\s*$', line)
        if m:
            stack.append(m.group(1))
            continue
        if line == '}':
            if stack:
                stack.pop()
            continue
        m = re.match(r'^([\w.@-]+):\s*(.*)$', line)
        if m:
            keys.add('.'.join(stack + [m.group(1)]))
    return keys


def required_keys():
    files = glob.glob(os.path.join(MOD, 'Config', '*.cs')) + glob.glob(os.path.join(MOD, 'Addons', '*', 'Game', '*Options.cs'))
    enum_files = glob.glob(os.path.join(MOD, '**', '*.cs'), recursive=True)
    enums = {}
    for f in enum_files:
        if os.sep + 'obj' + os.sep in f or os.sep + 'bin' + os.sep in f:
            continue
        for m in re.finditer(r'public enum (\w+)\s*\{([^}]*)\}', open(f, encoding='utf-8-sig').read()):
            body = re.sub(r'///[^\n]*|//[^\n]*', '', m.group(2))
            enums[m.group(1)] = [x.split('=')[0].strip() for x in body.split(',') if x.strip()]
    need, used_types = set(), set()
    for f in files:
        src = open(f, encoding='utf-8-sig').read()
        for cm in re.finditer(r'public sealed class (\w+)(\s*:\s*ModConfig)?', src):
            cls, is_config = cm.group(1), bool(cm.group(2))
            if is_config:
                need.add(f'{cls}.DisplayName')
            else:
                need.add(f'{cls}.Tooltip')
            for h in re.finditer(r'\[Header\("(\w+)"\)\]', src):
                need.add(f'{cls}.Headers.{h.group(1)}')
            for pm in re.finditer(r'public (?!static|override|const)([\w.<>?]+) (\w+) \{ get; set; \}', src):
                typ, name = pm.group(1), pm.group(2)
                need.add(f'{cls}.{name}.Label')
                need.add(f'{cls}.{name}.Tooltip')
                used_types.add(typ.split('.')[-1].rstrip('?'))
    for t in used_types:
        if t in enums:
            need.add(f'{t}.Tooltip')
            for v in enums[t]:
                need.add(f'{t}.{v}.Label')
    return need


need = required_keys()
bad = False
for lang in ['zh-Hans', 'en-US']:
    path = os.path.join(LOC, f'{lang}_Mods.HexCastingTerraria.Configs.hjson')
    have = hjson_keys(path)
    wrapped = [k for k in have if k.startswith('Mods.')]
    missing = sorted(need - have)
    print(f'{lang}：需要 {len(need)} 个键，缺 {len(missing)} 个' + (f'，多包了一层 Mods.（{len(wrapped)} 个键）' if wrapped else ''))
    for k in missing[:20]:
        print('  缺 ' + k)
    bad = bad or bool(missing) or bool(wrapped)
sys.exit(1 if bad else 0)
