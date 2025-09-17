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
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientSubmarine()
    {
      harmony.Patch(
        original: typeof(Submarine).GetMethod("CullEntities", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_CullEntities_Replace"))
      );

      harmony.Patch(
        original: typeof(Submarine).GetMethod("DrawFront", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawFront_Replace"))
      );

      harmony.Patch(
        original: typeof(Submarine).GetMethod("DrawBack", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawBack_Replace"))
      );

      harmony.Patch(
        original: typeof(Submarine).GetMethod("DrawDamageable", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawDamageable_Replace"))
      );

      harmony.Patch(
        original: typeof(Submarine).GetMethod("DrawPaintedColors", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawPaintedColors_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Submarine.cs#L35
    public static bool Submarine_CullEntities_Replace(Camera cam)
    {
      Rectangle camView = cam.WorldView;
      camView = new Rectangle(camView.X - Submarine.CullMargin, camView.Y + Submarine.CullMargin, camView.Width + Submarine.CullMargin * 2, camView.Height + Submarine.CullMargin * 2);

      if (Level.Loaded?.Renderer?.CollapseEffectStrength is > 0.0f)
      {
        //force everything to be visible when the collapse effect (which moves everything to a single point) is active
        camView = Rectangle.Union(Submarine.AbsRect(camView.Location.ToVector2(), camView.Size.ToVector2()), new Rectangle(Point.Zero, Level.Loaded.Size));
        camView.Y += camView.Height;
      }

      if (Math.Abs(camView.X - Submarine.prevCullArea.X) < Submarine.CullMoveThreshold &&
          Math.Abs(camView.Y - Submarine.prevCullArea.Y) < Submarine.CullMoveThreshold &&
          Math.Abs(camView.Right - Submarine.prevCullArea.Right) < Submarine.CullMoveThreshold &&
          Math.Abs(camView.Bottom - Submarine.prevCullArea.Bottom) < Submarine.CullMoveThreshold &&
          Submarine.prevCullTime > Timing.TotalTime - Submarine.CullInterval)
      {
        return false;
      }

      Submarine.visibleSubs.Clear();
      foreach (Submarine sub in Submarine.Loaded)
      {
        if (Level.Loaded != null && sub.WorldPosition.Y < Level.MaxEntityDepth) { continue; }

        Rectangle worldBorders = new Rectangle(
            sub.VisibleBorders.X + (int)sub.WorldPosition.X,
            sub.VisibleBorders.Y + (int)sub.WorldPosition.Y,
            sub.VisibleBorders.Width,
            sub.VisibleBorders.Height);

        if (Submarine.RectsOverlap(worldBorders, camView))
        {
          Submarine.visibleSubs.Add(sub);
        }
      }

      if (Submarine.visibleEntities == null)
      {
        Submarine.visibleEntities = new List<MapEntity>(MapEntity.MapEntityList.Count);
      }
      else
      {
        Submarine.visibleEntities.Clear();
      }

      foreach (MapEntity entity in MapEntity.MapEntityList)
      {
        if (entity == null || entity.Removed) { continue; }
        if (entity.Submarine != null)
        {
          if (!Submarine.visibleSubs.Contains(entity.Submarine)) { continue; }
        }
        if (entity.IsVisible(camView)) { Submarine.visibleEntities.Add(entity); }
      }

      Submarine.prevCullArea = camView;
      Submarine.prevCullTime = Timing.TotalTime;

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Submarine.cs#L116
    public static bool Submarine_DrawFront_Replace(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
    {
      var entitiesToRender = !editing && Submarine.visibleEntities != null ? Submarine.visibleEntities : MapEntity.MapEntityList;

      foreach (MapEntity e in entitiesToRender)
      {
        if (!e.DrawOverWater) { continue; }

        if (predicate != null)
        {
          if (!predicate(e)) { continue; }
        }

        e.Draw(spriteBatch, editing, false);
      }

      if (GameMain.DebugDraw)
      {
        foreach (Submarine sub in Submarine.Loaded)
        {
          Rectangle worldBorders = sub.Borders;
          worldBorders.Location += (sub.DrawPosition + sub.HiddenSubPosition).ToPoint();
          worldBorders.Y = -worldBorders.Y;

          GUI.DrawRectangle(spriteBatch, worldBorders, Color.White, false, 0, 5);

          if (sub.SubBody == null || sub.subBody.PositionBuffer.Count < 2) continue;

          Vector2 prevPos = ConvertUnits.ToDisplayUnits(sub.subBody.PositionBuffer[0].Position);
          prevPos.Y = -prevPos.Y;

          for (int i = 1; i < sub.subBody.PositionBuffer.Count; i++)
          {
            Vector2 currPos = ConvertUnits.ToDisplayUnits(sub.subBody.PositionBuffer[i].Position);
            currPos.Y = -currPos.Y;

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 10, (int)currPos.Y - 10, 20, 20), Color.Blue * 0.6f, true, 0.01f);
            GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.5f, 0, 5);

            prevPos = currPos;
          }
        }
      }

      return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Submarine.cs#L224
    public static bool Submarine_DrawBack_Replace(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
    {
      var entitiesToRender = !editing && Submarine.visibleEntities != null ? Submarine.visibleEntities : MapEntity.MapEntityList;

      foreach (MapEntity e in entitiesToRender)
      {
        if (!e.DrawBelowWater) continue;

        if (predicate != null)
        {
          if (!predicate(e)) continue;
        }

        e.Draw(spriteBatch, editing, true);
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Submarine.cs#L165
    public static bool Submarine_DrawDamageable_Replace(SpriteBatch spriteBatch, Effect damageEffect, bool editing = false, Predicate<MapEntity> predicate = null)
    {
      var entitiesToRender = !editing && Submarine.visibleEntities != null ? Submarine.visibleEntities : MapEntity.MapEntityList;

      Submarine.depthSortedDamageable.Clear();

      //insertion sort according to draw depth
      foreach (MapEntity e in entitiesToRender)
      {
        if (e is Structure structure && structure.DrawDamageEffect)
        {
          if (predicate != null)
          {
            if (!predicate(e)) { continue; }
          }
          float drawDepth = structure.GetDrawDepth();
          int i = 0;
          while (i < Submarine.depthSortedDamageable.Count)
          {
            float otherDrawDepth = Submarine.depthSortedDamageable[i].GetDrawDepth();
            if (otherDrawDepth < drawDepth) { break; }
            i++;
          }
          Submarine.depthSortedDamageable.Insert(i, structure);
        }
      }

      foreach (Structure s in Submarine.depthSortedDamageable)
      {
        s.DrawDamage(spriteBatch, damageEffect, editing);
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Submarine.cs#L204
    public static bool Submarine_DrawPaintedColors_Replace(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
    {
      var entitiesToRender = !editing && Submarine.visibleEntities != null ? Submarine.visibleEntities : MapEntity.MapEntityList;

      foreach (MapEntity e in entitiesToRender)
      {
        if (e is Hull hull)
        {
          if (hull.SupportsPaintedColors)
          {
            if (predicate != null)
            {
              if (!predicate(e)) { continue; }
            }
            hull.DrawSectionColors(spriteBatch);
          }
        }
      }

      return false;
    }
  }
}