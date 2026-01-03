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

      harmony.Patch(
        original: typeof(Limb).GetMethod("ApplyStatusEffects", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Limb_ApplyStatusEffects_Replace"))
      );
    }


    public static bool Limb_ApplyStatusEffects_Replace(Limb __instance, ActionType actionType, float deltaTime)
    {
      Limb _ = __instance;

      if (!_.statusEffects.TryGetValue(actionType, out var statusEffectList)) { return false; }
      foreach (StatusEffect statusEffect in statusEffectList)
      {
        if (statusEffect.ShouldWaitForInterval(_.character, deltaTime)) { return false; }

        statusEffect.sourceBody = _.body;
        if (statusEffect.type == ActionType.OnDamaged)
        {
          if (!statusEffect.HasRequiredAfflictions(_.character.LastDamage)) { continue; }
          if (statusEffect.OnlyWhenDamagedByPlayer)
          {
            if (_.character.LastAttacker == null || !_.character.LastAttacker.IsPlayer)
            {
              continue;
            }
          }
        }
        if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
            statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
        {
          _.targets.Clear();
          statusEffect.AddNearbyTargets(_.WorldPosition, _.targets);
          statusEffect.Apply(actionType, deltaTime, _.character, _.targets);
        }
        else if (statusEffect.targetLimbs != null)
        {
          foreach (var limbType in statusEffect.targetLimbs)
          {
            if (statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
            {
              // Target all matching limbs
              foreach (var limb in _.ragdoll.Limbs)
              {
                if (limb.IsSevered) { continue; }
                if (limb.type == limbType)
                {
                  ApplyToLimb(actionType, deltaTime, statusEffect, _.character, limb);
                }
              }
            }
            else if (statusEffect.HasTargetType(StatusEffect.TargetType.Limb) || statusEffect.HasTargetType(StatusEffect.TargetType.Character) || statusEffect.HasTargetType(StatusEffect.TargetType.This))
            {
              // Target just the first matching limb
              Limb limb = _.ragdoll.GetLimb(limbType);
              if (limb != null)
              {
                ApplyToLimb(actionType, deltaTime, statusEffect, _.character, limb);
              }
            }
            else if (statusEffect.HasTargetType(StatusEffect.TargetType.LastLimb))
            {
              // Target just the last matching limb
              Limb limb = _.ragdoll.Limbs.LastOrDefault(l => l.type == limbType && !l.IsSevered && !l.Hidden);
              if (limb != null)
              {
                ApplyToLimb(actionType, deltaTime, statusEffect, _.character, limb);
              }
            }
          }
        }
        else if (statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
        {
          // Target all limbs
          foreach (var limb in _.ragdoll.Limbs)
          {
            if (limb.IsSevered) { continue; }
            ApplyToLimb(actionType, deltaTime, statusEffect, _.character, limb);
          }
        }
        else if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
        {
          statusEffect.Apply(actionType, deltaTime, _.character, _.character, _.WorldPosition);
        }
        else if (statusEffect.HasTargetType(StatusEffect.TargetType.This) || statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
        {
          ApplyToLimb(actionType, deltaTime, statusEffect, _.character, limb: _);
        }
      }
      static void ApplyToLimb(ActionType actionType, float deltaTime, StatusEffect statusEffect, Character character, Limb limb)
      {
        statusEffect.sourceBody = limb.body;
        statusEffect.Apply(actionType, deltaTime, entity: character, target: limb);
      }

      return false;
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
        float finalDamageModifier = affliction.AffectedByAttackMultipliers ? damageMultiplier : 1.0f;
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