using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Extensions;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientLevelRenderer()
    {
      harmony.Patch(
        original: typeof(LevelRenderer).GetMethod("DrawBackground", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelRenderer_DrawBackground_Replace"))
      );

      harmony.Patch(
        original: typeof(LevelRenderer).GetMethod("DrawForeground", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelRenderer_DrawForeground_Replace"))
      );

      harmony.Patch(
        original: typeof(LevelRenderer).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelRenderer_Update_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelRenderer.cs#L149
    public static bool LevelRenderer_Update_Replace(float deltaTime, Camera cam, LevelRenderer __instance)
    {
      LevelRenderer _ = __instance;

      if (_.CollapseEffectStrength > 0.0f)
      {
        _.CollapseEffectStrength = Math.Max(0.0f, _.CollapseEffectStrength - deltaTime);
      }
      if (_.ChromaticAberrationStrength > 0.0f)
      {
        _.ChromaticAberrationStrength = Math.Max(0.0f, _.ChromaticAberrationStrength - deltaTime * 10.0f);
      }

      if (_.level.GenerationParams.FlashInterval.Y > 0)
      {
        _.flashCooldown -= deltaTime;
        if (_.flashCooldown <= 0.0f)
        {
          _.flashTimer = 1.0f;
          if (_.level.GenerationParams.FlashSound != null)
          {
            _.level.GenerationParams.FlashSound.Play(1.0f, "default");
          }
          _.flashCooldown = Rand.Range(_.level.GenerationParams.FlashInterval.X, _.level.GenerationParams.FlashInterval.Y, Rand.RandSync.Unsynced);
        }
        if (_.flashTimer > 0.0f)
        {
          float brightness = _.flashTimer * 1.1f - PerlinNoise.GetPerlin((float)Timing.TotalTime, (float)Timing.TotalTime * 0.66f) * 0.1f;
          _.FlashColor = _.level.GenerationParams.FlashColor.Multiply(MathHelper.Clamp(brightness, 0.0f, 1.0f));
          _.flashTimer -= deltaTime * 0.5f;
        }
        else
        {
          _.FlashColor = Color.TransparentBlack;
        }
      }

      //calculate the sum of the forces of nearby level triggers
      //and use it to move the water texture and water distortion effect
      Vector2 currentWaterParticleVel = _.level.GenerationParams.WaterParticleVelocity;
      foreach (LevelObject levelObject in _.level.LevelObjectManager.GetVisibleObjects())
      {
        if (levelObject.Triggers == null) { continue; }
        //use the largest water flow velocity of all the triggers
        Vector2 objectMaxFlow = Vector2.Zero;
        foreach (LevelTrigger trigger in levelObject.Triggers)
        {
          Vector2 vel = trigger.GetWaterFlowVelocity(cam.WorldViewCenter);
          if (vel.LengthSquared() > objectMaxFlow.LengthSquared())
          {
            objectMaxFlow = vel;
          }
        }
        currentWaterParticleVel += objectMaxFlow;
      }

      _.waterParticleVelocity = Vector2.Lerp(_.waterParticleVelocity, currentWaterParticleVel, deltaTime);

      WaterRenderer.Instance?.ScrollWater(_.waterParticleVelocity, deltaTime);

      if (_.level.GenerationParams.WaterParticles != null)
      {
        Vector2 waterTextureSize = _.level.GenerationParams.WaterParticles.size * _.level.GenerationParams.WaterParticleScale;
        _.waterParticleOffset += new Vector2(_.waterParticleVelocity.X, -_.waterParticleVelocity.Y) * _.level.GenerationParams.WaterParticleScale * deltaTime;
        while (_.waterParticleOffset.X <= -waterTextureSize.X) { _.waterParticleOffset.X += waterTextureSize.X; }
        while (_.waterParticleOffset.X >= waterTextureSize.X) { _.waterParticleOffset.X -= waterTextureSize.X; }
        while (_.waterParticleOffset.Y <= -waterTextureSize.Y) { _.waterParticleOffset.Y += waterTextureSize.Y; }
        while (_.waterParticleOffset.Y >= waterTextureSize.Y) { _.waterParticleOffset.Y -= waterTextureSize.Y; }
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelRenderer.cs#L241
    public static bool LevelRenderer_DrawBackground_Replace(
                LevelRenderer __instance,
                SpriteBatch spriteBatch, Camera cam,
                LevelObjectManager backgroundSpriteManager = null,
                BackgroundCreatureManager backgroundCreatureManager = null,
                ParticleManager particleManager = null)
    {
      LevelRenderer _ = __instance;

      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearWrap);

      Vector2 backgroundPos = cam.WorldViewCenter;

      backgroundPos.Y = -backgroundPos.Y;
      backgroundPos *= 0.05f;

      if (_.level.GenerationParams.BackgroundTopSprite != null)
      {
        int backgroundSize = (int)_.level.GenerationParams.BackgroundTopSprite.size.Y;
        if (backgroundPos.Y < backgroundSize)
        {
          if (backgroundPos.Y < 0)
          {
            var backgroundTop = _.level.GenerationParams.BackgroundTopSprite;
            backgroundTop.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, backgroundSize, (int)Math.Min(-backgroundPos.Y, backgroundSize));
            backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                color: _.level.BackgroundTextureColor);
          }
          if (-backgroundPos.Y < GameMain.GraphicsHeight && _.level.GenerationParams.BackgroundSprite != null)
          {
            var background = _.level.GenerationParams.BackgroundSprite;
            background.SourceRect = new Rectangle((int)backgroundPos.X, (int)Math.Max(backgroundPos.Y, 0), backgroundSize, backgroundSize);
            background.DrawTiled(spriteBatch,
                (backgroundPos.Y < 0) ? new Vector2(0.0f, (int)-backgroundPos.Y) : Vector2.Zero,
                new Vector2(GameMain.GraphicsWidth, (int)Math.Min(Math.Ceiling(backgroundSize - backgroundPos.Y), backgroundSize)),
                color: _.level.BackgroundTextureColor);
          }
        }
      }

      spriteBatch.End();

      spriteBatch.Begin(SpriteSortMode.Deferred,
          BlendState.NonPremultiplied,
          SamplerState.LinearWrap, DepthStencilState.DepthRead, null, null,
          cam.Transform);

      backgroundSpriteManager?.DrawObjectsBack(spriteBatch, cam);
      if (cam.Zoom > 0.05f)
      {
        backgroundCreatureManager?.Draw(spriteBatch, cam);
      }

      if (_.level.GenerationParams.WaterParticles != null && cam.Zoom > 0.05f)
      {
        float textureScale = _.level.GenerationParams.WaterParticleScale;

        Rectangle srcRect = new Rectangle(0, 0, 2048, 2048);
        Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
        Vector2 offset = -origin + _.waterParticleOffset;
        while (offset.X <= -srcRect.Width * textureScale) offset.X += srcRect.Width * textureScale;
        while (offset.X > 0.0f) offset.X -= srcRect.Width * textureScale;
        while (offset.Y <= -srcRect.Height * textureScale) offset.Y += srcRect.Height * textureScale;
        while (offset.Y > 0.0f) offset.Y -= srcRect.Height * textureScale;
        for (int i = 0; i < 4; i++)
        {
          float scale = (1.0f - i * 0.2f);

          //alpha goes from 1.0 to 0.0 when scale is in the range of 0.1 - 0.05
          float alpha = (cam.Zoom * scale) < 0.1f ? (cam.Zoom * scale - 0.05f) * 20.0f : 1.0f;
          if (alpha <= 0.0f) continue;

          Vector2 offsetS = offset * scale
              + new Vector2(cam.WorldView.Width, cam.WorldView.Height) * (1.0f - scale) * 0.5f
              - new Vector2(256.0f * i);

          float texScale = scale * textureScale;

          while (offsetS.X <= -srcRect.Width * texScale) offsetS.X += srcRect.Width * texScale;
          while (offsetS.X > 0.0f) offsetS.X -= srcRect.Width * texScale;
          while (offsetS.Y <= -srcRect.Height * texScale) offsetS.Y += srcRect.Height * texScale;
          while (offsetS.Y > 0.0f) offsetS.Y -= srcRect.Height * texScale;

          _.level.GenerationParams.WaterParticles.DrawTiled(
              spriteBatch, origin + offsetS,
              new Vector2(cam.WorldView.Width - offsetS.X, cam.WorldView.Height - offsetS.Y),
              color: _.level.GenerationParams.WaterParticleColor * alpha, textureScale: new Vector2(texScale));
        }
      }

      GameMain.ParticleManager?.Draw(spriteBatch, inWater: true, inSub: false, ParticleBlendState.AlphaBlend, background: true);

      spriteBatch.End();

      _.RenderWalls(GameMain.Instance.GraphicsDevice, cam);

      spriteBatch.Begin(SpriteSortMode.Deferred,
          BlendState.NonPremultiplied,
          SamplerState.LinearClamp, DepthStencilState.DepthRead, null, null,
          cam.Transform);
      backgroundSpriteManager?.DrawObjectsMid(spriteBatch, cam);
      spriteBatch.End();

      return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelRenderer.cs#L341
    public static bool LevelRenderer_DrawForeground_Replace(LevelRenderer __instance, SpriteBatch spriteBatch, Camera cam, LevelObjectManager backgroundSpriteManager = null)
    {
      spriteBatch.Begin(SpriteSortMode.Deferred,
          BlendState.NonPremultiplied,
          SamplerState.LinearClamp, DepthStencilState.DepthRead, null, null,
          cam.Transform);
      backgroundSpriteManager?.DrawObjectsFront(spriteBatch, cam);
      spriteBatch.End();

      return false;
    }
  }
}