using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace GearScout;

internal static class InventoryReader
{
    private static readonly (InventoryType Type, string Label, bool Equipped)[] Containers =
    [
        (InventoryType.EquippedItems, "Equipped", true),
        (InventoryType.Inventory1, "Bag 1", false),
        (InventoryType.Inventory2, "Bag 2", false),
        (InventoryType.Inventory3, "Bag 3", false),
        (InventoryType.Inventory4, "Bag 4", false),
        (InventoryType.ArmoryMainHand, "Armoury Chest", false),
        (InventoryType.ArmoryOffHand, "Armoury Chest", false),
        (InventoryType.ArmoryHead, "Armoury Chest", false),
        (InventoryType.ArmoryBody, "Armoury Chest", false),
        (InventoryType.ArmoryHands, "Armoury Chest", false),
        (InventoryType.ArmoryLegs, "Armoury Chest", false),
        (InventoryType.ArmoryFeets, "Armoury Chest", false),
        (InventoryType.ArmoryEar, "Armoury Chest", false),
        (InventoryType.ArmoryNeck, "Armoury Chest", false),
        (InventoryType.ArmoryWrist, "Armoury Chest", false),
        (InventoryType.ArmoryRings, "Armoury Chest", false),
    ];

    // Called only from the Dalamud framework thread; never from the HTTP worker.
    public static List<OwnedItem>? Read(IGameInventory inventory, IDataManager data)
        => ReadContainers(Containers, inventory, data);

    internal static List<OwnedItem>? ReadContainers(IEnumerable<(InventoryType Type, string Label, bool Equipped)> containers,
        IGameInventory inventory, IDataManager data)
    {
        var result = new List<OwnedItem>();
        foreach (var (type, label, equipped) in containers)
        {
            var slots = inventory.GetInventoryItems((GameInventoryType)type);
            // An empty container still has slots. No slots means unavailable, not missing gear.
            if (slots.IsEmpty) return null;
            foreach (ref readonly var item in slots)
            {
                if (!item.IsEmpty && item.BaseItemId != 0 && item.Quantity > 0)
                {
                    var melds = new List<uint>();
                    var known = true;
                    var types = item.Materia;
                    var grades = item.MateriaGrade;
                    for (var index = 0; index < types.Length; index++)
                    {
                        if (types[index] == 0) continue;
                        if (index >= grades.Length || !data.GetExcelSheet<Materia>().TryGetRow(types[index], out var row) ||
                            grades[index] >= row.Item.Count || row.Item[grades[index]].RowId == 0)
                        {
                            known = false;
                            break;
                        }
                        melds.Add(row.Item[grades[index]].RowId);
                    }
                    result.Add(new(item.BaseItemId, item.Quantity, label, equipped, known ? melds : null));
                }
            }
        }
        return result;
    }
}
