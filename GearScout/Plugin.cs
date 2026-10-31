using System.Net.Http;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Inventory.InventoryEventArgTypes;

namespace GearScout;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IPlayerState Player { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    private readonly WindowSystem windows = new("GearScout");
    private readonly MainWindow window;
    private readonly SetEditorWindow setEditor;
    private readonly MeldingWindow meldingWindow;
    private bool openMeldRequested;
    private long meldingOpenDeadline;
    private readonly LootHighlighter lootHighlighter;
    private readonly StorageScanner storageScanner = new();
    private readonly WantedItemTracker wantedItems = new();
    private readonly OwnershipCache ownershipCache = new();
    private uint lastJobId;
    internal long SettingsRevision { get; private set; }
    private readonly ZoneEquipSession zoneEquip = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<uint, string> itemNames = [];
    private Task<List<TrackedSet>>? pendingImport;
    private (string Name, ulong Owner, string Job)? pendingGearCapture;
    internal bool SavingCurrentGear => pendingGearCapture != null;
    private long nextScan;
    private ulong characterId;
    private long nextStorageScan;
    private long nextStorageSave;
    private bool storageScanRequested;
    private bool storageSavePending;
    private long storageRefreshUntil;
    private bool storageRefreshPending;
    private long storageTransferUntil;
    private List<OwnedItem>? lootOwnership;
    private HashSet<uint>? missingLootTargets;
    internal HashSet<uint> MissingLootTargets
    {
        get
        {
            if (missingLootTargets == null || !ReferenceEquals(lootOwnership, Owned))
            {
                missingLootTargets = LootMatcher.MissingItems(Config.Sets, Owned);
                lootOwnership = Owned;
            }
            return missingLootTargets;
        }
    }
    private static readonly string[] StorageAddons = ["InventoryRetainer", "InventoryRetainerLarge", "RetainerCharacter", "MiragePrismPrismBox", "Cabinet", "CabinetWithdraw", "InventoryBuddy"];

