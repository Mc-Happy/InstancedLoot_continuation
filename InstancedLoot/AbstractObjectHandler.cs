using System;
using System.Collections.Generic;
using System.Linq;
using InstancedLoot.Components;
using InstancedLoot.Enums;
using RoR2;
using UnityEngine;

namespace InstancedLoot;

public abstract class AbstractObjectHandler
{
    private static Dictionary<string, SpawnCard> _SpawnCardsForPrefabName;
    public static Dictionary<string, SpawnCard> SpawnCardsForPrefabName
    {
        get
        {
            if(_SpawnCardsForPrefabName == null)
                GenerateSpawnCards();
            return _SpawnCardsForPrefabName;
        }
    }
    public static void GenerateSpawnCards()
    {
        var spawnCards = Resources.FindObjectsOfTypeAll<InteractableSpawnCard>();
        if (_SpawnCardsForPrefabName == null) _SpawnCardsForPrefabName = new Dictionary<string, SpawnCard>();

        foreach (var spawnCard in spawnCards)
        {
            string name = spawnCard.prefab.name;
            if(!_SpawnCardsForPrefabName.ContainsKey(name))
                _SpawnCardsForPrefabName.Add(name, spawnCard);
        }
    }
    
    protected ObjectHandlerManager Manager;
    protected InstancedLoot Plugin => Manager.Plugin;

    /// <summary>
    /// Register hook handlers and other events here
    /// </summary>
    public virtual void Init(ObjectHandlerManager manager)
    {
        Manager = manager;
    }
    
    public abstract string[] HandledObjectTypes { get; }
    public abstract ObjectInstanceMode ObjectInstanceMode { get; }
    public virtual bool CanObjectBeOwned(string objectType) => false;
    public readonly Dictionary<GameObject, AwaitedObjectInfo> InfoForAwaitedObjects = new();

    public struct AwaitedObjectInfo
    {
        public GameObject SourceObject;
        public PlayerCharacterMasterController[] Players;
        public object ExtraInfo; // Just in case, to store anything
    }

    public virtual bool IsValidForObject(string objectType, GameObject gameObject)
    {
        return true;
    }

    public virtual void InstanceObject(string objectType, GameObject gameObject, PlayerCharacterMasterController[] players)
    {
        InstanceHandler[] instanceHandlers;
        PlayerCharacterMasterController[] primaryPlayers;
        InstanceHandler.SharedInstanceInfo sharedInstanceInfo = new InstanceHandler.SharedInstanceInfo();
        sharedInstanceInfo.SourceObject = gameObject;
        sharedInstanceInfo.ObjectInstanceMode = ObjectInstanceMode;
        
        switch (ObjectInstanceMode)
        {
            case ObjectInstanceMode.InstancedObject:
                instanceHandlers = new InstanceHandler[1];
                primaryPlayers = players;
                break;
            case ObjectInstanceMode.CopyObject:
                instanceHandlers = new InstanceHandler[players.Length];
                primaryPlayers = new[] { players[0] };
                break;
            default:
                throw new InvalidOperationException("Object handler doesn't support instancing objects (?)");
        }

        InstanceHandler primary =
            instanceHandlers[0] = InstanceSingleObjectFrom(gameObject, gameObject, primaryPlayers);
        primary.SharedInfo = sharedInstanceInfo;

        if (ObjectInstanceMode == ObjectInstanceMode.CopyObject)
            for (int i = 1; i < players.Length; i++)
            {
                GameObject newInstance = CloneObject(objectType, gameObject);

                if (newInstance != null)
                    AwaitObjectFor(newInstance, new AwaitedObjectInfo
                    {
                        SourceObject = gameObject, Players = new[]
                        {
                            players[i]
                        }
                    });
            }

        FinalizeSourceObjectIfNotAwaited(gameObject);
    }

