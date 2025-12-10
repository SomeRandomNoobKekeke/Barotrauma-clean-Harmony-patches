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
    public static void PatchClientLevel()
    {
      harmony.Patch(
        original: typeof(Level).GetMethod("DrawBack", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Level_DrawBack_Replace"))
      );

      harmony.Patch(
        original: typeof(Level).GetMethod("DrawFront", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Level_DrawFront_Replace"))
      );


    }


    public static bool Level_DrawBack_Replace(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Level __instance)
    {
      Level _ = __instance;

      float brightness = MathHelper.Clamp(1.1f + (cam.Position.Y - _.Size.Y) / 100000.0f, 0.1f, 1.0f);
      var lightColorHLS = _.GenerationParams.AmbientLightColor.RgbToHLS();
      lightColorHLS.Y *= brightness;

      GameMain.LightManager.AmbientLight = ToolBox.HLSToRGB(lightColorHLS);

      graphics.Clear(_.BackgroundColor);

      if (_.renderer != null)
      {
        GameMain.LightManager.AmbientLight = GameMain.LightManager.AmbientLight.Add(_.renderer.FlashColor);
        _.renderer?.DrawBackground(spriteBatch, cam, _.LevelObjectManager, _.backgroundCreatureManager);
      }

      return false;
    }


    public static bool Level_DrawFront_Replace(SpriteBatch spriteBatch, Camera cam, Level __instance)
    {
      __instance.renderer?.DrawForeground(spriteBatch, cam, __instance.LevelObjectManager);
      return false;
    }

  }
}