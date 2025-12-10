using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using LimbParams = Barotrauma.RagdollParams.LimbParams;
using JointParams = Barotrauma.RagdollParams.JointParams;
using Barotrauma.Abilities;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedLimb()
    {
      harmony.Patch(
        original: typeof(Limb).GetMethod("AddDamage", AccessTools.all, new Type[]{
          typeof(Vector2),
          typeof(IEnumerable<Affliction>),
          typeof(bool),
          typeof(float),
          typeof(float),
          typeof(Character),
        }),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Limb_AddDamage_Replace"))
      );
    }


    public static bool Limb_AddDamage_Replace(Vector2 simPosition, IEnumerable<Affliction> afflictions, bool playSound, float damageMultiplier, float penetration, Character attacker, Limb __instance, ref AttackResult __result)
    {
      Limb _ = __instance;

      _.appliedDamageModifiers.Clear();
      _.afflictionsCopy.Clear();
      foreach (var affliction in afflictions)
      {
        _.tempModifiers.Clear();
        var newAffliction = affliction;
        float random = Rand.Value(Rand.RandSync.Unsynced);
        bool foundMatchingModifier = false;
        bool applyAffliction = true;
        foreach (DamageModifier damageModifier in _.DamageModifiers)
        {
          if (!damageModifier.MatchesAffliction(affliction)) { continue; }
          foundMatchingModifier = true;
          if (random > affliction.Probability * damageModifier.ProbabilityMultiplier)
          {
            applyAffliction = false;
            continue;
          }
          if (_.SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
          {
            _.tempModifiers.Add(damageModifier);
          }
        }
        foreach (WearableSprite wearable in _.WearingItems)
        {
          foreach (DamageModifier damageModifier in wearable.WearableComponent.DamageModifiers)
          {
            if (!damageModifier.MatchesAffliction(affliction)) { continue; }
            foundMatchingModifier = true;
            if (random > affliction.Probability * damageModifier.ProbabilityMultiplier)
            {
              applyAffliction = false;
              continue;
            }
            if (_.SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
            {
              _.tempModifiers.Add(damageModifier);
            }
          }
        }
        if (!foundMatchingModifier && random > affliction.Probability) { continue; }
        float finalDamageModifier = damageMultiplier;
        if (_.character.EmpVulnerability > 0 && affliction.Prefab.AfflictionType == AfflictionPrefab.EMPType)
        {
          finalDamageModifier *= _.character.EmpVulnerability;
        }
        if (!_.character.Params.Health.PoisonImmunity)
        {
          if (affliction.Prefab.AfflictionType == AfflictionPrefab.PoisonType || affliction.Prefab.AfflictionType == AfflictionPrefab.ParalysisType)
          {
            finalDamageModifier *= _.character.PoisonVulnerability;
          }
        }
        foreach (DamageModifier damageModifier in _.tempModifiers)
        {
          float damageModifierValue = damageModifier.DamageMultiplier;
          if (damageModifier.DeflectProjectiles && damageModifierValue < 1f)
          {
            damageModifierValue = MathHelper.Lerp(damageModifierValue, 1f, penetration);
          }
          finalDamageModifier *= damageModifierValue;
        }
        if (affliction.MultiplyByMaxVitality)
        {
          finalDamageModifier *= _.character.MaxVitality / 100f;
        }
        if (!MathUtils.NearlyEqual(finalDamageModifier, 1.0f))
        {
          newAffliction = affliction.CreateMultiplied(finalDamageModifier, affliction);
        }
        else
        {
          newAffliction.SetStrength(affliction.NonClampedStrength);
        }
        if (attacker != null)
        {
          var abilityAfflictionCharacter = new AbilityAfflictionCharacter(newAffliction, _.character);
          attacker.CheckTalents(AbilityEffectType.OnAddDamageAffliction, abilityAfflictionCharacter);
          newAffliction = abilityAfflictionCharacter.Affliction;
        }
        if (applyAffliction)
        {
          _.afflictionsCopy.Add(newAffliction);
          newAffliction.Source ??= attacker;
        }
        _.appliedDamageModifiers.AddRange(_.tempModifiers);
      }
      var result = new AttackResult(_.afflictionsCopy, _, _.appliedDamageModifiers);
      if (result.Afflictions.None())
      {
        playSound = false;
      }
#if CLIENT
      _.AddDamageProjSpecific(playSound, result);
#endif

      float bleedingDamage = 0;
      if (_.character.CharacterHealth.DoesBleed)
      {
        foreach (var affliction in result.Afflictions)
        {
          if (affliction is AfflictionBleeding)
          {
            bleedingDamage += affliction.GetVitalityDecrease(_.character.CharacterHealth);
          }
        }
        if (bleedingDamage > 0)
        {
          float bloodDecalSize = MathHelper.Clamp(bleedingDamage / 5, 0.1f, 1.0f);
          if (_.character.CurrentHull != null && !string.IsNullOrEmpty(_.character.BloodDecalName))
          {
            _.character.CurrentHull.AddDecal(_.character.BloodDecalName, _.WorldPosition, MathHelper.Clamp(bloodDecalSize, 0.5f, 1.0f), isNetworkEvent: false);
          }
        }
      }

      __result = result;
      return false;
    }

  }
}