# 合成界面（CraftPanel）UI 内部结构调研报告

- 目标游戏：《Casualties: Unknown Demo》（Unity 2022.3.62f3 / Mono / BepInEx 5）
- 源码依据（权威）：`D:\maple_sword\workspace\_GameSource\Assembly-CSharp\`（dnSpy 导出）
  - ⚠️ 该目录有 dnSpy 重复副本：`SpeedType.cs` / `UISoundType.cs` / `DefibInfo.cs` / `Item.cs` 内含**整份 `PlayerCamera` 类副本**（行号相同）。报告一律引用 `PlayerCamera.cs`（3150 行）。
- 资产依据（只读 UnityPy 解析，未改动游戏文件）：`CasualtiesUnknown_Data\level1`（场景）、`resources.assets`（预制体）；脚本类名经 `globalgamemanagers.assets` 的 MonoScript 映射确认：`354=UITooltip`、`653=Button`、`1615=UILocalizer`、`1804=TextMeshProUGUI`、`1878=Image`。
- 本报告为只读调研，未修改/移动任何文件。

---

## 1. 合成界面的开关状态与打开键

**结论**
1. 「合成界面是否打开」**没有布尔字段**，唯一状态是 `PlayerCamera.main.craftingPanel.activeSelf`（字段：`PlayerCamera.cs:329`）。**未找到** `craftScreenOpen` / `openedCraft` / `crafting`(bool) 之类字段。
2. 开与关是**同一个方法** `OpenCraftScreen()`（`PlayerCamera.cs:492-505`）；**未找到** `CloseCraftScreen`——关闭＝再调一次它。
3. C 键默认值在 `Settings.cs:473-481`；读取点在 `PlayerCamera.cs:1175`，bind 名字符串就是 `"craft"`。

```csharp
// PlayerCamera.cs:329-333（合成相关字段起点）
public GameObject craftingPanel;
public GameObject craftButton;
public int selectedRecipe;
```
```csharp
// PlayerCamera.cs:492-505（唯一开关方法）
public void OpenCraftScreen()
{
    if (!woundView.activeSelf)
    {
        PlayUISound(craftingPanel.activeSelf ? UISoundType.Close : UISoundType.Click);
        craftingPanel.SetActive(!craftingPanel.activeSelf);
        radialOpen = false;
        if (craftingPanel.activeSelf)
        {
            craftingPanel.transform.localScale = Vector3.one * 0.1f;
            RefreshRecipeList();
        }
    }
}
```
```csharp
// PlayerCamera.cs:1175-1178（C 键）
if (Input.GetKeyDown(KeyBinds.GetBind("craft")) && (!craftingPanel.activeSelf || !recipeFilterField.isFocused))
{
    OpenCraftScreen();
}
```
```csharp
// Settings.cs:473-481（默认 C 键）
new SettingKeybind
{
    name = "craft",
    value = KeyCode.C,
    apply = delegate { },
    category = Setting.SettingCategory.Input
},
```

**合成相关字段全表（`PlayerCamera.cs:329-367`）**

| 行 | 声明 | 用途 |
|---|---|---|
| 329 | `public GameObject craftingPanel;` | 合成面板根物体（`Canvas/MainView/CraftPanel`） |
| 331 | `public GameObject craftButton;` | `Canvas/MainView/CraftButton`（拖物品到它 → 打开并按物品过滤） |
| 333 | `public int selectedRecipe;` | 当前选中配方的索引 |
| 335 | `public string recipeFilter;` | 搜索框文本 |
| 337 | `public TMP_InputField recipeFilterField;` | 搜索框组件 |
| 339 | `public RectTransform recipeListContent;` | `CraftPanel/3:RecipeList/Viewport/Content` |
| 341 | `private List<GameObject> recipeObjects;` | 已生成的配方行缓存 |
| 343 | `private List<GameObject> recipeItemPreviewObjects;` | 已生成的材料预览图缓存 |
| 345 | `public GameObject searchClearButton;` | `12:ClearSearch` |
| 347 | `public ScrollRect recipeScrollRect;` | `3:RecipeList` |
| 349 | `public GameObject recipesBackToTopButton;` | `13:BackToTop` |
| 351 | `private Coroutine recipeScrollCoroutine;` | 平滑滚动协程 |
| 353 | `private Recipes.RecipeCategory? selectedRecipeCategory;` | 分类过滤（null=全部） |
| 355/357 | `public Image pinRecipeImage;` / `public TextMeshProUGUI pinRecipeText;` | 置顶配方显示 |
| 363 | `public Image[] recipeCategorySelectImages;` | 分类按钮背景图 |
| 397 | `public static bool clearerRecipes;` | 配方行可做/不可做的配色开关 |
| 413 | `public int? pinnedRecipe { get; private set; }` | 置顶配方索引（private set） |

**其它关闭/影响路径**：`PlayerCamera.cs:1107`（`ToggleWoundView` 中先关合成面板）、`1142-1145`（pause 分支）、`1181-1184`（`iteminteract` 按下且面板已开 → 关闭）、`2067`、`2003-2006`。`BasicCourse.cs:414` 用 `yield return new WaitUntil(() => PlayerCamera.main.craftingPanel.activeSelf);` 等待面板打开。

**KeyBinds API 与 `GetBind` 全部调用点（bind 名字符串）**

`KeyBinds.cs:13-20 public static KeyCode GetBind(string action)`；`KeyBinds.cs:22-25 public static string GetBindName(string action)`；`KeyBinds.cs:8-11 public static void CacheBind(SettingKeybind keybind)`。

| bind 名 | 调用位置（`PlayerCamera.cs`，除注明外） |
|---|---|
| `craft` | 1175 |
| `pause` | 1128 |
| `speed1/2/3` | 1163 / 1167 / 1171 |
| `iteminteract` | 1181, 1517, 1521, 2590 |
| `right/left/up/down` | 1197-1199 |
| `switchhands` | 1207（另 `BasicCourse.cs:151`） |
| `toggleinventory` | 1211 |
| `throw` | 1217, 1228 |
| `ragdoll` | 1236 |
| `jump` | 1240, 1244 |
| `woundview` | 1254 |
| `bark` | 1258（另 `CraftingCourse.cs:133`、`FirstAidCourse.cs` 多处、`SceneEntity.cs:133`） |
| `expanddesc` | 1353, 1374, 1379, 1702, 1874 |
| `favourite` | 1878 |
| `attack` | 1960, 1964 |
| `altview` | `AltHoverScript.cs:46,48` |
| `console` | `ConsoleScript.cs:178` |

---

## 2. 材料预览的生成与点击链路

**结论**
- 生成分两步：`RefreshRecipeList()`（`PlayerCamera.cs:547-608`）重建**配方行**；`RefreshCurrentlySelectedRecipe()`（`671-753`）写**当前配方详情**并生成**材料预览图**。前者末尾（`:607`）调用后者。
- 配方行 = `Utils.Create("Special/RecipeBox", recipeListContent)`（`:582`），子物体 `0:Name`(TMP) / `1:INT`(TMP) / `2:Icon`(Image)，自身 `Image + Button`。
- 材料预览 = `Utils.Create("Special/RecipeItemPreview", component.transform)`（`:714`），**挂在 `CraftPanel/8:Ingredients` 这个 TextMeshProUGUI 的 transform 下**，子物体 `0:Item`(Image)。
- 缓存只有 `List<GameObject> recipeObjects`（`:341`）与 `List<GameObject> recipeItemPreviewObjects`（`:343`）；**未找到** `Image[]` / `Text[]` 形式的材料缓存。
- **材料预览图不可点击**：`resources.assets` 中 `RecipeItemPreview` 预制体组件仅 `RectTransform + CanvasRenderer + Image`，子 `Item` 也是 `Image`——无 Button / 无 Selectable / 无 EventTrigger；代码里对预览物体只有创建与定位，没有任何点击回调。`RecipeItemButton.cs:9 public void Clicked()` 是**空实现**且无任何引用。

```csharp
// PlayerCamera.cs:565-570（配方排序：可做的排前面）
List<(Recipe, bool)> source = (from rec in Recipes.GetVisibleRecipes(recipeItemFilter)
    orderby rec.INT
    select (rec: rec, rec.GetItemsForRecipe() != null)).ToList();
