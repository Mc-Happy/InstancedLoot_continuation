using InstancedLoot.Components;
using InstancedLoot.Enums;
using InstancedLoot.Hooks;
using RoR2;
using UnityEngine;

namespace InstancedLoot.ObjectHandlers;

public class ShrineChanceHandler : AbstractObjectHandler
{
    public override ObjectInstanceMode ObjectInstanceMode => ObjectInstanceMode.CopyObject;
    public override string[] HandledObjectTypes { get; } = { ObjectType.ShrineChance };

    public override bool IsValidForObject(string objectType, GameObject gameObject)
    {
        return gameObject.GetComponent<ShrineChanceBehavior>() != null;
    }

    public override void Init(ObjectHandlerManager manager)
    {
        base.Init(manager);
        
        Plugin.HookManager.RegisterHandler<ShrineChanceBehaviorHandler>();
        Plugin.HookManager.RegisterHandler<PurchaseInteractionHandler>();
        Plugin.HookManager.RegisterHandler<HackingBeaconHandler>();
    }

    public override InstanceHandler InstanceSingleObjectFrom(GameObject source, GameObject target,
        PlayerCharacterMasterController[] players)
    {
        ShrineChanceBehavior sourceShrine = source.GetComponent<ShrineChanceBehavior>();
        ShrineChanceBehavior targetShrine = target.GetComponent<ShrineChanceBehavior>();
        
        targetShrine.rng = new Xoroshiro128Plus(sourceShrine.rng);
        targetShrine.purchaseInteraction.Networkcost = sourceShrine.purchaseInteraction.Networkcost;
        
        return base.InstanceSingleObjectFrom(source, target, players);
    }
}