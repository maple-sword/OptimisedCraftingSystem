# Optimised Crafting System（原 BetterCraftingLogic）

《Casualties: Unknown Demo》的合成辅助模组。基于 **BetterInventoryLogic**（作者 BalaurBohemianBroken，CC BY 4.0）重写，
把原模组的「合成后补救」改成「合成前先算清楚」：材料用哪件、会消耗多少、产物放到哪、液体倒进哪个瓶子，**在点合成之前就告诉你，并且可以手动改**。

- BepInEx 5（Mono）插件，GUID `maple.optimisedcraftingsystem`，程序集 `OptimisedCraftingSystem.dll`
- **10-17 08:43:52 版起由 BetterCraftingLogic 改名而来**：程序集/DLL、命名空间、GUID、设置键前缀（`bcl.*` → `ocs.*`）全部换新。
  ⚠️ 因为设置键换了名字，改名后第一次启动时本模组的设置会**回到默认值**（游戏 `settings.json` 里旧的 `bcl.*` 条目不再被读取，留在文件里无害）。
- 与原模组 **不能同时安装**（启动时会在日志里报错提醒）
- 署名与许可证说明见 [NOTICE.md](NOTICE.md)；许可证全文见 [licence.txt](licence.txt)

## 安装

1. 把 `OptimisedCraftingSystem.dll` 放进 `Casualties Unknown Demo\BepInEx\plugins\`
2. 如果之前装过原模组，删除 `plugins\CraftingLogic.dll`；如果装过旧名字的本模组，把 `plugins\BetterCraftingLogic.dll` 改名成 `BetterCraftingLogic.dll.disabled` 或直接移出 `plugins\`（**两个名字同时存在会各自加载一份，补丁重复生效**）
3. 启动游戏，日志里应出现：

```
Loading [Optimised Crafting System 1.0.0]
OptimisedCraftingSystem 1.0.0 已加载（Harmony 补丁已应用）。
[Loc] 界面语言判定为 中文。
[自检] 语言=中文 设置项=36 项 热键=M 字体=Microsoft YaHei
[自检] 配方=278 个 物品类型=371 种 玩家=在场
[自检] Recipe.GetItemsForRecipe ← 本模组：maple.optimisedcraftingsystem/prefix(p200)；其它模组：com.HerobrinesProsthetics/prefix(p400)
[自检] RecipeResult.SpawnResult ← 本模组：无；其它模组：net.cucorelib/prefix(p400)
[实测] 材料总览窗口：已打开
[实测] C 键面板消耗块（帆布背包）：需要 1. 可切割 → 整件消耗 / 将存放于 帆布背包
```

`[自检]` 会把 14 个目标方法（3 条关键、11 条提示级）的补丁归属逐条打印出来 —— 本模组用哪个优先级、别的模组在同一入口挂了什么，方便排查抢入口的问题；
「设置项=36 项」= 35 个开关 + 1 个热键，与设置界面里的数量一致。

日志里另有 `[合成]`（每次合成的选材 / 消耗量 / 落点）与 `[实测]`（C 键消耗块、M 窗口、Shift 容器总量、手动选材、恢复默认这几项交互各留一条证据行，同一个 key 只写一次）—— 见 [docs/改进前后对比.md](docs/改进前后对比.md) 的「四点二」。

## 用法

| 操作 | 效果 |
|---|---|
| 打开 **C** 键合成界面 | 配方说明下多一块「需要」：每槽的消耗量（`120 mL` / `整件消耗` / `耐久 -12%（还能用 8 次）`），末行写产物去向「将存放于 帆布背包（剩余 3.2 kg）」。**液体槽位显示成「液体名（容器名）」**（例 `净水（水壶）`）——报的是这一槽实际会倒出来的那层液体，容器名只在括号里 |
| 合成界面里的紧凑覆盖层 | 同一套信息的紧凑版，贴在 **C 键面板右侧的空白处**（不再压住面板内容）。**点材料名**展开该槽的候选材料表、**点「将存放于」那行**展开容器/瓶子候选表，点一项即生效；「自动（推荐）」交还给算法。液体配方的候选表会**列出身上、容器里乃至手边地上的每一个瓶子**（连内容物与余量），自动不会用的那些也列出来并写明原因、整行置灰点不动（不能靠手选绕过混装/空瓶/容量这些设置）。**按住顶部标题条可以把它拖到任意位置，双击标题条复位**（没挪过时自动贴右侧） |
| 按 **M**（可在设置→输入改） | 材料总览窗口：同 id 物品堆叠成一行（数量 / 位置 / 提供的性质 / 耐久 / 价值 / 能用它的配方数），点一行看详情与「使用该性质的配方」，可直接跳配方。**装着液体的容器不堆叠**：两个瓶子只要里面的液体（种类或余量）不一样就各占一行，行名写成 `水壶（生化流体 500 mL）`，只有内容物完全一样的才并成 `水壶 ×2`；**收藏的容器不接液体**（见上面一行） |
| **Shift** + 悬停容器 | 物品提示追加内容物明细、内容物总重、剩余空间（用的是游戏自己的「展开描述」键位，默认左 Shift）；同样按「液体 id + 余量」给瓶子分行，不把不同液体的瓶子并成 `×2` |
| 自动选材（不必手动指定） | **同类液体优先用临时瓶里的**：同一槽位既有临时瓶里的某种液体、又有真瓶子里的同一种液体时，自动挑临时瓶那份（它 5 分钟后连瓶带液体一起消失，本来就注定要倒掉）——手动指定真瓶子仍然照办 |
| 收藏容器（给瓶子打星标） | **打了星标的容器不会被倒进液体**：自动规划跳过它，候选表里整行置灰点不动、写明「已收藏（本模组不往里倒液体）」；想让它继续接液体就取消星标或关掉设置 `ocs.liquid.skip_favorited`（默认开） |
| 被消耗的容器（整件消耗槽位） | **瓶里的液体不丢**：需要整件消耗液体容器时优先挑液体最少的瓶子，真要消耗就先把液体装进现场做的临时瓶（与原版一致 300 秒后连瓶带液体消失）；**容器里的东西也不丢**：装有物品的容器现在能当材料，内容物先摘出来保住、新产物出现时装进去，装不下就掉在脚边并提示。三个开关 `ocs.craft.prefer_empty_bottle` / `ocs.craft.rescue_liquid` / `ocs.craft.container_contents` 默认全开，关掉即回原版 |
| 合成完成后 | 提示「已存放：帆布背包、右手」（跟随语言，关掉 `notify_where_stored` 就不提示） |

## 设置

全部 36 项都在游戏自己的「设置」界面里（35 个开关在「游戏」页、热键在「输入」页），
存进游戏自己的 `settings.json`，**不用** BepInEx 的 `.cfg`。中英文说明都会按界面语言注入。

几项值得注意的：

| 设置 | 默认 | 作用 |
|---|---|---|
| `ocs.pick.smart` | 开 | 按性价比选材（关掉就退回原模组那套偏好链） |
| `ocs.pick.reserve_scarce` | 开 | 稀缺保护：唯一能提供某性质的工具不会被当耗材烧掉 |
| `prefer_*` / `compare_*` | 全开 | 原模组的偏好开关，名称沿用（不破坏已有汉化）；只在成本接近（差距 ≤25%）时才起作用 |
| `ocs.store.predict` | 开 | 合成前预测并显示产物落点 |
| `ocs.craft.*` 三项 | 全开 | 整件消耗的容器：优先空瓶 / 救出瓶内液体 / 内容物跟着材料走（见上一节） |
| `ocs.store.honor` | 开 | 真的按预测执行（关掉只显示不干预） |
| `ocs.liquid.tempbottle` | 开 | 装不下时现场做临时瓶；临时瓶的生命周期**与原版一致**——装着液体的 5 分钟后连瓶带液体一起销毁，只有一滴都没装进去的空瓶 30 秒后清掉 |
| `ocs.liquid.skip_favorited` | 开 | 收藏（打星标）的容器不接液体：候选表里整行置灰点不动；关掉则收藏的瓶子照样能装 |
| `ocs.tip.container_totals` | 开 | Shift 悬停显示内容物总量 |
| `ocs.window.floor` | 开 | 材料总览是否包含 10 米内地面可捡的物品 |

完整表格见 [docs/新功能说明与设置.md](docs/新功能说明与设置.md)。

## 文档

| 文档 | 内容 |
|---|---|
| [docs/功能完整报告.md](docs/功能完整报告.md) | 原模组 `CraftingLogic.dll` 已实现功能的完整报告（5 个补丁、选材/存放/液体算法、23 个设置项与 38 条语言键、13 条缺陷），每处都带 `文件.cs:行号` |
| [docs/改动对照与Bug修复.md](docs/改动对照与Bug修复.md) | 旧→新的改动统计与 28 条改动/bug 修复对照表（文末附《成员行号索引》） |
| [docs/新功能说明与设置.md](docs/新功能说明与设置.md) | 新功能逐条说明（含液体容器不堆叠、同类液体优先临时瓶、收藏容器不接液体）+ 36 项设置全表 + 测试清单（T1–T19）+ 已知限制 |
| [docs/改进前后对比.md](docs/改进前后对比.md) | **改进前后对照大表**（18 个场景）+ 原模组作者 TODO 对照 + 游戏内实测证据 |
| [docs/流体存放逻辑重做方案.md](docs/流体存放逻辑重做方案.md) | 流体逻辑重做的 6 个方案（A–F）与推荐组合（**方案性质，未实现全部**） |
| [docs/自定义消耗偏好方案.md](docs/自定义消耗偏好方案.md) | 自定义消耗偏好的可行性验证与 4 个方案（**方案性质，未实现**） |
| [docs/_research/合成界面UI.md](docs/_research/合成界面UI.md) | 游戏合成界面的结构与可插入点调研 |
| [docs/_research/性质与品质系统.md](docs/_research/性质与品质系统.md) | 11 个性质 id、品质与消耗公式的取证 |

## 已知限制

- 手选的容器/材料不写盘，重启后回到「自动」；但自动选材每次都会重新算。
- 液体槽位显示的是「**这一槽会倒出来的那层液体**（容器名）」；本模组认不出的第三方液体（例 `Blood O+`）直接显示它的 id 而不是中文名。
- 装不下的液体若关掉 `ocs.liquid.tempbottle`，会退回原版行为（留在原版挑中的瓶子里）。
- 收藏（打星标）的容器不接液体（`ocs.liquid.skip_favorited`，默认开）：若所有能用的瓶子都被收藏、临时瓶又关着，这部分液体将流失（合成前的预测行会说明去向）。
- IMGUI 面板依赖系统里的中文字体（Microsoft YaHei / 微软雅黑 / SimHei / SimSun / Noto Sans CJK / Arial Unicode MS），一个都找不到时会显示方框（`[自检]` 会打印实际拿到的字体名）。
- 与 `net.cucorelib` 在 `Body.AutoPickUpItem` 上都有前缀，CUCoreLib 的优先级更高（p400 > p200），个别物品的自动拾取仍可能由它先处理。
- 装了 `prosthetics`（义体）且身上有**钛合金手**时，含「锤击」槽的配方由它接管（用手代替锤子）；此时本模组的选材不参与，C 键「需要」块里那一行也不代表实际消耗。详见 [docs/改进前后对比.md](docs/改进前后对比.md) 第四点五节。
- 与 `QoL Unknown`（`org.bepinex.plugins.qol_unknown.modoptionsmenu`）在设置界面的「恢复默认」上存在真实冲突：它的前缀会 `return false` 接管这次点击，而 Harmony 在前缀返回 false 时会连同其余前缀一起跳过，本模组原来的标记因此永远置不上（表现为「点了恢复默认、本模组 32 项纹丝不动」）。19:32:32 版起已修：标记前缀提到 `Priority.HigherThanNormal`(500) 抢在它之前，并新增后缀兜底 `ModSettings.ForceReset`，两者可共存。取证见 [docs/改进前后对比.md](docs/改进前后对比.md) 第四点五节 与 [docs/改动对照与Bug修复.md](docs/改动对照与Bug修复.md) 第 18 条。
- 装了 `Crafting Tweaks`（`kef.casualtiesunknown.craftingtweaks`）时，它给配方图标加「(库存/需求)」计数的后缀会调用原版 `RecipeItem.DoesUseItemType`，而该方法对空物品/未注册液体不做 null 检查 —— 19:42 那轮真机实测在本模组的刷新路径上触发过一次 `NullReferenceException`。19:48:58 版起本模组给它挂了一条 `[HarmonyFinalizer]` 兼容垫片：只吞这一条异常（结果按「不满足该槽位」处理）并写一行 `[Compat] …` 诊断日志，不再让界面刷新中断；代价是万一原版判定真的出错，库存计数会偏低而不是崩溃。详见 [docs/改动对照与Bug修复.md](docs/改动对照与Bug修复.md) §2 第 19 条与 §4.5。

## 许可

本模组是 BetterInventoryLogic 的衍生作品，原创作者 BalaurBohemianBroken，原作品采用 **CC BY 4.0**。
本模组按 CC BY-SA 4.0 发布（与 CC BY 4.0 兼容）。详见 [NOTICE.md](NOTICE.md) 与 [licence.txt](licence.txt)。
