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
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Particles/ParticleManager.cs#L223
    public static bool ParticleManager_Draw_Replace(SpriteBatch spriteBatch, bool inWater, bool? inSub, ParticleBlendState blendState, bool? background, ParticleManager __instance)
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