using System.Collections.Generic;
using InstancedLoot.Components;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace InstancedLoot.Hooks;

// Captain hacking-beacon behaviour for purchasables. Detection lives in HackingBeaconHandler
// (hooks the hacking entity-states); this handler applies the consequences:
//   * Lock: while a purchasable is the beacon's *active* hack target, only the beacon owner may
//     interact with / complete it (non-owners are blocked) — prevents buy-stealing.
//   * Ownership: the auto-bought (or owner-bought) loot is instance-owned by the beacon owner.
//   * Escalation: once a beacon has hacked an escalating shrine, non-owners pay base x 1.4^uses
//     instead of the beacon-zeroed price; the owner stays free.
public class PurchaseInteractionHandler : AbstractHookHandler
{
    public const float ShrineEscalationMultiplier = 1.4f; // +40% per successful use (non-owners)

    // Base cost captured at Start (before any beacon hack) and per-purchasable successful-use count.
    private static readonly Dictionary<PurchaseInteraction, int> _baseCost = new();
    private static readonly Dictionary<PurchaseInteraction, int> _useCount = new();

    // ---- logging (set Verbose = false for release) ----
    internal static BepInEx.Logging.ManualLogSource Log;
    internal static bool Verbose = true;
    private static readonly Dictionary<int, float> _lastLog = new();

    internal static void V(string msg) { if (Verbose) Log?.LogInfo("[IL-Hack] " + msg); }

    // Log without the [IL-Hack] prefix (used by [IL-Loot] / [IL-DUMP] callers that bring their own tag).
    internal static void Raw(string msg) { if (Verbose) Log?.LogInfo(msg); }

    private static void VThrottled(int key, string msg, float interval = 1f)
    {
        float now = Time.unscaledTime;
        if (_lastLog.TryGetValue(key, out var t) && now - t < interval) return;
        _lastLog[key] = now; V(msg);
    }

    internal static string NameOf(PlayerCharacterMasterController p)
        => p == null ? "<none/auto>" : p.gameObject.name;

    public override void RegisterHooks()
    {
        Log = Plugin._logger;
        V("PurchaseInteractionHandler hooks registered");
        On.RoR2.PurchaseInteraction.Start += On_Start;
        On.RoR2.PurchaseInteraction.GetInteractability += On_GetInteractability;
        On.RoR2.PurchaseInteraction.OnInteractionBegin += On_OnInteractionBegin;
    }

    public override void UnregisterHooks()
    {
        On.RoR2.PurchaseInteraction.Start -= On_Start;
        On.RoR2.PurchaseInteraction.GetInteractability -= On_GetInteractability;
        On.RoR2.PurchaseInteraction.OnInteractionBegin -= On_OnInteractionBegin;
        _baseCost.Clear(); _useCount.Clear(); _lastLog.Clear();
    }

    // ---- cost helpers ----

    internal static int BaseCostOf(PurchaseInteraction self)
        => _baseCost.TryGetValue(self, out var c) ? c : self.cost;

    internal static int UseCountOf(PurchaseInteraction self)
        => _useCount.TryGetValue(self, out var n) ? n : 0;

    // Non-owner price for a beacon-affected escalating shrine, ramped per successful use.
    // Single-use chests/shops keep a use count of 0 (1.4^0 = 1), so they are unaffected.
    internal static int EffectiveBaseCostFor(PurchaseInteraction self)
    {
        int uses = UseCountOf(self);
        if (uses == 0) return BaseCostOf(self);
        return Mathf.RoundToInt(BaseCostOf(self) * Mathf.Pow(ShrineEscalationMultiplier, uses));
    }

    // One-line counterfactual matrix for solo testing: owner pays vs. a hypothetical non-owner.
    internal static string DecisionString(PurchaseInteraction self)
    {
        int uses = UseCountOf(self);
        return $"base={BaseCostOf(self)} uses={uses} ramp=1.4^{uses}={Mathf.Pow(ShrineEscalationMultiplier, uses):0.##} " +
               $"owner->0 nonOwner->{EffectiveBaseCostFor(self)} disp={self.Networkcost}";
    }

