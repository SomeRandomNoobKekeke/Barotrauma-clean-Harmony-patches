using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;

using Barotrauma;
using HarmonyLib;

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Particles;
using Barotrauma.SpriteDeformations;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
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
    public static void PatchClientRagdoll()
    {
      harmony.Patch(
        original: typeof(Ragdoll).GetMethod("PlayImpactSound", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Ragdoll_PlayImpactSound_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Characters/Animation/Ragdoll.cs#L377
    public static void Ragdoll_PlayImpactSound_Replace(Ragdoll __instance, ref bool __runOriginal, Limb limb)
    {
      Ragdoll _ = __instance;
      __runOriginal = false;


      limb.LastImpactSoundTime = (float)Timing.TotalTime;
      if (!string.IsNullOrWhiteSpace(limb.HitSoundTag))
      {
        bool inWater = limb.InWater;
        if (_.character.CurrentHull != null &&
            _.character.CurrentHull.Surface > _.character.CurrentHull.Rect.Y - _.character.CurrentHull.Rect.Height + 5.0f &&
            limb.SimPosition.Y < ConvertUnits.ToSimUnits(_.character.CurrentHull.Rect.Y - _.character.CurrentHull.Rect.Height) + limb.body.GetMaxExtent())
        {
          inWater = true;
        }
        SoundPlayer.PlaySound(inWater ? "footstep_water" : limb.HitSoundTag, limb.WorldPosition, hullGuess: _.character.CurrentHull);
      }
      foreach (WearableSprite wearable in limb.WearingItems)
      {
        if (limb.type == wearable.Limb && !string.IsNullOrWhiteSpace(wearable.Sound))
        {
          SoundPlayer.PlaySound(wearable.Sound, limb.WorldPosition, hullGuess: _.character.CurrentHull);
        }
      }
    }

  }
}