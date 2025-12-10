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
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedHumanAIController()
    {
      harmony.Patch(
        original: typeof(HumanAIController).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("HumanAIController_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(HumanAIController).GetMethod("ReportProblems", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("HumanAIController_ReportProblems_Replace"))
      );

      harmony.Patch(
        original: typeof(HumanAIController).GetMethod("StructureDamaged", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("HumanAIController_StructureDamaged_Replace"))
      );
    }


    public static bool HumanAIController_StructureDamaged_Replace(Structure structure, float damageAmount, Character character)
    {
      const float MaxDamagePerSecond = 5.0f;
      const float MaxDamagePerFrame = MaxDamagePerSecond * (float)Timing.Step;

      const float WarningThreshold = 5.0f;
      const float ArrestThreshold = 20.0f;
      const float KillThreshold = 50.0f;

      if (character == null || damageAmount <= 0.0f) { return false; }
      if (structure?.Submarine == null || !structure.Submarine.Info.IsOutpost || character.TeamID == structure.Submarine.TeamID) { return false; }
      //structure not indestructible = something that's "meant" to be destroyed, like an ice wall in mines
      if (!structure.Prefab.IndestructibleInOutposts) { return false; }

      bool someoneSpoke = false;
      float maxAccumulatedDamage = 0.0f;
      foreach (Character otherCharacter in Character.CharacterList)
      {
        if (otherCharacter == character || otherCharacter.TeamID == character.TeamID || otherCharacter.IsDead ||
            otherCharacter.Info?.Job == null ||
            otherCharacter.AIController is not HumanAIController otherHumanAI ||
            Vector2.DistanceSquared(otherCharacter.WorldPosition, character.WorldPosition) > 1000.0f * 1000.0f)
        {
          continue;
        }
        if (!otherCharacter.CanSeeTarget(character, seeThroughWindows: true)) { continue; }

        otherHumanAI.structureDamageAccumulator.TryAdd(character, 0.0f);
        float prevAccumulatedDamage = otherHumanAI.structureDamageAccumulator[character];
        otherHumanAI.structureDamageAccumulator[character] += MathHelper.Clamp(damageAmount, -MaxDamagePerFrame, MaxDamagePerFrame);
        float accumulatedDamage = Math.Max(otherHumanAI.structureDamageAccumulator[character], maxAccumulatedDamage);
        maxAccumulatedDamage = Math.Max(accumulatedDamage, maxAccumulatedDamage);

        if (GameMain.GameSession?.Campaign?.Map?.CurrentLocation?.Reputation != null && character.IsPlayer)
        {
          var reputationLoss = damageAmount * Reputation.ReputationLossPerWallDamage;
          GameMain.GameSession.Campaign.Map.CurrentLocation.Reputation.AddReputation(-reputationLoss, Reputation.MaxReputationLossFromWallDamage);
        }

        if (!character.IsCriminal)
        {
          if (accumulatedDamage <= WarningThreshold) { return false; }

          if (accumulatedDamage > WarningThreshold && prevAccumulatedDamage <= WarningThreshold &&
              !someoneSpoke && !character.IsIncapacitated && character.Stun <= 0.0f)
          {
            //if the damage is still fairly low, wait and see if the character keeps damaging the walls to the point where we need to intervene
            if (accumulatedDamage < ArrestThreshold)
            {
              if (otherHumanAI.ObjectiveManager.CurrentObjective is AIObjectiveIdle idleObjective)
              {
                idleObjective.FaceTargetAndWait(character, 5.0f);
              }
            }
            otherCharacter.Speak(TextManager.Get("dialogdamagewallswarning").Value, null, Rand.Range(0.5f, 1.0f), "damageoutpostwalls".ToIdentifier(), 10.0f);
            someoneSpoke = true;
          }
        }

        // React if we are security
        if (character.IsCriminal ||
            (accumulatedDamage > ArrestThreshold && prevAccumulatedDamage <= ArrestThreshold) ||
            (accumulatedDamage > KillThreshold && prevAccumulatedDamage <= KillThreshold))
        {
          var combatMode = accumulatedDamage > KillThreshold ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Arrest;
          if (combatMode == AIObjectiveCombat.CombatMode.Offensive)
          {
            character.IsCriminal = true;
            character.IsActingOffensively = true;
          }
          if (!TriggerSecurity(otherHumanAI, combatMode))
          {
            // Else call the others
            foreach (Character security in Character.CharacterList.Where(c => c.TeamID == otherCharacter.TeamID).OrderBy(c => Vector2.DistanceSquared(character.WorldPosition, c.WorldPosition)))
            {
              if (!TriggerSecurity(security.AIController as HumanAIController, combatMode))
              {
                // Only alert one guard at a time
                return false;
              }
            }
          }
        }
      }

      bool TriggerSecurity(HumanAIController humanAI, AIObjectiveCombat.CombatMode combatMode)
      {
        if (humanAI == null) { return false; }
        if (!humanAI.Character.IsSecurity) { return false; }
        if (humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()) { return false; }
        humanAI.AddCombatObjective(combatMode, character, delay: HumanAIController.GetReactionTime(),
            onCompleted: () =>
            {
              //if the target is arrested successfully, reset the damage accumulator
              foreach (Character anyCharacter in Character.CharacterList)
              {
                if (anyCharacter.AIController is HumanAIController anyAI)
                {
                  anyAI.structureDamageAccumulator?.Remove(character);
                }
              }
            });
        return true;
      }

      return false;
    }

    public static void HumanAIController_ReportProblems_Replace(HumanAIController __instance, ref bool __runOriginal)
    {
      HumanAIController _ = __instance;
      __runOriginal = false;


      Order newOrder = null;
      Hull targetHull = null;

      // for now, escorted characters use the report system to get targets but do not speak. escort-character specific dialogue could be implemented
      bool speak = _.Character.SpeechImpediment < 100 && !_.Character.IsEscorted;
      if (_.Character.CurrentHull != null)
      {
        bool isFighting = _.ObjectiveManager.HasActiveObjective<AIObjectiveCombat>();
        bool isFleeing = _.ObjectiveManager.HasActiveObjective<AIObjectiveFindSafety>();
        foreach (var hull in _.VisibleHulls)
        {
          foreach (Character target in Character.CharacterList)
          {
            if (target.CurrentHull != hull || !target.Enabled || target.InDetectable) { continue; }
            if (AIObjectiveFightIntruders.IsValidTarget(target, _.Character, targetCharactersInOtherSubs: false))
            {
              if (HumanAIController.AddTargets<AIObjectiveFightIntruders, Character>(_.Character, target) && newOrder == null)
              {
                var orderPrefab = OrderPrefab.Prefabs["reportintruders"];
                newOrder = new Order(orderPrefab, hull, targetItem: null, orderGiver: _.Character);
                targetHull = hull;
                if (target.IsEscorted)
                {
                  if (!_.Character.IsPrisoner && target.IsPrisoner)
                  {
                    LocalizedString msg = TextManager.GetWithVariables("orderdialog.prisonerescaped", ("[roomname]", targetHull.DisplayName, FormatCapitals.No));
                    _.Character.Speak(msg.Value, ChatMessageType.Order);
                    speak = false;
                  }
                  else if (!_.IsMentallyUnstable && target.AIController.IsMentallyUnstable)
                  {
                    LocalizedString msg = TextManager.GetWithVariables("orderdialog.mentalcase", ("[roomname]", targetHull.DisplayName, FormatCapitals.No));
                    _.Character.Speak(msg.Value, ChatMessageType.Order);
                    speak = false;
                  }
                }
              }
              if (_.Character.CombatAction == null && !isFighting)
              {
                // Immediately react to enemies when they are spotted. AIObjectiveFightIntruders and AIObjectiveFindSafety would make the bot react to the threats,
                // but the reaction is delayed (and doesn't necessarily target this enemy), and in many cases the reaction would come only when the enemy attacks and triggers AIObjectiveCombat.
                _.AddCombatObjective(_.ObjectiveManager.HasObjectiveOrOrder<AIObjectiveFightIntruders>() ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Defensive, target);
              }
            }
          }
          if (AIObjectiveExtinguishFires.IsValidTarget(hull, _.Character))
          {
            if (HumanAIController.AddTargets<AIObjectiveExtinguishFires, Hull>(_.Character, hull) && newOrder == null)
            {
              var orderPrefab = OrderPrefab.Prefabs["reportfire"];
              newOrder = new Order(orderPrefab, hull, targetItem: null, orderGiver: _.Character);
              targetHull = hull;
            }
          }
          if (HumanAIController.IsBallastFloraNoticeable(_.Character, hull) && newOrder == null)
          {
            var orderPrefab = OrderPrefab.Prefabs["reportballastflora"];
            newOrder = new Order(orderPrefab, hull, targetItem: null, orderGiver: _.Character);
            targetHull = hull;
          }
          if (!isFighting)
          {
            foreach (var gap in hull.ConnectedGaps)
            {
              if (AIObjectiveFixLeaks.IsValidTarget(gap, _.Character))
              {
                if (HumanAIController.AddTargets<AIObjectiveFixLeaks, Gap>(_.Character, gap) && newOrder == null && !gap.IsRoomToRoom)
                {
                  var orderPrefab = OrderPrefab.Prefabs["reportbreach"];
                  newOrder = new Order(orderPrefab, hull, targetItem: null, orderGiver: _.Character);
                  targetHull = hull;
                }
              }
            }
            if (!isFleeing)
            {
              _.CheckForDraggedCorpses();
              foreach (Character target in Character.CharacterList)
              {
                if (target.CurrentHull != hull) { continue; }
                if (AIObjectiveRescueAll.IsValidTarget(target, _.Character, out bool ignoredAsMinorWounds))
                {
                  if (HumanAIController.AddTargets<AIObjectiveRescueAll, Character>(_.Character, target) && newOrder == null && (!_.Character.IsMedic || _.Character == target) && !_.ObjectiveManager.HasActiveObjective<AIObjectiveRescue>())
                  {
                    var orderPrefab = OrderPrefab.Prefabs["requestfirstaid"];
                    newOrder = new Order(orderPrefab, hull, targetItem: null, orderGiver: _.Character);
                    targetHull = hull;
                  }
                }
              }
              foreach (Item item in Item.RepairableItems)
              {
                if (item.CurrentHull != hull) { continue; }
                if (AIObjectiveRepairItems.IsValidTarget(item, _.Character))
                {
                  if (!item.Repairables.Any(r => r.IsBelowRepairIconThreshold)) { continue; }
                  if (HumanAIController.AddTargets<AIObjectiveRepairItems, Item>(_.Character, item) && newOrder == null && !_.ObjectiveManager.HasActiveObjective<AIObjectiveRepairItem>())
                  {
                    var orderPrefab = OrderPrefab.Prefabs["reportbrokendevices"];
                    newOrder = new Order(orderPrefab, hull, item.Repairables?.FirstOrDefault(), orderGiver: _.Character);
                    targetHull = hull;
                  }
                }
              }
            }
          }
        }
      }
      if (newOrder != null && speak)
      {
        string msg = newOrder.GetChatMessage(string.Empty, targetHull?.DisplayName?.Value ?? string.Empty, givingOrderToSelf: false);
        if (_.Character.TeamID == CharacterTeamType.FriendlyNPC)
        {
          _.Character.Speak(msg, ChatMessageType.Default, identifier: $"{newOrder.Prefab.Identifier}{targetHull?.RoomName ?? "null"}".ToIdentifier(), minDurationBetweenSimilar: 60f);
        }
        else if (_.Character.IsOnPlayerTeam && GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
        {
          _.Character.Speak(msg, messageType: ChatMessageType.Order);
#if SERVER
          GameMain.Server.SendOrderChatMessage(new OrderChatMessage(newOrder
              .WithManualPriority(CharacterInfo.HighestManualOrderPriority)
              .WithTargetEntity(targetHull)
              .WithOrderGiver(_.Character), msg, targetCharacter: null, sender: _.Character));
#endif
        }
      }
    }


    // Base call detected, you also need AIController_Update_Replace to run this
    public static bool HumanAIController_Update_Replace(float deltaTime, HumanAIController __instance)
    {
      HumanAIController _ = __instance;

      if (HumanAIController.DisableCrewAI || _.Character.Removed) { return false; }

      bool isIncapacitated = _.Character.IsIncapacitated;
      if (_.freezeAI && !isIncapacitated)
      {
        _.freezeAI = false;
      }
      if (isIncapacitated) { return false; }

      _.wasConscious = true;

      _.respondToAttackTimer -= deltaTime;
      if (_.respondToAttackTimer <= 0.0f)
      {
        foreach (var previousAttackResult in _.previousAttackResults)
        {
          _.RespondToAttack(previousAttackResult.Key, previousAttackResult.Value);
          if (_.previousHealAmounts.ContainsKey(previousAttackResult.Key))
          {
            //gradually forget past heals
            _.previousHealAmounts[previousAttackResult.Key] = Math.Min(_.previousHealAmounts[previousAttackResult.Key] - 5.0f, 100.0f);
            if (_.previousHealAmounts[previousAttackResult.Key] <= 0.0f)
            {
              _.previousHealAmounts.Remove(previousAttackResult.Key);
            }
          }
        }
        _.previousAttackResults.Clear();
        _.respondToAttackTimer = HumanAIController.RespondToAttackInterval;
      }

      //base.Update(deltaTime);
      AIController_Update_Replace(deltaTime, _);

      foreach (var values in _.knownHulls)
      {
        HumanAIController.HullSafety hullSafety = values.Value;
        hullSafety.Update(deltaTime);
      }

      if (_.unreachableClearTimer > 0)
      {
        _.unreachableClearTimer -= deltaTime;
      }
      else
      {
        _.unreachableClearTimer = HumanAIController.clearUnreachableInterval;
        _.UnreachableHulls.Clear();
        _.IgnoredItems.Clear();
      }

      // Note: returns false when useTargetSub is 'true' and the target is outside (targetSub is 'null')
      bool IsCloseEnoughToTarget(float threshold, bool targetSub = true)
      {
        Entity target = _.SelectedAiTarget?.Entity;
        if (target == null)
        {
          return false;
        }
        if (targetSub)
        {
          if (target.Submarine is Submarine sub)
          {
            target = sub;
            threshold += Math.Max(sub.Borders.Size.X, sub.Borders.Size.Y) / 2;
          }
          else
          {
            return false;
          }
        }
        return Vector2.DistanceSquared(_.Character.WorldPosition, target.WorldPosition) < MathUtils.Pow2(threshold);
      }

      bool isOutside = _.Character.Submarine == null;
      if (isOutside)
      {
        _.obstacleRaycastTimer -= deltaTime;
        if (_.obstacleRaycastTimer <= 0)
        {
          bool hasValidPath = _.HasValidPath();
          _.isBlocked = false;
          _.UseOutsideWaypoints = false;
          _.obstacleRaycastTimer = _.obstacleRaycastIntervalLong;
          ISpatialEntity spatialTarget = _.SelectedAiTarget?.Entity ?? _.ObjectiveManager.GetLastActiveObjective<AIObjectiveGoTo>()?.Target;
          if (spatialTarget != null && (spatialTarget.Submarine == null || !IsCloseEnoughToTarget(2000, targetSub: false)))
          {
            // If the target is behind a level wall, switch to the pathing to get around the obstacles.
            IEnumerable<FarseerPhysics.Dynamics.Body> ignoredBodies = null;
            Vector2 rayEnd = spatialTarget.SimPosition;
            Submarine targetSub = spatialTarget.Submarine;
            if (targetSub != null)
            {
              rayEnd += targetSub.SimPosition;
              ignoredBodies = targetSub.PhysicsBody.FarseerBody.ToEnumerable();
            }
            var obstacle = Submarine.PickBody(_.SimPosition, rayEnd, ignoredBodies, collisionCategory: Physics.CollisionLevel | Physics.CollisionWall);
            _.isBlocked = obstacle != null;
            // Don't use outside waypoints when blocked by a sub, because we should use the waypoints linked to the sub instead.
            _.UseOutsideWaypoints = _.isBlocked && (obstacle.UserData is not Submarine sub || sub.Info.IsRuin);
            bool resetPath = false;
            if (_.UseOutsideWaypoints)
            {
              bool isUsingInsideWaypoints = hasValidPath && _.HasValidPath(nodePredicate: n => n.Submarine != null || n.Ruin != null);
              if (isUsingInsideWaypoints)
              {
                resetPath = true;
              }
            }
            else
            {
              bool isUsingOutsideWaypoints = hasValidPath && _.HasValidPath(nodePredicate: n => n.Submarine == null && n.Ruin == null);
              if (isUsingOutsideWaypoints)
              {
                resetPath = true;
              }
            }
            if (resetPath)
            {
              _.PathSteering.ResetPath();
            }
          }
          else if (hasValidPath)
          {
            _.obstacleRaycastTimer = _.obstacleRaycastIntervalShort;
            // Swimming outside and using the path finder -> check that the path is not blocked with anything (the path finder doesn't know about other subs).
            if (Submarine.MainSub != null)
            {
              foreach (var connectedSub in Submarine.MainSub.GetConnectedSubs())
              {
                if (connectedSub == Submarine.MainSub) { continue; }
                Vector2 rayStart = _.SimPosition - connectedSub.SimPosition;
                Vector2 dir = _.PathSteering.CurrentPath.CurrentNode.WorldPosition - _.WorldPosition;
                Vector2 rayEnd = rayStart + dir.ClampLength(_.Character.AnimController.Collider.GetLocalFront().Length() * 5);
                if (Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true) != null)
                {
                  _.PathSteering.CurrentPath.Unreachable = true;
                  break;
                }
              }
            }
          }
        }
      }
      else
      {
        _.UseOutsideWaypoints = false;
        _.isBlocked = false;
      }

      if (isOutside || _.Character.IsOnPlayerTeam && !_.Character.IsEscorted && !_.Character.IsOnFriendlyTeam(_.Character.Submarine.TeamID))
      {
        // Spot enemies while staying outside or inside an enemy ship.
        // does not apply for escorted characters, such as prisoners or terrorists who have their own behavior
        _.enemyCheckTimer -= deltaTime;
        if (_.enemyCheckTimer < 0)
        {
          _.SpotEnemies();
          _.enemyCheckTimer = _.enemyCheckInterval * Rand.Range(0.75f, 1.25f);
        }
      }
      bool useInsideSteering = !isOutside || _.isBlocked || _.HasValidPath() || IsCloseEnoughToTarget(_.steeringBuffer);
      if (useInsideSteering)
      {
        if (_.steeringManager != _.insideSteering)
        {
          _.insideSteering.Reset();
          _.PathSteering.ResetPath();
          _.steeringManager = _.insideSteering;
        }
        if (IsCloseEnoughToTarget(_.maxSteeringBuffer))
        {
          _.steeringBuffer += _.steeringBufferIncreaseSpeed * deltaTime;
        }
        else
        {
          _.steeringBuffer = _.minSteeringBuffer;
        }
      }
      else
      {
        if (_.steeringManager != _.outsideSteering)
        {
          _.outsideSteering.Reset();
          _.steeringManager = _.outsideSteering;
        }
        _.steeringBuffer = _.minSteeringBuffer;
      }
      _.steeringBuffer = Math.Clamp(_.steeringBuffer, _.minSteeringBuffer, _.maxSteeringBuffer);

      _.AnimController.Crouching = _.shouldCrouch;
      _.CheckCrouching(deltaTime);
      _.Character.ClearInputs();

      if (_.SortTimer > 0.0f)
      {
        _.SortTimer -= deltaTime;
      }
      else
      {
        _.objectiveManager.SortObjectives();
        _.SortTimer = HumanAIController.sortObjectiveInterval;
      }
      _.objectiveManager.UpdateObjectives(deltaTime);

      _.UpdateDragged(deltaTime);

      if (_.reportProblemsTimer > 0)
      {
        _.reportProblemsTimer -= deltaTime;
      }
      if (_.reactTimer > 0.0f)
      {
        _.reactTimer -= deltaTime;
        if (_.findItemState != HumanAIController.FindItemState.None)
        {
          // Update every frame only when seeking items
          _.UnequipUnnecessaryItems();
        }
      }
      else
      {
        _.Character.UpdateTeam();
        if (_.Character.CurrentHull != null)
        {
          if (_.Character.IsOnPlayerTeam)
          {
            foreach (Hull h in _.VisibleHulls)
            {
              HumanAIController.PropagateHullSafety(_.Character, h);
              _.dirtyHullSafetyCalculations.Remove(h);
            }
          }
          else
          {
            foreach (Hull h in _.VisibleHulls)
            {
              _.RefreshHullSafety(h);
              _.dirtyHullSafetyCalculations.Remove(h);
            }
          }
          foreach (Hull h in _.dirtyHullSafetyCalculations)
          {
            _.RefreshHullSafety(h);
          }
        }
        _.dirtyHullSafetyCalculations.Clear();
        if (_.reportProblemsTimer <= 0.0f)
        {
          if (_.Character.Submarine != null && (_.Character.Submarine.TeamID == _.Character.TeamID || _.Character.Submarine.TeamID == _.Character.OriginalTeamID || _.Character.IsEscorted) && !_.Character.Submarine.Info.IsWreck)
          {
            _.ReportProblems();

          }
          else
          {
            bool ignoredAsMinorWounds;
            // Allows bots to heal targets autonomously while swimming outside of the sub.
            if (AIObjectiveRescueAll.IsValidTarget(_.Character, _.Character, out ignoredAsMinorWounds))
            {
              HumanAIController.AddTargets<AIObjectiveRescueAll, Character>(_.Character, _.Character);
            }
          }
          _.reportProblemsTimer = _.reportProblemsInterval;
        }
        _.SpeakAboutIssues();
        _.UnequipUnnecessaryItems();
        _.reactTimer = HumanAIController.GetReactionTime();
      }

      if (_.objectiveManager.CurrentObjective == null) { return false; }

      _.objectiveManager.DoCurrentObjective(deltaTime);
      var currentObjective = _.objectiveManager.CurrentObjective;
      bool run = !currentObjective.ForceWalk && (currentObjective.ForceRun || _.objectiveManager.GetCurrentPriority() > AIObjectiveManager.RunPriority);
      if (currentObjective is AIObjectiveGoTo goTo)
      {
        run = goTo.ShouldRun(run);
      }

      //if someone is grabbing the bot and the bot isn't trying to run anywhere, let them keep dragging and "control" the bot
      if (_.Character.SelectedBy == null || run)
      {
        _.steeringManager.Update(_.Character.AnimController.GetCurrentSpeed(run && _.Character.CanRun));
      }

      bool ignorePlatforms = _.Character.AnimController.TargetMovement.Y < -0.5f && (-_.Character.AnimController.TargetMovement.Y > Math.Abs(_.Character.AnimController.TargetMovement.X));
      if (_.steeringManager == _.insideSteering)
      {
        var currPath = _.PathSteering.CurrentPath;
        if (currPath != null && currPath.CurrentNode != null)
        {
          if (currPath.CurrentNode.SimPosition.Y < _.Character.AnimController.GetColliderBottom().Y)
          {
            // Don't allow to jump from too high.
            float allowedJumpHeight = _.Character.AnimController.ImpactTolerance / 2;
            float height = Math.Abs(currPath.CurrentNode.SimPosition.Y - _.Character.SimPosition.Y);
            ignorePlatforms = height < allowedJumpHeight;
          }
        }
        if (_.Character.IsClimbing && _.PathSteering.IsNextLadderSameAsCurrent)
        {
          _.Character.AnimController.TargetMovement = new Vector2(0.0f, Math.Sign(_.Character.AnimController.TargetMovement.Y));
        }
      }
      _.Character.AnimController.IgnorePlatforms = ignorePlatforms;

      Vector2 targetMovement = _.AnimController.TargetMovement;
      if (!_.Character.AnimController.InWater)
      {
        targetMovement = new Vector2(_.Character.AnimController.TargetMovement.X, MathHelper.Clamp(_.Character.AnimController.TargetMovement.Y, -1.0f, 1.0f));
      }
      _.Character.AnimController.TargetMovement = _.Character.ApplyMovementLimits(targetMovement, _.AnimController.GetCurrentSpeed(run));

      _.flipTimer -= deltaTime;
      if (_.flipTimer <= 0.0f)
      {
        Direction newDir = _.Character.AnimController.TargetDir;
        if (_.Character.IsKeyDown(InputType.Aim))
        {
          var cursorDiffX = _.Character.CursorPosition.X - _.Character.Position.X;
          if (cursorDiffX > 10.0f)
          {
            newDir = Direction.Right;
          }
          else if (cursorDiffX < -10.0f)
          {
            newDir = Direction.Left;
          }
          if (_.Character.SelectedItem != null)
          {
            _.Character.SelectedItem.SecondaryUse(deltaTime, _.Character);
          }
        }
        else if (_.AutoFaceMovement && Math.Abs(_.Character.AnimController.TargetMovement.X) > 0.1f && !_.Character.AnimController.InWater)
        {
          newDir = _.Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
        }
        if (newDir != _.Character.AnimController.TargetDir)
        {
          _.Character.AnimController.TargetDir = newDir;
          _.flipTimer = HumanAIController.FlipInterval;
        }
      }
      _.AutoFaceMovement = true;

      _.MentalStateManager?.Update(deltaTime);
      _.ShipCommandManager?.Update(deltaTime);

      return false;
    }



  }
}