    private void EnsureKeeper(PurchaseInteraction self)
    {
        if (self.GetComponent<HackingCostKeeper>() == null)
            self.gameObject.AddComponent<HackingCostKeeper>();
    }

    private static Interactability ApplyInstanceGate(
        PurchaseInteraction self, PlayerCharacterMasterController player, Interactability interactability)
    {
        var ih = self.GetComponent<InstanceHandler>();
        if (player && ih && !ih.Players.Contains(player))
            return Interactability.Disabled;
        return interactability;
    }

    // ---- hooks ----

    private void On_Start(On.RoR2.PurchaseInteraction.orig_Start orig, PurchaseInteraction self)
    {
        orig(self);
        if (self.costType == CostTypeIndex.Money && !_baseCost.ContainsKey(self))
        {
            _baseCost[self] = self.cost; // captured before any beacon hack
            V($"Start: base cost {self.name} = {self.cost}");
        }
    }

    private Interactability On_GetInteractability(
        On.RoR2.PurchaseInteraction.orig_GetInteractability orig, PurchaseInteraction self, Interactor activator)
    {
        var player = activator.GetComponent<CharacterBody>()?.master?.playerCharacterMasterController;

        // 1) Active hack: lock the target to the beacon owner (others can't buy-steal it).
        if (HackingBeaconHandler.HackedBy.TryGetValue(self, out var lockOwner))
        {
            var locked = orig(self, activator);
            if (player != lockOwner) locked = Interactability.Disabled;
            VThrottled(self.GetInstanceID(),
                $"GetInteractability(lock) {self.name} player={NameOf(player)} owner={NameOf(lockOwner)} -> {locked}");
            return ApplyInstanceGate(self, player, locked);
        }

        // 2) Beacon-affected escalating shrine, no longer actively hacked: non-owner pays ramped base.
        int saved = self.cost;
        PlayerCharacterMasterController affOwner = null;
        if (self.costType == CostTypeIndex.Money && HackingBeaconHandler.BeaconAffected.TryGetValue(self, out affOwner))
        {
            EnsureKeeper(self);
            self.cost = (player == affOwner) ? 0 : EffectiveBaseCostFor(self);
            VThrottled(self.GetInstanceID(),
                $"GetInteractability(escalate) {self.name} player={NameOf(player)} owner={NameOf(affOwner)} " +
                $"cost(swapped)={self.cost} | {DecisionString(self)}");
        }

        var interactability = orig(self, activator);
        self.cost = saved;
        return ApplyInstanceGate(self, player, interactability);
    }

    private void On_OnInteractionBegin(
        On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
    {
        var player = activator.GetComponent<CharacterBody>()?.master?.playerCharacterMasterController;
        bool hacked = HackingBeaconHandler.HackedBy.TryGetValue(self, out var lockOwner);

        // Server-authoritative anti-steal: a non-owner cannot complete the actively-hacked purchase.
        if (hacked && NetworkServer.active && player != null && player != lockOwner)
        {
            V($"lock refuse {self.name} player={NameOf(player)} owner={NameOf(lockOwner)} (actively hacked)");
            return; // do not call orig -> purchase blocked
        }

        PlayerCharacterMasterController affOwner = null;
        bool affected = self.costType == CostTypeIndex.Money &&
                        HackingBeaconHandler.BeaconAffected.TryGetValue(self, out affOwner);

        int saved = self.cost;
        if (!hacked && affected)
        {
            EnsureKeeper(self);
            self.cost = (player == affOwner) ? 0 : EffectiveBaseCostFor(self);
        }

        // Loot ownership: the manual opener, else (auto-buy) the beacon owner.
        if (player != null)
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, player);
        else if (hacked && lockOwner != null)
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, lockOwner);

        V($"OnInteractionBegin {self.name} player={NameOf(player)} hacked={hacked} affected={affected} " +
          $"owner={NameOf(hacked ? lockOwner : affOwner)} costCharged={self.cost} costType={self.costType} | {DecisionString(self)}");

        orig(self, activator);
        self.cost = saved;

        // Count successful uses so a beacon-affected escalating shrine ramps the next non-owner price.
        if (HackingBeaconHandler.BeaconAffected.ContainsKey(self))
            _useCount[self] = UseCountOf(self) + 1;
    }
}
