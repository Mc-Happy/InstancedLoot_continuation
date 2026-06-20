using InstancedLoot.Hooks;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace InstancedLoot.Components;

// Server-side: once a Captain hacking beacon has hacked this escalating purchasable, hold its
// networked (displayed) cost at the effective (ramped) value so non-owners see and pay the real
// price instead of the beacon-zeroed one. Skipped while the purchasable is the beacon's *active*
// hack target (it is locked to the owner then). The owner waiver is applied transiently in
// PurchaseInteractionHandler, not here. Attached on demand via EnsureKeeper.
public class HackingCostKeeper : InstancedLootBehaviour
{
    private PurchaseInteraction _pi;
    private float _timer;

    private void Awake() => _pi = GetComponent<PurchaseInteraction>();

    private void FixedUpdate()
    {
        if (!NetworkServer.active || _pi == null) return;
        if (_pi.costType != CostTypeIndex.Money) return;

        _timer += Time.fixedDeltaTime;
        if (_timer < 0.25f) return; // throttle
        _timer = 0f;

        bool affected = HackingBeaconHandler.BeaconAffected.ContainsKey(_pi);
        bool activelyHacked = HackingBeaconHandler.HackedBy.ContainsKey(_pi);

        if (affected && !activelyHacked)
        {
            int effective = PurchaseInteractionHandler.EffectiveBaseCostFor(_pi);
            if (_pi.Networkcost != effective)
            {
                PurchaseInteractionHandler.V($"keeper pin {_pi.name}: Networkcost {_pi.Networkcost} -> {effective}");
                _pi.Networkcost = effective;
            }
        }
    }
}
