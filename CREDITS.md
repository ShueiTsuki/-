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
- 作者：**petrak@（gamma-delta）** 及贡献者
- 许可：**MIT**
- 本模组用到：整个玩法体系、图案数据、材质与实现思路（本模组即其移植版）
- MIT 要求保留版权声明与许可文本，见 `LICENSE-HexMod`（分发前需一并放入）

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

- [ ] 添加 `LICENSE`（CC BY-NC-SA 3.0 全文）
- [ ] 添加 `LICENSE-Patchouli`（CC BY-NC-SA 3.0 全文，随 Patchouli 分发要求）
- [ ] 添加 `LICENSE-HexMod`（MIT 全文 + 原版权行）
- [ ] `description.txt` 里加上上面的署名
- [ ] 确认模组分发渠道（Steam Workshop / GitHub）都为免费
