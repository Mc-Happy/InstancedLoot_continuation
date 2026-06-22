using System.Collections.Generic;
using EntityStates;
using EntityStates.CaptainSupplyDrop;
using InstancedLoot.Components;
using InstancedLoot.Enums;
using RoR2;
using UnityEngine;

namespace InstancedLoot.Hooks;

// Scopes the Captain "Beacon: Hacking" effect to the beacon owner's own instance.
//
// Why: instanced purchasables use ObjectInstanceMode.CopyObject, so every player gets a separate,
// spatially-overlapping PurchaseInteraction clone at the same position. Vanilla's beacon scans for
// the nearest valid PurchaseInteraction in its dome, hacks it to $0 and auto-buys it, then re-scans.
// With the clones stacked on top of each other the beacon hacks every player's clone in turn, so the
// discount and auto-pop leak to everyone. We exclude clones that don't belong to the beacon owner
// from the scan's validity check, so the beacon only ever targets (and auto-buys) the owner's copy.
public class HackingBeaconHandler : AbstractHookHandler
{
    // The beacon owner of the scan currently running, set around ScanForTarget so the static
    // PurchaseInteractionIsValidTarget hook knows whose clones are eligible. Server logic is
    // single-threaded, so a plain static is safe.
    private static PlayerCharacterMasterController _scanOwner;

    // Active hacks keyed by their target, so PurchaseInteractionHandler can attribute auto-popped
    // loot to the beacon owner even if the auto-buy passes a null activator.
    private readonly Dictionary<PurchaseInteraction, PlayerCharacterMasterController> _activeHacks = new();

    public override void RegisterHooks()
    {
        On.EntityStates.CaptainSupplyDrop.HackingMainState.ScanForTarget += On_ScanForTarget;
        On.EntityStates.CaptainSupplyDrop.HackingMainState.PurchaseInteractionIsValidTarget +=
            On_PurchaseInteractionIsValidTarget;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnEnter += On_HackingInProgress_OnEnter;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnExit += On_HackingInProgress_OnExit;
    }

    public override void UnregisterHooks()
    {
        On.EntityStates.CaptainSupplyDrop.HackingMainState.ScanForTarget -= On_ScanForTarget;
        On.EntityStates.CaptainSupplyDrop.HackingMainState.PurchaseInteractionIsValidTarget -=
            On_PurchaseInteractionIsValidTarget;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnEnter -= On_HackingInProgress_OnEnter;
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.OnExit -= On_HackingInProgress_OnExit;
        _activeHacks.Clear();
    }

    // Resolve the player that owns the beacon running the given state. The deployed beacon carries
    // a Deployable whose ownerMaster is the Captain's master.
    public static PlayerCharacterMasterController ResolveBeaconOwner(EntityState state)
    {
        GameObject go = state?.outer != null ? state.outer.gameObject : null;
        if (go == null) return null;
        Deployable deployable = go.GetComponentInParent<Deployable>();
        CharacterMaster master = deployable != null ? deployable.ownerMaster : null;
        return master != null ? master.playerCharacterMasterController : null;
    }

    public bool TryGetHackOwner(PurchaseInteraction target, out PlayerCharacterMasterController owner)
        => _activeHacks.TryGetValue(target, out owner) && owner != null;

    private PurchaseInteraction On_ScanForTarget(
        On.EntityStates.CaptainSupplyDrop.HackingMainState.orig_ScanForTarget orig,
        EntityStates.CaptainSupplyDrop.HackingMainState self)
    {
        PlayerCharacterMasterController previous = _scanOwner;
        _scanOwner = ResolveBeaconOwner(self);
        try
        {
            return orig(self);
        }
        finally
        {
            _scanOwner = previous;
        }
    }

    private bool On_PurchaseInteractionIsValidTarget(
        On.EntityStates.CaptainSupplyDrop.HackingMainState.orig_PurchaseInteractionIsValidTarget orig,
        PurchaseInteraction purchaseInteraction)
    {
        if (!orig(purchaseInteraction))
            return false;

        // Only constrain instanced (per-player copied) purchasables, and only while we know which
        // beacon is scanning. A clone that wasn't instanced for the beacon owner is not a valid
        // target for this beacon — that keeps the hack on the owner's own copy.
        if (_scanOwner != null && purchaseInteraction != null)
        {
            InstanceHandler instanceHandler = purchaseInteraction.GetComponent<InstanceHandler>();
            if (instanceHandler != null
                && instanceHandler.ObjectInstanceMode == ObjectInstanceMode.CopyObject
                && !instanceHandler.IsObjectInstancedFor(_scanOwner))
                return false;
        }

        return true;
    }

    private void On_HackingInProgress_OnEnter(
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.orig_OnEnter orig,
        EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        orig(self);

        if (self.target != null)
        {
            PlayerCharacterMasterController owner = ResolveBeaconOwner(self);
            if (owner != null)
                _activeHacks[self.target] = owner;
        }
    }

    private void On_HackingInProgress_OnExit(
        On.EntityStates.CaptainSupplyDrop.HackingInProgressState.orig_OnExit orig,
        EntityStates.CaptainSupplyDrop.HackingInProgressState self)
    {
        if (self.target != null)
            _activeHacks.Remove(self.target);

        orig(self);
    }
}
