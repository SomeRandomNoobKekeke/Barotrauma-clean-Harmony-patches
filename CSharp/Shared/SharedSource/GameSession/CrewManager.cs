using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;

using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.Tutorials;
#endif
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedCrewManager()
    {
      harmony.Patch(
        original: typeof(CrewManager).GetMethod("SaveActiveOrders", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_SaveActiveOrders_Replace"))
      );
    }

    public static void CrewManager_SaveActiveOrders_Replace(CrewManager __instance, ref bool __runOriginal, XElement element)
    {
      CrewManager _ = __instance;
      __runOriginal = false;


      // Only save orders with no fade out time (e.g. ignore orders)
      var ordersToSave = new List<Order>();
      foreach (var activeOrder in _.ActiveOrders)
      {
        var order = activeOrder?.Order;
        if (order == null || activeOrder.FadeOutTime.HasValue) { continue; }
        ordersToSave.Add(order.WithManualPriority(CharacterInfo.HighestManualOrderPriority));
      }
      CharacterInfo.SaveOrders(element, ordersToSave.ToArray());
    }

  }
}