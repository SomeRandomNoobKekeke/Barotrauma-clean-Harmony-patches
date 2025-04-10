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
    public static void PatchClientParticle()
    {
      harmony.Patch(
        original: typeof(Particle).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Particle_Draw_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Particles/Particle.cs#L575
    public static bool Particle_Draw_Replace(SpriteBatch spriteBatch, Particle __instance)
    {
      Particle _ = __instance;

      if (_.startDelay > 0.0f) { return false; }

      Vector2 drawSize = _.size;
      if (_.prefab.GrowTime > 0.0f && _.totalLifeTime - _.lifeTime < _.prefab.GrowTime)
      {
        drawSize *= MathUtils.SmoothStep((_.totalLifeTime - _.lifeTime) / _.prefab.GrowTime);
      }

      Color currColor = new Color(_.color.ToVector4() * _.ColorMultiplier);

      Vector2 drawPos = _.drawPosition;
      if (_.currentHull?.Submarine is Submarine sub)
      {
        drawPos += sub.DrawPosition;
      }

      drawPos = new Vector2(drawPos.X, -drawPos.Y);
      if (_.prefab.Sprites[_.spriteIndex] is SpriteSheet sheet)
      {
        sheet.Draw(
            spriteBatch, _.animFrame,
            drawPos,
            currColor * (currColor.A / 255.0f),
            _.prefab.Sprites[_.spriteIndex].Origin, _.drawRotation,
            drawSize, SpriteEffects.None, _.prefab.Sprites[_.spriteIndex].Depth);
      }
      else
      {
        _.prefab.Sprites[_.spriteIndex].Draw(spriteBatch,
            drawPos,
            currColor * (currColor.A / 255.0f),
            _.prefab.Sprites[_.spriteIndex].Origin, _.drawRotation,
            drawSize, SpriteEffects.None, _.prefab.Sprites[_.spriteIndex].Depth);
      }

      /*
      if (GameMain.DebugDraw && _.prefab.UseCollision)
      {
        GUI.DrawLine(spriteBatch,
            drawPos - Vector2.UnitX * _.colliderRadius.X,
            drawPos + Vector2.UnitX * _.colliderRadius.X,
            Color.Gray);
        GUI.DrawLine(spriteBatch,
            drawPos - Vector2.UnitY * _.colliderRadius.Y,
            drawPos + Vector2.UnitY * _.colliderRadius.Y,
            Color.Gray);
      }
      */

      return false;
    }

  }
}