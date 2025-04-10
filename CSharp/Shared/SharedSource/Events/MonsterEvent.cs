using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedMonsterEvent()
    {
      harmony.Patch(
        original: typeof(MonsterEvent).GetMethod("InitEventSpecific", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("MonsterEvent_InitEventSpecific_Replace"))
      );
    }

    public static bool MonsterEvent_InitEventSpecific_Replace(EventSet parentSet, MonsterEvent __instance)
    {
      MonsterEvent _ = __instance;

      // apply pvp stun resistance (reduce stun amount via resist multiplier)
      if (GameMain.NetworkMember is { } networkMember && GameMain.GameSession?.GameMode is PvPMode && !networkMember.ServerSettings.PvPSpawnMonsters)
      {
        if (GameSettings.CurrentConfig.VerboseLogging)
        {
          DebugConsole.NewMessage($"PvP setting: disabling monster event ({_.SpeciesName})", Color.Yellow);
        }

        _.disallowed = true;
        return false;
      }

      if (parentSet != null && _.resetTime == 0)
      {
        // Use the parent reset time only if there's no reset time defined for the event.
        _.resetTime = parentSet.ResetTime;
      }
      if (GameSettings.CurrentConfig.VerboseLogging)
      {
        DebugConsole.NewMessage("Initialized MonsterEvent (" + _.SpeciesName + ")", Color.White);
      }

      _.monsters.Clear();

      //+1 because Range returns an integer less than the max value
      int amount = Rand.Range(_.MinAmount, _.MaxAmount + 1);
      for (int i = 0; i < amount; i++)
      {
        string seed = i.ToString() + Level.Loaded.Seed;
        Character createdCharacter = Character.Create(_.SpeciesName, Vector2.Zero, seed, characterInfo: null, isRemotePlayer: false, hasAi: true, createNetworkEvent: true, throwErrorIfNotFound: false);
        if (createdCharacter == null)
        {
          DebugConsole.AddWarning($"Error in MonsterEvent: failed to spawn the character \"{_.SpeciesName}\". Content package: \"{_.prefab.ConfigElement?.ContentPackage?.Name ?? "unknown"}\".",
              _.Prefab.ContentPackage);
          _.disallowed = true;
          continue;
        }
        if (_.overridePlayDeadProbability.HasValue)
        {
          createdCharacter.EvaluatePlayDeadProbability(_.overridePlayDeadProbability);
        }
        createdCharacter.DisabledByEvent = true;
        _.monsters.Add(createdCharacter);
      }

      return false;
    }


  }
}