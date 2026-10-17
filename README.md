# Optimised Crafting System

A crafting companion mod for **Casualties: Unknown Demo** (Unity 2022.3.62f3 / Mono, BepInEx 5).

The vanilla crafting screen tells you *what* a recipe needs. This mod tells you **what it will actually cost you, and where the result will go — before you press the button**, and lets you overrule every one of those decisions.

> Formerly named **BetterCraftingLogic**. It is a derivative work of **BetterInventoryLogic** by *BalaurBohemianBroken* (CC BY 4.0) — see [Credits & license](#credits--license).

- BepInEx 5 (Mono) plugin · assembly `OptimisedCraftingSystem.dll` · GUID `maple.optimisedcraftingsystem`
- Settings live in the **game's own settings menu** (36 entries), stored in the game's `settings.json` — **not** a BepInEx `.cfg`
- All evidence in the docs is captured from real play sessions (`BepInEx\LogOutput.log`), with line numbers
- Chinese documentation: [README.zh-CN.md](README.zh-CN.md) · in-depth Chinese docs in [`docs/`](docs/)

---

## What it changes

| | Vanilla | With this mod |
|---|---|---|
| Ingredient choice | fixed priority list | **cost-based picking** — what this craft actually destroys (value, durability, location, scarcity) |
| Consumption | invisible | every slot shows `120 mL` / `whole item` / `durability −12% (8 uses left)` |
| Result destination | you find out afterwards | **predicted before crafting**, and you can re-route it by hand |
| Liquid destination | vanilla picks a bottle | candidate list of **every** bottle (carried, in containers, on the floor) with contents + free space; unusable rows are greyed out with the reason |
| Filled bottle consumed as an ingredient | the liquid **vanishes** | liquid is poured into a makeshift bottle first (5-minute lifetime, same as vanilla) |
| Filled container consumed as an ingredient | not allowed at all | allowed: contents are held, moved into the new container, overflow drops at your feet |
| Favourited (starred) container | gets filled anyway | **never receives liquid** (optional) |
| Containers with different liquids | stacked as `×2` | kept apart, each row names its liquid |
| Materials overview | — | press **M**: every item grouped, qualities, weight, condition, value, "recipes that can use this quality" |

Everything above is a real patch to vanilla behaviour. Nothing is written to your save beyond normal in-game crafting.

## Install

1. Install **BepInEx 5 (x64, Mono)** into `Casualties Unknown Demo\`.
2. Put `OptimisedCraftingSystem.dll` into `Casualties Unknown Demo\BepInEx\plugins\`.
3. Remove conflicting copies — the plugin refuses to run alongside the original mod:
   - delete `plugins\CraftingLogic.dll` (the original **BetterInventoryLogic**), and
   - make sure an old `plugins\BetterCraftingLogic.dll` is **not** next to the new file (rename it to `BetterCraftingLogic.dll.disabled` or move it out of `plugins\`) — two names = two plugins = every patch applied twice.
4. Start the game. The log should contain:

```
Loading [Optimised Crafting System 1.0.0]
OptimisedCraftingSystem 1.0.0 loaded (Harmony patches applied).
[Self-check] language=English settings=36 hotkey=M font=...
[Self-check] recipes=278 item types=371 player=present
```

`[Self-check]` also prints, for all 14 patched methods, **which mod owns which patch prefix** — the fastest way to diagnose a conflict with another mod.

## Usage

| Action | What you get |
|---|---|
| Open the crafting screen (**C**) | an extra *Needs* block under the recipe: per-slot consumption, plus a final line `will be stored in: canvas backpack (3.2 kg free)`. Liquid slots read `purified water (canteen)` — the actual liquid layer that will be poured, with the container in parentheses |
| Compact overlay next to the crafting screen | the same information in condensed form. Click an ingredient to expand its candidate list, click the *will be stored in* row to expand the container/bottle candidates; pick one and it takes effect immediately. **Drag the title bar to move it, double-click to reset** |
| **M** (rebindable) | materials overview window: items grouped by id, with count, location, qualities, condition, value and how many recipes can use them; click a row for details |
| **Shift** + hover a container | tooltip gains contents detail, total contents weight and free space |
| Star a container (**favourite**) | starred containers are skipped as liquid destinations (candidate rows greyed out with `favourited (this mod will not pour into it)`). Turn off `ocs.liquid.skip_favorited` to restore vanilla behaviour |
| Craft something that consumes a filled bottle | the liquid is poured into a makeshift bottle first (5 minutes, like vanilla) instead of disappearing |
| Craft something that consumes a filled container | contents are moved into the new container; if they don't fit they are dropped at your feet with an alert |

Every one of those interactions writes a one-line evidence record to the BepInEx log (`[measured] ...`, `[craft] ...`), so you can verify what actually happened.

## Settings

36 entries in the game's settings menu — 35 toggles on the **Game** page, 1 keybind on the **Input** page. Chinese and English titles/descriptions are injected according to the interface language.

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
| `ocs.liquid.tempbottle` | true | Allow makeshift bottles |
| `ocs.liquid.skip_favorited` | true | Keep liquid out of favourited containers |
| `ocs.craft.container_contents` | true | Allow filled containers as ingredients |
| `ocs.craft.prefer_empty_bottle` | true | Prefer empty bottles |
| `ocs.craft.rescue_liquid` | true | Save liquid from consumed bottles |
| `ocs.ui.consumption` | true | Show consumption in the crafting screen |
| `ocs.ui.stacking` | true | Stack identical ingredients |
| `ocs.ui.verbose` | false | Show detailed numbers |
| `ocs.tip.container_totals` | true | Hold Shift for container contents |
| `ocs.tip.container_weight` | true | Also show contents weight |
| `ocs.window.enabled` | true | Enable the materials window |
| `ocs.window.floor` | true | Include floor items in the window |

> Settings with the `ocs.*` prefix are this mod's own; the unprefixed ones keep the original mod's names so existing localisations and habits keep working.

## Compatibility

- **Other mods' patches are respected.** This mod registers everything at Harmony priority `Low` (200) and yields whenever another mod already handled the call. The only two deliberate exceptions: a `High` (600) postfix that must run last, and a notification prefix at `HigherThanNormal` (500) that competes with *QoL Unknown*'s "reset to default" short-circuit (both mods work; see `docs/改动对照与Bug修复.md` §2 item 18).
- Tested alongside: `prosthetics` (HerobrinesProsthetics), `Crafting Tweaks`, `CUCoreLib`, `QoL Unknown`, `Nutrition System`, `Hemopneumothorax`, `New Firearms`, and a Chinese localisation pack.
- The original **BetterInventoryLogic / CraftingLogic.dll** is refused at load time (it patches the same methods and would double-apply).

## Known limitations

- Manual ingredient/container picks are not persisted across restarts; automatic picking is recomputed every time.
- A liquid slot shows the layer that will actually be poured. Liquids this mod doesn't recognise (e.g. a third-party `Blood O+`) are shown by raw id.
- If *every* usable bottle is favourited and makeshift bottles are disabled, that liquid is lost — the pre-craft prediction line says so in advance.
- If another mod takes over the craft entirely (e.g. `prosthetics` with a titanium hand), this mod's *Needs* block does not describe the real consumption.
- The IMGUI panels need a CJK-capable font on the system; without one, Chinese text renders as boxes (the self-check prints the font it got).

## Build from source

Requires the .NET SDK and a local copy of the game (the game's assemblies are referenced but **not** redistributed here).

```powershell
dotnet build src\OptimisedCraftingSystem.csproj -c Release -p:GameDir="D:\Steam\steamapps\common\Casualties Unknown Demo"
```

`src/OptimisedCraftingSystem.csproj` resolves every game assembly from `$(GameDir)\CasualtiesUnknown_Data\Managed` with `<Private>false</Private>`, targets `netstandard2.1`, and produces `src\bin\Release\netstandard2.1\OptimisedCraftingSystem.dll`. Copy that file into your `BepInEx\plugins\`.

Layout: `src/Core/` (picking, storage planning, liquid routing, session state, localisation, settings), `src/UI/` (the two IMGUI windows), `src/Patches/` (Harmony patches), `src/Plugin.cs` (entry point).

## Credits & license

- Original mod this project derives from: **BetterInventoryLogic**, by **BalaurBohemianBroken**, licensed **CC BY 4.0**.
- This mod is released under **CC BY-SA 4.0** (compatible with the original licence). See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md) for the full attribution and for what is reused versus independently implemented.
- Game content and assets belong to their respective owners; no game files are distributed in this repository.
