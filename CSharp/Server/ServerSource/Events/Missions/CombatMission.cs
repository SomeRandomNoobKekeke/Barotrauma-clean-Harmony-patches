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

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaServer/ServerSource/Events/Missions/CombatMission.cs#L92
    public static void CombatMission_CheckTeamCharacters_Replace(CombatMission __instance, ref bool __runOriginal)
    {
      CombatMission _ = __instance;
      __runOriginal = false;

      for (int i = 0; i < crews.Length; i++)
      {
        foreach (var character in crews[i])
        {
          if (character.IsDead)
          {
            AddKill(character);
          }
        }
      }

      crews[0].Clear();
      crews[1].Clear();
      foreach (Character character in Character.CharacterList)
      {
        if (character.IsDead) { continue; }
        if (character.TeamID == CharacterTeamType.Team1)
        {
          crews[0].Add(character);
        }
        else if (character.TeamID == CharacterTeamType.Team2)
        {
          crews[1].Add(character);
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