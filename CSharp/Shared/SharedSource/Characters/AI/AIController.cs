using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedAIController()
    {
      // Use it either as a harmony patch or as a base call substitution
      // But not both or it will cause ExecutionEngineException

      // harmony.Patch(
      //   original: typeof(AIController).GetMethod("Update", AccessTools.all),
      //   prefix: new HarmonyMethod(typeof(Mod).GetMethod("AIController_Update_Replace"))
      // );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/AI/AIController.cs#L160
    public static bool AIController_Update_Replace(float deltaTime, AIController __instance)
    {
      if (__instance.hullVisibilityTimer > 0)
      {
        __instance.hullVisibilityTimer--;
      }
      else
      {
        __instance.hullVisibilityTimer = AIController.hullVisibilityInterval;
        __instance.VisibleHulls = __instance.Character.GetVisibleHulls();
      }

      return false;
    }

  }
}