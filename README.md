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
- Save target sets and per-job selection across sessions. Reimporting the same link refreshes its saved sets.

**Missing** means absent from the checked storage. Retainers, saddlebags, glamour storage, and other characters are excluded. Materia, HQ quality, food, and relic stat allocations are not validated. No character or inventory data is sent to XIVGear; imports only send the supplied link to its public API.

This is an initial version. Compilation, importer checks, inventory-matching logic, and the supplied live XIVGear URL have been verified; in-game loading, layout, and inventory/job transitions still require manual verification.

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
