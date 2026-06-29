using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InstancedLoot.Enums;
using RoR2;
using UnityEngine;

namespace InstancedLoot.Components;

public class FadeBehavior : InstancedLootBehaviour
{
    private static readonly int Fade = Shader.PropertyToID("_Fade");
    
    public float FadeLevel = 0.3f;

    private bool needsRefresh = true;

    public HashSet<GameObject> ExtraGameObjects = new();
    
    public HashSet<Renderer> Renderers;
    public HashSet<Renderer> DitherModelRenderers;
    public DitherModel[] DitherModels;
    private MaterialPropertyBlock propertyStorage;
    
    public Behaviour[] ComponentsForPreCull;
    // public Behaviour[] ComponentsForPreRender;

    public static readonly List<FadeBehavior> InstancesList = new();
    
    private CameraRigController lastCameraRigController;
    private PlayerCharacterMasterController lastPlayer;
    private PlayerCharacterMasterController lastDitherModelPlayer;
    private bool lastVisible;

    private bool isBeingDestroyed;

    static FadeBehavior()
    {
        SceneCamera.onSceneCameraPreCull += RefreshForPreCull;
        SceneCamera.onSceneCameraPreRender += RefreshForPreRender;
    }

    public void OnDestroy()
    {
        isBeingDestroyed = true;

        RefreshForPreCull(lastPlayer);
        RefreshForPreRender(lastPlayer);
    }

    public static void RefreshForPreCull(SceneCamera sceneCamera)
    {
        RefreshAllInstances(sceneCamera, true);
    }

    public static void RefreshForPreRender(SceneCamera sceneCamera)
    {
        RefreshAllInstances(sceneCamera, false);
    }

    public static void RefreshAllInstances(SceneCamera sceneCamera, bool isPreCull)
    {
        CameraRigController cameraRigController = sceneCamera.cameraRigController;
        if (!cameraRigController) return;

        CharacterBody body = cameraRigController.targetBody;
        if (!body) return;

        PlayerCharacterMasterController player = body.master != null ? body.master.playerCharacterMasterController : null;
        if (!player) return;
        
        if(isPreCull)
            foreach (var fadeBehavior in InstancesList)
                fadeBehavior.RefreshForPreCull(player);
        else
            foreach (var fadeBehavior in InstancesList)
                fadeBehavior.RefreshForPreRender(player);
    }

    public void RefreshInstanceForCamera(SceneCamera sceneCamera)
    {
        CameraRigController cameraRigController = sceneCamera.cameraRigController;
        if (!cameraRigController) return;

        CharacterBody body = cameraRigController.targetBody;
        if (!body) return;

        PlayerCharacterMasterController player = body.master != null ? body.master.playerCharacterMasterController : null;
        if (!player) return;
        
        RefreshForPreRender(player);
        RefreshForPreCull(player);
    }
    
    private void Awake()
    {
        propertyStorage = new MaterialPropertyBlock();
    }

