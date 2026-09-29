# tModLoader 模组开发环境（本机已配置完成）

本目录是你（MSI-PC）的泰拉瑞亚 tModLoader 模组开发工作区。环境已经装好并**实测编译通过**。

## 目录结构

```
D:\DeepSeekHarness\tmod\
├── .vscode\                  VS Code 工作区配置（对下面所有子工程生效）
│   ├── settings.json         C# 扩展 / 编辑器设置
│   ├── tasks.json            Ctrl+Shift+B 可用的任务：同步、编译、看日志
│   ├── launch.json           F5 附加调试 tModLoader 进程
│   └── extensions.json       推荐扩展
├── MyFirstMod\               一个完整可编译的示例模组工程（可复制多份做新模组）
│   ├── MyFirstMod.csproj     工程文件，文件名必须与文件夹名一致
│   ├── Content\Items\        内容代码
│   ├── sync-to-modSources.ps1 一键同步进游戏 ModSources
│   └── README.md             详细环境说明、验证结果与踩坑记录
└── .gitignore
```

## 新模组怎么建

1. 复制 `MyFirstMod\` 整个文件夹，改名成你的模组名（例如 `SuperSword\`）
2. 把里面的 `MyFirstMod.csproj` 改名为 `SuperSword.csproj`，并改这两个属性：

```xml
<AssemblyName>SuperSword</AssemblyName>
<RootNamespace>SuperSword</RootNamespace>
```

3. 跑同步脚本（或在 VS Code 里执行任务「同步到 ModSources」）：

```powershell
powershell -ExecutionPolicy Bypass -File .\SuperSword\sync-to-modSources.ps1
```

4. 编译（VS Code `Ctrl+Shift+B`，或命令行）：

```powershell
dotnet build "$env:USERPROFILE\Documents\My Games\Terraria\tModLoader\ModSources\SuperSword\SuperSword.csproj"
```

5. 产物 `SuperSword.tmod` 会自动落到 `Documents\My Games\Terraria\tModLoader\Mods\`，
   启动 tModLoader 后在「模组」列表里勾选启用即可。

## 关键注意

- **文件夹名 = 模组内部名**。tModLoader 用工程文件夹名判定模组名，光改 csproj 名字不够。
- 编译必须在 `ModSources\<模组名>\` 里进行（同步脚本会保证这点），在别处编译会产出名字错误的 `.tmod`。
- 详细环境说明、命令行用法、踩坑清单见 `MyFirstMod\README.md`。
