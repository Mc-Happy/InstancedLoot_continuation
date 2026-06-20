using System;
using System.Collections.Generic;

namespace InstancedLoot.Hooks;

public class HookManager
{
    public readonly Dictionary<Type, AbstractHookHandler> HookHandlers = new();

    public readonly InstancedLoot Plugin;

    public HookManager(InstancedLoot pluginInstance)
    {
        Plugin = pluginInstance;
        
        #if DEBUG
        RegisterHandler<DevelopmentHooksHandler>();
        #endif
        
        RegisterHandler<SceneDirectorHandler>();
        RegisterHandler<SpawnCardHandler>();
        RegisterHandler<ChatHandler>();
        RegisterHandler<DeathRewardsHandler>();
        RegisterHandler<PurchaseInteractionHandler>();
        RegisterHandler<HackingBeaconHandler>();
        // RegisterHandler<EventFunctionsHandler>();
        
        RegisterHandler<PingHandler>();
        RegisterHandler<DitherModelHandler>();
        RegisterHandler<HologramProjectorHandler>();
        RegisterHandler<AnimationEventsHandler>();
        RegisterHandler<EffectManagerHandler>();
        
        RegisterHandler<GenericPickupControllerHandler>();
        RegisterHandler<PickupPickerControllerHandler>();
        RegisterHandler<PickupDropletControllerHandler>();
        
        // RegisterHandler<InteractorHandler>();
    }

    public void RegisterHandler<T>() where T : AbstractHookHandler, new()
    {
        if (HookHandlers.ContainsKey(typeof(T))) return;
        var instance = new T();
        instance.Init(this);
        HookHandlers[typeof(T)] = instance;
    }

    public T GetHandler<T>() where T : AbstractHookHandler
    {
        return (T)HookHandlers[typeof(T)];
    }

    public void RegisterHooks()
    {
        foreach (var handler in HookHandlers.Values) handler.RegisterHooks();
    }

    public void UnregisterHooks()
    {
        foreach (var handler in HookHandlers.Values)
            try
            {
                handler.UnregisterHooks();
            }
            catch (Exception e)
            {
                Plugin._logger.LogError($"Error while unloading HookHandler {handler.GetType()}, continuing:\n{e}");
            }
    }
}