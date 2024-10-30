using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;


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
  }
}