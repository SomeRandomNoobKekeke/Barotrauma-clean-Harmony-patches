using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Particles;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientParticleManager()
    {
      harmony.Patch(
        original: typeof(ParticleManager).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ParticleManager_Draw_Replace"))
      );

      harmony.Patch(
        original: typeof(ParticleManager).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ParticleManager_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(ParticleManager).GetMethod("CreateParticle", AccessTools.all, new Type[]{
          typeof(ParticlePrefab),
          typeof(Vector2),
          typeof(Vector2),
          typeof(float),
          typeof(Hull),
          typeof(ParticleDrawOrder),
          typeof(float),
          typeof(float),
          typeof(Tuple<Vector2, Vector2> )
        }),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ParticleManager_CreateParticle_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Particles/ParticleManager.cs#L183
    public static bool ParticleManager_Update_Replace(float deltaTime, ParticleManager __instance)
    {
      ParticleManager _ = __instance;

      _.MaxParticles = GameSettings.CurrentConfig.Graphics.ParticleLimit;

      for (int i = 0; i < _.particleCount; i++)
      {
        bool remove;
        try
        {
          remove = _.particles[i].Update(deltaTime) == Particle.UpdateResult.Delete;
        }
        catch (Exception e)
        {
          DebugConsole.ThrowError("Particle update failed", e);
          remove = true;
        }

        if (remove) { _.RemoveParticle(i); }
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Particles/ParticleManager.cs#L101
    public static bool ParticleManager_CreateParticle_Replace(ParticlePrefab prefab, Vector2 position, Vector2 velocity, float rotation, Hull hullGuess, ParticleDrawOrder drawOrder, float collisionIgnoreTimer, float lifeTimeMultiplier, Tuple<Vector2, Vector2> tracerPoints, ParticleManager __instance, ref Particle __result)
    {
      ParticleManager _ = __instance;

      if (prefab == null || prefab.Sprites.Count == 0) { __result = null; return false; }
      if (_.particleCount >= _.MaxParticles)
      {
        for (int i = 0; i < _.particleCount; i++)
        {
          if (_.particles[i].Prefab.Priority < prefab.Priority ||
              (!_.particles[i].Prefab.DrawAlways && prefab.DrawAlways))
          {
            _.RemoveParticle(i);
            break;
          }
        }
        if (_.particleCount >= _.MaxParticles) { __result = null; return false; }
      }

      Vector2 particleEndPos = prefab.CalculateEndPosition(position, velocity);

      Vector2 minPos = new Vector2(Math.Min(position.X, particleEndPos.X), Math.Min(position.Y, particleEndPos.Y));
      Vector2 maxPos = new Vector2(Math.Max(position.X, particleEndPos.X), Math.Max(position.Y, particleEndPos.Y));

      if (tracerPoints != null)
      {
        minPos = new Vector2(
            Math.Min(Math.Min(minPos.X, tracerPoints.Item1.X), tracerPoints.Item2.X),
            Math.Min(Math.Min(minPos.Y, tracerPoints.Item1.Y), tracerPoints.Item2.Y));
        maxPos = new Vector2(
            Math.Max(Math.Max(maxPos.X, tracerPoints.Item1.X), tracerPoints.Item2.X),
            Math.Max(Math.Max(maxPos.Y, tracerPoints.Item1.Y), tracerPoints.Item2.Y));
      }

      Rectangle expandedViewRect = MathUtils.ExpandRect(_.cam.WorldView, ParticleManager.MaxOutOfViewDist);

      if (!prefab.DrawAlways)
      {
        if (minPos.X > expandedViewRect.Right || maxPos.X < expandedViewRect.X) { __result = null; return false; }
        if (minPos.Y > expandedViewRect.Y || maxPos.Y < expandedViewRect.Y - expandedViewRect.Height) { __result = null; return false; }
      }

      if (_.particles[_.particleCount] == null) { _.particles[_.particleCount] = new Particle(); }
      Particle particle = _.particles[_.particleCount];

      particle.Init(prefab, position, velocity, rotation, hullGuess, drawOrder, collisionIgnoreTimer, lifeTimeMultiplier, tracerPoints: tracerPoints);
      _.particleCount++;
      _.particlesInCreationOrder.AddFirst(particle);

      __result = particle; return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Particles/ParticleManager.cs#L223
    public static bool ParticleManager_Draw_Replace(ParticleManager __instance, SpriteBatch spriteBatch, bool inWater, bool? inSub, ParticleBlendState blendState, bool? background = false)
    {
      ParticleManager _ = __instance;

      ParticlePrefab.DrawTargetType drawTarget = inWater ? ParticlePrefab.DrawTargetType.Water : ParticlePrefab.DrawTargetType.Air;

      foreach (var particle in _.particlesInCreationOrder)
      {
        if (particle.BlendState != blendState) { continue; }
        //equivalent to !particles[i].DrawTarget.HasFlag(drawTarget) but garbage free and faster
        if ((particle.DrawTarget & drawTarget) == 0) { continue; }
        if (inSub.HasValue)
        {
          bool isOutside = particle.CurrentHull == null;
          if (particle.DrawOrder != ParticleDrawOrder.Foreground && isOutside == inSub.Value)
          {
            continue;
          }
        }
        if (background.HasValue)
        {
          bool isBackgroundParticle = particle.DrawOrder == ParticleDrawOrder.Background;
          if (background.Value != isBackgroundParticle) { continue; }
        }
        particle.Draw(spriteBatch);
      }

      return false;
    }
  }
}