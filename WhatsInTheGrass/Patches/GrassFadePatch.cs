using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using Object = StardewValley.Object;

namespace WhatsInTheGrass.Patches;

/// <summary>Fades grass that covers spawned forage (e.g. pig truffles) or farm animals so they show through.</summary>
internal static class GrassFadePatch
{
  // Grass blades overflow into the tiles above and beside them
  private static readonly Vector2[] ForageOffsets = [new(0, 0), new(0, -1), new(-1, 0), new(1, 0)];

  private static Grass? _lastGrass;
  private static int _lastTick = -1;
  private static Color _lastColor;

  public static void Initialize(Harmony harmony, bool moreGrassLoaded)
  {
    TryPatch(
      harmony,
      "Grass.draw",
      () => AccessTools.Method(typeof(Grass), nameof(Grass.draw), [typeof(SpriteBatch)])
    );

    if (moreGrassLoaded)
    {
      TryPatch(
        harmony,
        "More Grass DrawPrefix",
        () => AccessTools.Method("MoreGrass.Patches.GrassPatch:DrawPrefix")
      );
    }

    ModEntry.MonitorObject.Log(
      $"Grass fade patch applied, moreGrass={moreGrassLoaded}",
      LogLevel.Trace
    );
  }

  private static void TryPatch(Harmony harmony, string targetName, Func<MethodInfo?> getTarget)
  {
    try
    {
      MethodInfo? target = getTarget();
      if (target == null)
      {
        ModEntry.MonitorObject.Log($"{targetName} not found, grass won't fade", LogLevel.Warn);
        return;
      }

      harmony.Patch(
        target,
        transpiler: new HarmonyMethod(typeof(GrassFadePatch), nameof(GrassDraw_Transpiler))
      );
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log(
        $"Failed to patch {targetName}, grass won't fade: {ex.Message}",
        LogLevel.Warn
      );
    }
  }

  public static void ClearCache()
  {
    _lastGrass = null;
    _lastTick = -1;
  }

  public static Color GetGrassColor(Grass grass)
  {
    if (ReferenceEquals(grass, _lastGrass) && _lastTick == Game1.ticks)
    {
      return _lastColor;
    }

    _lastGrass = grass;
    _lastTick = Game1.ticks;
    _lastColor = ComputeGrassColor(grass);
    return _lastColor;
  }

  private static Color ComputeGrassColor(Grass grass)
  {
    ModConfig config = ModEntry.Config;
    if (
      !(config.FadeOverForage || config.FadeOverAnimals)
      || grass.Location is not GameLocation location
    )
    {
      return Color.White;
    }

    Vector2 tile = grass.Tile;
    float opacity = 1f;
    if (config.FadeOverForage && IsNearForage(location, tile))
    {
      opacity = ToOpacity(config.ForageOpacityPercent);
    }

    if (config.FadeOverAnimals)
    {
      float progress = AnimalGrassFade.GetProgress(location, tile);
      if (progress > 0f)
      {
        float animalOpacity = MathHelper.Lerp(1f, ToOpacity(config.AnimalOpacityPercent), progress);
        opacity = Math.Min(opacity, animalOpacity);
      }
    }

    return Color.White * opacity;
  }

  private static float ToOpacity(int percent)
  {
    return Math.Clamp(percent, 0, 100) / 100f;
  }

  private static bool IsNearForage(GameLocation location, Vector2 tile)
  {
    foreach (Vector2 offset in ForageOffsets)
    {
      if (HasForage(location, tile + offset))
      {
        return true;
      }
    }

    return false;
  }

  private static bool HasForage(GameLocation location, Vector2 tile)
  {
    return location.objects.TryGetValue(tile, out Object? obj)
      && obj != null
      && obj.IsSpawnedObject
      && !obj.bigCraftable.Value
      && obj.QualifiedItemId is not ("(O)590" or "(O)SeedSpot");
  }

  /// <summary>Replaces the Color.White grass tint with GetGrassColor(grass).</summary>
  private static IEnumerable<CodeInstruction> GrassDraw_Transpiler(
    IEnumerable<CodeInstruction> instructions,
    MethodBase original
  )
  {
    string methodName = $"{original.DeclaringType?.Name}.{original.Name}";
    try
    {
      CodeInstruction? loadGrass = LoadGrassArgument(original);
      if (loadGrass == null)
      {
        ModEntry.MonitorObject.Log($"No Grass argument in {methodName}", LogLevel.Warn);
        return instructions;
      }

      var codes = new List<CodeInstruction>(instructions);
      MethodInfo colorWhite = AccessTools.PropertyGetter(typeof(Color), nameof(Color.White));
      MethodInfo getGrassColor = AccessTools.Method(typeof(GrassFadePatch), nameof(GetGrassColor));

      bool patched = false;
      for (int i = 0; i < codes.Count; i++)
      {
        if (codes[i].Calls(colorWhite))
        {
          loadGrass.MoveLabelsFrom(codes[i]);
          codes[i] = new CodeInstruction(OpCodes.Call, getGrassColor);
          codes.Insert(i, loadGrass);
          patched = true;
          break;
        }
      }

      if (!patched)
      {
        ModEntry.MonitorObject.Log(
          $"Could not find Color.White in {methodName}, another mod may have already patched it",
          LogLevel.Warn
        );
      }

      return codes;
    }
    catch (Exception ex)
    {
      ModEntry.MonitorObject.Log($"Transpiler failed for {methodName}\n{ex}", LogLevel.Error);
      return instructions;
    }
  }

  private static CodeInstruction? LoadGrassArgument(MethodBase method)
  {
    if (!method.IsStatic && typeof(Grass).IsAssignableFrom(method.DeclaringType))
    {
      return new CodeInstruction(OpCodes.Ldarg_0);
    }

    int index = Array.FindIndex(method.GetParameters(), p => p.ParameterType == typeof(Grass));
    if (index < 0)
    {
      return null;
    }

    return new CodeInstruction(OpCodes.Ldarg_S, (byte)(method.IsStatic ? index : index + 1));
  }
}
