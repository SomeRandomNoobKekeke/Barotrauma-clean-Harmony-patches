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
using System.Linq;
using System.Xml.Linq;



namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedRadiation()
    {
      harmony.Patch(
        original: typeof(Radiation).GetMethod("OnStep", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Radiation_OnStep_Replace"))
      );

      harmony.Patch(
        original: typeof(Radiation).GetMethod("UpdateRadiation", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Radiation_UpdateRadiation_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Map/Radiation.cs#L49
    public static bool Radiation_OnStep_Replace(Radiation __instance, float steps = 1)
    {
      Radiation _ = __instance;

      if (!_.Enabled) { return false; }
      if (steps <= 0) { return false; }

      float increaseAmount = _.Params.RadiationStep * steps;

      if (_.Params.MaxRadiation > 0 && _.Params.MaxRadiation < _.Amount + increaseAmount)
      {
        increaseAmount = _.Params.MaxRadiation - _.Amount;
      }

      _.IncreaseRadiation(increaseAmount);

      int amountOfOutposts = _.Map.Locations.Count(location => location.Type.HasOutpost && !location.IsCriticallyRadiated());

      foreach (Location location in _.Map.Locations.Where(_.Contains))
      {
        if (location.IsGateBetweenBiomes)
        {
          location.Connections.ForEach(c => c.Locked = false);
          continue;
        }

        if (amountOfOutposts <= _.Params.MinimumOutpostAmount) { break; }

        if (_.Map.CurrentLocation is { } currLocation)
        {
          // Don't advance on nearby locations to avoid buggy behavior
          if (currLocation == location || currLocation.Connections.Any(lc => lc.OtherLocation(currLocation) == location)) { continue; }
        }

        bool wasCritical = location.IsCriticallyRadiated();

        location.TurnsInRadiation++;

        if (location.Type.HasOutpost && !wasCritical && location.IsCriticallyRadiated())
        {
          location.ClearMissions();
          amountOfOutposts--;
        }
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Map/Radiation.cs#L101
    public static bool Radiation_UpdateRadiation_Replace(Radiation __instance, float deltaTime)
    {
      Radiation _ = __instance;

      if (!(GameMain.GameSession?.IsCurrentLocationRadiated() ?? false)) { return false; }

      if (GameMain.NetworkMember is { IsClient: true }) { return false; }

      if (_.radiationTimer > 0)
      {
        _.radiationTimer -= deltaTime;
        return false;
      }

      if (_.radiationAffliction == null)
      {
        float radiationStrengthChange = AfflictionPrefab.RadiationSickness.Effects.FirstOrDefault()?.StrengthChange ?? 0.0f;
        _.radiationAffliction = new Affliction(
            AfflictionPrefab.RadiationSickness,
            (_.Params.RadiationDamageAmount - radiationStrengthChange) * _.Params.RadiationDamageDelay);
      }

      _.radiationTimer = _.Params.RadiationDamageDelay;

      foreach (Character character in Character.CharacterList)
      {
        if (character.IsDead || character.Removed || !(character.CharacterHealth is { } health)) { continue; }

        if (_.IsEntityRadiated(character))
        {
          var limb = character.AnimController.MainLimb;
          AttackResult attackResult = limb.AddDamage(limb.SimPosition, _.radiationAffliction.ToEnumerable(), playSound: false);
          character.CharacterHealth.ApplyDamage(limb, attackResult);
        }
      }

      return false;
    }


  }
}