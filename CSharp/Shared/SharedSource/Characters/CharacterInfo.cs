using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Abilities;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedCharacterInfo()
    {
      harmony.Patch(
        original: typeof(CharacterInfo).GetMethod("GetManualOrderPriority", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CharacterInfo_GetManualOrderPriority_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaShared/SharedSource/Characters/CharacterInfo.cs#L592
    public static bool CharacterInfo_GetManualOrderPriority_Replace(CharacterInfo __instance, ref int __result, Order order)
    {
      CharacterInfo _ = __instance;

      if (order != null && order.AssignmentPriority < 100 && _.CurrentOrders.Any())
      {
        int orderPriority = CharacterInfo.HighestManualOrderPriority;
        for (int i = 0; i < _.CurrentOrders.Count; i++)
        {
          if (order.AssignmentPriority >= _.CurrentOrders[i].AssignmentPriority)
          {
            break;
          }
          else
          {
            orderPriority--;
          }
        }
        __result = Math.Max(orderPriority, 1); return false;
      }
      else
      {
        __result = CharacterInfo.HighestManualOrderPriority; return false;
      }
    }

  }
}