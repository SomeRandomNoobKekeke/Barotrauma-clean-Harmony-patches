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
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using System.Globalization;
using MoonSharp.Interpreter;
using Barotrauma.Abilities;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedCharacterHealth()
    {
      harmony.Patch(
        original: typeof(CharacterHealth).GetMethod("ApplyAfflictionStatusEffects", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CharacterHealth_ApplyAfflictionStatusEffects_Replace"))
      );
    }

    public static bool CharacterHealth_ApplyAfflictionStatusEffects_Replace(CharacterHealth __instance, ActionType type)
    {
      CharacterHealth _ = __instance;

      if (_.isApplyingAfflictionStatusEffects)
      {
        //pretty hacky: if we're already in the process of applying afflictions' status effects
        //(i.e. calling this method caused some additional afflictions to appear and trigger status effects)
        //let's instantiate a new list so we don't end up modifying afflictionsCopy while enumerating it
        foreach (Affliction affliction in _.afflictions.Keys.ToList())
        {
          affliction.ApplyStatusEffects(type, 1.0f, _, targetLimb: _.GetAfflictionLimb(affliction));
        }
      }
      else
      {
        _.isApplyingAfflictionStatusEffects = true;
        _.afflictionsCopy.Clear();
        _.afflictionsCopy.AddRange(_.afflictions.Keys);
        _.isApplyingAfflictionStatusEffects = true;
        foreach (Affliction affliction in _.afflictionsCopy)
        {
          affliction.ApplyStatusEffects(type, 1.0f, _, targetLimb: _.GetAfflictionLimb(affliction));
        }
        _.isApplyingAfflictionStatusEffects = false;
      }

      return false;
    }

  }
}