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
        original: typeof(Submarine).GetMethod("DrawFront", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawFront_Replace"))
      );

      harmony.Patch(
        original: typeof(Submarine).GetMethod("DrawBack", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Submarine_DrawBack_Replace"))
      );
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
          worldBorders.Location += sub.WorldPosition.ToPoint();
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
    public static void Submarine_DrawBack_Replace(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
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
    }
  }
}