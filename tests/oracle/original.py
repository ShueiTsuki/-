"""原版咒法学的标准答案机：编译测试模组、在没有界面的 MC 服务器里跑原版，导出注册表或用例结果。

用法：
  python tests/oracle/original.py build                 编译 mc/ 下的测试模组，放进运行目录的 mods/
  python tests/oracle/original.py dump                  导出原版运行时的图案注册表 → golden/registry.json
  python tests/oracle/original.py run CASES OUT         用原版执行用例文件 CASES，结果写到 OUT（以 .gz 结尾就压缩，入库的标准答案都压缩）

运行目录（MC 本体、依赖、世界存档，都不进仓库）默认是 D:/DeepSeekHarness/oracle_run，可用环境变量 HEXORACLE_RUN 改。
里面要有：minecraft-1.20.1.jar（MC 本体）、classpath.txt（Fabric 加载器和库）、mods/（咒法学 0.11.4 和依赖）、
eula.txt、server.properties。搭法见 tests/oracle/README.md。
"""
import glob
import gzip
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
RUN = os.environ.get('HEXORACLE_RUN', 'D:/DeepSeekHarness/oracle_run')
GAME_JAR = os.path.join(RUN, 'minecraft-1.20.1.jar')
MC_INTERMEDIARY = os.path.join(RUN, '.fabric', 'remappedJars', 'minecraft-1.20.1-0.19.5', 'server-intermediary.jar')
HEX_JAR = os.path.join(RUN, 'mods', 'hexcasting-fabric-1.20.1-0.11.4.jar')
ORACLE_JAR = os.path.join(RUN, 'mods', 'hexoracle.jar')


def runtime_classpath():
    return open(os.path.join(RUN, 'classpath.txt'), encoding='utf-8').read().strip()


def nested_jars(outer, names):
    """把模组里内嵌的库 jar（META-INF/jars/）解出来，编译时要用。"""
    out_dir = os.path.join(RUN, 'compile_libs', os.path.basename(outer))
    os.makedirs(out_dir, exist_ok=True)
    found = []
    with zipfile.ZipFile(outer) as z:
        for n in z.namelist():
            base = os.path.basename(n)
            if n.startswith('META-INF/jars/') and any(base.startswith(p) for p in names):
                target = os.path.join(out_dir, base)
                if not os.path.exists(target):
                    with z.open(n) as src, open(target, 'wb') as dst:
                        shutil.copyfileobj(src, dst)
                found.append(target)
    return found


def build():
    if not os.path.exists(MC_INTERMEDIARY):
        sys.exit('还没有中间名的 MC 本体：先空跑一次服务器（python original.py dump 会自动跑），Fabric 会生成它')
    libs = [p for p in runtime_classpath().split(';') if not p.endswith('minecraft-1.20.1.jar')]
    libs += nested_jars(glob.glob(os.path.join(RUN, 'mods', 'fabric-api-*.jar'))[0],
                        ['fabric-api-base', 'fabric-lifecycle-events-v1', 'fabric-events-interaction-v0'])
    libs += nested_jars(glob.glob(os.path.join(RUN, 'mods', 'fabric-language-kotlin-*.jar'))[0], ['kotlin-stdlib-2'])
    cp = ';'.join([MC_INTERMEDIARY, HEX_JAR] + libs)
    sources = glob.glob(os.path.join(HERE, 'mc', 'src', '**', '*.java'), recursive=True)
    with tempfile.TemporaryDirectory() as classes:
        r = subprocess.run(['javac', '--release', '17', '-encoding', 'UTF-8', '-nowarn', '-cp', cp, '-d', classes] + sources)
        if r.returncode != 0:
            sys.exit('编译失败')
        with zipfile.ZipFile(ORACLE_JAR, 'w', zipfile.ZIP_DEFLATED) as z:
            z.write(os.path.join(HERE, 'mc', 'fabric.mod.json'), 'fabric.mod.json')
            for root, _, files in os.walk(classes):
                for f in files:
                    full = os.path.join(root, f)
                    z.write(full, os.path.relpath(full, classes).replace(os.sep, '/'))
    print('测试模组 ->', ORACLE_JAR)


def launch(mode, out_path, cases_path=None, timeout=900):
    """起服务器跑一次；测试模组做完会自己关服。成败看输出文件里的 ok，不看进程退出码。"""
    if os.path.exists(out_path):
        os.remove(out_path)
    args = ['java', '-Xmx3G', '-Dfabric.gameJarPath=' + GAME_JAR, '-Dhexoracle.mode=' + mode,
            '-Dhexoracle.out=' + os.path.abspath(out_path)]
    if cases_path:
        args.append('-Dhexoracle.in=' + os.path.abspath(cases_path))
    args += ['-cp', runtime_classpath(), 'net.fabricmc.loader.impl.launch.knot.KnotServer', 'nogui']
    with open(os.path.join(RUN, 'last_run.log'), 'w', encoding='utf-8', errors='replace') as log:
        subprocess.run(args, cwd=RUN, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
    if not os.path.exists(out_path):
        sys.exit('没有输出文件，看 ' + os.path.join(RUN, 'last_run.log'))
    doc = json.load(open(out_path, encoding='utf-8'))
    if not doc.get('ok'):
        sys.exit('原版那边出错：' + str(doc.get('error'))[:2000])
    return doc


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    if cmd == 'build':
        build()
    elif cmd == 'dump':
        out = os.path.join(HERE, 'golden', 'registry.json')
        os.makedirs(os.path.dirname(out), exist_ok=True)
        doc = launch('dump', out + '.raw')
        actions = sorted(doc['actions'], key=lambda a: a['id'])
        json.dump({'source': 'hexcasting-fabric-1.20.1-0.11.4', 'actions': actions}, open(out, 'w', encoding='utf-8'),
                  ensure_ascii=False, indent=1)
        os.remove(out + '.raw')
        print('注册表：', len(actions), '个图案 ->', out)
    elif cmd == 'run':
        out = sys.argv[3]
        raw = out[:-3] if out.endswith('.gz') else out
        doc = launch('run', raw, sys.argv[2])
        if out.endswith('.gz'):
            with open(raw, 'rb') as src, gzip.open(out, 'wb', compresslevel=9) as dst:
                shutil.copyfileobj(src, dst)
            os.remove(raw)
        bad = [r['id'] for r in doc['results'] if 'exception' in r]
        print('用例：', len(doc['results']), '个；原版这边抛异常的', len(bad), '个', bad[:5])
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == '__main__':
    main()