...
list.AddRange(collection);          // list=可做、collection=不可做
```
```csharp
// PlayerCamera.cs:580-604（配方行生成，节选）
GameObject gameObject = Utils.Create("Special/RecipeBox", recipeListContent);          // 582
gameObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(-9f, -num2 * 64); // 583
// 584-585 GetChild(0) = TextMeshProUGUI ← list[num2].Item1.fullName，颜色 color/color2
// 586-587 GetChild(1) = TextMeshProUGUI ← INT.ToString()，颜色 RecipeINTToColor(INT)
// 588-591 GetChild(2) = Image ← resultSprite，sizeDelta = ImageSizeDelta(..., 3f, 42f)
gameObject.GetComponent<Image>().color = ...;                                          // 592
int toIndex = list[num2].Item1.index;                                                  // 593
gameObject.GetComponent<Button>().onClick.AddListener(delegate { SelectRecipe(toIndex); }); // 594-597
UITooltip uITooltip = gameObject.AddComponent<UITooltip>();                            // 598
uITooltip.skipLocale = true;
uITooltip.tipName = list[num2].Item1.simpleName;                                       // 600
uITooltip.tipDesc = list[num2].Item1.description;                                      // 601
recipeObjects.Add(gameObject);                                                         // 602
```
```csharp
// PlayerCamera.cs:679-733（材料文本 → 预览图生成，节选）
TextMeshProUGUI component = craftingPanel.transform.GetChild(8).GetComponent<TextMeshProUGUI>(); // 679
List<Item> itemsForRecipeThorough = recipe.GetItemsForRecipeThorough();                          // 680
component.text = IngredientTextForRecipe(recipe, itemsForRecipeThorough);                        // 681
// 682-693：Destroy 旧 recipeItemPreviewObjects 后 Clear()（!= null 分支；== null 时 new List）
component.ForceMeshUpdate();                                                                    // 694
TMP_TextInfo textInfo = component.textInfo;
// 695-733：遍历 characterInfo，命中 <sprite index=23|24> 的可见字符时按序对应 recipe.items[num]
if (tMP_CharacterInfo.isVisible && tMP_CharacterInfo.elementType == TMP_TextElementType.Sprite
    && (tMP_CharacterInfo.spriteIndex == 23 || tMP_CharacterInfo.spriteIndex == 24))            // 702
    if (num >= recipe.items.Count) break;                                                        // 706-709
    Item item = itemsForRecipeThorough[num];                                                     // 711
    if ((bool)item)
        GameObject gameObject = Utils.Create("Special/RecipeItemPreview", component.transform);   // 714
        Vector2 anchoredPosition = new Vector2(-20f, tMP_CharacterInfo.topLeft.y - 20f);          // 715
        // 716-723：相邻行距 <32f 时左右交错（x += ±20f）；726 赋给 RectTransform.anchoredPosition
        Image component2 = gameObject.transform.GetChild(0).GetComponent<Image>();                // 727
        component2.sprite = item.GetComponent<SpriteRenderer>().sprite;                           // 728
        component2.rectTransform.sizeDelta = ImageSizeDelta(component2.sprite.texture, 4f, 20f);  // 729
        recipeItemPreviewObjects.Add(gameObject);                                                 // 730
