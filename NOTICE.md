# 署名与来源说明（NOTICE）

## 原始作品

- **名称**：BetterInventoryLogic
- **作者**：BalaurBohemianBroken
- **BepInEx GUID**：`com.balaur.BetterLogic`
- **程序集 / 部署文件名**：`CraftingLogic.dll`（源码工程 `BetterInventoryLogic.csproj` 的 `AssemblyName` 是 `CraftingLogic`）
- **版本串**：源码里写的是 `1.0.4`（`Main.cs:23`），源码目录名却是 `1.0.5`
- **许可证**：**Creative Commons Attribution 4.0 International（CC BY 4.0）**

  ⚠️ 关于许可证的更正：原始作品自带的 `licence.txt` **是 CC BY 4.0，不是 CC BY-SA 4.0**。
  该文件共 396 行、18657 字节，首行即 `Attribution 4.0 International`，全文
  `ShareAlike` / `Attribution-ShareAlike` / `NonCommercial` 出现次数均为 **0**（已逐词检索确认）。
  两者都要求署名，但**只有 BY-SA 才要求衍生作品使用相同许可证**。
  本模组按 CC BY 4.0 的要求做署名；同时仍**自愿**以 CC BY-SA 4.0 发布（见下），
  所以在任何解释下都合规。

## 本作品

- **名称**：OptimisedCraftingSystem
- **BepInEx GUID**：`maple.optimisedcraftingsystem`
- **程序集 / 部署文件名**：`OptimisedCraftingSystem.dll`
- **许可证**：CC BY-SA 4.0（与 CC BY 4.0 兼容，且比原作品更严格）

## 仓库里的两个许可证文件

| 文件 | 内容 | 用途 |
|---|---|---|
| `LICENSE` | **Creative Commons Attribution-ShareAlike 4.0 International** 全文（官方 legalcode 原文，427 行 / 20133 字节） | 本模组（衍生作品）自身采用的许可证 |
| `LICENSE-original.md` | **Creative Commons Attribution 4.0 International** 全文（原作品 `licence.txt` 的逐字节拷贝，396 行 / 18657 字节，首行 `Attribution 4.0 International`） | 记录被改编作品的原始许可条款，便于对照「署名」与「相同方式共享」两项义务 |

说明：原作品采用 CC BY 4.0，它只要求署名、不要求衍生作品沿用同一许可证；本模组**自愿**选择更严格的 CC BY-SA 4.0 发布，因此在任何解释下都满足原作品的许可条件。按 CC BY-SA 4.0 第 3 条，分发本模组或其修改版时需保留本文件与 `LICENSE-original.md` 里的署名与许可信息。

## 做了什么改动

本模组是原作品的**衍生作品**，但代码是**重写**的：

- 新的程序集名、新的 GUID、新的工程文件（原工程的 csproj 里 6 条 `HintPath` 与 PostBuild 全是作者 Linux 机器上的绝对路径，在 Windows 上无法编译，无法直接续作）。
- 原模组的 5 个 Harmony 补丁里，有 3 个是「Prefix 返回 false 整体替换」；本模组**不复制**这些被替换的方法体，改为「前缀开会话 + 后缀收尾」，产物仍由游戏原版代码生成。
- 沿用（并因此署名）的部分：
  - **设置项名称**：`prefer_on_floor`、`prefer_not_container`、`prefer_not_wearable`、`prefer_on_body`、`prefer_high_quality`、`prefer_low_value`、`prefer_low_condition`、`notify_where_stored`、`store_in_containers_first`、`compare_storage_*`、`liquids_can_fill_new_bottles`、`liquids_can_mix`、`compare_liquid_*` —— 沿用是为了**不破坏汉化模组已有的 `gameset*` 键翻译**。
  - **偏好比较链的设计思路**（把若干条偏好按顺序组成比较链、首个非 0 即返回）。
  - **"输出到哪个容器"的候选筛选思路**（`Container.CanHoldItem` + 标签限制）。
- 本模组**独立实现**的部分（详见 `docs\新功能说明与设置.md`）：按性价比的两段式选材与稀缺保护、按帧缓存的物品扫描、独立的材料总览 GUI 与热键、Shift 内容物总量提示、合成前的产物落点预测与手动改容器、液体余量续瓶临时瓶策略、片内 `[合成]` 日志与自检。

## 与原作品不能共存

两者都接管 `Recipe.GetItemsForRecipe` / `GetItemsForRecipeThorough` 等同一批入口，**必须二选一**。
本模组启动时会检测 `com.balaur.BetterLogic` 是否已加载，若已加载会在 BepInEx 日志里报错提醒。

如果只是想要**原作品本身**，请去用原作品，不要用本模组。
