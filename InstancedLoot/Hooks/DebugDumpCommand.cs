using System.Linq;
using InstancedLoot.Components;
using RoR2;
using UnityEngine;

namespace InstancedLoot.Hooks;

// On-demand diagnostic snapshot for verifying the Captain hacking-beacon ownership features.
// RoR2 auto-discovers [ConCommand] statics in scanned assemblies (we register ours in Awake via
// HG.Reflection.SearchableAttribute.ScanAssembly). Run as host (server-authoritative state).
//   Console:  il_dump
public static class DebugDumpCommand
{
    // Unconditional (the user explicitly invoked the command); reuses the handler's static log source.
    private static void Print(string msg) => PurchaseInteractionHandler.Log?.LogInfo(msg);

    private static string Name(PlayerCharacterMasterController p)
        => p == null ? "<none>" : p.gameObject.name;

    [ConCommand(commandName = "il_dump", flags = ConVarFlags.None,
        helpText = "InstancedLoot: dump hack-beacon state, grounded items, and affected purchasables.")]
    public static void CCDump(ConCommandArgs args)
    {
        DumpHackState();
        DumpGroundedItems();
    }

    private static void DumpHackState()
    {
        var hacked = HackingBeaconHandler.HackedBy;
        Print($"[IL-DUMP] ===== actively hacked / locked ({hacked.Count}) =====");
        foreach (var kv in hacked)
            Print($"  lock name={(kv.Key ? kv.Key.name : "<destroyed>")} owner={Name(kv.Value)}");

        var affected = HackingBeaconHandler.BeaconAffected;
        Print($"[IL-DUMP] ===== beacon-affected purchasables ({affected.Count}) =====");
        int i = 0;
        foreach (var kv in affected)
        {
            var pi = kv.Key;
            if (pi == null) { Print($"  pi#{i++} <destroyed> owner={Name(kv.Value)}"); continue; }
            Print($"  pi#{i++} name={pi.name} owner={Name(kv.Value)} " +
                  $"base={PurchaseInteractionHandler.BaseCostOf(pi)} disp={pi.Networkcost} " +
                  $"uses={PurchaseInteractionHandler.UseCountOf(pi)} " +
                  $"owner->0 nonOwner->{PurchaseInteractionHandler.EffectiveBaseCostFor(pi)} " +
                  $"locked={hacked.ContainsKey(pi)}");
        }
    }

    private static void DumpGroundedItems()
    {
        var pickups = Object.FindObjectsOfType<GenericPickupController>();
        Print($"[IL-DUMP] ===== grounded items ({pickups.Length}) =====");
        for (int i = 0; i < pickups.Length; i++)
        {
            var p = pickups[i];
            var def = PickupCatalog.GetPickupDef(p.pickupIndex);
            string item = def != null ? def.internalName : "?";
            var info = p.GetComponent<InstanceInfoTracker>();
            string owner = info != null && info.Owner != null ? info.Owner.gameObject.name : "<none>";
            var ih = p.GetComponent<InstanceHandler>();
            string players = ih == null ? "<not instanced>"
                : "{" + string.Join(",", ih.Players.Select(pl => pl.gameObject.name)) + "}";
            string mode = ih == null ? "None" : ih.ObjectInstanceMode.ToString();
            Print($"  item#{i} name={p.name} item={item} owner={owner} players={players} mode={mode}");
        }
    }
}
