using InstancedLoot.Components;
using InstancedLoot.Enums;
using InstancedLoot.Hooks;
using RoR2;
using UnityEngine;

namespace InstancedLoot.ObjectHandlers;

public class RouletteChestHandler : AbstractObjectHandler
{
    public override string[] HandledObjectTypes { get; } = { "CasinoChest" };
    public override ObjectInstanceMode ObjectInstanceMode => ObjectInstanceMode.CopyObject;
    
    public override bool IsValidForObject(string objectType, GameObject gameObject)
    {
        return gameObject.GetComponent<RouletteChestController>() != null;
    }

    public override void Init(ObjectHandlerManager manager)
    {
        base.Init(manager);
        
        Plugin.HookManager.RegisterHandler<RouletteChestControllerHandler>();
        Plugin.HookManager.RegisterHandler<PurchaseInteractionHandler>();
        Plugin.HookManager.RegisterHandler<HackingBeaconHandler>();
    }

    public override InstanceHandler InstanceSingleObjectFrom(GameObject source, GameObject target,
        PlayerCharacterMasterController[] players)
    {
        RouletteChestController sourceChest = source.GetComponent<RouletteChestController>();
        RouletteChestController targetChest = target.GetComponent<RouletteChestController>();

        targetChest.rng = new Xoroshiro128Plus(sourceChest.rng);
        
        return base.InstanceSingleObjectFrom(source, target, players);
    }
}