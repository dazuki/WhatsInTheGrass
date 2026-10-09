using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using StardewValley;

namespace WhatsInTheGrass;

/// <summary>Per-tile fade progress (0-1) for grass around outdoor farm animals, eased so it doesn't pop as they walk.</summary>
internal static class AnimalGrassFade
{
  private const float FadeTicks = 15f;

  // Per location so split-screen players in different locations don't reset each other
  private static readonly ConditionalWeakTable<GameLocation, LocationState> States = new();

  public static float GetProgress(GameLocation location, Vector2 tile)
  {
    LocationState state = States.GetValue(location, _ => new LocationState());
    if (state.Tick != Game1.ticks)
    {
      Update(location, state);
    }

    return state.Progress.TryGetValue(tile, out float progress) ? progress : 0f;
  }

  // Once per tick instead of scanning every animal for every grass tile
  private static void Update(GameLocation location, LocationState state)
  {
    int elapsed = Game1.ticks - state.Tick;
    float step = state.Tick < 0 || elapsed <= 0 ? 1f : Math.Min(1f, elapsed / FadeTicks);
    state.Tick = Game1.ticks;

    CollectCoveredTiles(location, state.Covered);
    foreach (Vector2 tile in state.Covered)
    {
      state.Progress.TryGetValue(tile, out float progress);
      state.Progress[tile] = Math.Min(1f, progress + step);
    }

    state.Keys.Clear();
    state.Keys.AddRange(state.Progress.Keys);
    foreach (Vector2 tile in state.Keys)
    {
      if (state.Covered.Contains(tile))
      {
        continue;
      }

      float progress = state.Progress[tile] - step;
      if (progress <= 0f)
      {
        state.Progress.Remove(tile);
      }
      else
      {
        state.Progress[tile] = progress;
      }
    }
  }

  private static void CollectCoveredTiles(GameLocation location, HashSet<Vector2> tiles)
  {
    tiles.Clear();
    foreach (FarmAnimal? animal in location.animals.Values)
    {
      // Sprite is net-synced and can be missing on farmhands mid-sync or with broken modded animals
      if (animal?.Sprite == null)
      {
        continue;
      }

      // Sprite columns (wide animals span 2 tiles), feet rows plus the row below, whose blades reach up over the animal
      Rectangle box = animal.GetBoundingBox();
      int left = (int)animal.Position.X / Game1.tileSize;
      int right = ((int)animal.Position.X + animal.Sprite.SpriteWidth * 4 - 1) / Game1.tileSize;
      int top = box.Top / Game1.tileSize;
      int bottom = (box.Bottom - 1) / Game1.tileSize + 1;

      for (int x = left; x <= right; x++)
      {
        for (int y = top; y <= bottom; y++)
        {
          tiles.Add(new Vector2(x, y));
        }
      }
    }
  }

  private sealed class LocationState
  {
    public readonly Dictionary<Vector2, float> Progress = new();
    public readonly HashSet<Vector2> Covered = [];
    public readonly List<Vector2> Keys = [];
    public int Tick = -1;
  }
}
