# Optimised Crafting System

> Know before you craft **what it will cost and where the result goes**.

The vanilla crafting screen only lists what a recipe *needs*. Everything else you find out the hard way: which bottle gets drained, how much durability disappears, which container swallows the result, whether the parts in your bag survive. **This mod fills in all of it — and lets you override every single decision.**

It started life as **BetterInventoryLogic** (by BalaurBohemianBroken, CC BY 4.0). The picking algorithm, storage planning, liquid routing and both windows were rewritten: the original patched *after* the fact and ranked ingredients by preference toggles; this version **works the cost out before you press craft and lets you change the answer by hand**.

- BepInEx 5 (Mono) plugin · GUID `maple.optimisedcraftingsystem` · assembly `OptimisedCraftingSystem.dll`
- **Cannot be installed alongside the original `CraftingLogic.dll`** — it refuses to load and says so in the log
- **36 settings** live in the game's own settings menu (35 toggles on the *Game* page, 1 keybind on the *Input* page) and are stored in the game's `settings.json`
- Chinese + English UI and docs · licence **CC BY-SA 4.0** (see [NOTICE.md](NOTICE.md), [LICENSE](LICENSE); the original mod's CC BY 4.0 text is kept verbatim in [LICENSE-original.md](LICENSE-original.md))

## How it differs from the original mod

| | Original BetterInventoryLogic | This mod |
|---|---|---|
| **Picking** | seven preference toggles in one comparison chain, sorted and then **the last entry wins**; no cost model at all, so ties are effectively random | computes the **real cost** of this craft (value destroyed, durability, where the item lives, whether another recipe needs it): if the gap is >25% the cheaper option wins outright, and only within 25% do the preference toggles decide. Also **protects scarce items** — the only tool that provides a required quality is never burned as an ingredient |
| **Consumption** | none — the recipe tooltip shows tick/cross icons | the crafting screen gains a **Needs** block: `120 mL` / `whole item` / `durability −12% (8 uses left)` per slot, and liquid slots name **the exact liquid layer that will be poured, with its container** |
| **Storage** | a hard-coded English `Stored: …` line **after** crafting — and it reports success even when `LoadItem` silently failed | a prediction **before** crafting (`will be stored in: canvas backpack (3.2 kg free)` / `right hand` / `on the ground`), and after loading every item is **verified** (parent transform, slot actually taken); if verification fails it falls back to vanilla instead of lying |
| **Overriding** | nothing (the author left "in the future" TODOs in the source) | click an ingredient or the *will be stored in* row to expand a candidate list and pick one — first row is always *Automatic (recommended)*; picks are remembered and impossible picks are skipped. **Slots with only one possible item get no picker at all** (several copies of the same item do not count as a choice), so you never see a list with a single meaningful entry |
| **Liquid routing** | only bottles that can take the whole amount in one go, otherwise a makeshift bottle | three tiers: fits in one bottle → **split across bottles** by free space → only then a makeshift bottle. The candidate list shows **every bottle** (carried, inside containers, even on the floor) with its contents and free space; rows the automatic planner won't use are listed too, greyed out with the reason |
| **UI** | none at all (five logic patches only) | press **M** for the materials overview (880×560, draggable, rebindable); a compact **overlay** on the right of the crafting screen repeats the current recipe's costs and destination — also draggable, double-click to reset, click an ingredient to expand its candidates |
| **Makeshift bottles** | `Destroy(go, 300f)` — filled bottles vanish with their liquid after 5 minutes (vanilla behaviour) | **the same, deliberately**: a filled makeshift bottle is destroyed with its liquid after 5 minutes; an empty one (nothing could be poured) is cleaned up after 30 s |
| **Consumed containers** | liquid in a bottle used as an ingredient **simply disappears**; a container with anything inside **cannot be used as an ingredient at all** | the liquid is saved (poured into a makeshift bottle first) and empty bottles are **preferred** during picking; filled containers **can** be used — contents are lifted out, moved into the new container, and anything that doesn't fit is dropped at your feet with an alert |
| **Favourited containers** | neither vanilla nor the original mod cares — starred bottles get filled anyway | **starred containers never receive liquid** (skipped by the planner, greyed out in the list, and blocked even if picked by hand) |
| **Compatibility** | replaced vanilla `RecipeResult.SpawnResult` outright with a `Prefix` returning `false`, which erased other mods' patches and made load order with CUCoreLib undefined | never touches `SpawnResult`: it opens a session before the craft and only takes over the storage step afterwards; every patch sits at Harmony priority `Low` and yields when another mod already handled the call |
| **Performance** | every refresh re-scanned *all* items and ran a physics circle query **once per visible recipe** (the author's own TODO says "STILL get a lag spike") | scan results are **cached per frame**, so one refresh scans once no matter how many recipes exist |
| **Settings & localisation** | 19 English-only toggles, 38 English-only keys injected with `Dictionary.Add` (throws if run twice), and **"reset to default" never reset the mod's settings** | 36 entries with Chinese + English titles/descriptions following the UI language; injected keys **never overwrite existing ones** (translation packs win); *reset to default* really does reset them |

The original mod's 28 defects are all fixed as well — the usual suspects: comparison chains with the sign flipped (floor/body preferences), liquid comparison using `break` instead of `continue` (so only the first layer was compared), an indexer lookup that throws on unregistered liquids, "prefer fuller containers" implemented as "prefer emptiest", per-frame rescans, localisation injection clobbering translation mods, and the "reports stored but never stored" bug above.

## Everything it does

**Before you craft**

- Per-slot consumption and the result's destination, on the crafting screen.
- Missing-ingredient behaviour is identical to vanilla — this mod never relaxes a recipe.
- The overlay mirrors the same information without covering the vanilla panel.

**Picking**

- Cost-aware smart picking (value / durability / location / scarcity).
- The original ordered chain is kept as a fallback (`ocs.pick.smart` off), with its direction bug fixed.
- Manual picking: candidate list, remembered choice, *Automatic* to reset; one item can never fill two slots; slots with a single possible item show no picker.
- Scarcity protection: an item some other slot needs exclusively is not consumed as material.

**Storage**

- Pre-craft prediction of where the result goes (container / inventory slot / equipment slot / ground).
- Manual re-routing, verified after loading, silently falling back to vanilla on failure.
- Post-craft alert naming what received the result, localised and in a stable order.

**Liquids**

- Three-tier routing (one bottle → split across bottles → makeshift bottle).
- Candidate list with every bottle (floor included); unusable ones greyed out with the reason (mixing / no empty bottles / capacity / favourited).
- **Same liquid prefers the makeshift bottle** — that portion is doomed to vanish in 5 minutes anyway.
- **Liquid containers are never stacked together** — only bottles with identical contents merge into `×2`.
- **Favourited containers never receive liquid** (planner, manual pick and fallback all blocked).
- Liquid inside a bottle used as an ingredient is rescued into a makeshift bottle instead of vanishing.

**Consumed containers**

- Filled containers can be ingredients: contents are lifted out first, then loaded into the new container item by item.
- Overflow drops at your feet with an alert; if the new container itself is full, the result goes back to vanilla for inventory placement.
- Two safety nets (session start and end) guarantee nothing is left frozen in limbo.

**Windows**

- **M** materials overview: items merged by id (count / location / qualities / condition / value / how many recipes can use them), click a row for details.
- **Shift**-hover a container for a contents breakdown, total contents weight and free space.
- Both the window and the overlay can be dragged.

**Engineering**

- All patches at `Priority.Low`; verified to coexist with `QoL Unknown`, `Crafting Tweaks`, `CUCoreLib`, `prosthetics`, and others.
- Built-in `[Self-check]`: on startup it prints which mod owns each of the 14 patched methods, plus setting count, recipe/item type counts and the font it got.
- 14 patch classes, 36 settings, Chinese + English.

## Settings

All 36 entries live in the game's own settings menu (35 toggles on *Game*, the keybind on *Input*) and are stored in the game's `settings.json` — **not** in a BepInEx `.cfg`. The full table with per-item explanations is in the Chinese docs (`docs/新功能说明与设置.md`).

| Name | Default | Meaning |
|---|---|---|
| `ocs.key.window` | **M** | Keybind: materials overview window (Input page) |
| `ocs.pick.smart` | true | Smart picking: match by quality value |
| `ocs.pick.reserve_scarce` | true | Smart picking: keep scarce items |
| `prefer_on_floor` | true | Prefer items on the floor |
| `prefer_not_container` | true | Prefer items outside containers |
| `prefer_not_wearable` | true | Prefer not to consume worn gear |
| `prefer_on_body` | true | Prefer items on your body |
| `prefer_high_quality` | true | Prefer richer quality |
| `prefer_low_value` | true | Prefer lower-value materials |
| `prefer_low_condition` | true | Prefer more worn materials |
| `notify_where_stored` | true | Announce where the result was stored |
| `store_in_containers_first` | true | Put results into containers first |
| `compare_storage_type` | true | Storage: prefer matching tags |
| `compare_storage_reduction` | true | Storage: prefer weight reduction |
| `compare_storage_full` | true | Storage: prefer fuller containers |
| `compare_storage_capacity` | true | Storage: prefer larger capacity |
| `compare_storage_best_condition` | true | Storage: prefer sturdier containers |
| `ocs.store.predict` | true | Predict where results will go |
| `ocs.store.honor` | true | Actually follow the prediction |
| `liquids_can_fill_new_bottles` | true | Allow filling empty bottles |
| `liquids_can_mix` | false | Allow mixing liquids |
| `compare_liquid_unmixed_enabled` | true | Liquid: prefer unmixed bottles |
| `compare_liquid_stacking_enabled` | true | Liquid: prefer same liquid |
| `compare_liquid_weight_enabled` | true | Liquid: prefer lighter bottles |
| `ocs.liquid.tempbottle` | true | Allow makeshift bottles (5-minute lifetime, like vanilla) |
| `ocs.liquid.skip_favorited` | true | Keep liquid out of favourited containers |
| `ocs.craft.container_contents` | true | Allow filled containers as ingredients |
| `ocs.craft.prefer_empty_bottle` | true | Prefer empty bottles |
| `ocs.craft.rescue_liquid` | true | Save liquid from consumed bottles |
| `ocs.ui.consumption` | true | Show consumption in the crafting screen |
| `ocs.ui.stacking` | true | Merge identical ingredients in the materials window |
| `ocs.ui.verbose` | false | Show detailed numbers |
| `ocs.tip.container_totals` | true | Hold Shift for container contents |
| `ocs.tip.container_weight` | true | Also show contents weight |
| `ocs.window.enabled` | true | Enable the materials window |
| `ocs.window.floor` | true | Include floor items in the window |

Unprefixed names are inherited from the original mod, so existing habits and translations keep working; `ocs.*` entries are new.

## Install

1. Drop `OptimisedCraftingSystem.dll` into `Casualties Unknown Demo\BepInEx\plugins\`.
2. Remove conflicting copies: delete `plugins\CraftingLogic.dll` (the original mod), and if an old `plugins\BetterCraftingLogic.dll` is still there, rename it to `BetterCraftingLogic.dll.disabled` or move it out — **two names means two plugins and every patch applied twice**.
3. Start the game; the log should show:

```
Loading [Optimised Crafting System 1.0.0]
OptimisedCraftingSystem 1.0.0 loaded (Harmony patches applied).
[Self-check] language=English settings=36 hotkey=M font=...
[Self-check] recipes=278 item types=371 player=present
[Self-check] Recipe.GetItemsForRecipe <- this mod: maple.optimisedcraftingsystem/prefix(p200); others: com.HerobrinesProsthetics/prefix(p400)
```

`[Self-check]` prints all 14 patched methods one per line — the fastest way to diagnose a mod conflict. A "legacy mod detected" error means step 2 was skipped.

## Compatibility

- Verified alongside `QoL Unknown`, `Crafting Tweaks`, `CUCoreLib`, `prosthetics`, `Nutrition System`, `Hemopneumothorax`, `New Firearms` and common translation packs. Everything sits at `Priority.Low` and yields when someone else handled the call.
- Two deliberate exceptions: a `High` postfix that must run last, and a notification prefix that races `QoL Unknown`'s *reset to default* short-circuit (both mods work; see the Chinese bug-fix table, item 18).

## Known limitations

- Manual picks are not persisted across restarts; automatic picking is recomputed every time.
- A liquid slot shows the layer that will actually be poured; liquids this mod doesn't recognise (e.g. a third-party `Blood O+`) are shown by raw id.
- A liquid that another mod injects without registering it (same `Blood O+` case) makes the vanilla "does this item satisfy the slot" check throw; this mod treats it as *not satisfied* (no crash, and the warning is logged only once), so containers holding such a liquid won't be picked for those slots.
- If every usable bottle is favourited and makeshift bottles are disabled, that liquid is lost — the pre-craft prediction says so in advance.
- Contents that don't fit into the new container are dropped at your feet; this mod does not try to stuff them into your backpack.
- If another mod takes over the craft entirely (e.g. `prosthetics` with a titanium hand), the *Needs* block does not describe the real consumption.
- The IMGUI panels rely on a system CJK font; with none available Chinese renders as boxes (the self-check prints the font it got).

## Build from source

```powershell
dotnet build src\OptimisedCraftingSystem.csproj -c Release -p:GameDir="D:\Steam\steamapps\common\Casualties Unknown Demo"
```

Targets `netstandard2.1`; game assemblies are referenced from `$(GameDir)\CasualtiesUnknown_Data\Managed` with `<Private>false</Private>` — **no game files are redistributed here**. Output: `src\bin\Release\netstandard2.1\OptimisedCraftingSystem.dll`.

Layout: `src/Core/` (picking, storage planning, liquid routing, session state, localisation, settings), `src/UI/` (the two IMGUI windows), `src/Patches/` (Harmony patches), `src/Plugin.cs`.

## Credits & license

- Derivative of **BetterInventoryLogic** by **BalaurBohemianBroken** (**CC BY 4.0**).
- Released under **CC BY-SA 4.0**. Attribution and the reused-vs-independent boundary are spelled out in [NOTICE.md](NOTICE.md); this mod's licence text is in [LICENSE](LICENSE), and the original mod's CC BY 4.0 text is kept verbatim in [LICENSE-original.md](LICENSE-original.md).
- Game content and assets belong to their respective owners; this repository contains no game files.

Deeper documentation (per-change evidence, before/after comparisons) is in [docs/](docs/).
