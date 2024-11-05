using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using Barotrauma.PerkBehaviors;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientGameSession()
    {
      harmony.Patch(
        original: typeof(GameSession).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameSession_Update_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/GameSession/GameSession.cs#L1044
    public static bool GameSession_Update_Replace(float deltaTime, GameSession __instance)
    {
      GameSession _ = __instance;

      _.RoundDuration += deltaTime;
      _.EventManager?.Update(deltaTime);
      _.GameMode?.Update(deltaTime);
      //backwards for loop because the missions may get completed and removed from the list in Update()
      for (int i = _.missions.Count - 1; i >= 0; i--)
      {
        _.missions[i].Update(deltaTime);
      }
      _.UpdateProjSpecific(deltaTime);

      return false;
    }
  }
}