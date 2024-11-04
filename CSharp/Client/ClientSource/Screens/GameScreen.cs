// #define DEBUG
// Note: i didn't really test it with debug

using System;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Collections.Immutable;

using HarmonyLib;
using Barotrauma;

using Barotrauma.Extensions;
using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientGameScreen()
    {
      harmony.Patch(
        original: typeof(GameScreen).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameScreen_Draw_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Screens/GameScreen.cs#L98
    public static bool GameScreen_Draw_Replace(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch, GameScreen __instance)
    {
      GameScreen _ = __instance;

      _.cam.UpdateTransform(true);
      Submarine.CullEntities(_.cam);

      foreach (Character c in Character.CharacterList)
      {
        c.AnimController.Limbs.ForEach(l => l.body.UpdateDrawPosition());
        bool wasVisible = c.IsVisible;
        c.DoVisibilityCheck(_.cam);
        if (c.IsVisible != wasVisible)
        {
          foreach (var limb in c.AnimController.Limbs)
          {
            if (limb.LightSource is LightSource light)
            {
              light.Enabled = c.IsVisible;
            }
          }
        }
      }

      Stopwatch sw = new Stopwatch();
      sw.Start();

      _.DrawMap(graphics, spriteBatch, deltaTime);

      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Draw:Map", sw.ElapsedTicks);
      sw.Restart();

      spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);

      if (Character.Controlled != null && _.cam != null) { Character.Controlled.DrawHUD(spriteBatch, _.cam); }

      if (GameMain.GameSession != null) { GameMain.GameSession.Draw(spriteBatch); }

      if (Character.Controlled == null && !GUI.DisableHUD)
      {
        _.DrawPositionIndicators(spriteBatch);
      }

      if (!GUI.DisableHUD)
      {
        foreach (Character c in Character.CharacterList)
        {
          c.DrawGUIMessages(spriteBatch, _.cam);
        }
      }

      GUI.Draw(_.cam, spriteBatch);

      spriteBatch.End();

      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Draw:HUD", sw.ElapsedTicks);
      sw.Restart();

      return false;
    }



  }
}