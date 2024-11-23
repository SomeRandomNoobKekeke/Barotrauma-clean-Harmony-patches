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
    }

    // Base call detected, you also need AIController_Update_Replace to run this
    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/AI/HumanAIController.cs#L193
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
          _.CheckEnemies();
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
        if (run && goTo == _.objectiveManager.ForcedOrder && goTo.IsWaitOrder && !_.Character.IsOnPlayerTeam)
        {
          // NPCs with a wait order don't run.
          run = false;
        }
        else if (goTo.Target != null)
        {
          if (_.Character.CurrentHull == null)
          {
            run = Vector2.DistanceSquared(_.Character.WorldPosition, goTo.Target.WorldPosition) > 300 * 300;
          }
          else
          {
            float yDiff = goTo.Target.WorldPosition.Y - _.Character.WorldPosition.Y;
            if (Math.Abs(yDiff) > 100)
            {
              run = true;
            }
            else
            {
              float xDiff = goTo.Target.WorldPosition.X - _.Character.WorldPosition.X;
              run = Math.Abs(xDiff) > 500;
            }
          }
        }
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