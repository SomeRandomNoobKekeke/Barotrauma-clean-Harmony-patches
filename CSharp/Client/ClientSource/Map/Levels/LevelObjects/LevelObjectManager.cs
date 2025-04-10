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
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientLevelObjectManager()
    {
      harmony.Patch(
        original: typeof(LevelObjectManager).GetMethod("RefreshVisibleObjects", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelObjectManager_RefreshVisibleObjects_Replace"))
      );

      harmony.Patch(
        original: typeof(LevelObjectManager).GetMethod("DrawObjects", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelObjectManager_DrawObjects_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelObjects/LevelObjectManager.cs#L58
    public static bool LevelObjectManager_RefreshVisibleObjects_Replace(Rectangle currentIndices, float zoom, LevelObjectManager __instance)
    {
      LevelObjectManager _ = __instance;

      _.visibleObjectsBack.Clear();
      _.visibleObjectsMid.Clear();
      _.visibleObjectsFront.Clear();

      float minSizeToDraw = MathHelper.Lerp(10.0f, 5.0f, Math.Min(zoom * 20.0f, 1.0f));

      //start from the grid cell at the center of the view
      //(if objects needs to be culled, better to cull at the edges of the view)
      int midIndexX = (currentIndices.X + currentIndices.Width) / 2;
      int midIndexY = (currentIndices.Y + currentIndices.Height) / 2;
      CheckIndex(midIndexX, midIndexY);

      for (int x = currentIndices.X; x <= currentIndices.Width; x++)
      {
        for (int y = currentIndices.Y; y <= currentIndices.Height; y++)
        {
          if (x != midIndexX || y != midIndexY) { CheckIndex(x, y); }
        }
      }

      void CheckIndex(int x, int y)
      {
        if (_.objectGrid[x, y] == null) { return; }
        foreach (LevelObject obj in _.objectGrid[x, y])
        {
          if (!obj.CanBeVisible) { continue; }
          if (obj.Prefab.HideWhenBroken && obj.Health <= 0.0f) { continue; }

          if (zoom < 0.05f)
          {
            //hide if the sprite is very small when zoomed this far out
            if ((obj.Sprite != null && Math.Min(obj.Sprite.size.X * zoom, obj.Sprite.size.Y * zoom) < 5.0f) ||
                (obj.ActivePrefab?.DeformableSprite != null && Math.Min(obj.ActivePrefab.DeformableSprite.Sprite.size.X * zoom, obj.ActivePrefab.DeformableSprite.Sprite.size.Y * zoom) < minSizeToDraw))
            {
              continue;
            }

            float zCutoff = MathHelper.Lerp(5000.0f, 500.0f, (0.05f - zoom) * 20.0f);
            if (obj.Position.Z > zCutoff)
            {
              continue;
            }
          }

          var objectList =
              obj.Position.Z >= 0 ?
                  _.visibleObjectsBack :
                  (obj.Position.Z < -1 ? _.visibleObjectsFront : _.visibleObjectsMid);
          if (objectList.Count >= LevelObjectManager.MaxVisibleObjects) { continue; }

          int drawOrderIndex = 0;
          for (int i = 0; i < objectList.Count; i++)
          {
            if (objectList[i] == obj)
            {
              drawOrderIndex = -1;
              break;
            }

            if (objectList[i].Position.Z > obj.Position.Z)
            {
              break;
            }
            else
            {
              drawOrderIndex = i + 1;
              if (drawOrderIndex >= LevelObjectManager.MaxVisibleObjects) { break; }
            }
          }

          if (drawOrderIndex >= 0 && drawOrderIndex < LevelObjectManager.MaxVisibleObjects)
          {
            objectList.Insert(drawOrderIndex, obj);
          }
        }
      }

      //object grid is sorted in an ascending order
      //(so we prefer the objects in the foreground instead of ones in the background if some need to be culled)
      //rendering needs to be done in a descending order though to get the background objects to be drawn first -> reverse the lists
      _.visibleObjectsBack.Reverse();
      _.visibleObjectsMid.Reverse();
      _.visibleObjectsFront.Reverse();

      _.currentGridIndices = currentIndices;

      return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelObjects/LevelObjectManager.cs#L171
    public static bool LevelObjectManager_DrawObjects_Replace(SpriteBatch spriteBatch, Camera cam, List<LevelObject> objectList, LevelObjectManager __instance)
    {
      LevelObjectManager _ = __instance;

      Rectangle indices = Rectangle.Empty;
      indices.X = (int)Math.Floor(cam.WorldView.X / (float)LevelObjectManager.GridSize);
      if (indices.X >= _.objectGrid.GetLength(0)) { return false; }
      indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)LevelObjectManager.GridSize);
      if (indices.Y >= _.objectGrid.GetLength(1)) { return false; }

      indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)LevelObjectManager.GridSize) + 1;
      if (indices.Width < 0) { return false; }
      indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)LevelObjectManager.GridSize) + 1;
      if (indices.Height < 0) { return false; }

      indices.X = Math.Max(indices.X, 0);
      indices.Y = Math.Max(indices.Y, 0);
      indices.Width = Math.Min(indices.Width, _.objectGrid.GetLength(0) - 1);
      indices.Height = Math.Min(indices.Height, _.objectGrid.GetLength(1) - 1);

      float z = 0.0f;
      if (_.ForceRefreshVisibleObjects || (_.currentGridIndices != indices && Timing.TotalTime > _.NextRefreshTime))
      {
        _.RefreshVisibleObjects(indices, cam.Zoom);
        _.ForceRefreshVisibleObjects = false;
        if (cam.Zoom < 0.1f)
        {
          //when zoomed very far out, refresh a little less often
          _.NextRefreshTime = Timing.TotalTime + MathHelper.Lerp(1.0f, 0.0f, cam.Zoom * 10.0f);
        }
      }

      foreach (LevelObject obj in objectList)
      {
        Vector2 camDiff = new Vector2(obj.Position.X, obj.Position.Y) - cam.WorldViewCenter;
        camDiff.Y = -camDiff.Y;

        Sprite activeSprite = obj.Sprite;
        activeSprite?.Draw(
            spriteBatch,
            new Vector2(obj.Position.X, -obj.Position.Y) - camDiff * obj.Position.Z * LevelObjectManager.ParallaxStrength,
            Color.Lerp(obj.Prefab.SpriteColor, obj.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), obj.Position.Z / obj.Prefab.FadeOutDepth),
            activeSprite.Origin,
            obj.CurrentRotation,
            obj.CurrentScale,
            SpriteEffects.None,
            z);

        if (obj.ActivePrefab.DeformableSprite != null)
        {
          if (obj.CurrentSpriteDeformation != null)
          {
            obj.ActivePrefab.DeformableSprite.Deform(obj.CurrentSpriteDeformation);
          }
          else
          {
            obj.ActivePrefab.DeformableSprite.Reset();
          }
          obj.ActivePrefab.DeformableSprite?.Draw(cam,
              new Vector3(new Vector2(obj.Position.X, obj.Position.Y) - camDiff * obj.Position.Z * LevelObjectManager.ParallaxStrength, z * 10.0f),
              obj.ActivePrefab.DeformableSprite.Origin,
              obj.CurrentRotation,
              obj.CurrentScale,
              Color.Lerp(obj.Prefab.SpriteColor, obj.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), obj.Position.Z / 5000.0f));
        }


        if (GameMain.DebugDraw)
        {
          GUI.DrawRectangle(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(10.0f, 10.0f), GUIStyle.Red, true);

          if (obj.Triggers == null) { continue; }
          foreach (LevelTrigger trigger in obj.Triggers)
          {
            if (trigger.PhysicsBody == null) continue;
            GUI.DrawLine(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), Color.Cyan, 0, 3);

            Vector2 flowForce = trigger.GetWaterFlowVelocity();
            if (flowForce.LengthSquared() > 1)
            {
              flowForce.Y = -flowForce.Y;
              GUI.DrawLine(spriteBatch, new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y) + flowForce * 10, GUIStyle.Orange, 0, 5);
            }
            trigger.PhysicsBody.UpdateDrawPosition();
            trigger.PhysicsBody.DebugDraw(spriteBatch, trigger.IsTriggered ? Color.Cyan : Color.DarkCyan);
          }
        }

        z += 0.0001f;
      }

      return false;
    }

  }
}