```

**合成界面里所有可点击入口（代码 + 预制体资产双证）**

| UI 元素（CraftPanel 子索引） | 绑定 | 位置 |
|---|---|---|
| 配方行 `Special/RecipeBox` 的 Button | `SelectRecipe(index)` | `PlayerCamera.cs:594-597` |
| `6:ResultImage` 的 Button | **`PlayerCamera.TryCraft`** | 场景 `level1` 持久化 onClick |
| 搜索框 `4:SearchBar` 的 InputField | `PlayerCamera.UpdateRecipeFilter` | 场景 `level1` |
| `12:ClearSearch` 的 Button | `PlayerCamera.ClearRecipeFilter` | 场景 `level1` |
| `13:BackToTop` 的 Button | `PlayerCamera.RecipeScrollBackToTop` | 场景 `level1` |
| `14:PinButton` 的 Button | `PlayerCamera.PinRecipe` | 场景 `level1` |
| `MainView/CraftButton`（`craftButton`）的 Button | `PlayerCamera.OpenCraftScreen` | 场景 `level1` |
| 材料预览图 `Special/RecipeItemPreview` | **无** | — |

- 输入主链：`PlayerCamera.Update()`（`:1484-1513`）→ `UIUtil.GetEventSystemRaycastResults()` → `HandleInput()`（`:1121`）→ `HandleDragging(uiCasts, ...)`（`:1515-1526`）→ 抬起时 `HandleReleaseDragging`（`:1607-1647`）→ `TryPerformUIActions`（`:1649-1667`）→ `TryPerformSpecialUIAction`（`:1793-1822`）。
- 合成相关只有两处特判：`1793-1822` 里 `if (hit.gameObject == craftButton) { if (!craftingPanel.activeSelf) OpenCraftScreen(); SeeRecipesWithItem(dragItem); return true; }`（`:1812-1820`）；其余交给 Unity `EventSystem`/`Button`。
- 结论：合成 UI 中**没有** `IPointerClickHandler`（`UIHoverDescription.cs` 的两个接口方法体是空的）、**没有**为合成界面写的 `Physics2D.OverlapPoint`、**没有** `EventTrigger`。要给材料预览加交互，必须由模组自己挂组件。

---

## 3. 界面文字：谁写入哪个组件

**CraftPanel 子物体 ↔ 名称 ↔ 组件（场景 `level1` 实测，索引即 `GetChild(i)`）**

| idx | 名称 | 组件 |
|---|---|---|
| 0-2 | `Back1` / `Back1 (1)` / `Back1 (2)` | RectTransform, CanvasRenderer, Image |
| 3 | `RecipeList` | + Image + ScrollRect |
| 4 | `SearchBar` | + Image + TMP_InputField（资产中带持久化调用 → `PlayerCamera.UpdateRecipeFilter`，具体是 onValueChanged 还是 onEndEdit 未能从字节确认），子 `0:Text Area` |
| **5** | **`CurrentRecipeName`** | TextMeshProUGUI |
| **6** | **`ResultImage`** | Image + **Button(TryCraft)** + UITooltip(`craftcreate`) |
| 7 | `Filler1` | TextMeshProUGUI(`Ingredients`) + UILocalizer(`craftingredients`) —— 材料区小标题 |
| **8** | **`Ingredients`** | TextMeshProUGUI（运行时再 `AddComponent<UITooltip>()`） |
| 9 | `Filler2` | TextMeshProUGUI + UILocalizer(`craftinfo`) —— 产物信息小标题 |
| **10** | **`RecipeInfo`** | TextMeshProUGUI |
| **11** | **`NewCraftTip`** | TextMeshProUGUI + UILocalizer(`craftnotmadebefore`) + UITooltip |
| 12-14 | `ClearSearch` / `BackToTop` / `PinButton` | Image + Button + UITooltip |
| 15 | `CategoryButtons` | 子 `0:Cat1 … 4:Cat5`,`5:Cat5 (1)` |

**结论**：所有动态文字都在 `RefreshCurrentlySelectedRecipe()`（`PlayerCamera.cs:671-753`）里写入，且**全部是 `TextMeshProUGUI`（不是 `Text`）**：`:674` → `5:CurrentRecipeName`；`:679-681` → `8:Ingredients`；`:751` → `10:RecipeInfo`；`:752` → `11:NewCraftTip`（只切 `enabled`）。材料文本没有独立数组缓存，只有预览图列表 `recipeItemPreviewObjects`。**未找到** `IngredientDescription` / `GetIngredientText` 这类函数名；材料文本的拼装函数是 `IngredientTextForRecipe`。

```csharp
// PlayerCamera.cs:673-681
Recipe recipe = Recipes.recipes[selectedRecipe];
craftingPanel.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = recipe.fullName;
// 675-678 GetChild(6): Image ← recipe.resultSprite（sprite+color），sizeDelta = ImageSizeDelta(tex, 12f, 150f)
TextMeshProUGUI component = craftingPanel.transform.GetChild(8).GetComponent<TextMeshProUGUI>();
List<Item> itemsForRecipeThorough = recipe.GetItemsForRecipeThorough();
component.text = IngredientTextForRecipe(recipe, itemsForRecipeThorough);
```
```csharp
// PlayerCamera.cs:625-669 IngredientTextForRecipe（节选）
public string IngredientTextForRecipe(Recipe recipe, List<Item> its = null)
{
    string text2 = "<sprite index=23>";     // 628 有料
    string text3 = "<sprite index=24>";     // 629 缺料
    if (its == null) its = recipe.GetItemsForRecipeThorough();   // 630-633
    for (int i = 0; i < recipe.items.Count; i++)
    {
        RecipeItem recit = recipe.items[i];                       // 636
        text += "<color=#FFFFFF>";
        text += (its[i] ? text2 : text3);                         // 637-638 料图标
        // 639 名称行：specific 固体→Locale.GetItem(specificId)；specific 液体→Locale.GetOther(specificId)；
        //      非 specific 固体→craftanytool(quality.id=="hammering"|"cutting") 或 craftanyitem；否则 craftanyliquid
        text += "<color=#666666>";                                // 640 详情行转灰
        // 645 液体 非specific：craftliquidquality(<1>=amount,<2>=quality.LocaleName)
        // 649 液体 specific：craftml(<>=minimumCondition)
        // 655 固体 非specific：craftitemquality(<1>=amount,<2>=LocaleName)
        // 656-660 命中 Recipes.QualityExamples（id+amount 相等）→ 追加 craftexample(<>=Locale.GetItem(例))
        // 665 固体 minimumCondition>0 → craftcondition(<>=百分比)
    }
    return text;
}
```
```csharp
// PlayerCamera.cs:734-752（产物信息 → 10:RecipeInfo；新手提示 → 11:NewCraftTip）
text = (!recipe.result.isLiquid)
    ? (Locale.GetOther("craftinfocondition") + (recipe.result.resultCondition * 100f).ToString("0.#") + "%\n")
    : (Locale.GetOther("craftinfovolume") + recipe.result.resultCondition.ToString("0.#") + "mL\n");
