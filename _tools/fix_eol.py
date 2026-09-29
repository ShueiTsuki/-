"""提交前把改过的文件的换行风格对齐到仓库里的版本（HEAD，或 --base 指定的提交）。

为什么需要：工作区里不少文件是 CRLF（曾以 autocrlf=true 检出），而仓库里存的是 LF；
仓库现在 autocrlf=false，改过的文件一提交就把 CRLF 带进去，diff 变成整文件改动。

用法：
  python _tools/fix_eol.py              # 对齐所有已修改 / 已暂存的文件到 HEAD
  python _tools/fix_eol.py --base REV   # 以 REV 为准（修正已经提交进去的翻转）
新文件（base 里没有）保持原样。只处理「base 是纯 LF、工作区含 CRLF」和「base 是纯 CRLF、工作区含裸 LF」两种情况。
"""
import subprocess
import sys

ROOT = subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True).stdout.strip()


def git(*args):
    return subprocess.run(['git', *args], capture_output=True, cwd=ROOT)


def main():
    base = 'HEAD'
    if '--base' in sys.argv:
        base = sys.argv[sys.argv.index('--base') + 1]
    names = set(git('diff', '--name-only', base).stdout.decode().split())
    names |= set(git('diff', '--name-only', '--cached', base).stdout.decode().split())
    fixed = 0
    for name in sorted(names):
        blob = git('cat-file', '-p', f'{base}:{name}')
        if blob.returncode != 0:
            continue   # 新文件
        old = blob.stdout
        path = f'{ROOT}/{name}'
        try:
            cur = open(path, 'rb').read()
        except FileNotFoundError:
            continue
        old_crlf, old_lf = old.count(b'\r\n'), old.count(b'\n')
        if old_lf == 0:
            continue
        if old_crlf == 0 and b'\r\n' in cur:
            open(path, 'wb').write(cur.replace(b'\r\n', b'\n'))
        elif old_crlf == old_lf and cur.count(b'\r\n') != cur.count(b'\n'):
            open(path, 'wb').write(cur.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
        else:
            continue
        fixed += 1
        print(f'  换行对齐到 {base}：{name}')
    print(f'共 {fixed} 个文件')


if __name__ == '__main__':
    main()