    internal Configuration Config { get; }
    internal string CurrentJob { get; private set; } = "";
    internal List<OwnedItem>? Owned { get; private set; }
    internal bool Importing => pendingImport != null;
    internal string Message { get; private set; } = "";
    internal bool MessageIsError { get; private set; }
    internal ulong CharacterId => characterId;
    internal string StorageStatus { get; private set; } = "Open each storage location in game to save its contents.";
    internal GearCatalog Catalog { get; }
    internal ZoneCatalog Zones { get; }
    internal string ZoneEquipStatus => zoneEquip.Status;
    internal MeldData MeldingData { get; }
    internal MeldGuide MeldingGuide { get; } = new();
    internal AutomaticMeldController AutoMelding { get; }
    internal EquipSetController Equipper { get; }
    internal string MeldingWindowStatus { get; private set; } = "";
    internal RetainerScanController RetainerScan { get; }
    internal bool StorageSavePending => storageSavePending;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new();
        Config.StorageSnapshots ??= [];
        foreach (var set in Config.Sets) set.AutoEquipTerritories ??= [];
        // Loot always checks all saved jobs. Resaving also drops the obsolete job-scope setting.
        if (Config.Version < 3)
        {
            Config.Version = 3;
            storageSavePending = true;
        }
        window = new MainWindow(this);
        Catalog = new GearCatalog(Data);
        Zones = new ZoneCatalog(Data);
        MeldingData = new MeldData(Data);
        AutoMelding = new AutomaticMeldController(this);
        Equipper = new EquipSetController(this);
        meldingWindow = new MeldingWindow(this);
        RetainerScan = new RetainerScanController(this);
        setEditor = new SetEditorWindow(this);
        lootHighlighter = new LootHighlighter(this);
        lootHighlighter.Initialize();
        windows.AddWindow(window);
        windows.AddWindow(setEditor);
        windows.AddWindow(meldingWindow);
        Commands.AddHandler("/gearscout", new CommandInfo(OnCommand) { HelpMessage = "Open XIVGear gear tracking." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.Draw += lootHighlighter.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        Framework.Update += Update;
        Client.TerritoryChanged += OnTerritoryChanged;
        Client.Logout += OnLogout;
        Inventory.InventoryChanged += OnInventoryChanged;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, StorageAddons, OnStorageOpened);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, StorageAddons, OnStorageClosing);
    }

    private void OnCommand(string command, string args) => Open();
    private void Open() => window.IsOpen = true;
    internal void CreateSet() { if (!string.IsNullOrEmpty(CurrentJob)) setEditor.Create(CurrentJob); }
    internal void CreateGlamSet() => setEditor.CreateGlam();
    internal void SaveCurrentGear(string name)
    {
        if (SavingCurrentGear) throw new InvalidOperationException("A current-gear save is already pending.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new ArgumentException("Enter a set name between 1 and 120 characters.");
        if (characterId == 0 || string.IsNullOrEmpty(CurrentJob)) throw new InvalidOperationException("Finish loading your character first.");
        pendingGearCapture = (name, characterId, CurrentJob);
        Message = "Saving current gear..."; MessageIsError = false;
    }
    internal void EditSet(TrackedSet set, string? slot = null) => setEditor.Open(set, slot);
    internal void OpenMelding(TrackedSet set) => meldingWindow.Open(set);
    internal void EquipSet(TrackedSet set)
    {
        zoneEquip.Cancel();
        try { Equipper.Start(set); nextScan = 0; Message = ""; MessageIsError = false; }
        catch (Exception ex) { ReportError(ex.Message); }
    }
    internal void RequestMeldingWindow() { openMeldRequested = true; MeldingWindowStatus = "Opening the game's melding window..."; }

    private void OnLogout(int type, int code) => zoneEquip.Cancel();
    private void OnTerritoryChanged(uint territory)
    {
        zoneEquip.Cancel();
        if (!Config.AutoEquipOnEntry || !Config.Sets.Any(s => !s.IsGlam && s.AutoEquipTerritories.Contains(territory))) return;
        zoneEquip.Enter(territory, Player.IsLoaded ? Player.ContentId : 0, Environment.TickCount64);
        Owned = null;
        nextScan = 0;
    }

    internal void AssignZone(TrackedSet target, uint territory)
    {
        var previous = Config.Sets.Select(s => (Set: s, Zones: s.AutoEquipTerritories.ToList())).ToList();
        try { ZoneEquipRules.Assign(Config.Sets, target, territory); Save(); }
        catch (Exception ex)
        {
            foreach (var entry in previous) entry.Set.AutoEquipTerritories = entry.Zones;
            ReportError("Could not save zone assignment: " + ex.Message);
        }
    }
    internal void RemoveZone(TrackedSet target, uint territory)
    {
        var previous = target.AutoEquipTerritories.ToList();
        target.AutoEquipTerritories.RemoveAll(id => id == territory);
        try { Save(); }
        catch (Exception ex) { target.AutoEquipTerritories = previous; ReportError("Could not save zone assignment: " + ex.Message); }
    }

    private void UpdateZoneEquip(bool ready)
    {
        if (!Config.AutoEquipOnEntry) { zoneEquip.Cancel(); return; }
        if (!zoneEquip.Pending) return;
        var set = zoneEquip.Tick(Client.TerritoryType, characterId, CurrentJob, ready && Owned != null,
            !Equipper.CanStart, Config.Sets, Environment.TickCount64);
        if (set != null)
        {
            try
            {
                Equipper.Start(set);
                nextScan = 0;
                Config.SelectedByJob[CurrentJob] = set.Id;
                Chat.Print($"Auto-equipping {set.Name} ({CurrentJob}) for {Zones.Name(Client.TerritoryType)}.", "GearScout");
            }
            catch (Exception ex)
            {
                ReportError("Auto-equip could not start: " + ex.Message);
                Chat.Print("Auto-equip could not start: " + ex.Message, "GearScout");
                Log.Warning(ex, "Zone auto-equip failed");
            }
        }
        else if (!zoneEquip.Pending && !string.IsNullOrWhiteSpace(zoneEquip.Status))
            Chat.Print(zoneEquip.Status, "GearScout");
    }

    internal void SaveLocalSet(TrackedSet set)
    {
        if (string.IsNullOrWhiteSpace(set.Name) || (!set.IsGlam && GearSearch.JobBit(set.Job) == 0))
            throw new ArgumentException("Give the set a name and a valid job.");
        if (set.IsGlam) set.Job = "";
        var selectionJob = set.IsGlam ? CurrentJob : set.Job;
        var oldSets = Config.Sets;
        var hadSelection = Config.SelectedByJob.TryGetValue(selectionJob, out var oldSelection);
        set.SourceUrl = "";
        Config.Sets = Config.Sets.Where(s => s.Id != set.Id).Append(set).ToList();
        if (!string.IsNullOrEmpty(selectionJob)) Config.SelectedByJob[selectionJob] = set.Id;
        try { Save(); }
        catch
        {
            Config.Sets = oldSets;
            if (hadSelection) Config.SelectedByJob[selectionJob] = oldSelection;
            else Config.SelectedByJob.Remove(selectionJob);
            throw;
        }
    }

    internal void RequestStorageScan()
    {
        storageScanRequested = true;
        nextStorageScan = 0;
        StorageStatus = "Checking open storage. Wait for it to finish loading.";
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (events.Count == 0) return;
        storageRefreshPending = true;
        storageRefreshUntil = Environment.TickCount64 + 3000;
        // Coalesce slot/stack transfer bursts and read the server-confirmed contents after they settle.
        nextStorageScan = Environment.TickCount64 + 150;
        nextScan = Math.Min(nextScan, nextStorageScan);
    }

    private void OnStorageOpened(AddonEvent type, AddonArgs args)
    {
        storageRefreshPending = true;
        storageRefreshUntil = Environment.TickCount64 + 3000;
        nextStorageScan = 0;
    }

    private void OnStorageClosing(AddonEvent type, AddonArgs args)
    {
        storageTransferUntil = Environment.TickCount64 + 3000;
        storageRefreshPending = true;
        storageRefreshUntil = Environment.TickCount64 + 3000;
        nextStorageScan = 0;
        // PreFinalize still owns valid data. Capture it before teardown, then allow short follow-up reads.
        if (characterId != 0 && Player.IsLoaded && Player.ContentId == characterId) UpdateStorage();
    }

    internal void ForgetStorage(string key)
    {
        Config.StorageSnapshots.RemoveAll(s => s.CharacterId == characterId && s.Key == key);
        storageSavePending = true;
        nextStorageSave = 0;
        nextScan = 0;
        Owned = null;
    }

    internal void StartImport(string url)
    {
        if (Importing) return;
        try
        {
            XivGearImporter.ValidateUrl(url);
            Message = "Importing from XIVGear...";
            MessageIsError = false;
            pendingImport = XivGearImporter.ImportAsync(http, url, lifetime.Token);
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    private void Update(IFramework framework)
    {
        Catalog.Update();
        Zones.Update();
        // All configuration and game access stays on the framework thread.
        if (pendingImport is { IsCompleted: true })
        {
            var finished = pendingImport;
            pendingImport = null;
            try
            {
                var imported = finished.GetAwaiter().GetResult();
                var source = imported[0].SourceUrl;
                var previous = Config.Sets.Where(s => s.SourceUrl == source).ToList();
                foreach (var set in imported)
                {
                    var old = previous.FirstOrDefault(s => s.Job == set.Job && s.Name == set.Name);
                    if (old == null) continue;
                    set.Id = old.Id;
                    set.AutoEquipTerritories = old.AutoEquipTerritories.ToList();
                    previous.Remove(old);
                }
                var oldSets = Config.Sets;
                Config.Sets = Config.Sets.Where(s => s.SourceUrl != source).Concat(imported).ToList();
                try { Save(); }
                catch { Config.Sets = oldSets; throw; }
                Message = $"Imported {imported.Count} set(s) for {string.Join(", ", imported.Select(s => s.Job).Distinct())}. Only your current job's sets appear below.";
                MessageIsError = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "XIVGear import failed");
                ReportError(ex is TaskCanceledException ? "Import timed out. Try again." : $"Import failed: {ex.Message}");
            }
        }

        var ready = Client.IsLoggedIn && Player.IsLoaded && Player.ClassJob.IsValid &&
                    !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51];
        var jobId = ready ? Player.ClassJob.RowId : 0;
        var job = ready ? (jobId == lastJobId ? CurrentJob : Player.ClassJob.Value.Abbreviation.ToString().ToUpperInvariant()) : "";
        lastJobId = jobId;
        var contentId = ready ? Player.ContentId : 0;
        if (pendingGearCapture is { } capture)
        {
            pendingGearCapture = null;
            try
            {
                if (!ready || contentId != capture.Owner || job != capture.Job)
                    throw new InvalidOperationException("Character, job or loading changed. Try saving your gear again.");
                if (Equipper.Running || AutoMelding.Running)
                    throw new InvalidOperationException("Wait for equipping or automatic melding to finish before saving your current gear.");
                var set = CurrentGearSet.Build(capture.Name, job, InventoryReader.ReadEquipped(Inventory, Data));
                SaveLocalSet(set);
                Message = $"Saved {set.Name}: {set.Pieces.Count} equipped pieces for {set.Job}.";
                MessageIsError = false;
            }
            catch (Exception ex) { ReportError("Could not save current gear: " + ex.Message); Log.Warning(ex, "Current gear capture failed"); }
        }
        if (job != CurrentJob || contentId != characterId)
        {
            if (contentId != characterId || !ready)
            {
                storageScanner.Reset();
                ownershipCache.Reset();
                nextStorageScan = 0;
                storageScanRequested = false;
                storageRefreshPending = false;
                storageRefreshUntil = 0;
                StorageStatus = "Open each storage location in game to save its contents for this character.";
            }
            CurrentJob = job;
            characterId = contentId;
            Owned = null;
            nextScan = Environment.TickCount64 + 500;
        }
        if (ready) UpdateStorage();
        else wantedItems.Suspend();
        if (!ready && AutoMelding.Running) AutoMelding.Session.Stop("Automatic melding stopped: character or loading changed.");
        if (!ready && Equipper.Running) Equipper.Stop("Character or loading changed.");
        if (openMeldRequested)
        {
            openMeldRequested = false;
            if (ready) ShowNativeMelding();
            else MeldingWindowStatus = "Wait until your character has finished loading, then try again.";
        }
        if (meldingOpenDeadline != 0)
        {
            if (GameGui.GetAddonByName("MateriaAttach").IsVisible) { meldingOpenDeadline = 0; MeldingWindowStatus = "Melding window opened."; }
            else if (Environment.TickCount64 >= meldingOpenDeadline) { meldingOpenDeadline = 0; MeldingWindowStatus = "The game did not open melding. Check Actions & Traits → General for Materia Melding; finish any active action or dialog, then try again."; }
        }
        RetainerScan.Update(ready);
        UpdateZoneEquip(ready);
        // Loot highlighting and chat notifications need fresh ownership with the checklist closed.
        if (!window.IsOpen && !meldingWindow.IsOpen && !MeldingGuide.Running && !Equipper.Running && !zoneEquip.Pending && !Config.HighlightLoot && !Config.NotifyWantedItems)
        {
            Owned = null;
            wantedItems.Suspend();
            return;
        }
        if (!ready || Environment.TickCount64 < nextScan) return;
        nextScan = Environment.TickCount64 + (AutoMelding.Running || Equipper.Running ? 150 : 1000);
        try
        {
            var live = InventoryReader.Read(Inventory, Data);
            var ownership = ownershipCache.Merge(live, Config.StorageSnapshots, characterId);
            // Keep UI plans cached while still reading fresh native slots for automation validation.
            Owned = ownership;
            AutoMelding.Update(ready);
            Equipper.Update(ready);
            UpdateWantedNotifications(live);
        }
        catch (Exception ex)
        {
            Owned = null;
            wantedItems.Suspend();
            AutoMelding.Session.Stop("Automatic melding stopped: live inventory is unavailable.");
            Equipper.Stop("Live inventory is unavailable.");
            nextScan = Environment.TickCount64 + 5000;
            Log.Warning(ex, "Could not read character inventory");
        }
    }

    private unsafe void ShowNativeMelding()
    {
        try
        {
            meldingOpenDeadline = 0;
            if (GameGui.GetAddonByName("MateriaAttach").IsVisible) { MeldingWindowStatus = "Melding window is already open."; return; }
            var manager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (manager == null) { MeldingWindowStatus = "Game actions are unavailable. Try again after loading."; return; }
            var type = FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction;
            // Invoke the same general action as the player's hotbar; Agent.Show alone does
            // not initialize the game's materia-attach workflow.
            uint action = manager->GetActionStatus(type, 13) == 0 ? 13u : 12u;
            var status = manager->GetActionStatus(type, action);
            if (status != 0)
            {
                MeldingWindowStatus = $"The game currently blocks Materia Melding (status {status}). Check that it is unlocked through Waking the Spirit and finish any active action or dialog.";
                return;
            }
            if (!manager->UseAction(type, action)) { MeldingWindowStatus = "The game rejected Materia Melding. Finish the current action or dialog and try again."; return; }
            meldingOpenDeadline = Environment.TickCount64 + 3000;
        }
        catch (Exception ex) { MeldingWindowStatus = "Could not open melding. See /xllog for details."; Log.Warning(ex, "Could not open the game's melding window"); }
    }

    private void UpdateWantedNotifications(IReadOnlyCollection<OwnedItem>? live)
    {
        try
        {
            var transferring = Environment.TickCount64 <= storageTransferUntil ||
                StorageAddons.Any(name => GameGui.GetAddonByName(name).IsVisible);
            var obtained = wantedItems.ObserveOwnership(characterId, Config.Sets, live, Owned, transferring);
            if (Config.NotifyWantedItems) PrintWantedItems("Obtained", obtained);
            CheckWantedLoot();
        }
        catch (Exception ex) { Log.Warning(ex, "Could not update wanted-item chat notifications"); }
    }

    private unsafe void CheckWantedLoot()
    {
        var loot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot.Instance();
        if (loot == null) return;
        var entries = new List<WantedLootItem>();
        foreach (ref readonly var entry in loot->Items)
        {
            if (entry.ItemId == 0 || entry.Time <= 0) continue;
            entries.Add(new(entry.ChestObjectId, entry.ChestItemIndex, entry.ItemId,
                (entry.RollState is FFXIVClientStructs.FFXIV.Client.Game.UI.RollState.UpToNeed or
                    FFXIVClientStructs.FFXIV.Client.Game.UI.RollState.UpToGreed) &&
                entry.RollResult == FFXIVClientStructs.FFXIV.Client.Game.UI.RollResult.UnAwarded));
        }
        var notices = wantedItems.ObserveLoot(characterId, Config.Sets, Owned, entries);
        if (Config.NotifyWantedItems) PrintWantedItems("Wanted loot dropped", notices);
    }

    private void PrintWantedItems(string action, IEnumerable<WantedItemNotice> notices)
    {
        foreach (var notice in notices)
            Chat.Print($"{action}: {ItemName(notice.ItemId)} — for {string.Join(", ", notice.Sets)}.", "GearScout");
    }

    private void UpdateStorage()
    {
        if (Environment.TickCount64 >= nextStorageScan)
        {
            nextStorageScan = Environment.TickCount64 + 500;
            try
            {
                var snapshots = storageScanner.Read(characterId, GameGui, Inventory, Data,
                    Environment.TickCount64 <= storageRefreshUntil);
                var changed = false;
                foreach (var snapshot in snapshots)
                    changed |= StorageCache.Replace(Config.StorageSnapshots, snapshot,
                        storageScanRequested || storageRefreshPending || storageScanner.NewlyOpenedSources.Contains(snapshot.Key));
                if (snapshots.Count > 0 && !storageSavePending)
                    StorageStatus = "Checked: " + string.Join(", ", snapshots.Select(s => s.Name));
                else if (storageScanRequested)
                    StorageStatus = "Open a retainer inventory, saddlebag, glamour dresser or armoire and wait for loading. Checks save automatically once ready.";
                storageScanRequested = false;
                if (snapshots.Count > 0 || Environment.TickCount64 > storageRefreshUntil) storageRefreshPending = false;
                if (changed)
                {
                    storageSavePending = true;
                    nextScan = 0; // Refresh live bags and saved storage together after transfers.
                }
            }
            catch (Exception ex)
            {
                StorageStatus = "Could not check open storage. Previous saved checks have been kept; see /xllog.";
                nextStorageScan = Environment.TickCount64 + 5000;
                Log.Warning(ex, "Storage check failed");
            }
        }
        if (!storageSavePending || Environment.TickCount64 < nextStorageSave) return;
        try { Save(); }
        catch (Exception ex)
        {
            nextStorageSave = Environment.TickCount64 + 5000;
            StorageStatus = "Storage checked, but saving locally failed. Retrying; see /xllog.";
            Log.Warning(ex, "Could not save storage checks");
        }
    }

    internal string ItemName(uint id)
    {
        if (itemNames.TryGetValue(id, out var name)) return name;
        name = Data.GetExcelSheet<Item>().TryGetRow(id, out var row) ? row.Name.ToString() : $"Unknown item #{id}";
        itemNames[id] = name;
        return name;
    }

    internal void Save()
    {
        SettingsRevision++;
        missingLootTargets = null;
        PluginInterface.SavePluginConfig(Config);
        storageSavePending = false;
    }
    internal void ReportError(string message) { Message = message; MessageIsError = true; }

    public void Dispose()
    {
        RetainerScan.Stop();
        AutoMelding.Stop();
        Equipper.Stop("GearScout unloaded.");
        Inventory.InventoryChanged -= OnInventoryChanged;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, StorageAddons, OnStorageOpened);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, StorageAddons, OnStorageClosing);
        if (storageSavePending)
        {
            try { Save(); }
            catch (Exception ex) { Log.Warning(ex, "Could not save pending storage checks during unload"); }
        }
        Framework.Update -= Update;
        Client.TerritoryChanged -= OnTerritoryChanged;
        Client.Logout -= OnLogout;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.Draw -= lootHighlighter.Draw;
        lootHighlighter.Dispose();
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/gearscout");
        windows.RemoveAllWindows();
        Catalog.Dispose();
        Zones.Dispose();
        lifetime.Cancel();
        // Observe a cancelled/faulted in-flight request without blocking the game thread.
        if (pendingImport != null)
            _ = pendingImport.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        http.Dispose();
        lifetime.Dispose();
    }
}
