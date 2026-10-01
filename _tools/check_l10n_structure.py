# 语言文件的结构：物品 / 方块的词条必须真的在 Items / Tiles 块里，同一路径不能有两个键，} 后面不能接别的键。
#
# 为什么要常驻（2026-10-01 子代理做种植盆时发现）：zh-Hans / en-US 里 Items 块提前一个 } 关掉了，
# 后面 27 个物品（各种法杖、核心、念珠、卷轴、石板、促动石、导向石……）的词条落在 Mods.HexCastingTerraria.<名字> 下，
# tML 查的是 Mods.HexCastingTerraria.Items.<名字>.DisplayName —— 游戏里这些物品退回英文类名，而「每个物品都有词条」的检查只按名字找，照样全绿。
# en-US 的 Tiles 块里还有几行 「}  键: {」 粘在一起、同一个方块写了两遍（后写的旧名字盖掉新名字）。
#
# 只认本项目用到的写法：name: { 开块、} 收块、key: value 一行一个、''' 多行字符串。
# 退出码 0 = 结构没问题。
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOC = os.path.join(ROOT, 'HexCastingTerraria', 'Localization')
FILES = ['zh-Hans.hjson', 'en-US.hjson']

# 这些字段只该出现在对应的块里（路径 Mods.HexCastingTerraria.<块>.<名字>.<字段>）
FIELD_BLOCKS = {
    'DisplayName': {'Items', 'NPCs', 'Projectiles', 'Buffs', 'Prefixes', 'Tiles'},
    'MapEntry': {'Tiles'},
}


def check(name):
    problems = []
    seen = set()
    stack = []
    in_multiline = False
    for n, raw in enumerate(open(os.path.join(LOC, name), encoding='utf-8-sig').read().split('\n'), 1):
        line = raw.strip()
        if line.startswith("'''"):
            if line.count("'''") == 1:
                in_multiline = not in_multiline
            continue
        if in_multiline or not line or line.startswith('#') or line.startswith('//'):
            continue
        if line.startswith('}'):
            rest = line.lstrip('}').strip()
            for _ in range(line.count('}')):
                if not stack:
                    problems.append(f'{name}:{n} 多出来的 }}')
                else:
                    stack.pop()
            if rest:
                problems.append(f'{name}:{n} }} 后面接了别的键：{rest[:40]}')
            continue
        m = re.match(r'^([\w.@-]+)\s*:', line)
        if not m:
            continue
        key = m.group(1)
        path = tuple(stack + [key])
        if path in seen:
            problems.append(f'{name}:{n} 重复的键 ' + '.'.join(path))
        seen.add(path)
        if re.match(r'^[\w.@-]+\s*:\s*\{\s*$', line):
            stack.append(key)
            continue
        blocks = FIELD_BLOCKS.get(key)
        if blocks and stack[:2] == ['Mods', 'HexCastingTerraria'] and len(stack) == 3:
            problems.append(f'{name}:{n} ' + '.'.join(path) + ' 不在 ' + ' / '.join(sorted(blocks)) + ' 块里（tML 查不到）')
        if key == 'MapEntry' and (len(stack) < 4 or stack[2] != 'Tiles'):
            problems.append(f'{name}:{n} ' + '.'.join(path) + ' 不在 Tiles 块里')
    if stack:
        problems.append(f'{name}: 结尾还有 {len(stack)} 个块没关：' + '.'.join(stack))
    # 代码里登记的快捷键都要有文字（曾经英文的 GiveDevKit 被写成了 Tiles 下的 MapEntry，快捷键设置里显示键名）
    for kb in registered_keybinds():
        if ('Mods', 'HexCastingTerraria', 'Keybinds', kb, 'DisplayName') not in seen:
            problems.append(f'{name}: 快捷键 {kb} 没有 Keybinds.{kb}.DisplayName')
    return problems


def registered_keybinds():
    names = set()
    mod = os.path.join(ROOT, 'HexCastingTerraria')
    for dirpath, dirs, files in os.walk(mod):
        dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
        for f in files:
            if f.endswith('.cs'):
                text = open(os.path.join(dirpath, f), encoding='utf-8-sig').read()
                names.update(re.findall(r'RegisterKeybind\(\s*this\s*,\s*"(\w+)"', text))
    return sorted(names)


def main():
    problems = []
    for f in FILES:
        problems += check(f)
    for p in problems:
        print(p)
    print(f'语言文件结构：{len(problems)} 处问题')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
