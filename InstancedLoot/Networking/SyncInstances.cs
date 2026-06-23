using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InstancedLoot.Components;
using InstancedLoot.Enums;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace InstancedLoot.Networking;

public class SyncInstances : INetMessage
{
    public struct InstanceHandlerEntry
    {
        public GameObject target;
        public GameObject[] players;
        public GameObject origPlayer;

        private NetworkInstanceId _target;
        private NetworkInstanceId[] _players;
        private NetworkInstanceId _origPlayer;
        private bool validated;

        public InstanceHandlerEntry(InstanceHandler instanceHandler)
        {
            target = instanceHandler.gameObject;
            players = instanceHandler.Players.Select(player => player.gameObject).ToArray();
            origPlayer = instanceHandler.OrigPlayer != null ? instanceHandler.OrigPlayer.gameObject : null;
        }

        public bool TryProcess()
        {
            if (validated) return true;

            target = Util.FindNetworkObject(_target);
            if (target == null) return false;

            if (_origPlayer != NetworkInstanceId.Invalid)
            {
                origPlayer = Util.FindNetworkObject(_origPlayer);
                if (origPlayer == null) return false;
            }

            players = new GameObject[_players.Length];
            for (int i = 0; i < _players.Length; i++)
            {
                var player = Util.FindNetworkObject(_players[i]);
                if (player == null) return false;
                players[i] = player;
            }

            validated = true;
            return true;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(target.GetComponent<NetworkIdentity>().netId);
            writer.Write(origPlayer == null ? NetworkInstanceId.Invalid : origPlayer.GetComponent<NetworkIdentity>().netId);
            
            writer.Write(players.Count());
            foreach (var player in players) writer.Write(player.GetComponent<NetworkIdentity>().netId);
        }

        public static InstanceHandlerEntry Deserialize(NetworkReader reader)
        {
            InstanceHandlerEntry entry = new();
            entry.validated = false;
            entry._target = reader.ReadNetworkId();
            entry._origPlayer = reader.ReadNetworkId();
            
            int count = reader.ReadInt32();
            NetworkInstanceId[] _players = new NetworkInstanceId[count];
            for (int i = 0; i < count; i++) _players[i] = reader.ReadNetworkId();

            entry._players = _players;

            return entry;
        }
    }

    private NetworkInstanceId _sourceObject;
    private ObjectInstanceMode objectInstanceMode;
    private InstanceHandlerEntry[] instanceHandlerEntries;

    public SyncInstances()
    {
        instanceHandlerEntries = Array.Empty<InstanceHandlerEntry>();
    }

    public SyncInstances(InstanceHandler.SharedInstanceInfo instanceInfo)
    {
        instanceHandlerEntries = instanceInfo.LinkedHandlers.Select(instanceHandler => new InstanceHandlerEntry(instanceHandler)).ToArray();
        objectInstanceMode = instanceInfo.ObjectInstanceMode;
        _sourceObject = instanceInfo.SourceObject == null
            ? NetworkInstanceId.Invalid
            : instanceInfo.SourceObject.GetComponent<NetworkIdentity>().netId;
    }

    public SyncInstances(IEnumerable<InstanceHandler> instanceHandlers)
    {
        instanceHandlerEntries = instanceHandlers.Select(instanceHandler => new InstanceHandlerEntry(instanceHandler)).ToArray();
    }
    
    public void Serialize(NetworkWriter writer)
    {
        writer.Write((int)objectInstanceMode);
        writer.Write(_sourceObject);
        writer.Write(instanceHandlerEntries.Length);
        foreach (var entry in instanceHandlerEntries) entry.Serialize(writer);
    }

    public void Deserialize(NetworkReader reader)
    {
        objectInstanceMode = (ObjectInstanceMode)reader.ReadInt32();
        _sourceObject = reader.ReadNetworkId();
        int entryCount = reader.ReadInt32();
        instanceHandlerEntries = new InstanceHandlerEntry[entryCount];
        for (int entryIndex = 0; entryIndex < entryCount; entryIndex++) instanceHandlerEntries[entryIndex] = InstanceHandlerEntry.Deserialize(reader);
    }

    public void OnReceived()
    {
        if (NetworkServer.active)
            //This ran a lot, let's just ignore it silently
            // InstancedLoot.Instance._logger.LogWarning("SyncInstances ran on Host, ignoring");
            return;

        InstancedLoot.Instance.StartCoroutine(HandleMessageInternal(objectInstanceMode, instanceHandlerEntries, _sourceObject));
    }

    private IEnumerator HandleMessageInternal(ObjectInstanceMode objectInstanceMode, InstanceHandlerEntry[] entries, NetworkInstanceId _sourceObject)
    {
        bool validated = false;

        // The only reason TryProcess fails is that a referenced networked object (a copy or a
        // player) hasn't spawned/registered on this client yet. That can take noticeably longer
        // than the old 40-frame (~0.7s) budget for late-spawning multishop terminals, after which
        // the sync was silently dropped forever (FailedSyncs is never drained) and both per-player
        // copies stayed visible. Retry every frame against a generous wall-clock timeout instead.
        float startTime = Time.unscaledTime;
        const float timeoutSeconds = 30f;

        GameObject sourceObject = null;

        while (!validated)
        {
            if (Time.unscaledTime - startTime > timeoutSeconds)
            {
                InstancedLoot.Instance._logger.LogError("SyncInstances failed to process within timeout; aborting.");
                InstancedLoot.FailedSyncs.Add(entries);
                yield break;
            }

            validated = true;

            for(int i = 0; i < entries.Length; i++) validated = validated && entries[i].TryProcess();

            if (sourceObject == null && _sourceObject != NetworkInstanceId.Invalid)
            {
                sourceObject = Util.FindNetworkObject(_sourceObject);

                validated = validated && sourceObject != null;
            }

            if (!validated) yield return 0;
        }

        InstanceHandler.SharedInstanceInfo sharedInstanceInfo = new();
        sharedInstanceInfo.ObjectInstanceMode = objectInstanceMode;
        sharedInstanceInfo.SourceObject = sourceObject;

        foreach (var entry in entries)
        {
            InstanceHandler instanceHandler = entry.target.GetComponent<InstanceHandler>();
            
            if (instanceHandler == null)
                instanceHandler = entry.target.AddComponent<InstanceHandler>();

            instanceHandler.SetPlayers(entry.players.Select(player =>
                player.GetComponent<PlayerCharacterMasterController>()), false);

            if(entry.origPlayer)
                instanceHandler.OrigPlayer = entry.origPlayer.GetComponent<PlayerCharacterMasterController>();

            if (sourceObject != null)
            {
                entry.target.transform.position = sourceObject.transform.position;
                entry.target.transform.rotation = sourceObject.transform.rotation;
                entry.target.transform.localScale = sourceObject.transform.localScale;
            }

            instanceHandler.SharedInfo = sharedInstanceInfo;
        }
        
        sharedInstanceInfo.RecalculateAllPlayers();

        InstanceHandler[] instanceHandlers = entries
            .Select(entry => entry.target.GetComponent<InstanceHandler>()).Where(handler => handler != null).ToArray();

        foreach (var instanceHandler in instanceHandlers) instanceHandler.UpdateVisuals();
    }
}