using System;
using InstancedLoot.Components;
using InstancedLoot.Enums;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;

namespace InstancedLoot.Hooks;

public class BossGroupHandler : AbstractHookHandler
{
    public override void RegisterHooks()
    {
        On.RoR2.BossGroup.Start += On_BossGroup_Start;
        On.RoR2.BossGroup.DropRewards += On_BossGroup_DropRewards;
        IL.RoR2.BossGroup.DropRewards += IL_BossGroup_DropRewards;
    }

    public override void UnregisterHooks()
    {
        On.RoR2.BossGroup.Start -= On_BossGroup_Start;
        On.RoR2.BossGroup.DropRewards -= On_BossGroup_DropRewards;
        IL.RoR2.BossGroup.DropRewards -= IL_BossGroup_DropRewards;
    }

    private PickupDropletControllerHandler pickupDropletControllerHandler;
    protected PickupDropletControllerHandler PickupDropletControllerHandler
    {
        get
        {
            if (pickupDropletControllerHandler == null)
                pickupDropletControllerHandler = hookManager.GetHandler<PickupDropletControllerHandler>();

            return pickupDropletControllerHandler;
        }
    }

    private void On_BossGroup_Start(On.RoR2.BossGroup.orig_Start orig, BossGroup self)
    {
        string objName = self.name;
        string objectType = null;

        if (objName.StartsWith("LunarTeleporter")) objectType = ObjectType.TeleporterBoss;
        if (objName.StartsWith("Teleporter")) objectType = ObjectType.TeleporterBoss;
        if (objName.StartsWith("SuperRoboBallEncounter")) objectType = ObjectType.SuperRoboBallEncounter;
        if (objectType == null) objectType = ObjectType.BossGroup;
        
        Plugin.HandleInstancing(self.gameObject, new InstanceInfoTracker.InstanceOverrideInfo(objectType), isObject: false);
    }

    private void On_BossGroup_DropRewards(On.RoR2.BossGroup.orig_DropRewards orig, BossGroup self)
    {
        InstanceInfoTracker instanceInfoTracker = self.GetComponent<InstanceInfoTracker>();

        if (instanceInfoTracker == null)
        {
            orig(self);
            return;
        }
        
        string objectType = instanceInfoTracker.ObjectType;
        InstanceMode instanceMode = ModConfig.GetInstanceMode(objectType);

        bool origScaleRewardsByPlayerCount = self.scaleRewardsByPlayerCount;
        
        if (ModConfig.ReduceBossDrops.Value && Utils.IncreasesItemCount(instanceMode)) self.scaleRewardsByPlayerCount = false;

        PickupDropletControllerHandler.InstanceOverrideInfo = instanceInfoTracker.Info;
        orig(self);
        PickupDropletControllerHandler.InstanceOverrideInfo = null;

        self.scaleRewardsByPlayerCount = origScaleRewardsByPlayerCount;
    }

    private void IL_BossGroup_DropRewards(ILContext il)
    {
        ILCursor cursor = new ILCursor(il);

        int varLoopCount = -1;

        cursor.GotoNext(MoveType.Before, i => i.MatchCallOrCallvirt<PickupDropletController>("CreatePickupDroplet"),
            i => i.MatchLdloc(out varLoopCount));

        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloc, varLoopCount);
        cursor.EmitDelegate<Action<BossGroup, int>>((self, loopCount) =>
        {
            // Assign a round-robin owner to every boss drop, regardless of
            // scaleRewardsByPlayerCount. Owner-only instance modes (e.g. the default
            // InstanceItemForOwnerOnly) need a valid owner, otherwise HandleInstancing
            // bails on its null-owner guard and the drop stays un-instanced (grabbable by
            // anyone). For non-owner modes the assigned owner is simply ignored.
            if (PickupDropletControllerHandler.InstanceOverrideInfo.HasValue)
            {
                var playerIndex = loopCount % Run.instance.participatingPlayerCount;
                var player = PlayerCharacterMasterController.instances[playerIndex];

                var instanceOverrideInfo = PickupDropletControllerHandler.InstanceOverrideInfo.Value;

                instanceOverrideInfo.Owner = player;

                PickupDropletControllerHandler.InstanceOverrideInfo = instanceOverrideInfo;
            }
        });
    }
}