using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using InstancedLoot.Components;
using InstancedLoot.Configuration;
using InstancedLoot.Enums;
using InstancedLoot.Hooks;
using InstancedLoot.Networking;
using MonoMod.RuntimeDetour.HookGen;
using R2API.Networking;
using R2API.Utils;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

//TODO: Chests now have PickupPickerController, which broke things. Applied dirty fix, should replace with something better, as chests now still get "instanced" as though they were items or pickup pickers.

//TODO: DitherModel objects start faded out
//      Seemingly fixed by checking player instead of camera. Should look for better fix later.

//TODO: Shrine of order on the moon failing to instance (missing prefab?)
//      Seems to be working, no idea what was wrong.

//TODO: Uninstance command needs networking for removing instancing
//      Temporary workaround - always use droplets, delete original if last player.

//TODO: PingerControllerRenderBehaviour needs special code for items, try to figure out why
//TODO: Test ReduceSacrificeSpawnChance
//TODO: Test ReduceInteractibleBudget
//TODO: Teleporter "claws" should only show things instanced for you
//TODO: Void cradle gave "Unknown command essence"?
//TODO: Handle disconnected players?
//      Compatibility with https://thunderstore.io/package/Moffein/Fix_Playercount/
//      Teleporter drop counting is going to be off and give items to the wrong players
//TODO: Instance drones (duh), perhaps later though - need to handle drones that broke correctly.
//      PurchaseInteraction - Interaction should be handled automatically
//      SummonMasterBehavior - If patched, won't have to copy object?
//      EntityStates.Drone.DeathState.OnImpactServer
//TODO: Lunar pods are fixed, but rely on coroutine running next frame.
//TODO: Instancing effects isn't complete, some effects don't work, some effects seem to use a different system.
//TODO: Instance pickup droplets for dithering - Dithering doesn't work, problem with networking, scrapping idea for now.
//TODO: PickupPickerControllerHandler needs coroutine, due to PickupPickerController only having Awake.
//TODO: Better way to forfeit items. Currently have only chat commands.
//TODO: See if dithering is possible for void objects (material doesn't support dithering)

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace InstancedLoot;

[BepInPlugin("com.kuberoot.instancedloot", "InstancedLoot", "1.4.0")]
[NetworkCompatibility]
[BepInDependency(NetworkingAPI.PluginGUID)]
public class InstancedLoot : BaseUnityPlugin
{
    public HookManager HookManager;
    public ObjectHandlerManager ObjectHandlerManager;

    internal ManualLogSource _logger => Logger;
    public static InstancedLoot Instance { get; private set; }
    public Config ModConfig { get; private set; }

    public static event Action<InstancedLoot> PreConfigInit;

    public void Awake()
    {
        ModConfig = new Config(this, Logger);
        HookManager = new HookManager(this);
        ObjectHandlerManager = new ObjectHandlerManager(this);

        NetworkingAPI.RegisterMessageType<SyncInstances>();

        // RoR2 doesn't auto-scan BepInEx-loaded assemblies for [ConCommand], so register ours
        // explicitly (before Console init) to expose the il_dump debug command.
        try { HG.Reflection.SearchableAttribute.ScanAssembly(typeof(InstancedLoot).Assembly); }
        catch (Exception e) { Logger.LogWarning($"il_dump command registration failed: {e}"); }
    }

    public static List<SyncInstances.InstanceHandlerEntry[]> FailedSyncs = new();

    public void Start()
    {
        PreConfigInit?.Invoke(this);
        
        ModConfig.Init();
    }

    public void OnEnable()
    {
        Instance = this;
        
        PlayerCharacterMasterController.onPlayerAdded += OnPlayerAdded;

        HookManager.RegisterHooks();
    }

    public void OnDisable()
    {
        if (Instance == this) Instance = null;
        
        HookManager.UnregisterHooks();
        
        PlayerCharacterMasterController.onPlayerAdded -= OnPlayerAdded;

        // TODO: Check if this works for non-hooks
        // Cleanup any leftover hooks
        HookEndpointManager.RemoveAllOwnedBy(HookEndpointManager.GetOwner((Action)OnDisable));
        
        foreach (var component in FindObjectsOfType<InstancedLootBehaviour>())
            if(component != null)
                Destroy(component);
    }
    
    private void OnPlayerAdded(PlayerCharacterMasterController player)
    {
        if (!NetworkServer.active) return;
        
        if (player == null || player.networkUser == null || player.networkUser.connectionToClient == null)
            return;
        
        HashSet<InstanceHandler> instancesToSend = new();

        foreach (var instanceHandler in InstanceHandler.Instances)
        {
            InstanceHandler main = instanceHandler.LinkedHandlers?[0] ?? instanceHandler;

            if (!instancesToSend.Contains(main))
            {
                instancesToSend.Add(main);
                main.SyncToPlayer(player);
            }
        }
    }

