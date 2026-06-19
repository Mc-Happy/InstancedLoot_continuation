using InstancedLoot.Hooks;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace InstancedLoot.Components;

// Server-side: while a Captain hacking beacon covers this purchasable, hold its networked
// (displayed) cost at the effective (ramped) value so non-owners see and pay the real price.
// The owner is waived transiently in PurchaseInteractionHandler, not here.
public class HackingCostKeeper : InstancedLootBehaviour
{
    private PurchaseInteraction _pi;
    private float _timer;

    private void Awake() => _pi = GetComponent<PurchaseInteraction>();

    private void FixedUpdate()
    {
        if (!NetworkServer.active || _pi == null) return;

        _timer += Time.fixedDeltaTime;
        if (_timer < 0.25f) return; // throttle the proximity scan
        _timer = 0f;

        if (PurchaseInteractionHandler.IsUnderHackingBeacon(_pi.transform.position))
        {
            int effective = PurchaseInteractionHandler.EffectiveBaseCostFor(_pi);
            if (_pi.costType == CostTypeIndex.Money && _pi.Networkcost != effective)
            {
                PurchaseInteractionHandler.V($"keeper pin {_pi.name}: Networkcost {_pi.Networkcost} -> {effective}");
                _pi.Networkcost = effective;
            }
        }
        else
        {
            PurchaseInteractionHandler.V($"keeper destroy {_pi.name} (no beacon)");
            Destroy(this);
        }
    }
}
