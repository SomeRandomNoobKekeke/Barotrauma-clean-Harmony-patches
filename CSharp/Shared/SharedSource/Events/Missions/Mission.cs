using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedMission()
    {
      harmony.Patch(
        original: typeof(Mission).GetMethod("LoadHuman", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Mission_LoadHuman_Replace"))
      );
    }


    public static bool Mission_LoadHuman_Replace(Mission __instance, ref Character __result, HumanPrefab humanPrefab, XElement element, Submarine submarine)
    {
      Mission _ = __instance;


      Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
      Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);
      var spawnPointType = element.GetAttributeEnum("spawnpointtype", SpawnType.Human);
      ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(
          _.GetSpawnLocationTypeFromSubmarineType(submarine), spawnPointType,
          moduleFlags ?? humanPrefab.GetModuleFlags(),
          spawnPointTags ?? humanPrefab.GetSpawnPointTags(),
          element.GetAttributeBool("asfaraspossible", false));
      spawnPos ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();
      var teamId = element.GetAttributeEnum("teamid", CharacterTeamType.None);
      var originalTeam = Level.Loaded.StartOutpost?.TeamID ?? teamId;
      Character spawnedCharacter = Mission.CreateHuman(humanPrefab, _.characters, _.characterItems, submarine, originalTeam, spawnPos);
      //consider the NPC to be "originally" from the team of the outpost it spawns in, and change it to the desired (hostile) team afterwards
      //that allows the NPC to fight intruders and otherwise function in the outpost if the mission is configured to spawn the hostile NPCs in a friendly outpost
      if (teamId != originalTeam)
      {
        spawnedCharacter.SetOriginalTeamAndChangeTeam(teamId, processImmediately: true);
      }
      if (element.GetAttribute("color") != null)
      {
        spawnedCharacter.UniqueNameColor = element.GetAttributeColor("color", Color.Red);
      }
      if (submarine.Info is { IsOutpost: true } outPostInfo)
      {
        outPostInfo.AddOutpostNPCIdentifierOrTag(spawnedCharacter, humanPrefab.Identifier);
        foreach (Identifier tag in humanPrefab.GetTags())
        {
          outPostInfo.AddOutpostNPCIdentifierOrTag(spawnedCharacter, tag);
        }
      }
      if (spawnPos is WayPoint wp)
      {
        spawnedCharacter.GiveIdCardTags(wp);
      }
      _.InitCharacter(spawnedCharacter, element);
      __result = spawnedCharacter; return false;
    }



  }
}