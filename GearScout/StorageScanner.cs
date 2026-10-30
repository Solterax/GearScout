using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using CabinetRow = Lumina.Excel.Sheets.Cabinet;

namespace GearScout;

internal sealed unsafe class StorageScanner
{
    private readonly Dictionary<string, long> firstSeen = [];
    private readonly Dictionary<string, long> lastVisible = [];
    private readonly Dictionary<string, int> generations = [];
    private readonly Dictionary<string, int> capturedGenerations = [];
    private readonly HashSet<string> openLastRead = [];
    public HashSet<string> NewlyOpenedSources { get; } = [];
    private static readonly InventoryType[] RetainerBags =
    [
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
        InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    ];

    public void Reset()
    {
        firstSeen.Clear(); lastVisible.Clear(); generations.Clear(); capturedGenerations.Clear();
        openLastRead.Clear(); NewlyOpenedSources.Clear();
    }

    public List<StorageSnapshot> Read(ulong characterId, IGameGui gui, IGameInventory inventory, IDataManager data, bool allowRecentlyClosed = false)
    {
        var result = new List<StorageSnapshot>();
        var active = new HashSet<string>();
        var opened = new HashSet<string>();
        NewlyOpenedSources.Clear();
        if (characterId == 0) return result;
        ReadRetainer();
        ReadDresser();
        ReadArmoire();
        foreach (var key in firstSeen.Keys.Where(k => !active.Contains(k)).ToArray()) firstSeen.Remove(key);
        openLastRead.Clear();
        openLastRead.UnionWith(opened);
        return result;

        bool Available(string key, bool visible)
        {
            if (visible)
            {
                opened.Add(key);
                lastVisible[key] = Environment.TickCount64;
                if (!openLastRead.Contains(key)) generations[key] = generations.GetValueOrDefault(key) + 1;
                return true;
            }
            // A short identity-bound grace period catches server-confirmed transfers that arrive as a window closes.
            return allowRecentlyClosed && lastVisible.TryGetValue(key, out var seen) && Environment.TickCount64 - seen <= 3000;
        }

        bool Stable(string key, int waitMilliseconds = 2000)
        {
            active.Add(key);
            if (!firstSeen.TryGetValue(key, out var since))
            {
                firstSeen[key] = Environment.TickCount64;
                return waitMilliseconds == 0;
            }
            return Environment.TickCount64 - since >= waitMilliseconds;
        }

        void Add(string key, string name, List<StoredItem> items, string? group = null)
        {
            var generation = generations.GetValueOrDefault(group ?? key);
            if (capturedGenerations.GetValueOrDefault(key, -1) != generation)
            {
                NewlyOpenedSources.Add(key);
                capturedGenerations[key] = generation;
            }
            result.Add(new() { CharacterId = characterId, Key = key, Name = name, CheckedAtUtc = DateTimeOffset.UtcNow, Items = items });
        }

        void ReadRetainer()
        {
            // LastSelectedRetainerId persists after closing the bell: never use it without an open inventory.
            var visible = Open("InventoryRetainer") || Open("InventoryRetainerLarge") || Open("RetainerCharacter");
            var manager = RetainerManager.Instance();
            if (manager == null || !manager->IsReady) return;
            var retainer = manager->GetActiveRetainer();
            if (retainer == null || retainer->RetainerId == 0 || retainer->RetainerId != manager->LastSelectedRetainerId) return;
            var id = retainer->RetainerId;
            var key = $"retainer:{id}";
            if (!Available(key, visible)) return;
            var ordering = ItemOrderModule.Instance();
            if (ordering == null || (ordering->ActiveRetainerId != id &&
                !(allowRecentlyClosed && !visible && ordering->ActiveRetainerId == 0))) return;
            if (!Stable(key)) return;
            var name = retainer->NameString;
            if (string.IsNullOrWhiteSpace(name)) name = id.ToString();
            var bags = ReadContainers(RetainerBags, inventory, data);
            if (bags != null) Add(key + ":bags", $"Retainer: {name} / bags", bags, key);
            var equipped = ReadContainers([InventoryType.RetainerEquippedItems], inventory, data);
            if (equipped != null) Add(key + ":equipped", $"Retainer: {name} / equipped", equipped, key);
        }

        void ReadDresser()
        {
            var agent = AgentMiragePrismPrismBox.Instance();
            var mirage = MirageManager.Instance();
            var visible = Open("MiragePrismPrismBox");
            if (!Available("dresser", visible) || agent == null || (visible && !agent->IsAgentActive()) ||
                !agent->IsDataLoaded || agent->Data == null || mirage == null || !mirage->PrismBoxLoaded ||
                !agent->Data->IsAsyncLoadComplete || !agent->Data->IsPopulatingComplete || agent->Data->IsPopulatingList)
                return;
            if (!Stable("dresser", 0)) return;
            var items = new List<StoredItem>();
            var seen = new HashSet<(uint Slot, uint ItemId)>();
            // This array includes expanded outfit pieces across every page/filter, not glamour plates.
            foreach (ref readonly var item in agent->Data->PrismBoxItems)
            {
                var id = LootMatcher.BaseItemId(item.ItemId);
                if (id == 0 || !data.GetExcelSheet<Item>().TryGetRow(id, out var row) ||
                    row.EquipSlotCategory.RowId == 0 || !seen.Add((item.Slot, id))) continue;
                items.Add(new() { ItemId = id, Quantity = 1, Materia = [] });
            }
            Add("dresser", "Glamour dresser", items);
        }

        void ReadArmoire()
        {
            if (!Available("armoire", Open("Cabinet") || Open("CabinetWithdraw"))) return;
            var ui = UIState.Instance();
            if (ui == null || !ui->Cabinet.IsCabinetLoaded() || !Stable("armoire", 0)) return;
            var items = new List<StoredItem>();
            foreach (var row in data.GetExcelSheet<CabinetRow>())
            {
                if (row.Item.RowId != 0 && ui->Cabinet.IsItemInCabinet(row.RowId))
                    items.Add(new() { ItemId = row.Item.RowId, Quantity = 1, Materia = [] });
            }
            Add("armoire", "Armoire", items);
        }

        bool Open(string name) => gui.GetAddonByName(name).IsVisible;
    }

    private static List<StoredItem>? ReadContainers(InventoryType[] types, IGameInventory inventory, IDataManager data)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        // Every bag in a snapshot must be loaded. Never overwrite a saved snapshot with partial data.
        foreach (var type in types)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded || container->Items == null || container->Size <= 0) return null;
        }
        var items = InventoryReader.ReadContainers(types.Select(type => (type, "", false)), inventory, data);
        return items?.Select(i => new StoredItem { ItemId = i.ItemId, Quantity = i.Quantity, Materia = i.Materia?.ToList(), HighQuality = i.HighQuality }).ToList();
    }
}
