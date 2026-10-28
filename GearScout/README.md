# GearScout 0.5.1

A first version of a Dalamud plugin for importing XIVGear gear sets and checking which pieces your current character owns.

## Use the compiled plugin

1. Extract `GearScout-plugin.zip` into a permanent folder. Keep `GearScout.dll` and `GearScout.json` together.
2. In FFXIV running with Dalamud, enter `/xlsettings`.
3. In **Experimental → Dev Plugin Locations**, add the full path to `GearScout.dll`.
4. Enter `/xlplugins`, open **Dev Tools → Installed Dev Plugins**, and enable **GearScout**.
5. Enter `/gearscout`, paste your XIVGear link, and click **Import**.

Example link: https://xivgear.app/sl/aadb9265-3645-45ae-8af6-3215eb26d637

Switch to Bard to see this example. When you switch jobs, only sets whose imported job exactly matches your active class/job appear. Bard sets do not appear on Archer. A sheet containing multiple sets imports each set; use the dropdown to switch between them. Selected-set parameters in links are passed to XIVGear's API. Reimporting the same URL refreshes its saved sets instead of duplicating them. Sets and selection are saved in Dalamud's normal plugin configuration.

## Ownership rules

- Checks equipped items, all four inventory bags, and the Armoury Chest of the logged-in character.
- Updates approximately once per second while the window is open; login, loading and character/job changes clear the old snapshot.
- **Equipped** means a matching copy is worn by the player; **Owned** includes live bags/Armoury Chest and saved retainer, glamour dresser and armoire checks for this character; **Missing** means absent from the checked storage.
- Duplicate items require enough physical copies. One ring cannot satisfy two ring slots. The side on which a matching ring is worn does not matter.
- HQ and NQ copies both count. Food, relic stat allocations, and item quality are not validated.
- Materia is compared by item ID (type and grade), with duplicates counted separately. Order does not matter, and all five meld slots are checked. Exact matches require no missing or extra materia.
- Hover the Materia column to see expected, current, missing and extra melds. Loose materia does not count as melded gear. The matcher allocates owned copies across the set to prefer correct melds, including duplicate rings.
- Existing sets from 0.1.0 have unknown meld targets until you click **Refresh from link**. A missing or unreadable materia list is never shown as correct. An explicit empty target expects no materia.
- Matching direct gear drops receive a soft blue icon glow in the standard Need/Greed window. All saved jobs are checked by default, even when farming other jobs' gear with Greed and when the checklist is closed. The current-job-only loot option is available as an explicit restriction. Version 0.4.1 switches older configurations to all-job loot scope once; the checklist's job filtering is unchanged.
- Wanted missing loot also receives a blue **!** badge over the icon. Matching selected item names and list-row names (where shown) turn blue. Name colors restore when the item no longer qualifies, highlighting is disabled, the addon finalizes or GearScout unloads.
- Version 0.5.1 enlarges the complete badge by 50% and removes embedded native rarity color macros while a matching name is blue. The original encoded text and color are restored afterward; unrelated formatting and icons are preserved. Nested name components and previews without a selected-item index are supported.
- The glow requires an item to be wanted by a saved set and missing from live inventory and the character's saved storage checks. Owned gear suppresses it regardless of materia; a set needing two identical rings still requires two copies, while alternative saved sets can reuse one owned copy. Ownership refreshes once per second with the checklist closed while highlighting is enabled. Unknown live inventory suppresses the glow.
- It is shown only for available Need/Greed rolls, and does not automate rolling. Coffers and exchange tokens are not mapped to their rewards.
- Manually open each retainer inventory, the glamour dresser and the armoire, then wait for loading. Complete contents save automatically, even with the checklist closed. **Saved storage checks** shows per-source timestamps and provides **Scan open storage** and **Forget** controls. Retainer bags and equipped gear are independent sources; selling/market listings are excluded. Dresser data includes expanded outfit pieces; glamour plates are not physical ownership.
- Saved checks are last-known contents. Reopen storage after changes; stale checks can count removed items or duplicate a transferred copy. Unvisited stores, chocobo saddlebags and other characters are excluded. Retainer materia is saved; dresser and armoire contents have no melds.
- Custom/negative XIVGear item IDs are unsupported. Invalid imports report an error and leave the previous saved sets intact.
- Saved target sets are shared by the plugin configuration. Live inventory is read from the current character; storage checks persist locally and are isolated by character content ID and source key.

## Build from source

Requires .NET 10 SDK and Dalamud API 15 assemblies installed by XIVLauncher. No need to compile Dalamud itself. From this directory run:

```powershell
.\build.ps1
```

The script uses `DALAMUD_HOME` when set, otherwise looks for the developer assemblies or the newest numbered Dalamud installation. Build output is `bin/Release/GearScout.dll`, with its generated manifest alongside it. Game/Dalamud updates may require rebuilding or updating the SDK/API use. If using a custom launcher installation, set `DALAMUD_HOME` to its Dalamud assembly folder first.

Run the importer and ownership checks:

```powershell
dotnet run --project Tests/GearScout.Tests.csproj
# Also check the supplied example against the live XIVGear API:
dotnet run --project Tests/GearScout.Tests.csproj -- --live
```

## Validation status

Compiled against the installed Dalamud 15.0.3.9 assemblies. Automated checks cover imports, materia, loot rules, saved-storage replacement, serialization, character separation, cached ownership and invalid scans. The supplied Bard URL has also been tested against the live API. Native storage readiness/reads/transfers, in-game loading and glow appearance still need manual verification; compilation does not prove those behaviours.

