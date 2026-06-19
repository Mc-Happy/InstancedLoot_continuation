using InstancedLoot.Components;
using RoR2;
using UnityEngine.Networking;

namespace InstancedLoot.Hooks;

public class RouletteChestControllerHandler : AbstractHookHandler
{
    public override void RegisterHooks()
    {
        On.RoR2.RouletteChestController.EjectPickupServer += On_RouletteChestController_EjectPickupServer;
        On.RoR2.RouletteChestController.Start += On_RouletteChestController_Start;
    }

    public override void UnregisterHooks()
    {
        On.RoR2.RouletteChestController.EjectPickupServer -= On_RouletteChestController_EjectPickupServer;
        On.RoR2.RouletteChestController.Start -= On_RouletteChestController_Start;
    }

    private void On_RouletteChestController_EjectPickupServer(On.RoR2.RouletteChestController.orig_EjectPickupServer orig, RouletteChestController self, UniquePickup pickup)
    {
        if (self.GetComponent<InstanceInfoTracker>() is var instanceInfoTracker && instanceInfoTracker != null)
        {
            hookManager.GetHandler<PickupDropletControllerHandler>().InstanceOverrideInfo = instanceInfoTracker.Info;
            orig(self, pickup);
            hookManager.GetHandler<PickupDropletControllerHandler>().InstanceOverrideInfo = null;
        }
        else
        {
            orig(self, pickup);
        }
    }

    private void On_RouletteChestController_Start(On.RoR2.RouletteChestController.orig_Start orig, RouletteChestController self)
    {
        if (NetworkServer.active)
        {
            if (Plugin.ObjectHandlerManager.HandleAwaitedObject(self.gameObject))
                return;
            
            InstanceHandler instanceHandler = self.GetComponent<InstanceHandler>();
            if (instanceHandler == null)
            {
                orig(self);

                string objName = self.name;
                string objectType = null;
                
                if(objName.StartsWith("CasinoChest")) objectType = Enums.ObjectType.CasinoChest;
                
                if(objectType != null) Plugin.HandleInstancing(self.gameObject, new InstanceInfoTracker.InstanceOverrideInfo(objectType));
            }
        }
        else
        {
            orig(self);
        }
    }
    
}