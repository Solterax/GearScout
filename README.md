# GearScout

Build gear sets inside the game or import them from XIVGear. Switch between saved sets for your active class/job and see which pieces are equipped, owned, or missing from checked storage.

## Install

1. Open Dalamud settings with `/xlsettings`.
2. Under **Experimental → Custom Plugin Repositories**, add and enable:

   ```text
   https://raw.githubusercontent.com/Solterax/DalamudPlugins/main/pluginmaster.json
   ```

3. Save, open `/xlplugins`, and install **GearScout**.
4. Open `/gearscout`, paste an XIVGear link, and click **Import**.

If you already loaded GearScout as a development plugin, disable that copy before installing this one.

The shared repository lists all Solterax plugins. The previous GearScout-specific repository URL continues to work. Existing users can replace it with the shared URL in Dalamud settings; GearScout's plugin identity and saved sets stay the same.

Example Bard link: https://xivgear.app/sl/aadb9265-3645-45ae-8af6-3215eb26d637

## Build a set in game

1. Open `/gearscout` and click **New set**. The set belongs to the job you are currently playing.
2. Give it a name. Click **Select gear...** beside a slot (or click the existing piece) to open the search text box.
3. Search by item name, words in any order, or item ID. Results are filtered to equipment that fits the slot and can be equipped by the set's job, sorted by item level. Higher-level gear is included for planning ahead.
4. Select a result, fill other slots, then click **Save set**. **Clear** empties a slot; **Cancel** discards the draft. Empty sets can also be saved and filled later.

Use **Edit set** to change or rename a local set, or click an item name directly in the checklist to open its slot search. **Edit local copy** on an imported set creates an independent custom copy; refreshing the original XIVGear link cannot overwrite it. Local sets participate in ownership and missing-loot checks immediately after saving.

The builder and search popup have fixed dimensions; scrolling stays inside their item lists.

Search reads installed game data locally and builds its index in small batches. The first search may briefly show **Loading local gear data...**. It includes both ring slots and prevents selecting the same unique ring twice. This is a gear planner; it does not create/equip an FFXIV character gearset. New or changed local pieces initially have no specified materia target; use **Targets** to choose up to five materia, including repeats. Unchanged imported-copy pieces retain their meld plans. Empty targets explicitly request no melds.

## Build a job-independent glam set

Click **New glam set**, name the outfit, and fill its slots through the same item search. Glam searches filter by slot only, showing gear from every job. The set appears as **[Glam]** in the selector on every job and can be edited, renamed, saved and reloaded like a regular set.

Each glam slot can contain as many different pieces as you want. In the editor, click **Add gear...** under a slot, then check multiple search results; the search stays open while you select items or change the search text. Uncheck an item to remove it, or click **Done** and use the individual **Remove** buttons in the editor. Click **Save set** to keep the list. Selecting an item already in that slot does not add a duplicate.

The main checklist groups all selected pieces by slot, with a separate ownership status and location for each piece. **Needs work only** shows the missing appearances. Normal gear sets continue to allow one selected item per slot.

Glam sets track appearances, hide the materia column, and include saved dresser/armoire/retainer ownership. One owned appearance can satisfy repeated slots, so the same unique ring appearance can be used on both hands. Missing glam pieces participate in the same all-job loot indicators. Regular sets retain job filtering and separate-copy requirements. This plans an outfit; it does not apply a glamour plate or change the game's equip rules.

## Features and scope

- Import individual sets or multi-set sheets and choose between saved sets.
- Create, rename and edit local sets with a slot/job-filtered gear search; edit imported sets as independent local copies.
- Display regular sets for the active class/job, plus every glam set. Bard gear sets appear on Bard, not Archer; glam sets appear on both.
- Count equipped items, inventory bags, and Armoury Chest items on the current character. Refresh approximately once per second while open.
- Show a location for matching gear and require separate physical copies for duplicate target items.
- Compare melded materia against the imported target by type and grade, including duplicate materia and all five overmeld slots. Hover the Materia column to see required, current, missing and extra melds. Slot order is ignored.
- Prefer correctly melded owned copies when duplicate gear exists. Use **Needs work only** to include both missing gear and incorrect or unknown melds.
- Save target sets and per-job selection across sessions. Reimporting the same link refreshes its saved sets.
- Save per-character checks of retainer bags/equipped gear, the glamour dresser (including expanded outfit pieces), and the armoire. Saved items count as owned after those windows close and across plugin/game restarts.
- Subtle blue glow and blue **!** badge on wanted, missing item icons in the game's standard loot-roll window, with the matching selected item name (and list-row names where present) colored blue. Works while the checklist is closed and always checks **all saved jobs**, so farming another job's gear while playing your main job still works. Disable with **Blue glow on missing set items**.

