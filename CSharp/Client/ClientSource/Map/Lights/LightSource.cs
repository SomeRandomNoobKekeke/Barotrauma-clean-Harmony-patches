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
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Barotrauma.Lights;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientLightSource()
    {
      harmony.Patch(
        original: typeof(LightSource).GetMethod("DrawLightVolume", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LightSource_DrawLightVolume_Replace"))
      );

    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightSource.cs#L1460
    public static bool LightSource_DrawLightVolume_Replace(SpriteBatch spriteBatch, BasicEffect lightEffect, Matrix transform, bool allowRecalculation, ref int recalculationCount, LightSource __instance)
    {
      LightSource _ = __instance;

      if (_.Range < 1.0f || _.Color.A < 1 || _.CurrentBrightness <= 0.0f) { return false; }

      //if the light doesn't cast shadows, we can simply render the texture without having to calculate the light volume
      if (!_.CastShadows)
      {
        Texture2D currentTexture = _.texture ?? LightSource.LightTexture;
        if (_.OverrideLightTexture != null) { currentTexture = _.OverrideLightTexture.Texture; }

        Vector2 center = _.OverrideLightTexture == null ?
            new Vector2(currentTexture.Width / 2, currentTexture.Height / 2) :
            _.OverrideLightTexture.Origin;
        float scale = _.Range / (currentTexture.Width / 2.0f);

        Vector2 drawPos = _.position;
        if (_.ParentSub != null) { drawPos += _.ParentSub.DrawPosition; }
        drawPos.Y = -drawPos.Y;

        spriteBatch.Draw(currentTexture, drawPos, null, _.Color.Multiply(_.CurrentBrightness), -_.rotation + MathHelper.ToRadians(_.LightSourceParams.Rotation), center, scale, SpriteEffects.None, 1);
        return false;
      }

      _.CheckConvexHullsInRange();

      if (_.NeedsRecalculation && allowRecalculation)
      {
        if (_.state == LightSource.LightVertexState.UpToDate)
        {
          recalculationCount++;
          _.FindRaycastHits();
        }
        else if (_.state == LightSource.LightVertexState.PendingVertexRecalculation)
        {
          if (_.verts == null)
          {
#if DEBUG
            DebugConsole.ThrowError($"Failed to generate vertices for a light source. Range: {_.Range}, color: {_.Color}, brightness: {_.CurrentBrightness}, parent: {ParentBody?.UserData ?? "Unknown"}");
#endif
            _.Enabled = false;
            return false;
          }

          foreach (var visibleConvexHull in _.visibleConvexHulls)
          {
            foreach (var convexHullList in _.convexHullsInRange)
            {
              convexHullList.IsHidden.Remove(visibleConvexHull);
              convexHullList.HasBeenVisible.Add(visibleConvexHull);
            }
          }

          _.CalculateLightVertices(_.verts);

          _.LastRecalculationTime = (float)Timing.TotalTime;
          _.NeedsRecalculation = _.needsRecalculationWhenUpToDate;
          _.needsRecalculationWhenUpToDate = false;

          _.state = LightSource.LightVertexState.UpToDate;
        }
      }

      if (_.vertexCount == 0) { return false; }

      Vector2 offset = _.ParentSub == null ? Vector2.Zero : _.ParentSub.DrawPosition;
      lightEffect.World =
          Matrix.CreateTranslation(-new Vector3(_.position, 0.0f)) *
          Matrix.CreateRotationZ(MathHelper.ToRadians(_.LightSourceParams.Rotation)) *
          Matrix.CreateTranslation(new Vector3(_.position + offset + _.translateVertices, 0.0f)) *
          transform;


      lightEffect.DiffuseColor = (new Vector3(_.Color.R, _.Color.G, _.Color.B) * (_.Color.A / 255.0f * _.CurrentBrightness)) / 255.0f;
      if (_.OverrideLightTexture != null)
      {
        lightEffect.Texture = _.OverrideLightTexture.Texture;
      }
      else
      {
        lightEffect.Texture = _.texture ?? LightSource.LightTexture;
      }
      lightEffect.CurrentTechnique.Passes[0].Apply();

      GameMain.Instance.GraphicsDevice.SetVertexBuffer(_.lightVolumeBuffer);
      GameMain.Instance.GraphicsDevice.Indices = _.lightVolumeIndexBuffer;

      GameMain.Instance.GraphicsDevice.DrawIndexedPrimitives
      (
          PrimitiveType.TriangleList, 0, 0, _.indexCount / 3
      );

      return false;
    }
  }
}