namespace GearScout;

public sealed record RetainerScanTarget(ulong Id, string Name);
public enum RetainerScanAction { None, SelectRetainer, OpenInventory, CloseInventory, QuitRetainer }
public enum RetainerScanPhase { Stopped, List, Menu, Inventory, QuitMenu, ReturnList, Complete }
public sealed record RetainerScanObservation(ulong CharacterId, bool PlayerReady, bool ListReady,
    bool MenuReady, bool InventoryReady, ulong ActiveRetainerId, bool SnapshotSaved, bool UnexpectedDialog);

public sealed class RetainerScanSession
{
    private List<RetainerScanTarget> targets = [];
    private ulong characterId;
    private long phaseStarted;
    public RetainerScanPhase Phase { get; private set; }
    public bool Running => Phase is not (RetainerScanPhase.Stopped or RetainerScanPhase.Complete);
    public int Completed { get; private set; }
    public int Total => targets.Count;
    public RetainerScanTarget? Current => Running && Completed < targets.Count ? targets[Completed] : null;
    public string Status { get; private set; } = "Open a summoning bell to the retainer list, then start a scan.";

    public void Start(IEnumerable<RetainerScanTarget> available, ulong owner, long now)
    {
        var selected = available.Where(t => t.Id != 0).DistinctBy(t => t.Id).ToList();
        if (owner == 0 || selected.Count == 0) throw new ArgumentException("No available retainers for this character.");
        targets = selected;
        characterId = owner;
        Completed = 0;
        Advance(RetainerScanPhase.List, now);
    }

    public void Stop(string reason = "Retainer scan stopped.") { Phase = RetainerScanPhase.Stopped; Status = reason; }

    public RetainerScanAction Tick(RetainerScanObservation state, long now)
    {
        if (!Running) return RetainerScanAction.None;
        if (!state.PlayerReady || state.CharacterId != characterId)
            Stop("Retainer scan stopped: character changed or the game is loading.");
        else if (state.UnexpectedDialog)
            Stop("Retainer scan stopped at an unexpected dialog. Resolve it manually.");
        else if (now - phaseStarted > 30_000)
            Stop($"Retainer scan timed out while waiting for {Current!.Name}. Saved checks have been kept.");
        else if ((Phase is RetainerScanPhase.Menu or RetainerScanPhase.Inventory or RetainerScanPhase.QuitMenu) &&
                 (state.MenuReady || state.InventoryReady) &&
                 state.ActiveRetainerId != 0 && state.ActiveRetainerId != Current!.Id)
            Stop("Retainer scan stopped: a different retainer was selected.");
        if (!Running) return RetainerScanAction.None;
        switch (Phase)
        {
            case RetainerScanPhase.List when state.ListReady:
                Advance(RetainerScanPhase.Menu, now);
                return RetainerScanAction.SelectRetainer;
            case RetainerScanPhase.Menu when state.MenuReady && state.ActiveRetainerId == Current!.Id:
                Advance(RetainerScanPhase.Inventory, now);
                return RetainerScanAction.OpenInventory;
            case RetainerScanPhase.Inventory when state.InventoryReady && state.SnapshotSaved && state.ActiveRetainerId == Current!.Id:
                Advance(RetainerScanPhase.QuitMenu, now);
                return RetainerScanAction.CloseInventory;
            case RetainerScanPhase.QuitMenu when state.MenuReady && !state.InventoryReady && state.ActiveRetainerId == Current!.Id:
                Advance(RetainerScanPhase.ReturnList, now);
                return RetainerScanAction.QuitRetainer;
            case RetainerScanPhase.ReturnList when state.ListReady:
                Completed++;
                if (Completed == targets.Count)
                {
                    Phase = RetainerScanPhase.Complete;
                    Status = $"Saved inventories for {Completed} retainer(s).";
                }
                else Advance(RetainerScanPhase.List, now);
                break;
        }
        return RetainerScanAction.None;
    }

    private void Advance(RetainerScanPhase phase, long now)
    {
        Phase = phase;
        phaseStarted = now;
        Status = $"Retainer {Completed + 1}/{targets.Count}: {targets[Completed].Name} — {phase}";
    }
}
