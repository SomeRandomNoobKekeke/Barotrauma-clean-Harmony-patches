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
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedEnemyAIController()
    {
      harmony.Patch(
        original: typeof(EnemyAIController).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("EnemyAIController_Update_Replace"))
      );
    }

    // Base call detected, you also need AIController_Update_Replace to run this
    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/AI/EnemyAIController.cs#L530
    public static bool EnemyAIController_Update_Replace(float deltaTime, EnemyAIController __instance)
    {
      EnemyAIController _ = __instance;

      if (EnemyAIController.DisableEnemyAI) { return false; }
      //base.Update(deltaTime);
      AIController_Update_Replace(deltaTime, _);
      _.UpdateTriggers(deltaTime);
      _.Character.ClearInputs();
      _.IsTryingToSteerThroughGap = false;
      _.Reverse = false;

      //doesn't do anything usually, but events may sometimes change monsters' (or pets' that use enemy AI) teams
      _.Character.UpdateTeam();

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

      if (Math.Abs(_.Character.AnimController.movement.X) > 0.1f && !_.Character.AnimController.InWater &&
          (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer || Character.Controlled == _.Character))
      {
        if (_.SelectedAiTarget?.Entity != null || _.EscapeTarget != null)
        {
          Entity t = _.SelectedAiTarget?.Entity ?? _.EscapeTarget;
          float referencePos = Vector2.DistanceSquared(_.Character.WorldPosition, t.WorldPosition) > 100 * 100 && _.HasValidPath() ? _.PathSteering.CurrentPath.CurrentNode.WorldPosition.X : t.WorldPosition.X;
          _.Character.AnimController.TargetDir = _.Character.WorldPosition.X < referencePos ? Direction.Right : Direction.Left;
        }
        else
        {
          _.Character.AnimController.TargetDir = _.Character.AnimController.movement.X > 0.0f ? Direction.Right : Direction.Left;
        }
      }
      if (_.isStateChanged)
      {
        if (_.State == AIState.Idle || _.State == AIState.Patrol)
        {
          _.stateResetTimer -= deltaTime;
          if (_.stateResetTimer <= 0)
          {
            _.ResetOriginalState();
          }
        }
      }
      if (_.targetIgnoreTimer > 0)
      {
        _.targetIgnoreTimer -= deltaTime;
      }
      else
      {
        _.ignoredTargets.Clear();
        _.targetIgnoreTimer = _.targetIgnoreTime;
      }
      _.avoidTimer -= deltaTime;
      if (_.avoidTimer < 0)
      {
        _.avoidTimer = 0;
      }
      _.UpdateCurrentMemoryLocation();
      if (_.updateMemoriesTimer > 0)
      {
        _.updateMemoriesTimer -= deltaTime;
      }
      else
      {
        _.FadeMemories(_.updateMemoriesInverval);
        _.updateMemoriesTimer = _.updateMemoriesInverval;
      }
      if (Math.Max(_.Character.HealthPercentage, 0) < _.FleeHealthThreshold && _.SelectedAiTarget != null)
      {
        Character target = _.SelectedAiTarget.Entity as Character;
        if (target == null && _.SelectedAiTarget.Entity is Item targetItem)
        {
          target = EnemyAIController.GetOwner(targetItem);
        }
        bool shouldFlee = false;
        if (target != null)
        {
          // Keep fleeing if being chased or if we see a human target (that don't have enemy ai).
          shouldFlee = target.IsHuman && _.CanPerceive(_.SelectedAiTarget) || _.IsBeingChasedBy(target);
        }
        // If we should not flee, just idle. Don't allow any other AI state when below the health threshold.
        _.State = shouldFlee ? AIState.Flee : AIState.Idle;
        _.wallTarget = null;
        if (_.State != AIState.Flee)
        {
          _.SelectedAiTarget = null;
          _._lastAiTarget = null;
        }
      }
      else
      {
        if (EnemyAIController.TargetingRestrictions != _.previousTargetingRestrictions)
        {
          _.previousTargetingRestrictions = EnemyAIController.TargetingRestrictions;
          // update targeting instantly when there's a change in targeting restrictions
          _.updateTargetsTimer = 0;
          _.SelectedAiTarget = null;
        }

        if (_.updateTargetsTimer > 0)
        {
          _.updateTargetsTimer -= deltaTime;
        }
        else if (_.avoidTimer <= 0 || _.activeTriggers.Any() && _.returnTimer <= 0)
        {
          _.UpdateTargets();
        }
      }

      if (_.Character.Params.UsePathFinding && _.AIParams.UsePathFindingToGetInside && _.AIParams.CanOpenDoors)
      {
        // Meant for monsters outside the player sub that target something inside the sub and can use the doors to access the sub (Husk).
        bool IsCloseEnoughToTargetSub(float threshold) => _.SelectedAiTarget?.Entity?.Submarine is Submarine sub && sub != null && Vector2.DistanceSquared(_.Character.WorldPosition, sub.WorldPosition) < MathUtils.Pow(Math.Max(sub.Borders.Size.X, sub.Borders.Size.Y) / 2 + threshold, 2);

        if (_.Character.Submarine != null || _.HasValidPath() && IsCloseEnoughToTargetSub(_.maxSteeringBuffer) || IsCloseEnoughToTargetSub(_.steeringBuffer))
        {
          if (_.steeringManager != _.insideSteering)
          {
            _.insideSteering.Reset();
          }
          _.steeringManager = _.insideSteering;
          _.steeringBuffer += _.steeringBufferIncreaseSpeed * deltaTime;
        }
        else
        {
          if (_.steeringManager != _.outsideSteering)
          {
            _.outsideSteering.Reset();
          }
          _.steeringManager = _.outsideSteering;
          _.steeringBuffer = _.minSteeringBuffer;
        }
        _.steeringBuffer = Math.Clamp(_.steeringBuffer, _.minSteeringBuffer, _.maxSteeringBuffer);
      }
      else
      {
        // Normally the monsters only use pathing inside submarines, not outside.
        if (_.Character.Submarine != null && _.Character.Params.UsePathFinding)
        {
          if (_.steeringManager != _.insideSteering)
          {
            _.insideSteering.Reset();
          }
          _.steeringManager = _.insideSteering;
        }
        else
        {
          if (_.steeringManager != _.outsideSteering)
          {
            _.outsideSteering.Reset();
          }
          _.steeringManager = _.outsideSteering;
        }
      }

      bool useSteeringLengthAsMovementSpeed = _.State == AIState.Idle && _.Character.AnimController.InWater;
      bool run = false;
      switch (_.State)
      {
        case AIState.Freeze:
          _.SteeringManager.Reset();
          break;
        case AIState.Idle:
          _.UpdateIdle(deltaTime);
          break;
        case AIState.PlayDead:
          _.Character.IsRagdolled = true;
          break;
        case AIState.Patrol:
          _.UpdatePatrol(deltaTime);
          break;
        case AIState.Attack:
          run = !_.IsCoolDownRunning || _.AttackLimb != null && _.AttackLimb.attack.FullSpeedAfterAttack;
          _.UpdateAttack(deltaTime);
          break;
        case AIState.Eat:
          _.UpdateEating(deltaTime);
          break;
        case AIState.Escape:
        case AIState.Flee:
          run = true;
          _.Escape(deltaTime);
          break;
        case AIState.Avoid:
        case AIState.PassiveAggressive:
        case AIState.Aggressive:
          if (_.SelectedAiTarget?.Entity == null || _.SelectedAiTarget.Entity.Removed)
          {
            _.State = AIState.Idle;
            return false;
          }
          float squaredDistance = Vector2.DistanceSquared(_.WorldPosition, _.SelectedAiTarget.WorldPosition);
          var attackLimb = _.AttackLimb ?? _.GetAttackLimb(_.SelectedAiTarget.WorldPosition);
          if (attackLimb != null && squaredDistance <= Math.Pow(attackLimb.attack.Range, 2))
          {
            run = true;
            if (_.State == AIState.Avoid)
            {
              _.Escape(deltaTime);
            }
            else
            {
              _.UpdateAttack(deltaTime);
            }
          }
          else
          {
            bool isBeingChased = _.IsBeingChased;
            float reactDistance = !isBeingChased && _.currentTargetingParams is { ReactDistance: > 0 } ? _.currentTargetingParams.ReactDistance : _.GetPerceivingRange(_.SelectedAiTarget);
            if (squaredDistance <= Math.Pow(reactDistance, 2))
            {
              float halfReactDistance = reactDistance / 2;
              float attackDistance = _.currentTargetingParams is { AttackDistance: > 0 } ? _.currentTargetingParams.AttackDistance : halfReactDistance;
              if (_.State == AIState.Aggressive || _.State == AIState.PassiveAggressive && squaredDistance < Math.Pow(attackDistance, 2))
              {
                run = true;
                _.UpdateAttack(deltaTime);
              }
              else
              {
                run = isBeingChased || squaredDistance < Math.Pow(halfReactDistance, 2);
                _.State = AIState.Escape;
                _.avoidTimer = _.AIParams.AvoidTime * 0.5f * Rand.Range(0.75f, 1.25f);
              }
            }
            else
            {
              _.UpdateIdle(deltaTime);
            }
          }
          break;
        case AIState.Protect:
        case AIState.Follow:
        case AIState.FleeTo:
        case AIState.HideTo:
        case AIState.Hiding:
          if (_.SelectedAiTarget?.Entity == null || _.SelectedAiTarget.Entity.Removed)
          {
            _.State = AIState.Idle;
            return false;
          }
          if (_.State == AIState.Protect)
          {
            if (_.SelectedAiTarget.Entity is Character targetCharacter)
            {
              bool ShouldRetaliate(Character.Attacker a)
              {
                Character c = a.Character;
                if (c == null || c.IsUnconscious || c.Removed) { return false; }
                // Can't target characters of same species/group because that would make us hostile to all friendly characters in the same species/group.
                if (_.Character.IsSameSpeciesOrGroup(c)) { return false; }
                if (targetCharacter.IsSameSpeciesOrGroup(c)) { return false; }
                //don't try to attack targets in a sub that belongs to a different team
                //(for example, targets in an outpost if we're in the main sub)
                if (c.Submarine?.TeamID != _.Character.Submarine?.TeamID) { return false; }
                if (c.IsPlayer || _.Character.IsOnFriendlyTeam(c))
                {
                  return a.Damage >= _.currentTargetingParams.Threshold;
                }
                return true;
              }
              Character attacker = targetCharacter.LastAttackers.LastOrDefault(ShouldRetaliate)?.Character;
              if (attacker?.AiTarget != null)
              {
                _.ChangeTargetState(attacker, AIState.Attack, _.currentTargetingParams.Priority * 2);
                _.SelectTarget(attacker.AiTarget);
                _.State = AIState.Attack;
                _.UpdateWallTarget(_.requiredHoleCount);
                return false;
              }
            }
          }
          float distX = Math.Abs(_.WorldPosition.X - _.SelectedAiTarget.WorldPosition.X);
          float distY = Math.Abs(_.WorldPosition.Y - _.SelectedAiTarget.WorldPosition.Y);
          if (_.Character.Submarine != null && distY > 50 && _.SelectedAiTarget.Entity is Character targetC && !_.VisibleHulls.Contains(targetC.CurrentHull))
          {
            // Target not visible, and possibly on a different floor.
            distY *= 3;
          }
          float dist = distX + distY;
          float reactDist = _.GetPerceivingRange(_.SelectedAiTarget);
          Vector2 offset = Vector2.Zero;
          if (_.currentTargetingParams != null)
          {
            if (_.currentTargetingParams.ReactDistance > 0)
            {
              reactDist = _.currentTargetingParams.ReactDistance;
            }
            offset = _.currentTargetingParams.Offset;
          }
          if (offset != Vector2.Zero)
          {
            reactDist += offset.Length();
          }
          if (dist > reactDist + _.movementMargin)
          {
            _.movementMargin = _.State is AIState.FleeTo or AIState.HideTo or AIState.Hiding ? 0 : reactDist;
            if (_.State == AIState.Hiding)
            {
              // Too far to hide.
              _.State = AIState.HideTo;
            }
            run = true;
            _.UpdateFollow(deltaTime);
          }
          else
          {
            if (_.State == AIState.HideTo)
            {
              // Close enough to hide.
              _.State = AIState.Hiding;
            }
            _.movementMargin = MathHelper.Clamp(_.movementMargin -= deltaTime, 0, reactDist);
            if (_.State is AIState.FleeTo or AIState.Hiding)
            {
              _.SteeringManager.Reset();
              _.Character.AnimController.TargetMovement = Vector2.Zero;
              if (_.Character.AnimController.InWater)
              {
                float force = _.Character.AnimController.Collider.Mass / 10;
                _.Character.AnimController.Collider.MoveToPos(_.SelectedAiTarget.Entity.SimPosition + ConvertUnits.ToSimUnits(offset), force);
                if (_.SelectedAiTarget.Entity is Item item)
                {
                  float rotation = item.Rotation;
                  _.Character.AnimController.Collider.SmoothRotate(rotation, _.Character.AnimController.SwimFastParams.SteerTorque);
                  var mainLimb = _.Character.AnimController.MainLimb;
                  if (mainLimb.type == LimbType.Head)
                  {
                    mainLimb.body.SmoothRotate(rotation, _.Character.AnimController.SwimFastParams.HeadTorque);
                  }
                  else
                  {
                    mainLimb.body.SmoothRotate(rotation, _.Character.AnimController.SwimFastParams.TorsoTorque);
                  }
                }
                if (_.disableTailCoroutine == null && _.SelectedAiTarget.Entity is Item i && i.HasTag(Tags.GuardianShelter))
                {
                  if (!CoroutineManager.IsCoroutineRunning(_.disableTailCoroutine))
                  {
                    _.disableTailCoroutine = CoroutineManager.Invoke(() =>
                    {
                      if (_.Character is { Removed: false })
                      {
                        _.Character.AnimController.HideAndDisable(LimbType.Tail, ignoreCollisions: false);
                      }
                    }, 1f);
                  }
                }
                _.Character.AnimController.ApplyPose(
                    new Vector2(0, -1),
                    new Vector2(0, -1),
                    new Vector2(0, -1),
                    new Vector2(0, -1), footMoveForce: 1);
              }
            }
            else
            {
              _.UpdateIdle(deltaTime);
            }
          }
          break;
        case AIState.Observe:
          if (_.SelectedAiTarget?.Entity == null || _.SelectedAiTarget.Entity.Removed)
          {
            _.State = AIState.Idle;
            return false;
          }
          run = false;
          float sqrDist = Vector2.DistanceSquared(_.WorldPosition, _.SelectedAiTarget.WorldPosition);
          reactDist = _.currentTargetingParams is { ReactDistance: > 0 } ? _.currentTargetingParams.ReactDistance : _.GetPerceivingRange(_.SelectedAiTarget);
          float halfReactDist = reactDist / 2;
          float attackDist = _.currentTargetingParams is { AttackDistance: > 0 } ? _.currentTargetingParams.AttackDistance : halfReactDist;
          if (sqrDist > Math.Pow(reactDist, 2))
          {
            // Too far to react
            _.UpdateIdle(deltaTime);
          }
          else if (sqrDist < Math.Pow(attackDist + _.movementMargin, 2))
          {
            _.movementMargin = attackDist;
            _.SteeringManager.Reset();
            if (_.Character.AnimController.InWater)
            {
              useSteeringLengthAsMovementSpeed = true;
              Vector2 dir = Vector2.Normalize(_.SelectedAiTarget.WorldPosition - _.Character.WorldPosition);
              if (sqrDist < Math.Pow(attackDist * 0.75f, 2))
              {
                // Keep the distance, if too close
                dir = -dir;
                useSteeringLengthAsMovementSpeed = false;
                _.Reverse = true;
                run = true;
              }
              _.SteeringManager.SteeringManual(deltaTime, dir * 0.2f);
            }
            else
            {
              // TODO: doesn't work right here
              _.FaceTarget(_.SelectedAiTarget.Entity);
            }
            _.observeTimer -= deltaTime;
            if (_.observeTimer < 0)
            {
              _.IgnoreTarget(_.SelectedAiTarget);
              _.State = AIState.Idle;
              _.ResetAITarget();
            }
          }
          else
          {
            run = sqrDist > Math.Pow(attackDist * 2, 2);
            _.movementMargin = MathHelper.Clamp(_.movementMargin -= deltaTime, 0, attackDist);
            _.UpdateFollow(deltaTime);
          }
          break;
        default:
          throw new NotImplementedException();
      }

      if (!_.Character.AnimController.SimplePhysicsEnabled)
      {
        _.LatchOntoAI?.Update(_, deltaTime);
      }
      _.IsSteeringThroughGap = false;
      if (_.SwarmBehavior != null)
      {
        _.SwarmBehavior.IsActive = _.SwarmBehavior.ForceActive || _.State == AIState.Idle && _.Character.CurrentHull == null;
        _.SwarmBehavior.Refresh();
        _.SwarmBehavior.UpdateSteering(deltaTime);
      }
      // Ensure that the creature keeps inside the level
      _.SteerInsideLevel(deltaTime);
      float speed = _.Character.AnimController.GetCurrentSpeed(run && _.Character.CanRun);
      // Doesn't work if less than 1, when we use steering length as movement speed.
      _.steeringManager.Update(Math.Max(speed, 1.0f));
      float movementSpeed = useSteeringLengthAsMovementSpeed ? _.Steering.Length() : speed;
      _.Character.AnimController.TargetMovement = _.Character.ApplyMovementLimits(_.Steering, movementSpeed);
      if (_.Character.CurrentHull != null && _.Character.AnimController.InWater)
      {
        // Limit the swimming speed inside the sub.
        _.Character.AnimController.TargetMovement = _.Character.AnimController.TargetMovement.ClampLength(5);
      }

      return false;
    }

  }
}