using InstancedLoot.Components;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using Duplicating = EntityStates.Duplicator.Duplicating;

namespace InstancedLoot.Hooks;

/// <summary>
///     Handles instancing of items dispensed by 3D printers (Duplicators).
///     The game no longer drops printer output through ShopTerminalBehavior.DropPickup; instead the
///     printer runs the EntityStates.Duplicator.Duplicating state, which spawns the pickup droplet in
///     DropDroplet(). This is the modern equivalent of the old ShopTerminalBehaviorHandler.DropPickup
///     hook: it pushes the printer's owner/objectType onto PickupDropletControllerHandler so the
///     resulting droplet flows through the normal droplet -> GenericPickupController instancing chain.
///     The owner is set earlier by PurchaseInteractionHandler.OnInteractionBegin (printers still use
///     PurchaseInteraction), recorded on the printer's InstanceInfoTracker.
/// </summary>
public class DuplicatorHandler : AbstractHookHandler
{
    public override void RegisterHooks()
    {
        On.EntityStates.Duplicator.Duplicating.DropDroplet += On_Duplicating_DropDroplet;
    }

    public override void UnregisterHooks()
    {
        On.EntityStates.Duplicator.Duplicating.DropDroplet -= On_Duplicating_DropDroplet;
    }

    private void On_Duplicating_DropDroplet(On.EntityStates.Duplicator.Duplicating.orig_DropDroplet orig, Duplicating self)
    {
        if (!NetworkServer.active || self.outer == null)
        {
            orig(self);
            return;
        }

        GameObject printer = self.outer.gameObject;

        // The printer prefab name (e.g. "Duplicator", "DuplicatorLarge(Clone)") may live on the
        // state machine's object or an ancestor; walk up to find which printer tier this is.
        string objectType = ResolveObjectTypeInParents(printer, out GameObject matchedPrinter);
        GameObject searchRoot = matchedPrinter != null ? matchedPrinter : printer;

        // Owner is recorded on the printer's InstanceInfoTracker by
        // PurchaseInteractionHandler.OnInteractionBegin when the player pays into the printer. That
        // tracker may sit on the matched object, an ancestor, or a child, so search the subtree.
        PlayerCharacterMasterController owner = FindOwner(searchRoot);

        if (objectType == null)
        {
            // Not a known printer (e.g. a cauldron sharing the Duplicating state) - leave vanilla.
            Plugin._logger.LogInfo(
                $"DuplicatorHandler: DropDroplet on unrecognised object '{printer.name}', not instancing.");
            orig(self);
            return;
        }

        if (owner == null)
            Plugin._logger.LogWarning(
                $"DuplicatorHandler: no owner found for printer '{printer.name}' ({objectType}); " +
                "dispensed item will not be instanced. Expected PurchaseInteraction.OnInteractionBegin to set it.");

        PickupDropletControllerHandler pickupDropletControllerHandler =
            hookManager.GetHandler<PickupDropletControllerHandler>();

        pickupDropletControllerHandler.InstanceOverrideInfo =
            new InstanceInfoTracker.InstanceOverrideInfo(objectType, owner);
        orig(self);
        pickupDropletControllerHandler.InstanceOverrideInfo = null;
    }

    private static string ResolveObjectTypeInParents(GameObject gameObject, out GameObject matchedObject)
    {
        for (Transform transform = gameObject.transform; transform != null; transform = transform.parent)
        {
            string objectType = ResolveObjectType(transform.gameObject.name);
            if (objectType != null)
            {
                matchedObject = transform.gameObject;
                return objectType;
            }
        }

        matchedObject = null;
        return null;
    }

    private static PlayerCharacterMasterController FindOwner(GameObject root)
    {
        InstanceInfoTracker inParent = root.GetComponentInParent<InstanceInfoTracker>();
        if (inParent != null && inParent.Owner != null) return inParent.Owner;

        foreach (InstanceInfoTracker tracker in root.GetComponentsInChildren<InstanceInfoTracker>())
            if (tracker.Owner != null) return tracker.Owner;

        return null;
    }

    // Order matters: the generic "Duplicator" prefix matches every tier, so the more specific
    // prefixes are checked last and overwrite it (mirrors ShopTerminalBehaviorHandler).
    private static string ResolveObjectType(string name)
    {
        if (name == null) return null;

        string objectType = null;
        if (name.StartsWith("Duplicator")) objectType = Enums.ObjectType.Duplicator;
        if (name.StartsWith("DuplicatorLarge")) objectType = Enums.ObjectType.DuplicatorLarge;
        if (name.StartsWith("DuplicatorWild")) objectType = Enums.ObjectType.DuplicatorWild;
        if (name.StartsWith("DuplicatorMilitary")) objectType = Enums.ObjectType.DuplicatorMilitary;
        return objectType;
    }
}