    private void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        InstancesList.Add(this);
    }

    private void OnDisable()
    {
        InstancesList.Remove(this);
    }

    public float GetFadeLevelForCameraRigController(CameraRigController cameraRigController)
    {
        if(needsRefresh)
            RefreshComponentLists();
        
        bool isVisible = false;

        if (cameraRigController.targetBody is var body
            && body
            && body.master is var master && master
            && master.playerCharacterMasterController is var player && player)
        {
            if (player == lastDitherModelPlayer)
            {
                isVisible = lastVisible;
            }
            else if(GetComponent<InstanceHandler>() is var instanceHandler && instanceHandler)
            {
                isVisible = instanceHandler.IsInstancedFor(player);
            }
            else
            {
                isVisible = true;
            }

            lastDitherModelPlayer = player;
        }
        else
        {
            lastDitherModelPlayer = null;
        }
        
        
        // if (lastCameraRigController == cameraRigController)
        // {
        //     isVisible = lastVisible;
        // }
        // else
        // {
        //     if (cameraRigController.targetBody is var body 
        //         && body 
        //         && body.master is var master && master
        //         && master.playerCharacterMasterController is var player && player
        //         && GetComponent<InstanceHandler>() is var instanceHandler && instanceHandler)
        //     {
        //         isVisible = instanceHandler.IsInstancedFor(player);
        //     }
        // }

        // lastCameraRigController = cameraRigController;
        lastVisible = isVisible;

        return isVisible ? 1.0f : FadeLevel;
    }

    private static IEnumerable<T> CustomGetComponents<T>(IEnumerable<GameObject> gameObjects)
    {
        return gameObjects.SelectMany(obj => obj.GetComponentsInChildren<T>());
    }

    public void RefreshNextFrame()
    {
        StartCoroutine(RefreshNextFrameCoroutine());
        return;

        IEnumerator RefreshNextFrameCoroutine()
        {
            yield return 0;
            Refresh();
        }
    }

    public void Refresh()
    {
        needsRefresh = true;
    }

    public void RefreshComponentLists()
    {
        needsRefresh = false;
        lastPlayer = null;
        lastCameraRigController = null;

        ExtraGameObjects.RemoveWhere(obj => obj == null);
        
        HashSet<GameObject> gameObjects = [gameObject];
        gameObjects.UnionWith(ExtraGameObjects);
        
        ModelLocator[] modelLocators = GetComponentsInChildren<ModelLocator>();
        gameObjects.UnionWith(modelLocators.Select(modelLocator => modelLocator.modelTransform.gameObject));
        // Guard against a CostHologramContent whose targetTextMesh isn't wired up yet: this runs
        // from the static SceneCamera pre-cull callback, so an NPE here would abort the per-player
        // hide pass for every other instance in the batch that frame (leaving copies un-hidden).
        gameObjects.UnionWith(CustomGetComponents<CostHologramContent>(gameObjects).ToArray()
            .Where(hologram => hologram != null && hologram.targetTextMesh != null)
            .Select(hologram => hologram.targetTextMesh.gameObject));
        
        DitherModels = CustomGetComponents<DitherModel>(gameObjects).ToArray();
        DitherModelRenderers = [..DitherModels.SelectMany(ditherModel => ditherModel.renderers)];
        Renderers =
            [..CustomGetComponents<Renderer>(gameObjects).Where(renderer => !DitherModelRenderers.Contains(renderer))];
        
        HashSet<Behaviour> componentsForPreCull = new(CustomGetComponents<Highlight>(gameObjects));
        componentsForPreCull.UnionWith(CustomGetComponents<Light>(gameObjects));
        
        ComponentsForPreCull = componentsForPreCull.ToArray();

        // HashSet<Behaviour> componentsForPreRender = new();
        
        // ComponentsForPreRender = componentsForPreRender.ToArray();

        foreach (var renderer in Renderers)
        foreach (var material in renderer.materials) material.EnableKeyword("DITHER");
    }

    public void RefreshForPreCull(PlayerCharacterMasterController player)
    {
        if (needsRefresh)
            RefreshComponentLists();
        
        if (player == lastPlayer && !isBeingDestroyed)
            return;
        
        var instanceHandler = GetComponent<InstanceHandler>();
        bool isCopyObject = instanceHandler != null ? instanceHandler.ObjectInstanceMode == ObjectInstanceMode.CopyObject : true;

        if (isCopyObject && (instanceHandler == null || instanceHandler.AllOrigPlayers.Contains(player)))
        {
            bool isOrigForCurrent = isBeingDestroyed || instanceHandler.OrigPlayer == player;
            foreach (var renderer in Renderers)
            {
                if (renderer == null)
                {
                    if (isBeingDestroyed) continue;
                    RefreshComponentLists();
                    return;
                }
                renderer.enabled = isOrigForCurrent;
            }
            
            foreach (var renderer in DitherModelRenderers)
            {
                if (renderer == null)
                {
                    if (isBeingDestroyed) continue;
                    RefreshComponentLists();
                    return;
                }
                renderer.enabled = isOrigForCurrent;
            }
            
            foreach (var component in ComponentsForPreCull)
            {
                if (component == null)
                {
                    if (isBeingDestroyed) continue;
                    RefreshComponentLists();
                    return;
                }
                component.enabled = isOrigForCurrent;
            }
        }
    }
    
    public void RefreshForPreRender(PlayerCharacterMasterController player)
    {
        if (needsRefresh)
            RefreshComponentLists();
        
        if (player == lastPlayer && !isBeingDestroyed)
            return;
        
        var instanceHandler = GetComponent<InstanceHandler>();
        bool isForCurrentPlayer = isBeingDestroyed || instanceHandler.IsInstancedFor(player);
        float actualFadeLevel = isForCurrentPlayer ? 1.0f : FadeLevel;
        
        foreach (var renderer in Renderers)
        {
            if (renderer == null)
            {
                if (isBeingDestroyed) continue;
                RefreshComponentLists();
                return;
            }
            renderer.GetPropertyBlock(propertyStorage);
            propertyStorage.SetFloat(Fade, actualFadeLevel);
            renderer.SetPropertyBlock(propertyStorage);
        }

        // bool isCopyObject = instanceHandler != null ? instanceHandler.ObjectInstanceMode == ObjectInstanceMode.CopyObject : true;
        //
        // if (isCopyObject)
        // {
        //     bool isOrigForCurrent = isBeingDestroyed || instanceHandler.OrigPlayer == player;
        //     
        //     foreach (var component in ComponentsForPreRender)
        //     {
        //         if (component == null)
        //         {
        //             if (isBeingDestroyed) continue;
        //             RefreshComponentLists();
        //             return;
        //         }
        //         component.enabled = isOrigForCurrent;
        //     }
        // }

        lastPlayer = player;
    }

    public static FadeBehavior Attach(GameObject obj)
    {
        FadeBehavior fadeBehavior = obj.GetComponent<FadeBehavior>();
        if (fadeBehavior != null)
        {
            fadeBehavior.Refresh();
            return fadeBehavior;
        }

        fadeBehavior = obj.AddComponent<FadeBehavior>();
        return fadeBehavior;
    }
}