The loot current-job-only option has been removed. Old saved values no longer affect loot checks. The checklist selector shows your current job's regular gear sets plus every glam set.

## Wanted-item chat messages

**Chat messages for wanted drops and new items** is enabled by default and works independently of the blue glow, including while the checklist is closed. GearScout prints local chat messages when a missing wanted item appears as eligible loot and when you newly obtain a wanted item. Each message includes the item name and matching set names, with their job or Glam label. It checks all saved jobs and glam lists.

For example: `[GearScout] Wanted loot dropped: Example Hat — for Bard plan (BRD), Hat collection (Glam).` A later acquisition produces an **Obtained** message. Each chest item is announced once, even if the loot window is reopened. Drop notices name only sets still missing the item, using the same ownership/copy rules as the glow.

The first complete inventory scan after login, reload or loading establishes a silent baseline. Existing inventory, cached storage items, extra copies of a known item and set edits do not produce acquisition messages. Checking storage is silent; transfers while storage is open or shortly after closing are also suppressed. During a monitored character session, an item already observed as owned is not announced again if it is moved, removed and reacquired. Missing or unvisited storage is still limited by the last-known ownership checks described below. These messages appear only in your local chat; GearScout does not post to party or other players.

## Melding planner

Select a normal gear set and click **Melding planner**. Glam lists do not use melding plans.

1. Select the exact copy for each target slot. The selector shows location, inventory slot and HQ/NQ quality. Different target slots must use different physical copies. Missing gear, unspecified targets, unknown quality/melds and impossible targets are flagged; older storage checks may need reopening to capture quality.
2. Use **Edit targets** to choose materia by name or item ID, including duplicates. Imported edits create a local copy; local edits update that set. Targets can also be edited through **Targets** in the set builder.
3. Review the bottom-first removal/rebuild steps, recovery percentages and melding success rates. The planner keeps the longest legal existing prefix and chooses a remaining order with the lowest expected attempt count. It handles one-slot accessories, guaranteed-only gear, HQ/NQ rates, minimum item levels and restricted overmeld grades using installed game data. It flags wasted stats and unknown stat caps. Cap calculations use base item/HQ stats; custom relic allocations are not included.
4. Choose a **90%, 95% or 99% whole-plan budget**. The shopping list separates bag stock and saved retainer stock, minimum and average attempts, confidence budgets and additional materia to obtain. Potential retrieval refunds are listed separately and credited only after they actually reach bags. Missing or invalid pieces make the estimate explicitly partial. Retainer quantities are last-known; withdraw them to make them usable.
5. Set the total **Meld-attempt limit** and click **Start automatic melding**. GearScout opens the game's melding interface, selects the exact gear and materia, retrieves bottom-first where planned, and confirms one attempt at a time. It verifies the gear and bag-count result before moving on or retrying a failed overmeld. The plan, stock and probabilities refresh after inventory changes. **Stop automatic melding** prevents further confirmations; an already confirmed attempt may still finish.

Items with zero materia sockets, including socketless relic weapons, are excluded before their target/current materia is checked. They cannot generate removal or melding actions. Automation defaults to ready live pieces with unfinished actions; **Include** chooses which pieces to process. Missing, invalid, saved-only and already-complete pieces are skipped with reasons and do not block the other selected pieces. Live copies are preferred to saved copies when choosing defaults.

**Open melding window** invokes the game's Materia Melding/Advanced Materia Melding general action. The planner shows whether it opened, was blocked by the game, or timed out. Opening does not confirm or spend materia.

Every new meld request reserves one attempt from the limit before opening confirmation, including possible confirmation by another plugin. Failed overmelds can retry only while that budget and bag stock remain. Starting requires at least the minimum materia in bags and a sufficient attempt limit; potential retrieval refunds are excluded from this preflight. Retrieval also requires a free inventory slot. The limit counts meld attempts, not materia potentially lost during planned retrievals.

