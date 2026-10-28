using System.Net.Http;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;

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

    private readonly WindowSystem windows = new("GearScout");
    private readonly MainWindow window;
    private readonly LootHighlighter lootHighlighter;
    private readonly StorageScanner storageScanner = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<uint, string> itemNames = [];
    private Task<List<TrackedSet>>? pendingImport;
    private long nextScan;
    private ulong characterId;
    private long nextStorageScan;
    private long nextStorageSave;
    private bool storageScanRequested;
    private bool storageSavePending;

    internal Configuration Config { get; }
    internal string CurrentJob { get; private set; } = "";
    internal List<OwnedItem>? Owned { get; private set; }
    internal bool Importing => pendingImport != null;
    internal string Message { get; private set; } = "";
    internal bool MessageIsError { get; private set; }
    internal ulong CharacterId => characterId;
    internal string StorageStatus { get; private set; } = "Open each storage location in game to save its contents.";

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new();
        Config.StorageSnapshots ??= [];
        window = new MainWindow(this);
        lootHighlighter = new LootHighlighter(this);
        windows.AddWindow(window);
        Commands.AddHandler("/gearscout", new CommandInfo(OnCommand) { HelpMessage = "Open XIVGear gear tracking." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.Draw += lootHighlighter.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        Framework.Update += Update;
    }

    private void OnCommand(string command, string args) => Open();
    private void Open() => window.IsOpen = true;

    internal void RequestStorageScan()
    {
        storageScanRequested = true;
        nextStorageScan = 0;
        StorageStatus = "Checking open storage. Wait for it to finish loading.";
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
        var job = ready ? Player.ClassJob.Value.Abbreviation.ToString().ToUpperInvariant() : "";
        var contentId = ready ? Player.ContentId : 0;
        if (job != CurrentJob || contentId != characterId || !ready)
        {
            if (contentId != characterId || !ready)
            {
                storageScanner.Reset();
                nextStorageScan = 0;
                storageScanRequested = false;
                StorageStatus = "Open each storage location in game to save its contents for this character.";
            }
            CurrentJob = job;
            characterId = contentId;
            Owned = null;
            nextScan = Environment.TickCount64 + 500;
        }
        if (ready) UpdateStorage();
        // Loot highlighting needs fresh ownership even when the checklist is closed.
        if (!window.IsOpen && !Config.HighlightLoot)
        {
            Owned = null;
            return;
        }
        if (!ready || Environment.TickCount64 < nextScan) return;
        nextScan = Environment.TickCount64 + 1000;
        try { Owned = StorageCache.Merge(InventoryReader.Read(Inventory, Data), Config.StorageSnapshots, characterId); }
        catch (Exception ex)
        {
            Owned = null;
            nextScan = Environment.TickCount64 + 5000;
            Log.Warning(ex, "Could not read character inventory");
        }
    }

    private void UpdateStorage()
    {
        if (Environment.TickCount64 >= nextStorageScan)
        {
            nextStorageScan = Environment.TickCount64 + 500;
            try
            {
                var snapshots = storageScanner.Read(characterId, GameGui, Inventory, Data);
                var changed = false;
                foreach (var snapshot in snapshots)
                    changed |= StorageCache.Replace(Config.StorageSnapshots, snapshot, storageScanRequested);
                if (snapshots.Count > 0 && !storageSavePending)
                    StorageStatus = "Checked: " + string.Join(", ", snapshots.Select(s => s.Name));
                else if (storageScanRequested)
                    StorageStatus = "Open a retainer inventory, glamour dresser or armoire and wait for loading. Checks save automatically once ready.";
                storageScanRequested = false;
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
        PluginInterface.SavePluginConfig(Config);
        storageSavePending = false;
    }
    internal void ReportError(string message) { Message = message; MessageIsError = true; }

    public void Dispose()
    {
        if (storageSavePending)
        {
            try { Save(); }
            catch (Exception ex) { Log.Warning(ex, "Could not save pending storage checks during unload"); }
        }
        Framework.Update -= Update;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.Draw -= lootHighlighter.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/gearscout");
        windows.RemoveAllWindows();
        lifetime.Cancel();
        // Observe a cancelled/faulted in-flight request without blocking the game thread.
        if (pendingImport != null)
            _ = pendingImport.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        http.Dispose();
        lifetime.Dispose();
    }
}
