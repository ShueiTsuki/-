# 从每个附属的 addon.json 生成 ADDONS.generated.md：附属一览 + 每个附属的「功能 -> 文件」表。
#
# 为什么要生成：以后要适配几十个附属，「某个功能在哪个文件」必须一眼能查到，
# 而手写的对照表一定会和代码对不上。清单（addon.json）由 check_arch 断言保证和代码一致，这里只负责排版。
#
# 用法：python _tools/gen_addons_index.py        （run_all.ps1 每次都会跑）
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(TOOLS)
ADDONS = os.path.join(REPO, 'HexCastingTerraria', 'Addons')
OUT = os.path.join(REPO, 'ADDONS.generated.md')

SWITCH = {
    'both': '模组配置「咒法学 · 附属兼容（服务端）」，需要重载；联机由开服的人决定',
    'client': '模组配置「咒法学 · 附属兼容（客户端）」，随时改；只影响自己',
}


def main():
    manifests = []
    for name in sorted(os.listdir(ADDONS)):
        path = os.path.join(ADDONS, name, 'addon.json')
        if os.path.isfile(path):
            with open(path, encoding='utf-8') as f:
                manifests.append((name, json.load(f)))

    lines = [
        '# 附属一览（自动生成，勿手改）',
        '',
        '由 `_tools/gen_addons_index.py` 从各附属的 `addon.json` 生成。规矩与计划见 [ADDONS.md](ADDONS.md)。',
        '「已做」= 功能表里列了本模组的文件；没列文件的功能还没做。',
        '',
        '| 附属 | 上游版本 | 许可 | 开关 | 功能已做 |',
        '|---|---|---|---|---|',
    ]
    for name, m in manifests:
        feats = m.get('features', [])
        done = sum(1 for x in feats if x.get('files'))
        up = m['upstream']
        lines.append(f"| [{m['name']}](#{m['id']}) | {up['version']} | {up['license']} | {SWITCH.get(m['side'], m['side'])} | {done} / {len(feats)} |")

    for name, m in manifests:
        up = m['upstream']
        lines += [
            '',
            f'<a id="{m["id"]}"></a>',
            f"## {m['name']}",
            '',
            f"上游：{up['repo']}（{up['version']}，{', '.join(up['authors'])}）；本地源码 `{up['src']}`；",
            f"代码目录 `HexCastingTerraria/Addons/{name}/`；图案 / iota 命名空间 `{m['namespace']}`。",
            '',
            '| 功能 | 本模组文件 | 上游文件（相对 `' + up.get('src_root', '') + '`） |',
            '|---|---|---|',
        ]
        for x in m.get('features', []):
            files = '<br>'.join(f'`{f}`' for f in x.get('files', [])) or '（待做）'
            ups = '<br>'.join(f'`{f}`' for f in x.get('upstream', []))
            lines.append(f"| {x['name']} | {files} | {ups} |")

    text = '\n'.join(lines) + '\n'
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    total = sum(len(m.get('features', [])) for _, m in manifests)
    done = sum(1 for _, m in manifests for x in m.get('features', []) if x.get('files'))
    print(f'已写出 ADDONS.generated.md：{len(manifests)} 个附属，功能 {done} / {total} 已做')
    return 0


if __name__ == '__main__':
    sys.exit(main())
