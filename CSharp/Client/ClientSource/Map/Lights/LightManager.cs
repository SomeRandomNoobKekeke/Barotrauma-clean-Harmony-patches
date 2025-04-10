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
using System.Collections.Generic;
using System.Linq;
using System;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Threading;
using Barotrauma.Lights;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientLightManager()
    {
      harmony.Patch(
        original: typeof(LightManager).GetMethod("UpdateObstructVision", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LightManager_UpdateObstructVision_Replace"))
      );

      harmony.Patch(
        original: typeof(LightManager).GetMethod("RenderLightMap", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LightManager_RenderLightMap_Replace"))
      );

      harmony.Patch(
        original: typeof(LightManager).GetMethod("UpdateHighlights", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LightManager_UpdateHighlights_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs#L684
    public static bool LightManager_UpdateObstructVision_Replace(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition, LightManager __instance)
    {
      LightManager _ = __instance;

      if ((!_.LosEnabled || _.LosMode == LosMode.None) && _.ObstructVisionAmount <= 0.0f) { return false; }
      if (LightManager.ViewTarget == null) { return false; }

      graphics.SetRenderTarget(_.LosTexture);

      if (_.ObstructVisionAmount > 0.0f)
      {
        graphics.Clear(Color.Black);
        Vector2 diff = lookAtPosition - LightManager.ViewTarget.WorldPosition;
        diff.Y = -diff.Y;
        if (diff.LengthSquared() > 20.0f * 20.0f) { _.losOffset = diff; }
        float rotation = MathUtils.VectorToAngle(_.losOffset);

        //the visible area stretches to the maximum when the cursor is this far from the character
        const float MaxOffset = 256.0f;
        //the magic numbers here are just based on experimentation
        float MinHorizontalScale = MathHelper.Lerp(3.5f, 1.5f, _.ObstructVisionAmount);
        float MaxHorizontalScale = MinHorizontalScale * 1.25f;
        float VerticalScale = MathHelper.Lerp(4.0f, 1.25f, _.ObstructVisionAmount);

        //Starting point and scale-based modifier that moves the point of origin closer to the edge of the texture if the player moves their mouse further away, or vice versa.
        float relativeOriginStartPosition = 0.1f; //Increasing this value moves the origin further behind the character
        float originStartPosition = _.visionCircle.Width * relativeOriginStartPosition * MinHorizontalScale;
        float relativeOriginLookAtPosModifier = -0.055f; //Increase this value increases how much the vision changes by moving the mouse
        float originLookAtPosModifier = _.visionCircle.Width * relativeOriginLookAtPosModifier;

        Vector2 scale = new Vector2(
            MathHelper.Clamp(_.losOffset.Length() / MaxOffset, MinHorizontalScale, MaxHorizontalScale), VerticalScale);

        spriteBatch.Begin(SpriteSortMode.Deferred, transformMatrix: cam.Transform * Matrix.CreateScale(new Vector3(GameSettings.CurrentConfig.Graphics.LightMapScale, GameSettings.CurrentConfig.Graphics.LightMapScale, 1.0f)));
        spriteBatch.Draw(_.visionCircle, new Vector2(LightManager.ViewTarget.WorldPosition.X, -LightManager.ViewTarget.WorldPosition.Y), null, Color.White, rotation,
            new Vector2(originStartPosition + (scale.X * originLookAtPosModifier), _.visionCircle.Height / 2), scale, SpriteEffects.None, 0.0f);
        spriteBatch.End();
      }
      else
      {
        graphics.Clear(Color.White);
      }


      //--------------------------------------

      if (_.LosEnabled && _.LosMode != LosMode.None && LightManager.ViewTarget != null)
      {
        Vector2 pos = LightManager.ViewTarget.DrawPosition;
        bool centeredOnHead = false;
        if (LightManager.ViewTarget is Character character &&
            character.AnimController?.GetLimb(LimbType.Head) is Limb head &&
            !head.IsSevered && !head.Removed)
        {
          pos = head.body.DrawPosition;
          centeredOnHead = true;
        }

        Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);
        Matrix shadowTransform = cam.ShaderTransform
            * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

        var convexHulls = ConvexHull.GetHullsInRange(LightManager.ViewTarget.Position, cam.WorldView.Width * 0.75f, LightManager.ViewTarget.Submarine);

        //make sure the head isn't peeking through any LOS segments, and if it is,
        //center the LOS on the character's collider instead
        if (centeredOnHead)
        {
          foreach (var ch in convexHulls)
          {
            if (!ch.Enabled) { continue; }
            Vector2 currentViewPos = pos;
            Vector2 defaultViewPos = LightManager.ViewTarget.DrawPosition;
            if (ch.ParentEntity?.Submarine != null)
            {
              defaultViewPos -= ch.ParentEntity.Submarine.DrawPosition;
              currentViewPos -= ch.ParentEntity.Submarine.DrawPosition;
            }
            //check if a line from the character's collider to the head intersects with the los segment (= head poking through it)
            if (ch.LosIntersects(defaultViewPos, currentViewPos))
            {
              pos = LightManager.ViewTarget.DrawPosition;
            }
          }
        }

        if (convexHulls != null)
        {
          LightManager.ShadowVertices.Clear();
          LightManager.PenumbraVertices.Clear();
          foreach (ConvexHull convexHull in convexHulls)
          {
            if (!convexHull.Intersects(camView)) { continue; }

            Vector2 relativeViewPos = pos;
            if (convexHull.ParentEntity?.Submarine != null)
            {
              relativeViewPos -= convexHull.ParentEntity.Submarine.DrawPosition;
            }

            convexHull.CalculateLosVertices(relativeViewPos);

            for (int i = 0; i < convexHull.ShadowVertexCount; i++)
            {
              LightManager.ShadowVertices.Add(convexHull.ShadowVertices[i]);
            }

            for (int i = 0; i < convexHull.PenumbraVertexCount; i++)
            {
              LightManager.PenumbraVertices.Add(convexHull.PenumbraVertices[i]);
            }
          }

          if (LightManager.ShadowVertices.Count > 0)
          {
            ConvexHull.shadowEffect.World = shadowTransform;
            ConvexHull.shadowEffect.CurrentTechnique.Passes[0].Apply();
            graphics.DrawUserPrimitives(PrimitiveType.TriangleList, LightManager.ShadowVertices.ToArray(), 0, LightManager.ShadowVertices.Count / 3, VertexPositionColor.VertexDeclaration);

            if (LightManager.PenumbraVertices.Count > 0)
            {
              ConvexHull.penumbraEffect.World = shadowTransform;
              ConvexHull.penumbraEffect.CurrentTechnique.Passes[0].Apply();
              graphics.DrawUserPrimitives(PrimitiveType.TriangleList, LightManager.PenumbraVertices.ToArray(), 0, LightManager.PenumbraVertices.Count / 3, VertexPositionTexture.VertexDeclaration);
            }
          }
        }
      }
      graphics.SetRenderTarget(null);


      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs#L246
    public static bool LightManager_RenderLightMap_Replace(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, RenderTarget2D backgroundObstructor, LightManager __instance)
    {
      LightManager _ = __instance;

      if (!_.LightingEnabled) { return false; }

      if (Math.Abs(_.currLightMapScale - GameSettings.CurrentConfig.Graphics.LightMapScale) > 0.01f)
      {
        //lightmap scale has changed -> recreate render targets
        _.CreateRenderTargets(graphics);
      }

      Matrix spriteBatchTransform = cam.Transform * Matrix.CreateScale(new Vector3(GameSettings.CurrentConfig.Graphics.LightMapScale, GameSettings.CurrentConfig.Graphics.LightMapScale, 1.0f));
      Matrix transform = cam.ShaderTransform
          * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

      bool highlightsVisible = _.UpdateHighlights(graphics, spriteBatch, spriteBatchTransform, cam);

      Rectangle viewRect = cam.WorldView;
      viewRect.Y -= cam.WorldView.Height;
      //check which lights need to be drawn

      // NOTE: in original it's a private LightManager field
      // but i can't pass it by ref in light.DrawLightVolume
      // it's used only here, so i think it's safe to just make it a local var
      int recalculationCount = 0;
      _.activeLights.Clear();
      foreach (LightSource light in _.lights)
      {
        if (!light.Enabled) { continue; }
        if ((light.Color.A < 1 || light.Range < 1.0f) && !light.LightSourceParams.OverrideLightSpriteAlpha.HasValue) { continue; }

        if (light.ParentBody != null)
        {
          light.ParentBody.UpdateDrawPosition();

          Vector2 pos = light.ParentBody.DrawPosition + light.OffsetFromBody;
          if (light.ParentSub != null) { pos -= light.ParentSub.DrawPosition; }
          light.Position = pos;
        }

        //above the top boundary of the level (in an inactive respawn shuttle?)
        if (Level.IsPositionAboveLevel(light.WorldPosition)) { continue; }

        float range = light.LightSourceParams.TextureRange;
        if (light.LightSprite != null)
        {
          float spriteRange = Math.Max(
              light.LightSprite.size.X * light.SpriteScale.X * (0.5f + Math.Abs(light.LightSprite.RelativeOrigin.X - 0.5f)),
              light.LightSprite.size.Y * light.SpriteScale.Y * (0.5f + Math.Abs(light.LightSprite.RelativeOrigin.Y - 0.5f)));

          float targetSize = Math.Max(light.LightTextureTargetSize.X, light.LightTextureTargetSize.Y);
          range = Math.Max(Math.Max(spriteRange, targetSize), range);
        }
        if (!MathUtils.CircleIntersectsRectangle(light.WorldPosition, range, viewRect)) { continue; }

        light.Priority = lightPriority(range, light);

        int i = 0;
        while (i < _.activeLights.Count && light.Priority < _.activeLights[i].Priority)
        {
          i++;
        }
        _.activeLights.Insert(i, light);
      }
      LightManager.ActiveLightCount = _.activeLights.Count;

      float lightPriority(float range, LightSource light)
      {
        return
            range *
            ((Character.Controlled?.Submarine != null && light.ParentSub == Character.Controlled?.Submarine) ? 2.0f : 1.0f) *
            (light.CastShadows ? 10.0f : 1.0f) *
            (light.LightSourceParams.OverrideLightSpriteAlpha ?? (light.Color.A / 255.0f)) *
            light.PriorityMultiplier;
      }

      //find the lights with an active light volume
      _.activeShadowCastingLights.Clear();
      foreach (var activeLight in _.activeLights)
      {
        if (!activeLight.CastShadows) { continue; }
        if (activeLight.Range < 1.0f || activeLight.Color.A < 1 || activeLight.CurrentBrightness <= 0.0f) { continue; }
        _.activeShadowCastingLights.Add(activeLight);
      }

      //remove some lights with a light volume if there's too many of them
      if (_.activeShadowCastingLights.Count > GameSettings.CurrentConfig.Graphics.VisibleLightLimit && Screen.Selected is { IsEditor: false })
      {
        for (int i = GameSettings.CurrentConfig.Graphics.VisibleLightLimit; i < _.activeShadowCastingLights.Count; i++)
        {
          _.activeLights.Remove(_.activeShadowCastingLights[i]);
        }
      }
      _.activeLights.Sort((l1, l2) => l1.LastRecalculationTime.CompareTo(l2.LastRecalculationTime));

      //draw light sprites attached to characters
      //render into a separate rendertarget using alpha blending (instead of on top of everything else with alpha blending)
      //to prevent the lights from showing through other characters or other light sprites attached to the same character
      //---------------------------------------------------------------------------------------------------
      graphics.SetRenderTarget(_.LimbLightMap);
      graphics.Clear(Color.Black);
      graphics.BlendState = BlendState.NonPremultiplied;
      spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform);
      foreach (LightSource light in _.activeLights)
      {
        if (light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
        //draw limb lights at this point, because they were skipped over previously to prevent them from being obstructed
        if (light.ParentBody?.UserData is Limb limb && !limb.Hide) { light.DrawSprite(spriteBatch, cam); }
      }
      spriteBatch.End();

      //draw background lights
      //---------------------------------------------------------------------------------------------------
      graphics.SetRenderTarget(_.LightMap);
      graphics.Clear(_.AmbientLight);
      graphics.BlendState = BlendState.Additive;
      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
      Level.Loaded?.BackgroundCreatureManager?.DrawLights(spriteBatch, cam);
      foreach (LightSource light in _.activeLights)
      {
        if (!light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
        light.DrawLightVolume(spriteBatch, _.lightEffect, transform, recalculationCount < LightManager.MaxLightVolumeRecalculationsPerFrame, ref recalculationCount);
        light.DrawSprite(spriteBatch, cam);
      }
      GameMain.ParticleManager.Draw(spriteBatch, true, null, Barotrauma.Particles.ParticleBlendState.Additive);
      spriteBatch.End();

      //draw a black rectangle on hulls to hide background lights behind subs
      //---------------------------------------------------------------------------------------------------

      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, transformMatrix: spriteBatchTransform);
      Dictionary<Hull, Rectangle> visibleHulls = _.GetVisibleHulls(cam);
      foreach (KeyValuePair<Hull, Rectangle> hull in visibleHulls)
      {
        GUI.DrawRectangle(spriteBatch,
            new Vector2(hull.Value.X, -hull.Value.Y),
            new Vector2(hull.Value.Width, hull.Value.Height),
            hull.Key.AmbientLight == Color.TransparentBlack ? Color.Black : hull.Key.AmbientLight.Multiply(hull.Key.AmbientLight.A / 255.0f), true);
      }
      spriteBatch.End();

      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
      Vector3 glowColorHSV = ToolBox.RGBToHSV(_.AmbientLight);
      glowColorHSV.Z = Math.Max(glowColorHSV.Z, 0.4f);
      Color glowColor = ToolBoxCore.HSVToRGB(glowColorHSV.X, glowColorHSV.Y, glowColorHSV.Z);
      Vector2 glowSpriteSize = new Vector2(_.gapGlowTexture.Width, _.gapGlowTexture.Height);
      foreach (var gap in Gap.GapList)
      {
        if (gap.IsRoomToRoom || gap.Open <= 0.0f || gap.ConnectedWall == null) { continue; }

        float a = MathHelper.Lerp(0.5f, 1.0f,
            PerlinNoise.GetPerlin((float)Timing.TotalTime * 0.05f, gap.GlowEffectT));

        float scale = MathHelper.Lerp(0.5f, 2.0f,
            PerlinNoise.GetPerlin((float)Timing.TotalTime * 0.01f, gap.GlowEffectT));

        float rot = PerlinNoise.GetPerlin((float)Timing.TotalTime * 0.001f, gap.GlowEffectT) * MathHelper.TwoPi;

        Vector2 spriteScale = new Vector2(gap.Rect.Width, gap.Rect.Height) / glowSpriteSize;
        Vector2 drawPos = new Vector2(gap.DrawPosition.X, -gap.DrawPosition.Y);

        spriteBatch.Draw(_.gapGlowTexture,
            drawPos,
            null,
            glowColor * a,
            rot,
            glowSpriteSize / 2,
            scale: Math.Max(spriteScale.X, spriteScale.Y) * scale,
            SpriteEffects.None,
            layerDepth: 0);
      }
      spriteBatch.End();

      if (backgroundObstructor != null)
      {
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap, transformMatrix: Matrix.Identity, effect: GameMain.GameScreen.DamageEffect);
        spriteBatch.Draw(backgroundObstructor, new Rectangle(0, 0,
            (int)(GameMain.GraphicsWidth * _.currLightMapScale), (int)(GameMain.GraphicsHeight * _.currLightMapScale)), Color.Black);
        spriteBatch.End();
      }
      else
      {
        GameMain.GameScreen.DamageEffect.CurrentTechnique = GameMain.GameScreen.DamageEffect.Techniques["StencilShaderSolidColor"];
        GameMain.GameScreen.DamageEffect.Parameters["solidColor"].SetValue(Color.Black.ToVector4());
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap, transformMatrix: spriteBatchTransform, effect: GameMain.GameScreen.DamageEffect);
        Submarine.DrawDamageable(spriteBatch, GameMain.GameScreen.DamageEffect);
        spriteBatch.End();
      }

      graphics.BlendState = BlendState.Additive;

      //draw the focused item and character to highlight them,
      //and light sprites (done before drawing the actual light volumes so we can make characters obstruct the highlights and sprites)
      //---------------------------------------------------------------------------------------------------
      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
      foreach (LightSource light in _.activeLights)
      {
        //don't draw limb lights at this point, they need to be drawn after lights have been obstructed by characters
        if (light.IsBackground || light.ParentBody?.UserData is Limb || light.CurrentBrightness <= 0.0f) { continue; }
        light.DrawSprite(spriteBatch, cam);
      }
      spriteBatch.End();

      if (highlightsVisible)
      {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
        spriteBatch.Draw(_.HighlightMap, Vector2.Zero, Color.White);
        spriteBatch.End();
      }

      //draw characters to obstruct the highlighted items/characters and light sprites
      //---------------------------------------------------------------------------------------------------
      if (cam.Zoom > LightManager.ObstructLightsBehindCharactersZoomThreshold)
      {
        _.SolidColorEffect.CurrentTechnique = _.SolidColorEffect.Techniques["SolidVertexColor"];
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, effect: _.SolidColorEffect, transformMatrix: spriteBatchTransform);
        DrawCharacters(spriteBatch, cam, drawDeformSprites: false);
        spriteBatch.End();

        DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShaderSolidVertexColor"];
        DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform);
        DrawCharacters(spriteBatch, cam, drawDeformSprites: true);
        spriteBatch.End();
      }

      static void DrawCharacters(SpriteBatch spriteBatch, Camera cam, bool drawDeformSprites)
      {
        foreach (Character character in Character.CharacterList)
        {
          if (character.CurrentHull == null || !character.Enabled || !character.IsVisible || character.InvisibleTimer > 0.0f) { continue; }
          if (Character.Controlled?.FocusedCharacter == character) { continue; }
          Color lightColor = character.CurrentHull.AmbientLight == Color.TransparentBlack ?
              Color.Black :
              character.CurrentHull.AmbientLight.Multiply(character.CurrentHull.AmbientLight.A / 255.0f).Opaque();
          foreach (Limb limb in character.AnimController.Limbs)
          {
            if (drawDeformSprites == (limb.DeformSprite == null)) { continue; }
            limb.Draw(spriteBatch, cam, lightColor);
          }
          foreach (var heldItem in character.HeldItems)
          {
            heldItem.Draw(spriteBatch, editing: false, overrideColor: Color.Black);
          }
        }
      }

      DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShader"];
      graphics.BlendState = BlendState.Additive;

      //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
      //---------------------------------------------------------------------------------------------------
      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);

      spriteBatch.Draw(_.LimbLightMap, new Rectangle(cam.WorldView.X, -cam.WorldView.Y, cam.WorldView.Width, cam.WorldView.Height), Color.White);

      foreach (ElectricalDischarger discharger in ElectricalDischarger.List)
      {
        discharger.DrawElectricity(spriteBatch);
      }

      foreach (LightSource light in _.activeLights)
      {
        if (light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
        light.DrawLightVolume(spriteBatch, _.lightEffect, transform, recalculationCount < LightManager.MaxLightVolumeRecalculationsPerFrame, ref recalculationCount);
      }

      if (ConnectionPanel.ShouldDebugDrawWiring)
      {
        foreach (MapEntity e in (Submarine.VisibleEntities ?? MapEntity.MapEntityList))
        {
          if (e is Item item && !item.IsHidden && item.GetComponent<Wire>() is Wire wire)
          {
            wire.DebugDraw(spriteBatch, alpha: 0.4f);
          }
        }
      }

      _.lightEffect.World = transform;

      GameMain.ParticleManager.Draw(spriteBatch, false, null, Barotrauma.Particles.ParticleBlendState.Additive);

      if (Character.Controlled != null)
      {
        DrawHalo(Character.Controlled);
      }
      else
      {
        foreach (Character character in Character.CharacterList)
        {
          if (character.Submarine == null || character.IsDead || !character.IsHuman) { continue; }
          DrawHalo(character);
        }
      }

      void DrawHalo(Character character)
      {
        if (character == null || character.Removed) { return; }
        Vector2 haloDrawPos = character.DrawPosition;
        haloDrawPos.Y = -haloDrawPos.Y;

        //ambient light decreases the brightness of the halo (no need for a bright halo if the ambient light is bright enough)
        float ambientBrightness = (_.AmbientLight.R + _.AmbientLight.B + _.AmbientLight.G) / 255.0f / 3.0f;
        Color haloColor = Color.White.Multiply(0.3f - ambientBrightness);
        if (haloColor.A > 0)
        {
          float scale = 512.0f / LightSource.LightTexture.Width;
          spriteBatch.Draw(
              LightSource.LightTexture, haloDrawPos, null, haloColor, 0.0f,
              new Vector2(LightSource.LightTexture.Width, LightSource.LightTexture.Height) / 2, scale, SpriteEffects.None, 0.0f);
        }
      }

      spriteBatch.End();

      //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
      //---------------------------------------------------------------------------------------------------

      graphics.SetRenderTarget(null);
      graphics.BlendState = BlendState.NonPremultiplied;

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs#L555
    public static bool LightManager_UpdateHighlights_Replace(GraphicsDevice graphics, SpriteBatch spriteBatch, Matrix spriteBatchTransform, Camera cam, LightManager __instance, ref bool __result)
    {
      LightManager _ = __instance;

      if (GUI.DisableItemHighlights) { __result = false; return false; }

      _.highlightedEntities.Clear();
      if (Character.Controlled != null && (!Character.Controlled.IsKeyDown(InputType.Aim) || Character.Controlled.HeldItems.Any(it => it.GetComponent<Sprayer>() == null)))
      {
        if (Character.Controlled.FocusedItem != null)
        {
          _.highlightedEntities.Add(Character.Controlled.FocusedItem);
        }
        if (Character.Controlled.FocusedCharacter != null)
        {
          _.highlightedEntities.Add(Character.Controlled.FocusedCharacter);
        }
        foreach (MapEntity me in MapEntity.HighlightedEntities)
        {
          if (me is Item item && item != Character.Controlled.FocusedItem)
          {
            _.highlightedEntities.Add(item);
          }
        }
      }
      if (_.highlightedEntities.Count == 0) { __result = false; return false; }

      //draw characters in light blue first
      graphics.SetRenderTarget(_.HighlightMap);
      _.SolidColorEffect.CurrentTechnique = _.SolidColorEffect.Techniques["SolidColor"];
      _.SolidColorEffect.Parameters["color"].SetValue(Color.LightBlue.ToVector4());
      _.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
      DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShaderSolidColor"];
      DeformableSprite.Effect.Parameters["solidColor"].SetValue(Color.LightBlue.ToVector4());
      DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
      spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, samplerState: SamplerState.LinearWrap, effect: _.SolidColorEffect, transformMatrix: spriteBatchTransform);
      foreach (Entity highlighted in _.highlightedEntities)
      {
        if (highlighted is Item item)
        {
          if (item.IconStyle != null && (item != Character.Controlled.FocusedItem || Character.Controlled.FocusedItem == null))
          {
            //wait until next pass
          }
          else
          {
            item.Draw(spriteBatch, false, true);
          }
        }
        else if (highlighted is Character character)
        {
          character.Draw(spriteBatch, cam);
        }
      }
      spriteBatch.End();

      //draw items with iconstyles in the style's color
      spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, samplerState: SamplerState.LinearWrap, effect: _.SolidColorEffect, transformMatrix: spriteBatchTransform);
      foreach (Entity highlighted in _.highlightedEntities)
      {
        if (highlighted is Item item)
        {
          if (item.IconStyle != null && (item != Character.Controlled.FocusedItem || Character.Controlled.FocusedItem == null))
          {
            _.SolidColorEffect.Parameters["color"].SetValue(item.IconStyle.Color.ToVector4());
            _.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
            item.Draw(spriteBatch, false, true);
          }
        }
      }
      spriteBatch.End();

      //draw characters in black with a bit of blur, leaving the white edges visible
      float phase = (float)(Math.Sin(Timing.TotalTime * 3.0f) + 1.0f) / 2.0f; //phase oscillates between 0 and 1
      Vector4 overlayColor = Color.Black.ToVector4() * MathHelper.Lerp(0.5f, 0.9f, phase);
      _.SolidColorEffect.Parameters["color"].SetValue(overlayColor);
      _.SolidColorEffect.CurrentTechnique = _.SolidColorEffect.Techniques["SolidColorBlur"];
      _.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
      DeformableSprite.Effect.Parameters["solidColor"].SetValue(overlayColor);
      DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
      spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, samplerState: SamplerState.LinearWrap, effect: _.SolidColorEffect, transformMatrix: spriteBatchTransform);
      foreach (Entity highlighted in _.highlightedEntities)
      {
        if (highlighted is Item item)
        {
          _.SolidColorEffect.Parameters["blurDistance"].SetValue(0.02f);
          item.Draw(spriteBatch, false, true);
        }
        else if (highlighted is Character character)
        {
          _.SolidColorEffect.Parameters["blurDistance"].SetValue(0.05f);
          character.Draw(spriteBatch, cam);
        }
      }
      spriteBatch.End();

      //raster pattern on top of everything
      spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearWrap);
      spriteBatch.Draw(_.highlightRaster,
          new Rectangle(0, 0, _.HighlightMap.Width, _.HighlightMap.Height),
          new Rectangle(0, 0, (int)(_.HighlightMap.Width / _.currLightMapScale * 0.5f), (int)(_.HighlightMap.Height / _.currLightMapScale * 0.5f)),
          Color.White * 0.5f);
      spriteBatch.End();

      DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShader"];

      __result = true; return false;
    }
  }
}