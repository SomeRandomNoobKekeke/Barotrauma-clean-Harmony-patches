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
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
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
    public static void PatchSharedStatusEffects()
    {
      harmony.Patch(
        original: typeof(StatusEffect).GetMethod("UpdateAll", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("StatusEffect_UpdateAll_Replace"))
      );
    }

    public static bool StatusEffect_UpdateAll_Replace(float deltaTime)
    {
#if CLIENT
      StatusEffect.UpdateAllProjSpecific(deltaTime);
#endif


      DelayedEffect.Update(deltaTime);
      for (int i = StatusEffect.DurationList.Count - 1; i >= 0; i--)
      {
        DurationListElement element = StatusEffect.DurationList[i];

        if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
        {
          StatusEffect.DurationList.RemoveAt(i);
          continue;
        }

        element.Targets.RemoveAll(t =>
            (t is Entity entity && entity.Removed) ||
            (t is Limb limb && (limb.character == null || limb.character.Removed)));
        if (element.Targets.Count == 0)
        {
          StatusEffect.DurationList.RemoveAt(i);
          continue;
        }

        foreach (ISerializableEntity target in element.Targets)
        {
          if (target?.SerializableProperties != null)
          {
            foreach (var (propertyName, value) in element.Parent.PropertyEffects)
            {
              if (!target.SerializableProperties.TryGetValue(propertyName, out SerializableProperty property))
              {
                continue;
              }
              element.Parent.ApplyToProperty(target, property, value, CoroutineManager.DeltaTime);
            }
          }

          foreach (Affliction affliction in element.Parent.Afflictions)
          {
            Affliction newAffliction = affliction;
            if (target is Character character)
            {
              if (character.Removed) { continue; }
              newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
              var result = character.AddDamage(character.WorldPosition, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attacker: element.User);
              element.Parent.RegisterTreatmentResults(element.Parent.user, element.Entity as Item, result.HitLimb, affliction, result);
            }
            else if (target is Limb limb)
            {
              if (limb.character.Removed || limb.Removed) { continue; }
              newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, limb.character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
              var result = limb.character.DamageLimb(limb.WorldPosition, limb, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attackImpulse: Vector2.Zero, attacker: element.User);
              element.Parent.RegisterTreatmentResults(element.Parent.user, element.Entity as Item, limb, affliction, result);
            }
          }

          foreach ((Identifier affliction, float amount) in element.Parent.ReduceAffliction)
          {
            Limb targetLimb = null;
            Character targetCharacter = null;
            if (target is Character character)
            {
              targetCharacter = character;
            }
            else if (target is Limb limb)
            {
              targetLimb = limb;
              targetCharacter = limb.character;
            }
            if (targetCharacter != null && !targetCharacter.Removed)
            {
              ActionType? actionType = null;
              if (element.Entity is Item item && item.UseInHealthInterface) { actionType = element.Parent.type; }
              float reduceAmount = amount * element.Parent.GetAfflictionMultiplier(element.Entity, targetCharacter, deltaTime);
              float prevVitality = targetCharacter.Vitality;
              if (targetLimb != null)
              {
                targetCharacter.CharacterHealth.ReduceAfflictionOnLimb(targetLimb, affliction, reduceAmount, treatmentAction: actionType, attacker: element.User);
              }
              else
              {
                targetCharacter.CharacterHealth.ReduceAfflictionOnAllLimbs(affliction, reduceAmount, treatmentAction: actionType, attacker: element.User);
              }
              if (!targetCharacter.IsDead)
              {
                float healthChange = targetCharacter.Vitality - prevVitality;
                targetCharacter.AIController?.OnHealed(healer: element.User, healthChange);
                if (element.User != null)
                {
                  if (element.Parent.CanGiveMedicalSkill)
                  {
                    targetCharacter.TryAdjustHealerSkill(element.User, healthChange);
                  }
#if SERVER
                  GameMain.Server.KarmaManager.OnCharacterHealthChanged(targetCharacter, element.User, -healthChange, 0.0f);
#endif
                }
              }
            }
          }

          element.Parent.TryTriggerAnimation(target, element.Entity);
        }

#if CLIENT
        element.Parent.ApplyProjSpecific(deltaTime,
            element.Entity,
            element.Targets,
            element.Parent.GetHull(element.Entity),
            element.Parent.GetPosition(element.Entity, element.Targets),
            playSound: element.Timer >= element.Duration);
#endif

        element.Timer -= deltaTime;

        if (element.Timer > 0.0f) { continue; }
        StatusEffect.DurationList.Remove(element);
      }

      return false;
    }
  }
}