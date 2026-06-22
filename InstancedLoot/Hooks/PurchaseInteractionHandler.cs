using System.Collections.Generic;
using InstancedLoot.Components;
using RoR2;
using UnityEngine.Networking;

namespace InstancedLoot.Hooks;

public class PurchaseInteractionHandler : AbstractHookHandler
{
    // Multishop instances that have already been purchased from. Vanilla's close-on-purchase is
    // ineffective on instanced multishops, so we enforce it ourselves by disabling these in
    // GetInteractability. Each instance has its own MultiShopController, so this is per-copy.
    private readonly HashSet<MultiShopController> _closedMultiShops = new();

    public override void RegisterHooks()
    {
        On.RoR2.PurchaseInteraction.GetInteractability += On_PurchaseInteraction_GetInteractability;
        On.RoR2.PurchaseInteraction.OnInteractionBegin += On_PurchaseInteraction_OnInteractionBegin;
    }

    public override void UnregisterHooks()
    {
        On.RoR2.PurchaseInteraction.GetInteractability -= On_PurchaseInteraction_GetInteractability;
        On.RoR2.PurchaseInteraction.OnInteractionBegin -= On_PurchaseInteraction_OnInteractionBegin;
    }

    private Interactability On_PurchaseInteraction_GetInteractability(On.RoR2.PurchaseInteraction.orig_GetInteractability orig, PurchaseInteraction self, Interactor activator)
    {
        var interactability = orig(self, activator);

        // Enforce multishop close-on-purchase: once any terminal of an instance has been bought,
        // all of that instance's terminals are disabled (vanilla's own close doesn't take effect
        // on instanced shops).
        if (self.GetComponent<ShopTerminalBehavior>() is var shopTerminal && shopTerminal != null
            && shopTerminal.serverMultiShopController is var multiShopController && multiShopController != null
            && _closedMultiShops.Contains(multiShopController))
            interactability = Interactability.Disabled;

        var body = activator.GetComponent<CharacterBody>();
        if (body)
        {
            var player = body.master.GetComponent<PlayerCharacterMasterController>();
            var instanceHandler = self.GetComponent<InstanceHandler>();
            if (player && instanceHandler)
                if (!instanceHandler.Players.Contains(player))
                    interactability = Interactability.Disabled;
        }

        return interactability;
    }

    private void On_PurchaseInteraction_OnInteractionBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
    {
        // Authoritative close: if this multishop instance was already purchased from, reject any
        // further purchase even if a racing interaction got past GetInteractability this frame.
        if (NetworkServer.active && self.GetComponent<ShopTerminalBehavior>() is var alreadyTerminal
            && alreadyTerminal != null && alreadyTerminal.serverMultiShopController is var alreadyController
            && alreadyController != null && _closedMultiShops.Contains(alreadyController))
            return;

        if(activator.GetComponent<CharacterBody>() is var characterBody && characterBody
           && characterBody.master is var master && master
           && master.playerCharacterMasterController is var player && player)
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, player);
        // A Captain hacking beacon auto-pops without a player activator. Attribute the loot to the
        // beacon owner so auto-popped chest/shop items reserve to them (requires an owner-only item
        // mode in config). The hack is already scoped to the owner's own instance by HackingBeaconHandler.
        else if (hookManager.HookHandlers.TryGetValue(typeof(HackingBeaconHandler), out var hackingHandler)
                 && ((HackingBeaconHandler)hackingHandler).TryGetHackOwner(self, out var beaconOwner))
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, beaconOwner);

        orig(self, activator);

        // Vanilla's "buy one of three closes the shop" is ineffective on instanced multishops, so
        // enforce it ourselves: mark this instance closed (our GetInteractability then disables all
        // of its terminals) and empty the sibling terminals so they visually clear out. Each instance
        // has its own serverMultiShopController, so only the buyer's own shop copy closes.
        if (NetworkServer.active && self.GetComponent<ShopTerminalBehavior>() is var shopTerminal
            && shopTerminal != null && shopTerminal.serverMultiShopController is var multiShopController
            && multiShopController != null)
        {
            _closedMultiShops.Add(multiShopController);
            multiShopController.Networkavailable = false;

            foreach (var terminalGameObject in multiShopController.terminalGameObjects)
            {
                if (terminalGameObject == null || terminalGameObject == self.gameObject) continue;
                if (terminalGameObject.GetComponent<ShopTerminalBehavior>() is var siblingTerminal && siblingTerminal != null)
                    siblingTerminal.SetNoPickup();
            }
        }
    }
}
