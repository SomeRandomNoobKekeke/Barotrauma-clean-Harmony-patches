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

      foreach (Location location in _.Map.Locations.Where(l => _.DepthInRadiation(l) > 0))
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

      _.radiationTimer = _.Params.RadiationDamageDelay;

      foreach (Character character in Character.CharacterList)
      {
        if (character.IsDead || character.Removed || !(character.CharacterHealth is { } health)) { continue; }

        float depthInRadiation = _.DepthInRadiation(character);
        if (depthInRadiation > 0)
        {
          AfflictionPrefab afflictionPrefab;
          // Get the related affliction (if necessary, fall back to the traditional radiation sickness for slightly better backwards compatibility)
          afflictionPrefab = AfflictionPrefab.JovianRadiation ?? AfflictionPrefab.RadiationSickness;
          float currentAfflictionStrength = character.CharacterHealth.GetAfflictionStrengthByIdentifier(afflictionPrefab.Identifier);

          // Get Jovian radiation strength, and cancel out the affliction's strength change (meant for decaying it)
          // (for simplicity, let's assume each Effect of the Affliction has the same strengthchange)
          float addedStrength = _.Params.RadiationDamageAmount - afflictionPrefab.Effects.FirstOrDefault()?.StrengthChange ?? 0.0f;

          // Damage is applied periodically, so we must apply the total damage for the full period at once (after deducting strengthchange)
          addedStrength *= _.Params.RadiationDamageDelay;

          // The JovianRadiation affliction has brackets of 25 strength determined by the multiplier (1x = 0-25, 2x = 25-50 etc.)
          int multiplier = (int)Math.Ceiling(depthInRadiation / _.Params.RadiationEffectMultipliedPerPixelDistance);
          float growthPotentialInBracket = (multiplier * 25) - currentAfflictionStrength;
          if (growthPotentialInBracket > 0)
          {
            addedStrength = Math.Min(addedStrength, growthPotentialInBracket);
            character.CharacterHealth.ApplyAffliction(
                character.AnimController?.MainLimb,
                afflictionPrefab.Instantiate(addedStrength));
          }
        }
      }

      return false;
    }


  }
}
