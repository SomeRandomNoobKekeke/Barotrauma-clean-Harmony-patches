using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientBackgroundCreatureManager()
    {
      harmony.Patch(
        original: typeof(BackgroundCreatureManager).GetMethod("SpawnCreatures", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("BackgroundCreatureManager_SpawnCreatures_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/BackgroundCreatures/BackgroundCreatureManager.cs#L64
    public static bool BackgroundCreatureManager_SpawnCreatures_Replace(Level level, int count, Vector2? position, BackgroundCreatureManager __instance)
    {
      BackgroundCreatureManager _ = __instance;

      _.creatures.Clear();

      if (_.prefabs.Count == 0) { return false; }

      count = Math.Min(count, BackgroundCreatureManager.MaxCreatures);

      List<BackgroundCreaturePrefab> availablePrefabs = new List<BackgroundCreaturePrefab>(_.prefabs);

      for (int i = 0; i < count; i++)
      {
        Vector2 pos = Vector2.Zero;
        if (position == null)
        {
          var wayPoints = WayPoint.WayPointList.FindAll(wp => wp.Submarine == null);
          if (wayPoints.Any())
          {
            WayPoint wp = wayPoints[Rand.Int(wayPoints.Count, Rand.RandSync.ClientOnly)];
            pos = new Vector2(wp.Rect.X, wp.Rect.Y);
            pos += Rand.Vector(200.0f, Rand.RandSync.ClientOnly);
          }
          else
          {
            pos = Rand.Vector(2000.0f, Rand.RandSync.ClientOnly);
          }
        }
        else
        {
          pos = (Vector2)position;
        }

        var prefab = ToolBox.SelectWeightedRandom(availablePrefabs, availablePrefabs.Select(p => p.GetCommonness(level.GenerationParams)).ToList(), Rand.RandSync.ClientOnly);
        if (prefab == null) { break; }

        int amount = Rand.Range(prefab.SwarmMin, prefab.SwarmMax + 1, Rand.RandSync.ClientOnly);
        List<BackgroundCreature> swarmMembers = new List<BackgroundCreature>();
        for (int n = 0; n < amount; n++)
        {
          var creature = new BackgroundCreature(prefab, pos + Rand.Vector(Rand.Range(0.0f, prefab.SwarmRadius, Rand.RandSync.ClientOnly), Rand.RandSync.ClientOnly));
          _.creatures.Add(creature);
          swarmMembers.Add(creature);
        }
        if (amount > 1)
        {
          new Swarm(swarmMembers, prefab.SwarmRadius, prefab.SwarmCohesion);
        }
        if (_.creatures.Count(c => c.Prefab == prefab) > prefab.MaxCount)
        {
          availablePrefabs.Remove(prefab);
          if (availablePrefabs.Count <= 0) { break; }
        }
      }

      return false;
    }


  }
}