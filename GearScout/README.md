# GearScout 0.2.0

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
- **Equipped** means a matching copy is worn; **Owned** means it is in a checked bag or Armoury Chest; **Missing** means absent from the checked storage.
- Duplicate items require enough physical copies. One ring cannot satisfy two ring slots. The side on which a matching ring is worn does not matter.
- HQ and NQ copies both count. Food, relic stat allocations, and item quality are not validated.
- Materia is compared by item ID (type and grade), with duplicates counted separately. Order does not matter, and all five meld slots are checked. Exact matches require no missing or extra materia.
- Hover the Materia column to see expected, current, missing and extra melds. Loose materia does not count as melded gear. The matcher allocates owned copies across the set to prefer correct melds, including duplicate rings.
- Existing sets from 0.1.0 have unknown meld targets until you click **Refresh from link**. A missing or unreadable materia list is never shown as correct. An explicit empty target expects no materia.
- Retainers, chocobo saddlebags, glamour dresser, armoire and other characters are excluded. An item in those places can appear as missing. Move it to your bags or Armoury Chest to have it counted.
- Custom/negative XIVGear item IDs are unsupported. Invalid imports report an error and leave the previous saved sets intact.
- Saved target sets are shared by the plugin configuration; ownership is always read from the current character and is never persisted.

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

Compiled against the installed Dalamud 15.0.3.9 assemblies. Automated checks cover imports, invalid links, duplicate rings, materia type/grade/count checks, overmelds, copy allocation and saved-target serialization. The supplied Bard URL has also been tested against the live API. In-game loading, appearance and live inventory/job transitions still need manual verification; compilation does not prove those behaviours.

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

## Implementation and references

`Plugin.cs` handles lifecycle and framework-thread updates. `MainWindow.cs` draws the UI. `InventoryReader.cs` uses Dalamud's inventory service. `XivGearImporter.cs` fetches and parses sets without game access. `Models.cs` contains copy-aware matching. HTTP work is asynchronous; game access and configuration changes run on the framework thread. Importing sends the supplied link to XIVGear's public API; no character or inventory data is sent.

- XIVGear API: https://github.com/xiv-gear-planner/gear-planner/blob/main/API_DOC.md
- Dalamud plugin development: https://dalamud.dev/plugin-development/getting-started/
- Dalamud template loading instructions: https://github.com/goatcorp/SamplePlugin#activating-in-game

This version was authored with AI assistance. If submitting to the official Dalamud repository, review its current restrictions and AI usage policy, disclose assistance, and ensure you understand and maintain the code: https://dalamud.dev/plugin-publishing/
