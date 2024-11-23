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
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Items.Components;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedLevelTrigger()
    {
      harmony.Patch(
        original: typeof(LevelTrigger).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelTrigger_Update_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Levels/LevelObjects/LevelTrigger.cs#L545
    public static bool LevelTrigger_Update_Replace(float deltaTime, LevelTrigger __instance)
    {
      LevelTrigger _ = __instance;

      if (_.ParentTrigger != null && !_.ParentTrigger.IsTriggered) { return false; }


      bool isNotClient = true;
#if CLIENT
      isNotClient = GameMain.Client == null;
#endif

      if (!_.UseNetworkSyncing || isNotClient)
      {
        if (_.GlobalForceDecreaseInterval > 0.0f && Level.Loaded?.LevelObjectManager != null &&
            Level.Loaded.LevelObjectManager.GlobalForceDecreaseTimer % (_.GlobalForceDecreaseInterval * 2) < _.GlobalForceDecreaseInterval)
        {
          _.NeedsNetworkSyncing |= _.currentForceFluctuation > 0.0f;
          _.currentForceFluctuation = 0.0f;
        }
        else if (_.ForceFluctuationStrength > 0.0f)
        {
          //no need for force fluctuation (or network updates) if the trigger limits velocity and there are no triggerers
          if (_.forceMode != LevelTrigger.TriggerForceMode.LimitVelocity || _.triggerers.Any())
          {
            _.forceFluctuationTimer += deltaTime;
            if (_.forceFluctuationTimer > _.ForceFluctuationInterval)
            {
              _.NeedsNetworkSyncing = true;
              _.currentForceFluctuation = Rand.Range(1.0f - _.ForceFluctuationStrength, 1.0f);
              _.forceFluctuationTimer = 0.0f;
            }
          }
        }

        if (_.randomTriggerProbability > 0.0f)
        {
          _.randomTriggerTimer += deltaTime;
          if (_.randomTriggerTimer > _.randomTriggerInterval)
          {
            if (Rand.Range(0.0f, 1.0f) < _.randomTriggerProbability)
            {
              _.NeedsNetworkSyncing = true;
              _.triggeredTimer = _.stayTriggeredDelay;
            }
            _.randomTriggerTimer = 0.0f;
          }
        }
      }

      LevelTrigger.RemoveInActiveTriggerers(_.PhysicsBody, _.triggerers);

      if (_.stayTriggeredDelay > 0.0f)
      {
        if (_.triggerers.Count == 0)
        {
          _.triggeredTimer -= deltaTime;
        }
        else
        {
          _.triggeredTimer = _.stayTriggeredDelay;
        }
      }

      if (_.triggerOnce && _.triggeredOnce)
      {
        return false;
      }

      foreach (Entity triggerer in _.triggerers)
      {
        if (triggerer.Removed) { continue; }

        LevelTrigger.ApplyStatusEffects(_.statusEffects, _.worldPosition, triggerer, deltaTime, _.targets);

        if (triggerer is IDamageable damageable)
        {
          LevelTrigger.ApplyAttacks(_.attacks, damageable, _.worldPosition, deltaTime);
        }
        else if (triggerer is Submarine submarine)
        {
          LevelTrigger.ApplyAttacks(_.attacks, _.worldPosition, deltaTime);
          if (!_.InfectIdentifier.IsEmpty)
          {
            submarine.AttemptBallastFloraInfection(_.InfectIdentifier, deltaTime, _.InfectionChance);
          }
        }

        if (_.Force.LengthSquared() > 0.01f)
        {
          if (triggerer is Character character)
          {
            _.ApplyForce(character.AnimController.Collider);
            foreach (Limb limb in character.AnimController.Limbs)
            {
              if (limb.IsSevered) { continue; }
              _.ApplyForce(limb.body);
            }
          }
          else if (triggerer is Submarine submarine)
          {
            _.ApplyForce(submarine.SubBody.Body);
          }
        }

        if (triggerer == Character.Controlled || triggerer == Character.Controlled?.Submarine)
        {
          GameMain.GameScreen.Cam.Shake = Math.Max(GameMain.GameScreen.Cam.Shake, _.cameraShake);
        }
      }

      if (_.triggerOnce && _.triggerers.Count > 0)
      {
        _.PhysicsBody.Enabled = false;
        _.triggeredOnce = true;
      }

      return false;
    }
  }
}