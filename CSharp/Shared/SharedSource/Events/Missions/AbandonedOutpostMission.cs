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
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedAbandonedOutpostMission()
    {
      harmony.Patch(
        original: typeof(AbandonedOutpostMission).GetMethod("LoadHuman", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("AbandonedOutpostMission_LoadHuman_Replace"))
      );
    }


    public static bool AbandonedOutpostMission_LoadHuman_Replace(AbandonedOutpostMission __instance, ref Character __result, HumanPrefab humanPrefab, XElement element, Submarine submarine)
    {
      AbandonedOutpostMission _ = __instance;


      //Character spawnedCharacter = base.LoadHuman(humanPrefab, element, submarine);
      Character spawnedCharacter = null; Mission_LoadHuman_Replace(_, ref spawnedCharacter, humanPrefab, element, submarine);

      bool requiresRescue = element.GetAttributeBool("requirerescue", false);
      if (requiresRescue)
      {
        _.requireRescue.Add(spawnedCharacter);
#if CLIENT
      if (_.allowOrderingRescuees)
      {
          GameMain.GameSession.CrewManager?.AddCharacterToCrewList(spawnedCharacter);
      }
#endif
      }
      else if (_.TimesAttempted > 0 && spawnedCharacter.AIController is HumanAIController)
      {
        var order = OrderPrefab.Prefabs["fightintruders"]
            .CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: spawnedCharacter)
            .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
        spawnedCharacter.SetOrder(order, isNewOrder: true, speak: false);
      }
      // Overrides the team change set in the base method.
      var teamId = element.GetAttributeEnum("teamid", requiresRescue ? CharacterTeamType.FriendlyNPC : CharacterTeamType.None);
      if (teamId != spawnedCharacter.TeamID)
      {
        spawnedCharacter.SetOriginalTeamAndChangeTeam(teamId);
      }
      __result = spawnedCharacter; return false;
    }


  }
}