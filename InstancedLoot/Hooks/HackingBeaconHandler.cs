using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace InstancedLoot.Hooks;

// Detects Captain "Beacon: Hacking" activity by hooking the hacking entity-states.
// The deployed beacon runs HackingMainState (scans) -> HackingInProgressState (hacks one
// PurchaseInteraction `target` at a time). We track:
//   * HackedBy      - the purchasable being hacked *right now* -> beacon owner (active lock set).
//   * BeaconAffected - every purchasable a beacon has hacked at least once -> beacon owner
//                      (persistent; drives shrine escalation after the hack window).
// PurchaseInteractionHandler reads these maps to lock / attribute / escalate.
public class HackingBeaconHandler : AbstractHookHandler
{
    internal static readonly Dictionary<PurchaseInteraction, PlayerCharacterMasterController> HackedBy = new();
    internal static readonly Dictionary<PurchaseInteraction, PlayerCharacterMasterController> BeaconAffected = new();

    private static bool _dumped;

    public override void RegisterHooks()
    {
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnEnter += On_OnEnter;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnExit += On_OnExit;
    }

    public override void UnregisterHooks()
    {
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnEnter -= On_OnEnter;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnExit -= On_OnExit;
        HackedBy.Clear();
        BeaconAffected.Clear();
        _dumped = false;
    }

    private void On_OnEnter(
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.orig_OnEnter orig,
        EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        orig(self);

        var target = self.target;
        if (target == null) return;

        var owner = ResolveOwner(self);
        HackedBy[target] = owner;
        BeaconAffected[target] = owner;

        PurchaseInteractionHandler.V($"hack begin target={target.name} owner={PurchaseInteractionHandler.NameOf(owner)}");
        DumpBeaconOnce(self);
    }

    private void On_OnExit(
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.orig_OnExit orig,
        EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        var target = self.target;
        if (target != null && HackedBy.Remove(target))
            PurchaseInteractionHandler.V($"hack end target={target.name}");

        orig(self);
    }

    // The hacking state runs on the deployed beacon object; its owner is the Captain master.
    internal static PlayerCharacterMasterController ResolveOwner(
        EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        var go = self.outer != null ? self.outer.gameObject : null;
        if (go == null) return null;
        var dep = go.GetComponentInParent<Deployable>();
        var master = dep != null ? dep.ownerMaster : null;
        return master != null ? master.playerCharacterMasterController : null;
    }

    // One-time component/owner dump of the real beacon object, to confirm owner resolution.
    private static void DumpBeaconOnce(EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        if (_dumped) return;
        _dumped = true;

        var go = self.outer != null ? self.outer.gameObject : null;
        if (go == null) return;

        PurchaseInteractionHandler.V($"===== hack beacon obj: name='{go.name}' pos={go.transform.position} =====");
        foreach (var c in go.GetComponents<Component>())
            PurchaseInteractionHandler.V($"  component: {c.GetType().FullName}");
        var dep = go.GetComponentInParent<Deployable>();
        PurchaseInteractionHandler.V($"  Deployable(inParent)={(dep ? "yes" : "null")} " +
            $"ownerMaster={(dep && dep.ownerMaster ? dep.ownerMaster.name : "null")}");
        var tf = go.GetComponent<TeamFilter>();
        PurchaseInteractionHandler.V($"  TeamFilter={(tf ? tf.teamIndex.ToString() : "null")}");
    }
}
