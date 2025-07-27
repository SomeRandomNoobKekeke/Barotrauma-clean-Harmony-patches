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
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedElectricalDischarger()
    {
      harmony.Patch(
        original: typeof(ElectricalDischarger).GetMethod("Discharge", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ElectricalDischarger_Discharge_Replace"))
      );
    }

    public static bool ElectricalDischarger_Discharge_Replace(ElectricalDischarger __instance)
    {
      ElectricalDischarger _ = __instance;

      _.reloadTimer = _.Reload;
      _.ApplyStatusEffects(ActionType.OnUse, 1.0f);
      _.FindNodes(_.item.WorldPosition, _.Range);
      if (_.attack != null)
      {
        foreach ((Character character, ElectricalDischarger.Node node) in _.charactersInRange)
        {
          if (character == null || character.Removed) { continue; }
          character.ApplyAttack(_.user, node.WorldPosition, _.attack, MathHelper.Clamp(_.Voltage, 1.0f, ElectricalDischarger.MaxOverVoltageFactor),
              impulseDirection: character.WorldPosition - node.WorldPosition);
        }
      }
      _.DischargeProjSpecific();
      _.charging = false;

      return false;
    }

  }
}