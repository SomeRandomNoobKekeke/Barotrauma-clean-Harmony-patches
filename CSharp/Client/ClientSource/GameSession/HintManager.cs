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
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientHintManager()
    {
      harmony.Patch(
        original: typeof(HintManager).GetMethod("OnStartedControlling", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("HintManager_OnStartedControlling_Replace"))
      );


    }


    public static void HintManager_OnStartedControlling_Replace()
    {
      if (Level.IsLoadedOutpost) { return; }
      if (Character.Controlled?.Info?.Job?.Prefab == null) { return; }
      Identifier hintIdentifier = $"onstartedcontrolling.job.{Character.Controlled.Info.Job.Prefab.Identifier}".ToIdentifier();
      HintManager.DisplayHint(hintIdentifier,
          icon: Character.Controlled.Info.Job.Prefab.Icon,
          iconColor: Character.Controlled.Info.Job.Prefab.UIColor,
          onDisplay: () =>
          {
            if (!HintManager.HintOrders.TryGetValue(hintIdentifier, out var orderInfo)) { return; }
            var orderPrefab = OrderPrefab.Prefabs[orderInfo.identifier];
            if (orderPrefab == null) { return; }
            Item targetEntity = null;
            ItemComponent targetItem = null;
            if (orderPrefab.MustSetTarget)
            {
              targetEntity = orderPrefab.GetMatchingItems(true, interactableFor: Character.Controlled, orderOption: orderInfo.option).FirstOrDefault();
              if (targetEntity == null) { return; }
              targetItem = orderPrefab.GetTargetItemComponent(targetEntity);
            }
            var order = new Order(orderPrefab, orderInfo.option, targetEntity, targetItem, orderGiver: Character.Controlled).WithManualPriority(CharacterInfo.HighestManualOrderPriority);
            GameMain.GameSession.CrewManager.SetCharacterOrder(Character.Controlled, order);
          });
    }
  }
}