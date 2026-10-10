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

namespace WhatsInTheGrass.Patches;

/// <summary>Fades grass covering spawned forage or farm animals.</summary>
internal static class GrassFadePatch
{
  // Blade draw position is its base
  private static readonly Vector2 BladeCenterOffset = new(0f, -32f);

  // Blade centers can sit half a tile outside their tile
  private const float GrassReach = 32f;

  // Shared by all blades of the same grass this tick
  private static readonly List<FadeSource> Nearby = [];
  private static Grass? _lastGrass;
  private static int _lastTick = -1;

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

  public static Color GetBladeColor(Grass grass, Vector2 bladePosition)
  {
    if (!ReferenceEquals(grass, _lastGrass) || _lastTick != Game1.ticks)
    {
      _lastGrass = grass;
      _lastTick = Game1.ticks;
      CollectNearby(grass);
    }

    if (Nearby.Count == 0)
    {
      return Color.White;
    }

    Vector2 center = bladePosition + BladeCenterOffset;
    float opacity = 1f;
    foreach (FadeSource source in Nearby)
    {
      float t = (Vector2.Distance(center, source.Center) - source.Radius) / FadeSource.FadeWidth;
      if (t < 1f)
      {
        opacity = Math.Min(opacity, MathHelper.SmoothStep(source.MinOpacity, 1f, Math.Max(0f, t)));
      }
    }

    return Color.White * opacity;
  }

  // Fallback without blade position, fades the whole tuft
  public static Color GetGrassColor(Grass grass)
  {
    return GetBladeColor(grass, grass.Tile * Game1.tileSize + new Vector2(32f, 64f));
  }

  private static void CollectNearby(Grass grass)
  {
    Nearby.Clear();
    ModConfig config = ModEntry.Config;
    if (
      !(config.FadeOverForage || config.FadeOverAnimals)
      || grass.Location is not GameLocation location
    )
    {
      return;
    }

    Vector2 min = grass.Tile * Game1.tileSize - new Vector2(GrassReach);
    Vector2 max = min + new Vector2(Game1.tileSize + GrassReach * 2);
    foreach (FadeSource source in GrassFadeSources.Get(location))
    {
      Vector2 closest = Vector2.Clamp(source.Center, min, max);
      float outer = source.OuterRadius;
      if (Vector2.DistanceSquared(closest, source.Center) < outer * outer)
      {
        Nearby.Add(source);
      }
    }
  }

  /// <summary>Replaces the Color.White blade tint with GetBladeColor.</summary>
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
      MethodInfo globalToLocal = AccessTools.Method(
        typeof(Game1),
        nameof(Game1.GlobalToLocal),
        [typeof(xTile.Dimensions.Rectangle), typeof(Vector2)]
      );

      bool patched = false;
      for (int i = 0; i < codes.Count; i++)
      {
        if (!codes[i].Calls(colorWhite))
        {
          continue;
        }

        // Blade position is the local passed to GlobalToLocal
        CodeInstruction? loadPosition = null;
        for (int j = i - 1; j > 0; j--)
        {
          if (codes[j].Calls(globalToLocal))
          {
            OpCode load = codes[j - 1].opcode;
            if (codes[j - 1].IsLdloc() && load != OpCodes.Ldloca && load != OpCodes.Ldloca_S)
            {
              loadPosition = new CodeInstruction(codes[j - 1].opcode, codes[j - 1].operand);
            }

            break;
          }
        }

        loadGrass.MoveLabelsFrom(codes[i]);
        if (loadPosition != null)
        {
          codes[i] = new CodeInstruction(
            OpCodes.Call,
            AccessTools.Method(typeof(GrassFadePatch), nameof(GetBladeColor))
          );
          codes.InsertRange(i, [loadGrass, loadPosition]);
        }
        else
        {
          ModEntry.MonitorObject.Log(
            $"No blade position in {methodName}, fading whole grass tiles",
            LogLevel.Trace
          );
          codes[i] = new CodeInstruction(
            OpCodes.Call,
            AccessTools.Method(typeof(GrassFadePatch), nameof(GetGrassColor))
          );
          codes.Insert(i, loadGrass);
        }

        patched = true;
        break;
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
