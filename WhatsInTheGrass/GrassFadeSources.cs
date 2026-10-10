using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using StardewValley;
using Object = StardewValley.Object;

namespace WhatsInTheGrass;

/// <summary>MinOpacity within Radius, then fades to full over FadeWidth.</summary>
internal readonly record struct FadeSource(Vector2 Center, float Radius, float MinOpacity)
{
  public const float FadeWidth = 64f;

  public float OuterRadius => Radius + FadeWidth;
}

/// <summary>Fade circles around forage and farm animals, rebuilt once per tick per location.</summary>
internal static class GrassFadeSources
{
  private const float FadeTicks = 15f;
  private const float ForageRadius = 64f;
  private const float AnimalRadiusMargin = 32f;

  // Per location, split-screen players can be in different locations
  private static readonly ConditionalWeakTable<GameLocation, LocationState> States = new();

  public static List<FadeSource> Get(GameLocation location)
  {
    LocationState state = States.GetValue(location, _ => new LocationState());
    if (state.Tick != Game1.ticks)
    {
      Update(location, state);
    }

    return state.Sources;
  }

  private static void Update(GameLocation location, LocationState state)
  {
    int elapsed = Game1.ticks - state.Tick;
    float step = state.Tick < 0 || elapsed <= 0 ? 1f : Math.Min(1f, elapsed / FadeTicks);
    state.Tick = Game1.ticks;
    state.Sources.Clear();

    ModConfig config = ModEntry.Config;
    if (config.FadeOverForage)
    {
      AddForage(location, state.Sources, ToOpacity(config.ForageOpacityPercent));
    }

    if (config.FadeOverAnimals)
    {
      UpdateAnimals(location, state, step);
      float animalOpacity = ToOpacity(config.AnimalOpacityPercent);
      foreach (AnimalFade fade in state.Animals.Values)
      {
        state.Sources.Add(
          new FadeSource(
            fade.Center,
            fade.Radius,
            MathHelper.Lerp(1f, animalOpacity, fade.Progress)
          )
        );
      }
    }
    else
    {
      state.Animals.Clear();
    }
  }

  private static void AddForage(GameLocation location, List<FadeSource> sources, float opacity)
  {
    foreach (KeyValuePair<Vector2, Object> pair in location.objects.Pairs)
    {
      Object? obj = pair.Value;
      if (
        obj != null
        && obj.IsSpawnedObject
        && !obj.bigCraftable.Value
        && obj.QualifiedItemId is not ("(O)590" or "(O)SeedSpot")
      )
      {
        Vector2 center = pair.Key * Game1.tileSize + new Vector2(Game1.tileSize / 2f);
        sources.Add(new FadeSource(center, ForageRadius, opacity));
      }
    }
  }

  // Eased so circles don't pop when animals enter or leave
  private static void UpdateAnimals(GameLocation location, LocationState state, float step)
  {
    state.Present.Clear();
    foreach (KeyValuePair<long, FarmAnimal> pair in location.animals.Pairs)
    {
      // Null on farmhands mid-sync or with broken modded animals
      FarmAnimal? animal = pair.Value;
      if (animal?.Sprite == null)
      {
        continue;
      }

      // Sprite draws 24px above Position
      float width = animal.Sprite.SpriteWidth * 4f;
      float height = animal.Sprite.SpriteHeight * 4f;
      Vector2 center = animal.Position + new Vector2(width / 2f, height / 2f - 24f);
      float radius = Math.Max(width, height) / 2f + AnimalRadiusMargin;

      state.Animals.TryGetValue(pair.Key, out AnimalFade fade);
      state.Animals[pair.Key] = new AnimalFade(center, radius, Math.Min(1f, fade.Progress + step));
      state.Present.Add(pair.Key);
    }

    state.Keys.Clear();
    state.Keys.AddRange(state.Animals.Keys);
    foreach (long id in state.Keys)
    {
      if (state.Present.Contains(id))
      {
        continue;
      }

      AnimalFade fade = state.Animals[id];
      float progress = fade.Progress - step;
      if (progress <= 0f)
      {
        state.Animals.Remove(id);
      }
      else
      {
        state.Animals[id] = fade with { Progress = progress };
      }
    }
  }

  private static float ToOpacity(int percent)
  {
    return Math.Clamp(percent, 0, 100) / 100f;
  }

  private readonly record struct AnimalFade(Vector2 Center, float Radius, float Progress);

  private sealed class LocationState
  {
    public readonly List<FadeSource> Sources = [];
    public readonly Dictionary<long, AnimalFade> Animals = new();
    public readonly HashSet<long> Present = [];
    public readonly List<long> Keys = [];
    public int Tick = -1;
  }
}
