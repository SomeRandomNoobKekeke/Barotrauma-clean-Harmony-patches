using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;




namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchServerAICharacter()
    {
      harmony.Patch(
        original: typeof(CombatMission).GetMethod("CheckTeamCharacters", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CombatMission_CheckTeamCharacters_Replace"))
      );
    }


    public static void CombatMission_CheckTeamCharacters_Replace(CombatMission __instance, ref bool __runOriginal)
    {
      CombatMission _ = __instance;
      __runOriginal = false;

      for (int i = 0; i < _.crews.Length; i++)
      {
        foreach (var character in _.crews[i])
        {
          if (character.IsDead)
          {
            _.AddKill(character);
          }
        }
      }

      _.crews[0].Clear();
      _.crews[1].Clear();
      foreach (Character character in Character.CharacterList)
      {
        if (character.IsDead) { continue; }
        if (character.TeamID == CharacterTeamType.Team1)
        {
          _.crews[0].Add(character);
        }
        else if (character.TeamID == CharacterTeamType.Team2)
        {
          _.crews[1].Add(character);
        }
        if (character.IsBot && character.AIController is HumanAIController humanAi)
        {
          if (!humanAi.ObjectiveManager.HasOrder<AIObjectiveFightIntruders>(o => o.TargetCharactersInOtherSubs) &&
              OrderPrefab.Prefabs.TryGet(Tags.AssaultEnemyOrder, out OrderPrefab? assaultOrder))
          {
            character.SetOrder(assaultOrder.CreateInstance(
                OrderPrefab.OrderTargetType.Entity, orderGiver: null).WithManualPriority(CharacterInfo.HighestManualOrderPriority),
                isNewOrder: true, speak: false);
          }
        }
      }
    }

  }
}