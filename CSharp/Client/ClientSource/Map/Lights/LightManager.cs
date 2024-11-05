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
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs#L684
    public static bool LightManager_UpdateObstructVision_Replace(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition, LightManager __instance)
    {
      LightManager _ = __instance;

      if ((!_.LosEnabled || _.LosMode == LosMode.None) && _.ObstructVisionAmount <= 0.0f) { return false; }
      if (LightManager.ViewTarget == null) return false;

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
          List<VertexPositionColor> shadowVerts = new List<VertexPositionColor>();
          List<VertexPositionTexture> penumbraVerts = new List<VertexPositionTexture>();
          foreach (ConvexHull convexHull in convexHulls)
          {
            if (!convexHull.Enabled || !convexHull.Intersects(camView)) { continue; }

            Vector2 relativeViewPos = pos;
            if (convexHull.ParentEntity?.Submarine != null)
            {
              relativeViewPos -= convexHull.ParentEntity.Submarine.DrawPosition;
            }

            convexHull.CalculateLosVertices(relativeViewPos);

            for (int i = 0; i < convexHull.ShadowVertexCount; i++)
            {
              shadowVerts.Add(convexHull.ShadowVertices[i]);
            }

            for (int i = 0; i < convexHull.PenumbraVertexCount; i++)
            {
              penumbraVerts.Add(convexHull.PenumbraVertices[i]);
            }
          }

          if (shadowVerts.Count > 0)
          {
            ConvexHull.shadowEffect.World = shadowTransform;
            ConvexHull.shadowEffect.CurrentTechnique.Passes[0].Apply();
            graphics.DrawUserPrimitives(PrimitiveType.TriangleList, shadowVerts.ToArray(), 0, shadowVerts.Count / 3, VertexPositionColor.VertexDeclaration);

            if (penumbraVerts.Count > 0)
            {
              ConvexHull.penumbraEffect.World = shadowTransform;
              ConvexHull.penumbraEffect.CurrentTechnique.Passes[0].Apply();
              graphics.DrawUserPrimitives(PrimitiveType.TriangleList, penumbraVerts.ToArray(), 0, penumbraVerts.Count / 3, VertexPositionTexture.VertexDeclaration);
            }
          }
        }
      }
      graphics.SetRenderTarget(null);

      return false;
    }
  }
}