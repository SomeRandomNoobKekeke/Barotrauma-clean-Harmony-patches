using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedAttack()
    {
      harmony.Patch(
        original: typeof(Attack).GetMethod("DoDamageToLimb", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Attack_DoDamageToLimb_Replace"))
      );
    }

    public static bool Attack_DoDamageToLimb_Replace(Attack __instance, ref AttackResult __result, Character attacker, Limb targetLimb, Vector2 worldPosition, float deltaTime, bool playSound = true, PhysicsBody sourceBody = null, Limb sourceLimb = null)
    {
      Attack _ = __instance;


      if (targetLimb == null)
      {
        __result = new AttackResult();
        return false;
      }

      if (_.OnlyHumans)
      {
        if (targetLimb.character != null && !targetLimb.character.IsHuman)
        {
          __result = new AttackResult();
          return false;
        }
      }

      _.SetUser(attacker);

#if CLIENT
      _.DamageParticles(deltaTime, worldPosition);
#endif

      float penetration = _.Penetration;

      RangedWeapon weapon =
          _.SourceItem?.GetComponent<RangedWeapon>() ??
          _.SourceItem?.GetComponent<Projectile>()?.Launcher?.GetComponent<RangedWeapon>();
      float? penetrationValue = weapon?.Penetration;
      if (penetrationValue.HasValue)
      {
        penetration += penetrationValue.Value;
      }

      Vector2 impulseDirection = _.GetImpulseDirection(targetLimb, worldPosition, _.SourceItem);
      var attackResult = targetLimb.character.ApplyAttack(attacker, worldPosition, _, deltaTime, impulseDirection, playSound, targetLimb, penetration);
      var conditionalEffectType = attackResult.Damage > 0.0f ? ActionType.OnSuccess : ActionType.OnFailure;

      foreach (StatusEffect effect in _.statusEffects)
      {
        effect.sourceBody = sourceBody;
        if (effect.HasTargetType(StatusEffect.TargetType.This) || effect.HasTargetType(StatusEffect.TargetType.Character))
        {
          effect.Apply(conditionalEffectType, deltaTime, attacker, sourceLimb ?? attacker as ISerializableEntity);
          effect.Apply(ActionType.OnUse, deltaTime, attacker, sourceLimb ?? attacker as ISerializableEntity);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.Parent))
        {
          effect.Apply(conditionalEffectType, deltaTime, attacker, attacker);
          effect.Apply(ActionType.OnUse, deltaTime, attacker, attacker);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.UseTarget))
        {
          effect.Apply(conditionalEffectType, deltaTime, targetLimb.character, targetLimb.character);
          effect.Apply(ActionType.OnUse, deltaTime, targetLimb.character, targetLimb.character);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.Limb))
        {
          effect.Apply(conditionalEffectType, deltaTime, targetLimb.character, targetLimb);
          effect.Apply(ActionType.OnUse, deltaTime, targetLimb.character, targetLimb);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
        {
          var targets = targetLimb.character.AnimController.Limbs;
          effect.Apply(conditionalEffectType, deltaTime, targetLimb.character, targets);
          effect.Apply(ActionType.OnUse, deltaTime, targetLimb.character, targets);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
            effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
        {
          _.targets.Clear();
          effect.AddNearbyTargets(worldPosition, _.targets);
          effect.Apply(conditionalEffectType, deltaTime, targetLimb.character, _.targets);
          effect.Apply(ActionType.OnUse, deltaTime, targetLimb.character, _.targets);
        }
        if (effect.HasTargetType(StatusEffect.TargetType.Contained))
        {
          _.targets.Clear();
          _.targets.AddRange(attacker.Inventory.AllItems);
          effect.Apply(conditionalEffectType, deltaTime, attacker, _.targets);
          effect.Apply(ActionType.OnUse, deltaTime, attacker, _.targets);
        }
      }

      __result = attackResult;
      return false;
    }

  }
}