# 版权与许可（CREDITS / LICENSES）

本模组是 **咒法学（Hex Casting）** 的泰拉瑞亚移植版，并在书本 UI 上移植了
**Patchouli** 的部分代码与贴图。两者的授权不同，逐条列在这里。

---

## 本模组的许可

本模组的整体发行适用 **CC BY-NC-SA 3.0**（因为包含 Patchouli 的派生部分，见下）。

- **BY 署名** —— 见本文件的「署名」一节
- **NC 非商业** —— 本模组**免费发布，不用于任何商业用途**：没有广告、没有付费墙、没有付费内容
- **SA 相同方式共享** —— 任何人分发本模组的修改版，必须同样以 CC BY-NC-SA 3.0 授权

> **这条是不可逆的**：只要模组里还有 Patchouli 的派生代码或贴图，
> 就不能改以 MIT / 闭源等形式发布。若要脱离，必须把 Patchouli 派生部分全部重写干净。

---

## 第三方来源与署名

### Patchouli —— CC BY-NC-SA 3.0

- 项目：[VazkiiMods/Patchouli](https://github.com/VazkiiMods/Patchouli)
- 作者：**Vazkii** 及贡献者
- 许可：[CC BY-NC-SA 3.0 Unported](https://creativecommons.org/licenses/by-nc-sa/3.0/)
- 本模组用到的部分：
  - 书本 GUI 的图集贴图（`book_*.png` / `crafting.png` / `page_filler.png` 等）
  - 页面模型与排版逻辑的移植（文字换行、翻页溢出、书签布局、页面类型系统）
- 协议全文见 `LICENSE-Patchouli`（分发前需一并放入）

### 咒法学（Hex Casting / HexMod）—— MIT

- 项目：[FallingColors/HexMod](https://github.com/FallingColors/HexMod)
- 作者：**petrak@（gamma-delta）** 及贡献者（LICENSE 原文版权行：`Copyright © 2021 gamma-delta`）
- 许可：**MIT**
- 本模组用到：整个玩法体系、图案数据、材质与实现思路（本模组即其移植版）
- MIT 要求保留版权声明与许可文本，见 `LICENSE-HexMod`（分发前需一并放入）

### 书本内容与贴图（2026-09-29 起）

| 文件 | 来源 | 许可 |
|---|---|---|
| `Core/Ui/BookContent.Generated.cs` 的正文 | FallingColors/HexMod v0.11.4：`lang/zh_cn.flatten.json5`（官方简体中文）+ `patchouli_books/thehexbook` 结构 | MIT |
| `Client/UI/BookAtlas/patchi_book.png`、`patchi_filler.png` | FallingColors/HexMod v0.11.4：`textures/gui/` | MIT |
| `Client/UI/BookAtlas/crafting.png` | VazkiiMods/Patchouli：`textures/gui/crafting.png` | CC BY-NC-SA 3.0 |
| `Core/Ui/PatchouliRenderer.cs` 的布局常量 | VazkiiMods/Patchouli：`GuiBook*` / `GuiButton*` / `Page*` | CC BY-NC-SA 3.0 |
| `Content/Items/*.png`、`Content/Items/Blocks/*.png`、`Content/Items/States/*.png`、`Content/Tiles/*.png`（除下面列出的保留贴图） | FallingColors/HexMod v0.11.4：`textures/item`、`textures/block`（取自本地的原版 jar，由 `_tools/gen_textures.py` 生成：物品最近邻 ×2，方块排成泰拉图集） | MIT |
| `Content/Items/AncientLoot.cs` 的远古杂件预设咒术 | FallingColors/HexMod：`AddHexToAncientCypherFunc.LOOT_HEXES` | MIT |

生成器：`_tools/gen_book_content.py`（原始文件放在仓库外的 `D:\DeepSeekHarness\hexsrc_assets`）。

**保留的移植版自绘贴图**（原版对应的是 MC 原生物品 / 方块，Mojang 的贴图不能用；或者是移植版自创的东西）：
紫水晶碎片、母岩、紫水晶芽 / 簇、构筑的方块 / 光源、紫晶烛台、挂轴框。

### 泰拉瑞亚 / tModLoader

- Terraria 与 tModLoader 的**美术与代码资源不在本仓库内、也不随本模组分发**。
  本模组只通过 tModLoader 的公开 API 引用它们。
- `LICENSE` 中的 CC 协议**不适用于**泰拉瑞亚本体或 tModLoader。

---

## 署名（可直接用于 Workshop 页面）

> 本模组是 **咒法学（Hex Casting）**（作者 petrak@ / FallingColors，MIT 许可）的泰拉瑞亚移植版。
> 书本界面移植自 **Patchouli**（作者 Vazkii，CC BY-NC-SA 3.0 许可）。
> 本模组免费发布、不用于商业用途，并以 CC BY-NC-SA 3.0 授权。
> Terraria 与 tModLoader 的资源不随本模组分发。

---

## TODO（发布前必须完成）

- [x] 添加 `LICENSE.txt`（CC BY-NC-SA 3.0 全文）
- [x] 添加 `LICENSE-Patchouli.txt`（CC BY-NC-SA 3.0 全文，随 Patchouli 分发要求）
- [x] 添加 `LICENSE-HexMod.txt`（MIT 全文 + 原版权行 `Copyright © 2021 gamma-delta`）
- [x] `description.txt` 里加上上面的署名
- [x] 版权声明 `HexCastingTerraria/NOTICE.txt`（非官方移植声明、原作者版权、许可、异议联系）
- [ ] NOTICE.txt 里的联系方式待填写
- [ ] 确认模组分发渠道（Steam Workshop / GitHub）都为免费
