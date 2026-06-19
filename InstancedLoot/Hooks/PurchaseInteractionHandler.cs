using System;
using System.Collections.Generic;
using InstancedLoot.Components;
using RoR2;
using UnityEngine;

namespace InstancedLoot.Hooks;

public class PurchaseInteractionHandler : AbstractHookHandler
{
    public const float HackingBeaconRadius = 15f; // verify vs 1.4.0-r.0; promote to Config if desired
    public const float ShrineEscalationMultiplier = 1.4f; // +40% per successful use (non-owners)

    // base cost captured at Start (before any beacon hack) and per-purchasable successful-use count.
    // Static so HackingCostKeeper can reach them via EffectiveBaseCostFor.
    private static readonly Dictionary<PurchaseInteraction, int> _baseCost = new();
    private static readonly Dictionary<PurchaseInteraction, int> _useCount = new();

    // ---- logging (set Verbose = false for release) ----
    internal static BepInEx.Logging.ManualLogSource Log;
    internal static bool Verbose = true;
    private static readonly HashSet<int> _dumped = new();
    private static readonly Dictionary<int, float> _lastLog = new();

    internal static void V(string msg) { if (Verbose) Log?.LogInfo("[IL-Hack] " + msg); }

    private static void VThrottled(int key, string msg, float interval = 1f)
    {
        float now = Time.unscaledTime;
        if (_lastLog.TryGetValue(key, out var t) && now - t < interval) return;
        _lastLog[key] = now; V(msg);
    }

    private static string Name(PlayerCharacterMasterController p)
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
        _baseCost.Clear(); _useCount.Clear(); _dumped.Clear(); _lastLog.Clear();
    }

    // ---- Captain hacking-beacon helpers (VERIFY members vs RiskOfRain2.GameLibs 1.4.0-r.0) ----

    public static CaptainSupplyDropController FindHackingBeaconAt(Vector3 pos)
    {
        CaptainSupplyDropController match = null;
        foreach (var beacon in UnityEngine.Object.FindObjectsOfType<CaptainSupplyDropController>())
        {
            DumpBeaconOnce(beacon);
            if (beacon.name.IndexOf("Hacking", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if ((beacon.transform.position - pos).sqrMagnitude <= HackingBeaconRadius * HackingBeaconRadius)
                match = beacon;
        }
        return match;
    }

    public static bool IsUnderHackingBeacon(Vector3 pos) => FindHackingBeaconAt(pos) != null;

    public static PlayerCharacterMasterController OwnerOf(CaptainSupplyDropController beacon)
    {
        var master = beacon != null ? beacon.GetComponent<Deployable>()?.ownerMaster : null;
        return master ? master.playerCharacterMasterController : null;
    }

    private static void DumpBeaconOnce(CaptainSupplyDropController b)
    {
        if (!_dumped.Add(b.GetInstanceID())) return;
        V($"===== beacon discovered: name='{b.name}' pos={b.transform.position} =====");
        foreach (var c in b.GetComponents<Component>()) V($"  component: {c.GetType().FullName}");
        var dep = b.GetComponent<Deployable>();
        V($"  Deployable={(dep ? "yes" : "null")} ownerMaster={(dep ? (object)dep.ownerMaster : "null")}");
        foreach (var f in b.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                 System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            if (f.FieldType == typeof(float)) V($"  float {f.Name} = {f.GetValue(b)}");
    }

    // ---- cost helpers ----

    private static int BaseCostOf(PurchaseInteraction self)
        => _baseCost.TryGetValue(self, out var c) ? c : self.cost;

    // Non-owner price under a beacon, ramped for escalating multi-use shrines.
    // Single-use chests/shops keep a use count of 0 (1.4^0 = 1), so they are unaffected.
    internal static int EffectiveBaseCostFor(PurchaseInteraction self)
    {
        int uses = _useCount.TryGetValue(self, out var n) ? n : 0;
        if (uses == 0) return BaseCostOf(self);
        return Mathf.RoundToInt(BaseCostOf(self) * Mathf.Pow(ShrineEscalationMultiplier, uses));
    }

    private void EnsureKeeper(PurchaseInteraction self)
    {
        if (self.GetComponent<HackingCostKeeper>() == null)
            self.gameObject.AddComponent<HackingCostKeeper>();
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
        var beacon = FindHackingBeaconAt(self.transform.position);

        int saved = self.cost;
        if (beacon != null && self.costType == CostTypeIndex.Money)
        {
            EnsureKeeper(self);                                                       // pin networked display to base/ramped
            self.cost = (OwnerOf(beacon) == player) ? 0 : EffectiveBaseCostFor(self); // owner free, others pay ramped base
            VThrottled(self.GetInstanceID(),
                $"GetInteractability {self.name} player={Name(player)} owner={Name(OwnerOf(beacon))} cost(swapped)={self.cost}");
        }

        var interactability = orig(self, activator);
        self.cost = saved;

        // existing instanced-object gate (keep)
        var ih = self.GetComponent<InstanceHandler>();
        if (player && ih && !ih.Players.Contains(player))
            interactability = Interactability.Disabled;
        return interactability;
    }

    private void On_OnInteractionBegin(
        On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
    {
        var player = activator.GetComponent<CharacterBody>()?.master?.playerCharacterMasterController;
        var beacon = FindHackingBeaconAt(self.transform.position);

        int saved = self.cost;
        bool underBeacon = beacon != null && self.costType == CostTypeIndex.Money;
        bool freeUse = beacon != null && (player == null || OwnerOf(beacon) == player);
        if (underBeacon)
        {
            EnsureKeeper(self);
            self.cost = freeUse ? 0 : EffectiveBaseCostFor(self); // auto-pop/owner free; others charged ramped price
        }

        if (player != null)
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, player);   // manual opener owns loot
        else if (OwnerOf(beacon) is var bo && bo != null)
            InstanceInfoTracker.InstanceOverrideInfo.SetOwner(self.gameObject, bo);       // auto-pop -> beacon owner

        V($"OnInteractionBegin {self.name} player={Name(player)} beacon={(beacon ? beacon.name : "none")} " +
          $"owner={Name(OwnerOf(beacon))} freeUse={freeUse} costCharged={self.cost} " +
          $"uses={(_useCount.TryGetValue(self, out var u) ? u : 0)} costType={self.costType}");

        orig(self, activator);
        self.cost = saved;

        // count successful uses so escalating multi-use shrines ramp the next non-owner price
        if (underBeacon)
            _useCount[self] = (_useCount.TryGetValue(self, out var n) ? n : 0) + 1;
    }
}
