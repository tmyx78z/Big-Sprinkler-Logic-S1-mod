using System.Collections;
using MelonLoader;
using BigSprinklerLogic.Helpers;
using HarmonyLib;
using S1API.Items;
using S1API.Shops;
using UnityEngine;
#if MONO
using FishNet;
using ScheduleOne.Employees;
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.EntityFramework;
using ScheduleOne.ObjectScripts;
using ScheduleOne.Tiles;
using System.Collections;
#else
using Il2CppFishNet;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Tiles;
using Il2CppSystem.Collections.Generic;
#endif

[assembly: MelonInfo(
    typeof(BigSprinklerLogic.BigSprinklerLogic),
    BigSprinklerLogic.BuildInfo.Name,
    BigSprinklerLogic.BuildInfo.Version,
    BigSprinklerLogic.BuildInfo.Author
)]
[assembly: MelonColor(1, 11, 57, 84)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BigSprinklerLogic;

public static class BuildInfo
{
    public const string Name = "BigSprinklerLogic";
    public const string Description = "Makes the big sprinkler work.";
    public const string Author = "k073l";
    public const string Version = "1.1.0";
}

public class BigSprinklerLogic : MelonMod
{
    private static MelonLogger.Instance Logger;
    private bool _shopsReady;

    public override void OnInitializeMelon()
    {
        Logger = LoggerInstance;
        Logger.Msg("BigSprinklerLogic initialized");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        switch (sceneName)
        {
            case "Menu":
                _shopsReady = false;
                break;
            case "Main":
                MelonCoroutines.Start(AddDelayed());
                break;
        }
    }

    private IEnumerator AddDelayed()
    {
        yield return new WaitForSeconds(2f);

        if (_shopsReady) yield break;

        var item = ItemManager.GetItemDefinition("bigsprinkler");
        if (item == null)
        {
            Logger.Error("Could not find bigsprinkler item definition!");
            yield break;
        }

        var addedCount = ShopManager.AddToCompatibleShops(item);
        Logger.Msg($"Added bigsprinkler to {addedCount} shops.");
        _shopsReady = true;
    }
}

internal static class SprinklerConvenienceMethods
{
    public static bool IsBigSprinkler(Sprinkler sprinkler)
    {
        var go = sprinkler.gameObject;
        var arrow = go.transform.Find("Arrow");
        return arrow == null;
    }

    public static System.Collections.Generic.HashSet<GridItem> GetItemsAroundItem(GridItem item)
    {
        var origin = new Coordinate(item._originCoordinate);

        var offsets = new System.Collections.Generic.List<Coordinate>();

        const int minX = -1;
        const int maxX = 2;
        const int minY = -1;
        const int maxY = 2;
        for (var x = minX; x <= maxX; x++)
        {
            offsets.Add(new Coordinate(x, minY)); // bottom row
            offsets.Add(new Coordinate(x, maxY)); // top row
        }

        for (var y = minY + 1; y <= maxY - 1; y++)
        {
            offsets.Add(new Coordinate(minX, y)); // left column
            offsets.Add(new Coordinate(maxX, y)); // right column
        }

        // rotate offsets
        var coords = offsets
            .Select(offset => origin + Coordinate.RotateCoordinates(offset, item._rotation))
            .ToList();

        var items = new System.Collections.Generic.HashSet<GridItem>();
        foreach (var coord in coords)
        {
            var tile = item.OwnerGrid.GetTile(coord);
            if (tile == null) continue;
            if (MelonDebug.IsEnabled())
                DrawDebugTile(tile.transform.position, 1f, Color.cyan);
            foreach (var occupant in tile.BuildableOccupants)
            {
                items.Add(occupant);
            }
        }

        return items;
    }

    private static void DrawDebugTile(Vector3 pos, float size, Color color)
    {
        var square = GameObject.CreatePrimitive(PrimitiveType.Quad);
        square.name = "DebugSquare";

        square.transform.position = pos + (Vector3.up * 0.01f);
        square.transform.rotation = Quaternion.Euler(90f, -30f, 0f);
        square.transform.localScale = Vector3.one * size * 0.25f;

        GameObject.Destroy(square.GetComponent<Collider>());

        var renderer = square.GetComponent<MeshRenderer>();

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        var mat = new Material(shader);

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);

        var baseColor = color;
        if (baseColor.a <= 0f)
            baseColor.a = 0.2f;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", baseColor);

        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 1.5f);
        }

        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;

        renderer.material = mat;
    }
}

[HarmonyPatch(typeof(Sprinkler), "GetPots")]
public class SprinklerPatches
{
    [HarmonyWrapSafe]
    public static bool Prefix(
        Sprinkler __instance,
#if MONO
        ref System.Collections.Generic.List<Pot> __result
#else
        ref Il2CppSystem.Collections.Generic.List<Pot> __result
#endif
    )
    {
        if (!SprinklerConvenienceMethods.IsBigSprinkler(__instance))
        {
            MelonDebug.Msg("Not a big sprinkler, skipping patch");
            return true;
        }

        MelonDebug.Msg("Applying big sprinkler logic");
        var pots = SprinklerConvenienceMethods.GetItemsAroundItem(__instance)
            .Select(x => (Success: Utils.Is<Pot>(x, out var pot), Pot: pot))
            .Where(t => t.Success && t.Pot != null)
            .Select(t => t.Pot);
#if MONO
        __result = pots.ToList();
#else
        __result = pots.ToIl2CppList();
#endif

        return false;
    }
}

// We have to patch OnActiveTick of GrowContainerBehaviour, instead of PerformAction,
// because IL2CPP and we can't have nice things. Also we can't just patch WaterPotBehaviour directly
// because the method isn't overriden (base class behavior is used) and Harmony can't find the target.
[HarmonyPatch(typeof(GrowContainerBehaviour), "OnActiveTick")]
public class WaterPotBehaviourPatches
{
    [HarmonyWrapSafe]
    public static void Postfix(GrowContainerBehaviour __instance)
    {
        if (!InstanceFinder.IsServer) return;
        if (__instance._currentState != GrowContainerBehaviour.EState.PerformingAction) return;
        if (!__instance.IsAtGrowContainer()) return;
        if (!Utils.Is<WaterPotBehaviour>(__instance, out var waterPotBehaviour)) return;
        if (!Utils.Is<Pot>(waterPotBehaviour._growContainer, out var pot)) return;

        var items = SprinklerConvenienceMethods.GetItemsAroundItem(pot);
        MelonDebug.Msg("Found " + items.Count + " items around the pot");
        foreach (var item in items)
        {
            if (!Utils.Is<Sprinkler>(item, out var sprinkler)) continue;
            if (!SprinklerConvenienceMethods.IsBigSprinkler(sprinkler)) continue;
            MelonDebug.Msg("It's a big sprinkler, activating it");
            if (!sprinkler.IsSprinkling)
                sprinkler.Interacted();
            waterPotBehaviour.OnStopPerformAction();
            waterPotBehaviour.OnActionSuccess(null);
            waterPotBehaviour.Disable_Networked(null);
            if (Utils.Is<Botanist>(waterPotBehaviour.Npc, out var botanist))
            {
                botanist?.SetIdle(true);
            }

            break;
        }
    }
}