Leave the game's melding controls alone while running. GearScout stops on unexpected dialogs or results, moved/changed gear, unavailable inventory, character/loading changes, edited/removed targets, missing materia or a 25-second operation timeout. Retainer scanning cannot run at the same time. It requests single attempts rather than native batch/repeat melding; changed success rates or consumption of more than one materia stop further actions. Stop cannot undo an already confirmed action or control actions initiated independently by other plugins or the player.

Confidence describes the probability of having enough attempts for the remaining valid plan, assuming independent attempts at the displayed rates and no retrieval refunds. It is not a guarantee. Per-materia-type budgets are allocated so their combined completion probability meets the selected confidence; the displayed chance with bag stock uses the whole plan, rather than applying that confidence separately to each slot.

The loot glow appears only for items required by a saved set and missing from live inventory and the current character's saved storage checks. Owned items suppress the glow regardless of materia. If one set requires two identical rings and you own only one, another copy is still missing; alternative saved sets can share the same owned copy. Ownership refreshes approximately once per second even while the checklist is closed, and unknown live inventory suppresses the glow.

The glow, badge and blue name appear only while Need or Greed is available and clear once you roll, pass, the item resolves, or the loot window closes. The original native name color is restored when highlighting stops or the plugin unloads. It uses exact gear item IDs: coffers and exchange tokens are not mapped to their rewards. The glow and badge are click-through visual overlays; they do not select or roll on items.

**Missing** means absent from checked live storage and saved checks. Saddlebags, items listed for sale, and other characters are excluded. HQ quality, food, and relic stat allocations are not validated. Loose materia in bags does not count as melded materia. No character or inventory data is sent to XIVGear; imports only send the supplied link to its public API.

## Check storage once, remember it locally

1. Open a summoning bell to its retainer list. In `/gearscout`, expand **Saved storage checks** and click **Scan all retainers**. GearScout opens each available retainer's inventory, waits for a complete local save, returns to the list and continues. Progress and a **Stop retainer scan** button appear during the scan. You can also open retainer inventories individually. Open retainer equipment too if it has not loaded with the bags.
2. Open your glamour dresser and armoire, allowing each to finish loading. Opening any supported storage automatically saves its first complete check, even with the GearScout checklist closed.
3. Moving items triggers another check and local save. Changes are also detected while storage stays open, with a short follow-up period after closing to catch completed transfers. **Scan open storage** remains available as an immediate recheck; no manual scan is required for normal use.

The full loaded contents are saved in Dalamud's local GearScout configuration and associated with the current character. Retainers have independent bag/equipment checks. A new check replaces the old contents of that source, including when it becomes empty; unopened or partially loaded storage does not erase a previous check. Retainer materia is retained, and dresser/armoire items are treated as unmelded.

The retainer loop only navigates the list, inventory and Quit menus. It does not move items, collect ventures or accept confirmation dialogs. It stops on character changes, loading timeouts, unexpected dialogs or a different retainer being selected. **Stop** leaves the current game window open and retains completed saves.

These are last-known contents, not a remote live inventory. Reopen storage after changes made while GearScout was disabled. Stale checks can otherwise count a removed item, or count a transferred copy twice. **Forget** removes an old source from ownership calculations; an open storage source will be detected again. Sources not yet visited have no saved ownership data. Nothing is uploaded to GitHub or any inventory service.

After updating from 0.1.0, click **Refresh from link** on existing sets to load their materia targets. Old saved sets remain available and show unknown materia until refreshed. An explicitly empty target expects an unmelded item; extra materia is shown as a mismatch.

Compilation, retainer-scan sequencing/cancellation/timeouts, editor search/draft rules, importer checks, inventory/materia matching, loot rules, saved-storage replacement/persistence/character isolation, and the supplied live XIVGear URL have been verified. The editor UI, native retainer navigation, storage reads and transfers, in-game loading, inventory/job transitions, and loot indicator appearance still require manual verification.

## Development disclosure

Authored with AI assistance. Before submitting to the official Dalamud repository, review its current [restrictions and AI usage policy](https://dalamud.dev/plugin-publishing/).