Manual acceptance checks:

1. Load the plugin; open `/gearscout`; import the example on Bard and verify the 11 rows.
2. Switch to another job and verify the Bard set disappears. Switch back and verify it returns.
3. Import another Bard set or a multi-set sheet and switch between entries in the dropdown.
4. Move a target item between equipped gear, Armoury Chest and bags; verify ownership updates. Store it outside checked storage and verify it appears missing.
5. Test a two-ring target with one matching physical copy, then two.
6. Reload the plugin and verify saved sets and per-job selection persist. Log into a different character and verify ownership is recalculated.
7. Try an invalid URL and a failed network connection; verify the existing saved sets remain available.
8. Refresh a set saved in 0.1.0; check that materia targets appear. Hover a mismatched piece and compare the lists with the game's item tooltip.
9. Test reordered correct melds, a wrong grade, duplicate materia, missing overmelds and an extra meld on a target with no materia.
10. With two copies of the same gear, verify the correctly melded copy is selected even if the other copy is equipped. Check **Needs work only** keeps mismatched melds visible.
11. With a matching direct gear drop, open the Need/Greed window and check for a soft blue outline around the icon. Non-target items and pass-only items must not glow. Close the checklist and verify the glow remains.
12. Roll, pass or let the matching drop resolve and verify the glow clears. Test duplicate loot slots with one rolled and one still eligible, and scroll a list longer than the visible area.
13. Move and rescale the loot window; check glow alignment and clipping. Hide the game UI and close the loot window to confirm no stale glow remains. Verify mouse and controller loot controls work normally.
14. Disable the glow, include other jobs, remove a target set and change jobs; verify the highlighted items follow the settings immediately. Unload the plugin while the loot window is open and confirm no highlight remains.
15. With the checklist closed, verify an owned wanted item does not glow. Move it out of checked storage and verify the glow appears after the ownership refresh; return it and verify it clears. Wrong melds on an owned copy must not trigger a glow. Test a two-identical-ring target with one and then two owned copies.
16. Open two different retainers' inventories/equipment, allowing a few seconds for loading. Verify independent sources and item counts; rapidly switch retainers or interrupt loading and ensure no source is misattributed or replaced by incomplete data.
17. Open the dresser and armoire. Check gear on another page/filter, expanded outfit pieces and an empty source. Verify counts against the game and correct item IDs. Reload GearScout and zone away: saved ownership should persist for this character only.
18. Move target gear between a retainer/dresser/armoire and player bags while the storage stays open. Verify refreshed snapshots remove withdrawn items and combine with live bags without persisting duplicate copies. Leave a source empty and verify the old saved items are removed.
19. Check cached retainer materia and a dresser/armoire unmelded target. Hover the location to see last-checked time. Forget a closed source and verify its gear no longer contributes. Check loot-glow suppression for a wanted item held only in saved storage.
20. Select wanted missing loot and verify its native item name is blue and the icon has a blue **!** badge. Select a non-target/owned/rolled item and verify its name has the original game color. Test switching repeatedly between targets and non-targets, duplicate loot, localized names, UI scaling, disabling highlighting, closing/reopening the loot window and unloading GearScout with a target selected.

## Implementation and references

`Plugin.cs` handles lifecycle and framework-thread updates. `MainWindow.cs` draws the UI. `InventoryReader.cs` uses Dalamud's inventory service. `XivGearImporter.cs` fetches and parses sets without game access. `Models.cs` contains copy-aware matching. HTTP work is asynchronous; game access and configuration changes run on the framework thread. Importing sends the supplied link to XIVGear's public API; no character or inventory data is sent.

`LootMatcher.cs` implements job scope, item-ID normalization and conservative roll-eligibility matching. `LootHighlighter.cs` reads the current NeedGreed list and live loot slots on each draw, finds visible item-icon bounds and draws the soft blue ImGui outline and punctuation badge without adding input handlers. A NeedGreed PreDraw listener colors exact, localized matching name text. `LootNameFormatting.cs` removes inline rarity foreground/glow payloads so they cannot override the blue base color, retaining other payloads. It calls native SetText to invalidate the layout cache. Original encoded text and colors restore through traversal of currently live nodes, only when those fields still match GearScout's applied values. PreFinalize and disposal restore remaining fields and unregister listeners. The icon overlays share ImGui's foreground layering relative to native windows. Native name coloring and badge appearance still need live visual verification.

`StorageScanner.cs` reads only manually opened, loaded sources after a short stabilization delay; retainer identity must agree with ItemOrderModule and every container in a snapshot must be loaded. Dresser reads use the complete agent item array after population, and armoire reads require loaded Cabinet state. No requests, withdrawal actions or hooks are sent to the game. `StorageCache.cs` replaces complete source snapshots and combines them with live inventory for the current character. Native data unavailable at a location leaves its previous saved snapshot unchanged. Changed checks save immediately; unchanged timestamps refresh at most every 30 seconds. Save failures retry and are surfaced in the storage status.

- XIVGear API: https://github.com/xiv-gear-planner/gear-planner/blob/main/API_DOC.md
- Dalamud plugin development: https://dalamud.dev/plugin-development/getting-started/
- Dalamud template loading instructions: https://github.com/goatcorp/SamplePlugin#activating-in-game

This version was authored with AI assistance. If submitting to the official Dalamud repository, review its current restrictions and AI usage policy, disclose assistance, and ensure you understand and maintain the code: https://dalamud.dev/plugin-publishing/