    public void HandleInstancing(GameObject obj, InstanceInfoTracker.InstanceOverrideInfo? overrideInfo = null, bool isObject = true)
    {
        InstanceInfoTracker instanceInfoTracker = obj.GetComponent<InstanceInfoTracker>();
        InstanceInfoTracker.InstanceOverrideInfo? existingOverrideInfo =
            instanceInfoTracker == null ? null : instanceInfoTracker.Info;

        // Unity overrides null comparison, shouldn't matter here, but the IDE keeps yelling at me, so just to be safe...
        if (instanceInfoTracker == null) instanceInfoTracker = null;

        string objectType = overrideInfo?.ObjectType ?? existingOverrideInfo?.ObjectType;
        PlayerCharacterMasterController owner = overrideInfo?.Owner;
        if(owner == null) owner = existingOverrideInfo?.Owner;

        if (instanceInfoTracker == null && objectType == null)
            return;
        
        InstanceMode instanceMode = ModConfig.GetInstanceMode(objectType ?? existingOverrideInfo?.ObjectType);
        
        if (instanceMode == InstanceMode.None)
            return;
        
        bool shouldInstance = false;
        bool ownerOnly = false;
        bool isSimple = false;

        if (existingOverrideInfo?.ObjectType == null)
        {
            switch (instanceMode)
            {
                case InstanceMode.InstanceBoth:
                case InstanceMode.InstanceObject:
                    shouldInstance = true;
                    ownerOnly = false;
                    break;
                case InstanceMode.InstanceBothForOwnerOnly:
                case InstanceMode.InstanceObjectForOwnerOnly:
                    shouldInstance = true;
                    ownerOnly = true;
                    break;
            }
        }

        if (((obj.GetComponent<GenericPickupController>() is var pickupController && pickupController != null &&
             (PickupCatalog.GetPickupDef(pickupController.pickupIndex)?.itemIndex ?? ItemIndex.None) != ItemIndex.None)
            || obj.GetComponent<PickupPickerController>() != null)
            && !isObject)
        {
            isSimple = true;
            switch (instanceMode)
            {
                case InstanceMode.InstanceObject:
                    shouldInstance = false;
                    break;
                case InstanceMode.InstanceBoth:
                case InstanceMode.InstanceItemForOwnerOnly:
                    shouldInstance = true;
                    ownerOnly = true;
                    break;
                case InstanceMode.InstanceItems:
                    shouldInstance = true;
                    ownerOnly = false;
                    break;
            }
        }

        if (!isSimple && shouldInstance) shouldInstance = ObjectHandlerManager.CanInstanceObject(objectType, obj);

        // if (existingOverrideInfo == null && overrideInfo != null)
        //     overrideInfo.Value.AttachTo(obj);
        // else if (instanceInfoTracker != null && owner != null) instanceInfoTracker.Info.Owner = owner;
        if(overrideInfo != null)
            overrideInfo.Value.AttachTo(obj);

        if (shouldInstance)
        {
            //If instancing should happen only for owner but owner is missing, don't instance to avoid duplication exploits
            if (ownerOnly && owner == null && existingOverrideInfo?.PlayerOverride == null)
                return;

            HashSet<PlayerCharacterMasterController> players;

            if (existingOverrideInfo?.PlayerOverride != null)
                players = new HashSet<PlayerCharacterMasterController>(existingOverrideInfo?.PlayerOverride);
            else if (ownerOnly)
                players = new HashSet<PlayerCharacterMasterController> { owner };
            else
                players = ModConfig.GetValidPlayersSet();

            if (isSimple)
            {
                InstanceHandler handler = obj.AddComponent<InstanceHandler>();

                handler.SharedInfo = new InstanceHandler.SharedInstanceInfo
                {
                    ObjectInstanceMode = ObjectInstanceMode.InstancedObject
                };

                handler.SetPlayers(players);
            }
            else
            {
                ObjectHandlerManager.InstanceObject(objectType, obj, players.ToArray());
            }
        }
    }

    public static bool CanUninstance(InstanceHandler instanceHandler, PlayerCharacterMasterController player)
    {
        if (!instanceHandler.Players.Contains(player)) return false;
        if (instanceHandler.AllPlayers.Count == 1) return true;
        if (instanceHandler.GetComponent<CreatePickupInfoTracker>()) return true;

        return false;
    }

    public void HandleUninstancing(InstanceHandler instanceHandler, PlayerCharacterMasterController player)
    {
        if (!instanceHandler.Players.Contains(player)) return;

        // if (instanceHandler.AllPlayers.Count == 1) // This is only instanced for one specific player
        // {
        //     Destroy(instanceHandler);
        //     return;
        // }

        if (instanceHandler.GetComponent<CreatePickupInfoTracker>() is var createPickupInfoTracker && createPickupInfoTracker)
        {
            Vector3 awayVector = instanceHandler.transform.position -
                                 (player.body != null ? player.body.transform.position : Vector3.zero);
            awayVector.y = 0.01f;
            awayVector.Normalize();

            GenericPickupController.CreatePickupInfo pickupInfo = createPickupInfoTracker.CreatePickupInfo;
            pickupInfo.position = instanceHandler.transform.position + Vector3.up * 3;

            PickupDropletController.CreatePickupDroplet(pickupInfo, pickupInfo.position, Vector3.up * 10 + awayVector * 5);

            instanceHandler.RemovePlayer(player);
            
            //Temporary workaround for networking issues - need a way to signal uninstancing of an object?
            if(instanceHandler.Players.Count == 0)
                Destroy(instanceHandler.gameObject);
        }

        // if (instanceHandler.GetComponent<GenericPickupController>() is var pickupController && pickupController)
        // {
        //     Vector3 awayVector = pickupController.transform.position -
        //                          (player.body != null ? player.body.transform.position : Vector3.zero);
        //     awayVector.z = 0.01f;
        //     awayVector.Normalize();
        //
        //     PickupDropletController.CreatePickupDroplet(pickupController.pickupIndex,
        //         pickupController.transform.position, Vector3.Normalize(Vector3.up * 3 + awayVector * 2));
        //     return;
        // }
    }
}