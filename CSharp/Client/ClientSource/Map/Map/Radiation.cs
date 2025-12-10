using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientRadiation()
    {
      harmony.Patch(
        original: typeof(Radiation).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Radiation_Draw_Replace"))
      );

      harmony.Patch(
        original: typeof(Radiation).GetMethod("DrawFront", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Radiation_DrawFront_Replace"))
      );

      harmony.Patch(
        original: typeof(Radiation).GetMethod("MapUpdate", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Radiation_MapUpdate_Replace"))
      );
    }


    public static void Radiation_Draw_Replace(Radiation __instance, ref bool __runOriginal, SpriteBatch spriteBatch, Rectangle container, float zoom)
    {
      __runOriginal = false;
      Radiation _ = __instance;


      if (!_.Enabled) { return; }

      UISprite? radiationMainSprite = GUIStyle.Radiation;
      var (offsetX, offsetY) = _.Map.DrawOffset * zoom;
      var (centerX, centerY) = container.Center.ToVector2();
      var (halfSizeX, halfSizeY) = new Vector2(container.Width / 2f, container.Height / 2f) * zoom;
      float viewBottom = centerY + _.Map.Height * zoom;
      Vector2 topLeft = new Vector2(centerX + offsetX - halfSizeX, centerY + offsetY - halfSizeY);
      Vector2 size = new Vector2((_.Amount - _.increasedAmount) * zoom + halfSizeX, viewBottom - topLeft.Y);
      if (size.X < 0) { return; }

      Vector2 spriteScale = new Vector2(zoom);

      radiationMainSprite?.Sprite.DrawTiled(spriteBatch, topLeft, size, color: _.Params.RadiationAreaColor, startOffset: Vector2.Zero, textureScale: spriteScale);

      Vector2 topRight = topLeft + Vector2.UnitX * size.X;

      int index = 0;
      if (_.radiationEdgeAnimSheet != null)
      {
        for (float i = 0; i <= size.Y; i += _.radiationEdgeAnimSheet.FrameSize.Y / 2f * zoom)
        {
          bool isEven = ++index % 2 == 0;
          Vector2 origin = new Vector2(0.5f, 0) * _.radiationEdgeAnimSheet.FrameSize.X;
          // every other sprite's animation is reversed to make it seem more chaotic
          int sprite = (int)MathF.Floor(isEven ? Radiation.spriteIndex : _.maxFrames - Radiation.spriteIndex);
          _.radiationEdgeAnimSheet.Draw(spriteBatch, sprite, topRight + new Vector2(0, i), _.Params.RadiationBorderTint, origin, 0f, spriteScale);
        }
      }

      _.radiationMultiplier = null;
      if (container.Contains(PlayerInput.MousePosition))
      {
        float rightEdge = topLeft.X + size.X;
        float distanceFromRight = rightEdge - PlayerInput.MousePosition.X;
        if (distanceFromRight >= 0)
        {
          _.radiationMultiplier = Math.Min(4, (int)(distanceFromRight / (_.Params.RadiationEffectMultipliedPerPixelDistance * zoom)) + 1);
        }
      }
    }


    public static void Radiation_DrawFront_Replace(Radiation __instance, ref bool __runOriginal, SpriteBatch spriteBatch)
    {
      __runOriginal = false;
      Radiation _ = __instance;

      if (_.radiationMultiplier is int multiplier)
      {
        var tooltip = TextManager.GetWithVariable("RadiationTooltip", "[jovianmultiplier]", multiplier.ToString());
        GUIComponent.DrawToolTip(spriteBatch, tooltip, PlayerInput.MousePosition + new Vector2(18 * GUI.Scale));
      }
    }


    public static void Radiation_MapUpdate_Replace(Radiation __instance, ref bool __runOriginal, float deltaTime)
    {
      __runOriginal = false;
      Radiation _ = __instance;

      float spriteStep = _.Params.BorderAnimationSpeed * deltaTime;
      Radiation.spriteIndex = (Radiation.spriteIndex + spriteStep) % _.maxFrames;

      if (_.increasedAmount > 0)
      {
        _.increasedAmount -= (_.lastIncrease / _.Params.AnimationSpeed) * deltaTime;
      }
    }


  }
}
