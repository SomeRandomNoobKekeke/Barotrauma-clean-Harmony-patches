using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;

using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedShipIssueWorker()
    {
      harmony.Patch(
        original: typeof(ShipIssueWorker).GetMethod("SetOrder", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ShipIssueWorker_SetOrder_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaShared/SharedSource/Characters/AI/ShipCommand/ShipIssueWorker.cs#L47
    public static void ShipIssueWorker_SetOrder_Replace(ShipIssueWorker __instance, ref bool __runOriginal, Character orderedCharacter)
    {
      ShipIssueWorker _ = __instance;
      __runOriginal = false;


      _.OrderedCharacter = orderedCharacter;
      if (_.OrderedCharacter.AIController is HumanAIController humanAI &&
          humanAI.ObjectiveManager.CurrentOrders.None(o => o.MatchesOrder(_.SuggestedOrder.Identifier, _.Option) && o.TargetEntity == _.TargetItem))
      {
        bool orderGivenByDifferentCharacter = orderedCharacter != _.CommandingCharacter;
        if (orderGivenByDifferentCharacter)
        {
          _.CommandingCharacter.Speak(_.SuggestedOrder.GetChatMessage(_.OrderedCharacter.Name, "", givingOrderToSelf: false),
              minDurationBetweenSimilar: 5,
              identifier: ("GiveOrder." + _.SuggestedOrder.Prefab.Identifier).ToIdentifier());
        }
        _.CurrentOrder = _.SuggestedOrder
            .WithOption(_.Option)
            .WithItemComponent(_.TargetItem, _.TargetItemComponent)
            .WithOrderGiver(_.CommandingCharacter)
            .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
        _.OrderedCharacter.SetOrder(_.CurrentOrder, _.CommandingCharacter != _.OrderedCharacter);
        if (orderGivenByDifferentCharacter)
        {
          _.OrderedCharacter.Speak(TextManager.Get("DialogAffirmative").Value, delay: 1.0f,
              minDurationBetweenSimilar: 5,
              identifier: ("ReceiveOrder." + _.SuggestedOrder.Prefab.Identifier).ToIdentifier());
        }
      }
      _.TimeSinceLastAttempt = 0f;
    }

  }
}