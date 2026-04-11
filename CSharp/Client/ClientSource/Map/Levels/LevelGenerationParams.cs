using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientLevelGenerationParams()
    {
      harmony.Patch(
        original: typeof(LevelGenerationParams).GetMethod("DrawWaterParticles", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelGenerationParams_DrawWaterParticles_Replace"))
      );
    }

    public static bool LevelGenerationParams_DrawWaterParticles_Replace(LevelGenerationParams __instance, SpriteBatch spriteBatch, Camera cam, Vector2 offset)
    {
      LevelGenerationParams _ = __instance;

      if (_.WaterParticles == null || cam.Zoom <= 0.05f) { return false; }

      float textureScale = _.WaterParticleScale;
      Vector2 textureSize = new Vector2(_.WaterParticles.Texture.Width, _.WaterParticles.Texture.Height);
      Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
      offset -= origin;

      // Draw 4 layers of particles.
      for (int i = 0; i < 4; i++)
      {
        float scale = 1f - i * 0.2f;
        float alpha = MathUtils.InverseLerp(0.05f, 0.1f, cam.Zoom * scale);
        if (alpha == 0f) { continue; }

        Vector2 newOffset = offset * scale;
        newOffset += cam.WorldView.Size.ToVector2() * (1f - scale) * 0.5f;
        newOffset -= new Vector2(256f * i);

        float newTextureScale = scale * textureScale;

        Vector2 newSize = textureSize * scale;
        while (newOffset.X <= -newSize.X) { newOffset.X += newSize.X; }
        while (newOffset.X > 0f) { newOffset.X -= newSize.X; }
        while (newOffset.Y <= -newSize.Y) { newOffset.Y += newSize.Y; }
        while (newOffset.Y > 0f) { newOffset.Y -= newSize.Y; }

        _.WaterParticles.DrawTiled(spriteBatch, origin + newOffset, cam.WorldView.Size.ToVector2() - newOffset,
            color: _.WaterParticleColor * alpha, textureScale: new Vector2(newTextureScale));
      }

      return false;
    }

  }
}