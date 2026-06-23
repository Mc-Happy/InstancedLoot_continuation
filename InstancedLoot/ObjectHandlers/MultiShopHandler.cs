using InstancedLoot.Components;
using InstancedLoot.Enums;
using InstancedLoot.Hooks;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace InstancedLoot.ObjectHandlers;

public class MultiShopHandler : AbstractObjectHandler
{
    public override string[] HandledObjectTypes { get; } =
    {
        ObjectType.TripleShop,
        // ObjectType.TripleShopLarge, // As far as I'm aware, this one's unused.
        ObjectType.TripleShopEquipment,
        ObjectType.FreeChestMultiShop
    };

    public override ObjectInstanceMode ObjectInstanceMode => ObjectInstanceMode.CopyObject;

    public override bool CanObjectBeOwned(string objectType)
    {
        if (objectType == ObjectType.FreeChestMultiShop)
            return true;
        
        return base.CanObjectBeOwned(objectType);
    }

    public override bool IsValidForObject(string objectType, GameObject gameObject)
    {
        return gameObject.GetComponent<MultiShopController>() != null ||
               gameObject.GetComponent<ShopTerminalBehavior>() != null;
    }

    public override void Init(ObjectHandlerManager manager)
    {
        base.Init(manager);
        
        Plugin.HookManager.RegisterHandler<MultiShopControllerHandler>();
        Plugin.HookManager.RegisterHandler<ShopTerminalBehaviorHandler>();
        Plugin.HookManager.RegisterHandler<PurchaseInteractionHandler>();
        Plugin.HookManager.RegisterHandler<HackingBeaconHandler>();
    }

    public override InstanceHandler InstanceSingleObjectFrom(GameObject source, GameObject target,
        PlayerCharacterMasterController[] players)
    {
        InstanceHandler instanceHandler = base.InstanceSingleObjectFrom(source, target, players);
        
        if (source == target)
        {
            InstanceInfoTracker instanceInfoTracker = source.GetComponent<InstanceInfoTracker>();
            MultiShopController multiShopController = source.GetComponent<MultiShopController>();
            ShopTerminalBehavior shopTerminalBehavior = source.GetComponent<ShopTerminalBehavior>();
    
            if (multiShopController != null)
                foreach (var terminalGameObject in multiShopController.terminalGameObjects)
                {
                    InstanceSingleObjectFrom(terminalGameObject, terminalGameObject, players);
                    
                    if(instanceInfoTracker != null)
                        instanceInfoTracker.Info.AttachTo(terminalGameObject);
                }

            if (shopTerminalBehavior != null)
                instanceHandler.SharedInfo = new InstanceHandler.SharedInstanceInfo
                {
                    SourceObject = target,
                    ObjectInstanceMode = ObjectInstanceMode
                };
        }
        else
        {
            MultiShopController targetMultiShopController = target.GetComponent<MultiShopController>();
            if (targetMultiShopController != null)
            {
                MultiShopController sourceMultiShopController = source.GetComponent<MultiShopController>();

                // The clone controller (spawned via CloneObject/SpawnCard) already created its own
                // terminal set during spawn. CreateTerminals() below replaces terminalGameObjects
                // with a fresh set but leaves the original GameObjects alive in the scene; those
                // orphans then fire Start, hit the deferred-terminal path, and get instanced as a
                // duplicate set for this player. Destroy them first so exactly one mapped set remains.
                if (targetMultiShopController.terminalGameObjects != null)
                    foreach (var oldTerminal in targetMultiShopController.terminalGameObjects)
                        if (oldTerminal != null)
                            NetworkServer.Destroy(oldTerminal);

                targetMultiShopController.rng = new Xoroshiro128Plus(0); //Temporary RNG
                targetMultiShopController.CreateTerminals();
                targetMultiShopController.Networkcost = sourceMultiShopController.Networkcost;
                targetMultiShopController.rng = new Xoroshiro128Plus(sourceMultiShopController.rng);

                var sourceTerminalGameObjects = sourceMultiShopController.terminalGameObjects;
                var targetTerminalGameObjects = targetMultiShopController.terminalGameObjects;

                for (int i = 0; i < targetTerminalGameObjects.Length; i++)
                    AwaitObjectFor(targetTerminalGameObjects[i],
                        new AwaitedObjectInfo
                        {
                            SourceObject = sourceTerminalGameObjects[i],
                            Players = players
                        });
            }
            
            ShopTerminalBehavior targetShopTerminalBehavior = target.GetComponent<ShopTerminalBehavior>();
            if (targetShopTerminalBehavior != null)
            {
                ShopTerminalBehavior sourceShopTerminalBehavior = source.GetComponent<ShopTerminalBehavior>();

                targetShopTerminalBehavior.hasStarted = true;
                targetShopTerminalBehavior.rng = new Xoroshiro128Plus(sourceShopTerminalBehavior.rng);
                targetShopTerminalBehavior.Networkpickup = sourceShopTerminalBehavior.Networkpickup;
                targetShopTerminalBehavior.Networkhidden = sourceShopTerminalBehavior.Networkhidden;
                targetShopTerminalBehavior.UpdatePickupDisplayAndAnimations();
            }

            PurchaseInteraction targetPurchaseInteraction = target.GetComponent<PurchaseInteraction>();
            if (targetPurchaseInteraction != null)
            {
                PurchaseInteraction sourcePurchaseInteraction = source.GetComponent<PurchaseInteraction>();

                targetPurchaseInteraction.rng = sourcePurchaseInteraction.rng;
                targetPurchaseInteraction.Networkcost = sourcePurchaseInteraction.Networkcost;
            }
        }

        return instanceHandler;
    }
}