    public virtual GameObject CloneObject(string objectType, GameObject gameObject)
    {
        GameObject clone = null;

        SpawnCard spawnCard = null;

        //Try to use exact SpawnCard used to create object
        if (gameObject.GetComponent<SpawnCardTracker>() is var spawnCardTracker && spawnCardTracker != null) spawnCard = spawnCardTracker.SpawnCard;

        //Fall back to scanning SpawnCards
        if (spawnCard == null)
        {
            string name = gameObject.name.Replace("(Cloned)", "");

            SpawnCardsForPrefabName.TryGetValue(name, out spawnCard);
        }

        if (spawnCard != null)
        {
            DirectorSpawnRequest spawnRequest = new(spawnCard, null, new Xoroshiro128Plus(0));
            SpawnCard.SpawnResult spawnResult = spawnCard.DoSpawn(gameObject.transform.position,
                gameObject.transform.rotation, spawnRequest);

            clone = spawnResult.spawnedInstance;

            if (clone != null)
            {
                clone.transform.position = gameObject.transform.position;
                clone.transform.rotation = gameObject.transform.rotation;
                clone.transform.localScale = gameObject.transform.localScale;
            }
        }
        else
        {
            Plugin._logger.LogError($"Failed to find spawn card for {gameObject}, objectType {objectType}");
        }

        return clone;
    }

    public virtual InstanceHandler InstanceSingleObjectFrom(GameObject source, GameObject target,
        PlayerCharacterMasterController[] players)
    {
        InstanceHandler instanceHandler = target.AddComponent<InstanceHandler>();
        instanceHandler.SetPlayers(players, false);
        if (ObjectInstanceMode == ObjectInstanceMode.CopyObject)
        {
            instanceHandler.OrigPlayer = players[0];

            InstanceInfoTracker instanceInfoTracker = source.GetComponent<InstanceInfoTracker>();
            if (instanceInfoTracker != null) instanceInfoTracker.Info.AttachTo(target);

            InstanceHandler sourceHandler = source.GetComponent<InstanceHandler>();
            instanceHandler.SharedInfo = sourceHandler.SharedInfo;

            // Clones (source != target) are spawned fresh via DoSpawn and generate their own
            // cost; force them to match the source object's authoritative cost so every player
            // sees the same price. (The primary instance has source == target, so it's untouched.)
            if (source != target)
            {
                PurchaseInteraction sourcePurchase = source.GetComponent<PurchaseInteraction>();
                PurchaseInteraction targetPurchase = target.GetComponent<PurchaseInteraction>();
                if (sourcePurchase != null && targetPurchase != null)
                    targetPurchase.Networkcost = sourcePurchase.Networkcost;
            }
        }
        return instanceHandler;
    }

    public virtual void FinalizeSourceObjectIfNotAwaited(GameObject sourceObject)
    {
        if (InfoForAwaitedObjects.All(pair => pair.Value.SourceObject != sourceObject)) FinalizeObject(sourceObject);
    }

    public virtual void FinalizeObject(GameObject sourceObject)
    {
        InstanceHandler sourceHandler = sourceObject.GetComponent<InstanceHandler>();
        
        sourceHandler.SharedInfo.SyncToAll();

        foreach (var instanceHandler in sourceHandler.SharedInfo.LinkedHandlers) instanceHandler.UpdateVisuals();
    }

    public virtual void AwaitObjectFor(GameObject target, AwaitedObjectInfo info)
    {
        InfoForAwaitedObjects.Add(target, info);
        Manager.RegisterAwaitedObject(target, this);
    }

    public virtual void HandleAwaitedObject(GameObject target)
    {
        AwaitedObjectInfo awaitedObjectInfo = InfoForAwaitedObjects[target];
        InfoForAwaitedObjects.Remove(target);
        InstanceSingleObjectFrom(awaitedObjectInfo.SourceObject, target, awaitedObjectInfo.Players);
        FinalizeSourceObjectIfNotAwaited(awaitedObjectInfo.SourceObject);
    }
}