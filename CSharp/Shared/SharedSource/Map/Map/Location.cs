using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;



namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedLocation()
    {
      harmony.Patch(
        original: typeof(Location).GetMethod("IsCriticallyRadiated", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Location_IsCriticallyRadiated_Replace"))
      );
    }


    public static bool Location_IsCriticallyRadiated_Replace(ref bool __result, Location __instance)
    {
      if (GameMain.GameSession?.Map?.Radiation != null)
      {
        __result = __instance.TurnsInRadiation > GameMain.GameSession.Map.Radiation.Params.CriticalRadiationThreshold;
        return false;
      }

      __result = false;
      return false;
    }
  }
}