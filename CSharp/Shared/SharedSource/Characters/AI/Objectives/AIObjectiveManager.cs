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
using Barotrauma.Networking; // used by the server
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedAIObjectiveManager()
    {
      harmony.Patch(
        original: typeof(AIObjectiveManager).GetMethod("GetOrderPriority", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("AIObjectiveManager_GetOrderPriority_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaShared/SharedSource/Characters/AI/Objectives/AIObjectiveManager.cs#L799
    public static bool AIObjectiveManager_GetOrderPriority_Replace(AIObjectiveManager __instance, ref float __result, AIObjective objective)
    {
      AIObjectiveManager _ = __instance;


      if (objective == _.ForcedOrder)
      {
        __result = AIObjectiveManager.HighestOrderPriority; return false;
      }
      var currentOrder = _.CurrentOrders.FirstOrDefault(o => o.Objective == objective);
      if (currentOrder.Objective == null)
      {
        __result = AIObjectiveManager.HighestOrderPriority; return false;
      }
      else if (currentOrder.ManualPriority > 0)
      {
        if (objective.ForceHighestPriority)
        {
          __result = AIObjectiveManager.HighestOrderPriority; return false;
        }
        if (objective.PrioritizeIfSubObjectivesActive && objective.SubObjectives.Any())
        {
          __result = AIObjectiveManager.HighestOrderPriority; return false;
        }
        __result = MathHelper.Lerp(AIObjectiveManager.LowestOrderPriority, AIObjectiveManager.HighestOrderPriority - 1, MathUtils.InverseLerp(1, CharacterInfo.HighestManualOrderPriority, currentOrder.ManualPriority)); return false;
      }
#if DEBUG
      DebugConsole.AddWarning("Error in order priority: shouldn't return 0!");
#endif
      __result = 0; return false;
    }

  }
}