text += Locale.GetOther("craftinfoamount") + recipe.result.amount + "\n";
text += "<color=#" + ColorUtility.ToHtmlStringRGB(RecipeINTToColor(recipe.INT)) + ">";
text += Locale.GetOther("craftinfoint").Replace("<1>", recipe.INT.ToString()).Replace("<2>", body.skills.INT.ToString()) + "\n";
// 739-750 INT 差 -1/-2/-3 → craftminorfail / craftmajorfail / craftcriticalfail
craftingPanel.transform.GetChild(10).GetComponent<TextMeshProUGUI>().text = text;            // 751
craftingPanel.transform.GetChild(11).GetComponent<TextMeshProUGUI>().enabled = !recipe.hasMadeBefore; // 752
```
**UITooltip 在合成界面的设置位置**
- 配方行：`PlayerCamera.cs:598-601`（运行时 `AddComponent`，`skipLocale = true`，`tipName = simpleName`、`tipDesc = description`）。
- `8:Ingredients`：`PlayerCamera.cs:1019-1022`（`Start()` 里 `AddComponent<UITooltip>()`，`craftingredients` / `craftingredientsdsc`，把 `<>` 替换为 `KeyBinds.GetBindName("favourite")`）。
- 预制体自带（无需代码）：`6:ResultImage`(craftcreate)、`11:NewCraftTip`、`12:ClearSearch`、`13:BackToTop`、`14:PinButton`、`MainView/CraftButton`(crafttipname/crafttipdsc)。
- 显示机制：`GlobalDark.HandleTooltip()`（`GlobalDark.cs:145-178`）每帧用 `UIUtil.GetUITooltip(UIUtil.GetEventSystemRaycastResults())` 覆盖 `tooltipText`，即**只要有 `UITooltip` 组件且在射线结果里就会被显示**；也可以直接调 `GlobalDark.main.SetTooltip((name, desc))` 强推（`GlobalDark.cs:94-97`）。

---

## 4. `Recipe` 与 `RecipeItem` 的 public 成员

`Recipe.cs`（200 行，`public class Recipe`，无基类）
```csharp
public bool specialKnown;                       // 6
public int INT;                                 // 8
public List<RecipeItem> items;                  // 10
public RecipeResult result;                     // 12
public bool hasMadeBefore;                      // 14
public Recipes.RecipeCategory category;         // 16
public bool isRepair;                           // 18
public int index;                               // 20
public (Sprite, Color) resultSprite { get; }    // 22-32 液体→("Sprites/droplet", Liquids.Registry[id].color)
public string fullName { get; }                 // 34-62
public string simpleName { get; }               // 64-88
public string description { get; }              // 90-100
public bool visible { get; }                    // 102-112 specialKnown? true : (INT差 >= -3)
public List<Item> GetItemsForRecipe();          // 114-142 缺任一材料→返回 null
public List<Item> GetItemsForRecipeThorough();  // 144-174 缺料处填 null，不返回 null
public void TryMake();                          // 176-199
```
`RecipeItem.cs`（177 行，`public class RecipeItem`）
```csharp
public bool specific;                           // 6
public string specificId;                       // 8
public bool isLiquid;                           // 10
public CraftingQuality quality;                 // 12
public float minimumCondition = 0.9f;           // 14
public bool destroyItem = true;                 // 16
public string ignoredId;                        // 18
public RecipeItem(float minimumCondition = 0.9f);                                  // 20-23
public Item GetMatchingItem(List<Item> items, List<Item> exclusions);              // 25-76
public bool DoesUseItemType(Item item);                                            // 78-116
public void UseItem(Item item);                                                    // 118-176
```
**消耗量公式（`RecipeItem.UseItem`，`RecipeItem.cs:118-176`）**
```csharp
// 液体（非 specific）：quality.amount / 实际满足品质的 amount 作为消耗比例
float max = Mathf.Lerp(0f, liquidStack.amount, quality.amount / qualityThatMeetsCriteria.amount);
component.Drain(component.CalculateDrainSingleLiquid(id, max));
// 液体（specific）
component.Drain(component.CalculateDrainSingleLiquid(specificId, minimumCondition));
// 固体（specific）
item.condition -= minimumCondition;
// 固体（非 specific）
CraftingQuality q = Item.GetQualityThatMeetsCriteria(quality, item.Stats.qualities);
item.condition -= quality.amount / q.amount;
// 收尾：destroyItem → 若有 Container 先 UnloadAllItems()，再 PlayerCamera.main.body.DropItem(item); Object.Destroy(item.gameObject);
// 电池：if ((bool)item.battery && destroyItem) item.battery.UnloadBattery();
```
- **`RecipeItem.CalculateDrainSingleLiquid` 不存在**。真实位置：`WaterContainerItem.cs:134 public List<float> CalculateDrainSingleLiquid(string id, float max)`（另 `WaterContainerItem.cs:53 HasLiquid(string)`、`:82 AmountOf(string liquid)`、`:148 Drain(List<float>)`、`:161 DrainAll()`）。`Container` 上没有该方法。
- `CraftingQuality.cs`：`public string id;`(3) `public float amount;`(5) `public string LocaleName => Locale.GetOther("cq" + id);`(7) `CraftingQuality(string id, float amount)`(9) `CraftingQuality(string id)`(15) `public static implicit operator CraftingQuality(string str)`(21)。
- `RecipeResult.cs`：`public string id;`(6) `public bool isLiquid;`(8) `public int amount = 1;`(10) `public float resultCondition = 1f;`(12) `public bool dontDrainResultLiquid;`(14) `public void SpawnResult(int recipeInt)`(16-83)。

---

## 5. 配方数据的索引方式

- `Recipes.cs:5 public class Recipes : MonoBehaviour`；`Recipes.cs:16 public static List<Recipe> recipes;`（静态列表，`SetUpRecipes()`（`Recipes.cs:38`）里逐条 `recipes.Add(new Recipe {...})`）。
- **`Recipe.index` 就是列表下标**，由 `Recipes.SetUpRecipes()` 末尾统一赋值：
```csharp
// Recipes.cs:4421-4432
for (int i = 0; i < recipes.Count; i++)
{
    recipes[i].index = i;
    foreach (RecipeItem item in recipes[i].items)
    {
        if (!string.IsNullOrEmpty(item.specificId)) item.specific = true;   // 4426-4429
        item.ignoredId = (recipes[i].isRepair ? "" : recipes[i].result.id); // 4430
    }
}
```
- 因此 `Recipes.recipes[selectedRecipe]`（`PlayerCamera.cs:673`、`:757`）与 `recipe.index` 等价（前提：没有模组在 `SetUpRecipes` 后插删配方——插入必须重排 `index`）。
- `Recipes.cs:4435-4452 public static List<Recipe> GetVisibleRecipes(Item filterItem = null)`：无参时 `recipes.Where(r => r.visible).ToList()`（保序）；带 filter 时按 `RecipeItem.DoesUseItemType(filterItem)` 过滤。
- `Recipes.cs:7-14 public enum RecipeCategory { Materials, Tools, Medicine, Utilities, Food }`；`Recipes.cs:18-36 public static Dictionary<CraftingQuality, string> QualityExamples`（heatsource→campfire、firestarter→firestarter、produce→geofruit、flammable→woodscraps）。
- 选中态字段：`PlayerCamera.cs:333 public int selectedRecipe;`（**public，可直接赋值**）；置顶态 `PlayerCamera.cs:413 public int? pinnedRecipe { get; private set; }`；分类过滤 `PlayerCamera.cs:353 private Recipes.RecipeCategory? selectedRecipeCategory;`。
- 外部改选中配方+打开面板的既有范例（蓝图物品）：`Item.cs:4185-4198`（`Recipe recipe = Recipes.recipes[item.GetComponent<BlueprintScript>().recipeIndex]; recipe.INT = 0; PlayerCamera.main.selectedRecipe = recipe.index; if (!PlayerCamera.main.craftingPanel.activeSelf) PlayerCamera.main.OpenCraftScreen(); else PlayerCamera.main.RefreshRecipeList();`）。

---

## 6. 合成动作链与刷新范围

```csharp
// PlayerCamera.cs:755-759
public void TryCraft()
{
    Recipes.recipes[selectedRecipe].TryMake();
    RefreshRecipeList();
}
```
```csharp
// Recipe.cs:176-199（TryMake，节选）
public void TryMake()
{
    List<Item> itemsForRecipe = GetItemsForRecipe();
    if (itemsForRecipe != null)
    {
        for (int i = 0; i < itemsForRecipe.Count; i++) items[i].UseItem(itemsForRecipe[i]);
        result.SpawnResult(INT);
        Sound.Play("combine", PlayerCamera.main.body.transform.position);
        if (!hasMadeBefore) { hasMadeBefore = true; PlayerCamera.main.body.happiness++; PlayerCamera.main.body.skills.AddExp(2, 10f); }
        PlayerCamera.main.PlayUISound(PlayerCamera.UISoundType.Click);
    }
    else PlayerCamera.main.PlayUISound(PlayerCamera.UISoundType.Deny);
}
```
- 入口绑定：`6:ResultImage` 的 `Button.onClick` → `PlayerCamera.TryCraft`（场景 `level1` 持久化调用，非代码）。`TryCraft` 在 C# 源码里**没有**任何调用点（grep 结果只有定义），只能由 UI 触发。
- `TryCraft()` → `TryMake()`（`UseItem` 扣料 + `SpawnResult` 产出）→ `RefreshRecipeList()`。

**`RefreshRecipeList()`（`PlayerCamera.cs:547-608`）每次重建/刷新内容**：销毁并重建**全部**配方行（`:556-563` 销毁 `recipeObjects` → 新建空列表 → `:582` 逐行 `Utils.Create("Special/RecipeBox", ...)`）；重算可做/不可做排序、分类与搜索过滤；`recipeListContent.sizeDelta`（`:605`）；清空 `recipeItemFilter`（`:606`）；末尾调用 `RefreshCurrentlySelectedRecipe()`（`:607`）。
**`RefreshCurrentlySelectedRecipe()`（`:671-753`）负责**：`5:CurrentRecipeName`、`6:ResultImage`、`8:Ingredients` 文本 + 销毁/重建全部材料预览图、`10:RecipeInfo`、`11:NewCraftTip.enabled`。
**不重建整表**的路径：`SelectRecipe(i)`（`:775-780`，仅 `selectedRecipe = i; PlayUISound(MiniClick); RefreshCurrentlySelectedRecipe();`）。每帧刷新（`LateUpdate`，`:2911-2931`）：面板缩放动画、`searchClearButton` / `recipesBackToTopButton` 显隐、`pinRecipeImage` 颜色；每 0.5s 更新 `pinRecipeText.text = Recipes.recipes[pinnedRecipe.Value].fullName + ":\n" + IngredientTextForRecipe(Recipes.recipes[pinnedRecipe.Value]);`（`:2925`）。

---

## 7. 可复用的现成 UI 工具

`UIUtil.cs`（`public class UIUtil : MonoBehaviour`，79 行）
```csharp
public static bool IsPointerOverUIElement();                                              // 7
public static bool IsPointerOverUIElement(List<RaycastResult> eventSystemRaysastResults); // 12
public static List<RaycastResult> GetEventSystemRaycastResults(Vector2? pos = null);      // 25
public static (string, string) GetUITooltip(List<RaycastResult> UICasts);                 // 40
public static bool IsPointerOverContextMenu();                                            // 52
```
`GlobalDark.cs`（`public class GlobalDark : MonoBehaviour`，197 行）
```csharp
public static GlobalDark main;                     // 9
public void SetTooltip((string, string) newText);   // 94-97
public void HandleTooltip();                        // 145-178 每帧：鼠标位置 + EventSystem 射线取 UITooltip
public void Darken();                               // 180
public bool IsDarkening();                          // 185
public static void CheckForDark();                  // 190
```
`UITooltip.cs`（26 行）：`public string localeName;`(5) `public string localeDesc;`(7) `[HideInInspector] public string tipName;`(10) `[HideInInspector] public string tipDesc;`(12) `[HideInInspector] public bool skipLocale;`(16)；`Start()` 中 `if (!skipLocale) { tipName = Locale.GetOther(localeName); tipDesc = Locale.GetOther(localeDesc); }`。**模组新建 tooltip 必须 `skipLocale = true`**，否则 `Start` 会用 locale key 覆盖。
`UILocalizer.cs`（21 行）：`public string key;`(6) `public bool upper;`(8)；`Start()` 里把同物体 `TextMeshProUGUI.text` 设为 `Locale.GetOther(key)`（可选大写）。
`PlayerCamera` 静态/公共工具
```csharp
public static PlayerCamera main;                                   // 全项目通用入口
public static float uiScale => main.mainCanvas.transform.localScale.y;      // 411
public void PlayUISound(UISoundType sound, float pitch = 1f);      // 417
public static void PlayUISound(string sound, float pitch = 1f);    // 422
public static string ConditionToColorCode(float cond);             // 930
public static Color ConditionToColor(float cond);                  // 935
public static string EndColor();                                   // 940 返回 "</color>"
public static Vector2 ImageSizeDelta(Texture tex, float pixelSize, float maxSize); // 1265（另 Utils.cs:59 同名）
public static (string, string) ItemHoverDescription(Item item);    // 1304
public Color RecipeINTToColor(int INT);                            // 610
```
`Utils.cs`：`public static GameObject Create(string id, Vector2 pos, float rot)`(8)、`public static GameObject Create(string id, Transform trans)`(13)、`public static bool GetBody(GameObject g, out Body body)`(18)、`public static Vector2 ImageSizeDelta(Texture tex, float pixelSize, float maxSize)`(59)。
- **未找到**通用 `Tooltip` 静态类；`UIHoverDescription.cs` 是空实现的 `IPointerEnterHandler/IPointerExitHandler`（不可复用）。
- 可参考的「动态悬挂 tooltip + hover 状态」范例：`InvButton.cs`（`slot/itemImg/itemCond/locked/waterFill/maxImageSize/refItem/woundView/hovered/miniHovered` 等字段；`UpdateRefs()`(67) 里 `tooltip.skipLocale = true;`；`UpdateGraphic()`(91) 里 `var t = PlayerCamera.ItemHoverDescription(item); tooltip.tipName = t.Item1; tooltip.tipDesc = t.Item2;`）。

---

## 模组可插入点建议（Harmony）

| 目标 | 建议补丁点 | 理由 / 注意 |
|---|---|---|
| 给材料列表追加文字（最省事） | `Postfix PlayerCamera.IngredientTextForRecipe(Recipe, List<Item> = null)` → `__result += ...` | 该函数是材料文本的唯一拼装器（`:625`），返回值直接进 `8:Ingredients`（`:681`）；追加 `<sprite index=23|24>` 还能自动被 `:694-733` 生成预览图（但会引起预览与 `recipe.items` 下标错位，慎用） |
| 往当前配方详情加自定义行 | `Postfix PlayerCamera.RefreshCurrentlySelectedRecipe()` | 唯一写详情的入口（`:671`，public）；可在里面改 `GetChild(8/10/11)`，或 `Utils.Create` 自己的 TMP 挂到 `craftingPanel`。注意顺序：Postfix 在预览图创建之后 |
| 追加产物信息行（不干扰预览） | `Postfix PlayerCamera.RefreshCurrentlySelectedRecipe()` 里操作 `GetChild(10)`(RecipeInfo) 或 `GetChild(11)`(NewCraftTip) | 这两处由 `:751-752` 写入，Postfix 覆盖/追加即可；`NewCraftTip.enabled` 每次都会按 `hasMadeBefore` 重设，需在其后改 |
| 监听面板开/关 | `Postfix PlayerCamera.OpenCraftScreen()` | 无参 void，开与关都走它；用 `craftingPanel.activeSelf` 判断方向（无 CloseCraftScreen）。若需旧状态，加 `Prefix` 记录 |
| 材料预览图可点击（如「点击换材料」） | `Postfix PlayerCamera.RefreshCurrentlySelectedRecipe()`：遍历 `recipeItemPreviewObjects`（private 字段，`AccessTools.FieldRefAccess`/`Traverse`），给每个元素 `AddComponent<Button>()` 或自写 `IPointerClickHandler` 组件，并保存 `index → Item` 映射 | 预览预制体无 Button/Selectable/EventTrigger（资产证实），点击原版无任何逻辑；`RecipeItemButton.Clicked()` 是空实现，可直接替换。注意 `:682-693` 每次刷新都会 Destroy 重建这些物体，必须在 Postfix 里重挂 |
| 点击配方行时做额外动作 | `Postfix PlayerCamera.SelectRecipe(int i)` | 只刷详情、不重建整表（`:775-780`），适合联动自定义 UI |
| 拦截/改写合成 | `Prefix PlayerCamera.TryCraft()`（返回 false 跳过原逻辑）或 `Prefix Recipe.TryMake()` | `TryCraft` 是 UI（`6:ResultImage` 的 Button）唯一入口；`TryMake` 是扣料+产出的核心（`Recipe.cs:176`）。改写消耗量可 `Prefix/Postfix RecipeItem.UseItem(Item)` |
| 配方列表增强（材料状态/质量色/额外图标） | `Postfix PlayerCamera.RefreshRecipeList()` | 每次重建后遍历 `recipeListContent` 子物体（或 private `recipeObjects`）逐个加工；不要在 `:580-604` 的匿名委托里做（无法单独 patch） |
| 过滤/分类/搜索扩展 | `Prefix PlayerCamera.UpdateRecipeFilter()` / `SetRecipeCategory(int)` / `SeeRecipesWithItem(Item)` | 三者末尾都会 `RefreshRecipeList()`（`:465/:510/:764`），可先改 `recipeFilter` / `selectedRecipeCategory` / 自建二次过滤 |
| 动态新增配方 | 在 `Recipes.SetUpRecipes()` 之后（`Postfix`）追加，并**重排 `recipes[i].index = i`** 且补 `RecipeItem.specific` / `ignoredId` | `index` 与列表下标强耦合（`Recipes.cs:4421-4423`），`Recipes.recipes[selectedRecipe]` 直接下标取值（`:673`、`:757`）；不重排会导致选中错配方 |
| 自绘 tooltip | 新元素 `AddComponent<UITooltip>()` + `skipLocale = true` + 赋 `tipName/tipDesc`；或 `GlobalDark.main.SetTooltip((n, d))` | `GlobalDark.HandleTooltip()` 每帧从 EventSystem 射线结果覆盖 tooltip，托管给 `UITooltip` 最稳；`SetTooltip` 是即时强推，会被下一帧射线结果覆盖 |
| 每帧联动（如高亮可做配方） | `Postfix PlayerCamera.LateUpdate()`（private，需 `AccessTools.Method`）或自建常驻 MonoBehaviour | `LateUpdate`（`:2911`）在面板打开时已每帧做事，适合挂轻量刷新；重活避免每帧重建 UI |

**共同注意事项**
1. `craftingPanel` 的子物体**按索引硬编码**（`GetChild(5/6/8/10/11)`），任何模组若在 `CraftPanel` 下插入新子物体（默认追加到末尾）不会影响原索引；但**不要**在索引 0-11 之间 `SetSiblingIndex` 插入。
2. `RefreshRecipeList()` 会销毁重建配方行，所有挂在 `RecipeBox` 上的自定义组件/引用必须在 Postfix 里重新挂。
3. `RefreshCurrentlySelectedRecipe()` 会销毁重建材料预览图（`:682-693`），同理。
4. `selectedRecipe` 是 `public int` 且直接作 `Recipes.recipes` 下标，模组改配方表时务必同步 `index`。
5. 面板打开时 `HandleInput` 会在 `:1185-1188` 提前 `return`，所以在面板打开状态下不能依赖原版世界输入；自定义交互请用 EventSystem/UI 组件。
