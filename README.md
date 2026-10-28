# GearScout

Import XIVGear gear sets into an in-game Dalamud checklist. Switch between saved sets for your active class/job and see which pieces are equipped, owned, or missing from checked storage.

## Install

1. Open Dalamud settings with `/xlsettings`.
2. Under **Experimental → Custom Plugin Repositories**, add and enable:

   ```text
   https://raw.githubusercontent.com/Solterax/GearScout/main/pluginmaster.json
   ```

3. Save, open `/xlplugins`, and install **GearScout**.
4. Open `/gearscout`, paste an XIVGear link, and click **Import**.

If you already loaded GearScout as a development plugin, disable that copy before installing this one.

Example Bard link: https://xivgear.app/sl/aadb9265-3645-45ae-8af6-3215eb26d637

## Features and scope

- Import individual sets or multi-set sheets and choose between saved sets.
- Display only sets whose imported job matches the active class/job. Bard targets appear on Bard, not Archer.
- Count equipped items, inventory bags, and Armoury Chest items on the current character. Refresh approximately once per second while open.
- Show a location for matching gear and require separate physical copies for duplicate target items.
- Compare melded materia against the imported target by type and grade, including duplicate materia and all five overmeld slots. Hover the Materia column to see required, current, missing and extra melds. Slot order is ignored.
- Prefer correctly melded owned copies when duplicate gear exists. Use **Needs work only** to include both missing gear and incorrect or unknown melds.
- Save target sets and per-job selection across sessions. Reimporting the same link refreshes its saved sets.
- Save per-character checks of retainer bags/equipped gear, the glamour dresser (including expanded outfit pieces), and the armoire. Saved items count as owned after those windows close and across plugin/game restarts.
- Subtle blue glow and blue **!** badge on wanted, missing item icons in the game's standard loot-roll window, with the matching selected item name (and list-row names where present) colored blue. Works while the checklist is closed. Checks **all saved jobs by default**, so farming another job's gear while playing your main job still works. Tick **Loot: current job only** to restrict it. Disable with **Blue glow on missing set items**.

Version 0.4.1 switches older configurations to all-job loot checking once. You can re-enable the current-job-only option afterward. The checklist selector still shows only the job you are playing; loot scope is independent of that selector.

The loot glow appears only for items required by a saved set and missing from live inventory and the current character's saved storage checks. Owned items suppress the glow regardless of materia. If one set requires two identical rings and you own only one, another copy is still missing; alternative saved sets can share the same owned copy. Ownership refreshes approximately once per second even while the checklist is closed, and unknown live inventory suppresses the glow.

The glow, badge and blue name appear only while Need or Greed is available and clear once you roll, pass, the item resolves, or the loot window closes. The original native name color is restored when highlighting stops or the plugin unloads. It uses exact gear item IDs: coffers and exchange tokens are not mapped to their rewards. The glow and badge are click-through visual overlays; they do not select or roll on items.

**Missing** means absent from checked live storage and saved checks. Saddlebags, items listed for sale, and other characters are excluded. HQ quality, food, and relic stat allocations are not validated. Loose materia in bags does not count as melded materia. No character or inventory data is sent to XIVGear; imports only send the supplied link to its public API.

## Check storage once, remember it locally

1. Update GearScout, then manually open each retainer's inventory at a summoning bell. Leave it open for a few seconds until loading completes. Open retainer equipment too if it has not loaded with the bags.
2. Manually open your glamour dresser and armoire, allowing each to finish loading.
3. In `/gearscout`, expand **Saved storage checks** to see the sources, item counts and last-checked times. **Scan open storage** requests an immediate recheck; completed checks also save automatically while those stores are open.

The full loaded contents are saved in Dalamud's local GearScout configuration and associated with the current character. Retainers have independent bag/equipment checks. A new check replaces the old contents of that source, including when it becomes empty; unopened or partially loaded storage does not erase a previous check. Retainer materia is retained, and dresser/armoire items are treated as unmelded.

These are last-known contents, not a remote live inventory. Reopen storage after moving/removing items, especially if changes happened with GearScout disabled. Stale checks can otherwise count a removed item, or count a transferred copy twice. **Forget** removes an old source from ownership calculations; an open storage source will be detected again. Sources not yet visited have no saved ownership data. Nothing is uploaded to GitHub or any inventory service.

After updating from 0.1.0, click **Refresh from link** on existing sets to load their materia targets. Old saved sets remain available and show unknown materia until refreshed. An explicitly empty target expects an unmelded item; extra materia is shown as a mismatch.

Compilation, importer checks, inventory/materia matching, loot rules, saved-storage replacement/persistence/character isolation, and the supplied live XIVGear URL have been verified. Native storage reads and transfers, in-game loading, inventory/job transitions, and the loot glow's alignment, clipping and appearance still require manual verification.

## Build and test

Requires .NET 10 SDK and Dalamud API 15 assemblies installed by XIVLauncher.

```powershell
.\GearScout\build.ps1
dotnet run --project .\GearScout\Tests\GearScout.Tests.csproj
# Optional live API check:
dotnet run --project .\GearScout\Tests\GearScout.Tests.csproj -- --live
```

The build script locates Dalamud automatically, or uses `DALAMUD_HOME` if set. See [the development guide](GearScout/README.md) for ownership rules and manual acceptance checks.

## Publish an update

1. Increase `<Version>` in `GearScout/GearScout.csproj`.
2. Run `./prepare-release.ps1`. This builds the plugin, runs the offline checks, creates `artifacts/GearScout-plugin.zip`, and regenerates `pluginmaster.json` from the compiled manifest.
3. Commit the source changes and generated index, then push to `main`.
4. Create a GitHub release with the matching version tag (for example `v0.1.1`) and upload `artifacts/GearScout-plugin.zip` as its release asset. Keep the asset name unchanged.

The index and release must use the same assembly version. The script prepares local files; it does not publish to GitHub itself. New releases still require verification against the current game/Dalamud version.

## Development disclosure

Authored with AI assistance. Before submitting to the official Dalamud repository, review its current [restrictions and AI usage policy](https://dalamud.dev/plugin-publishing/).
