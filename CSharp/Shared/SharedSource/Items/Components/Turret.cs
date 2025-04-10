using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics.Dynamics;
using System.Collections.Immutable;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedTurret()
    {
      harmony.Patch(
        original: typeof(Turret).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Turret_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(Turret).GetMethod("UpdateAutoOperate", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Turret_UpdateAutoOperate_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Components/Turret.cs#L438
    public static bool Turret_Update_Replace(float deltaTime, Camera cam, Turret __instance)
    {
      Turret _ = __instance;

      _.cam = cam;

      if (_.reload > 0.0f) { _.reload -= deltaTime; }
      if (!MathUtils.NearlyEqual(_.item.Rotation, _.prevBaseRotation) || !MathUtils.NearlyEqual(_.item.Scale, _.prevScale))
      {
        _.UpdateTransformedBarrelPos();
      }

      if (_.user is { Removed: true })
      {
        _.user = null;
      }
      else
      {
        _.resetUserTimer -= deltaTime;
        if (_.resetUserTimer <= 0.0f) { _.user = null; }
      }

      if (_.ActiveUser is { Removed: true })
      {
        _.ActiveUser = null;
      }
      else
      {
        _.resetActiveUserTimer -= deltaTime;
        if (_.resetActiveUserTimer <= 0.0f)
        {
          _.ActiveUser = null;
        }
      }

      _.ApplyStatusEffects(ActionType.OnActive, deltaTime);

      float previousChargeTime = _.currentChargeTime;

      if (_.SingleChargedShot && _.reload > 0f)
      {
        // single charged shot guns will decharge after firing
        // for cosmetic reasons, _ is done by lerping in half the reload time
        _.currentChargeTime = _.Reload > 0.0f ?
            Math.Max(0f, _.MaxChargeTime * (_.reload / _.Reload - 0.5f)) :
            0.0f;
      }
      else
      {
        float chargeDeltaTime = _.tryingToCharge ? deltaTime : -deltaTime;
        if (chargeDeltaTime > 0f && _.user != null)
        {
          chargeDeltaTime *= 1f + _.user.GetStatValue(StatTypes.TurretChargeSpeed);
        }
        _.currentChargeTime = Math.Clamp(_.currentChargeTime + chargeDeltaTime, 0f, _.MaxChargeTime);
      }
      _.tryingToCharge = false;

      if (_.currentChargeTime == 0f)
      {
        _.currentChargingState = Turret.ChargingState.Inactive;
      }
      else if (_.currentChargeTime < previousChargeTime)
      {
        _.currentChargingState = Turret.ChargingState.WindingDown;
      }
      else
      {
        // if we are charging up or at maxed charge, remain winding up
        _.currentChargingState = Turret.ChargingState.WindingUp;
      }

      // Not compiled on server
#if CLIENT
      _.UpdateProjSpecific(deltaTime);
#endif

      if (MathUtils.NearlyEqual(_.minRotation, _.maxRotation))
      {
        _.UpdateLightComponents();
        return false;
      }

      float targetMidDiff = MathHelper.WrapAngle(_.targetRotation - (_.minRotation + _.maxRotation) / 2.0f);

      float maxDist = (_.maxRotation - _.minRotation) / 2.0f;

      if (Math.Abs(targetMidDiff) > maxDist)
      {
        _.targetRotation = (targetMidDiff < 0.0f) ? _.minRotation : _.maxRotation;
      }

      float degreeOfSuccess = _.user == null ? 0.5f : _.DegreeOfSuccess(_.user);
      if (degreeOfSuccess < 0.5f) { degreeOfSuccess *= degreeOfSuccess; } //the ease of aiming drops quickly with insufficient skill levels
      float springStiffness = MathHelper.Lerp(_.SpringStiffnessLowSkill, _.SpringStiffnessHighSkill, degreeOfSuccess);
      float springDamping = MathHelper.Lerp(_.SpringDampingLowSkill, _.SpringDampingHighSkill, degreeOfSuccess);
      float rotationSpeed = MathHelper.Lerp(_.RotationSpeedLowSkill, _.RotationSpeedHighSkill, degreeOfSuccess);
      if (_.MaxChargeTime > 0)
      {
        rotationSpeed *= MathHelper.Lerp(1f, _.FiringRotationSpeedModifier, MathUtils.EaseIn(_.currentChargeTime / _.MaxChargeTime));
      }

      // Do not increase the weapons skill when operating a turret in an outpost level
      if (_.user?.Info != null && (GameMain.GameSession?.Campaign == null || !Level.IsLoadedFriendlyOutpost))
      {
        _.user.Info.ApplySkillGain(
            Tags.WeaponsSkill,
            SkillSettings.Current.SkillIncreasePerSecondWhenOperatingTurret * deltaTime);
      }

      float rotMidDiff = MathHelper.WrapAngle(_.Rotation - (_.minRotation + _.maxRotation) / 2.0f);

      float targetRotationDiff = MathHelper.WrapAngle(_.targetRotation - _.Rotation);

      if ((_.maxRotation - _.minRotation) < MathHelper.TwoPi)
      {
        float targetRotationMaxDiff = MathHelper.WrapAngle(_.targetRotation - _.maxRotation);
        float targetRotationMinDiff = MathHelper.WrapAngle(_.targetRotation - _.minRotation);

        if (Math.Abs(targetRotationMaxDiff) < Math.Abs(targetRotationMinDiff) &&
            rotMidDiff < 0.0f &&
            targetRotationDiff < 0.0f)
        {
          targetRotationDiff += MathHelper.TwoPi;
        }
        else if (Math.Abs(targetRotationMaxDiff) > Math.Abs(targetRotationMinDiff) &&
            rotMidDiff > 0.0f &&
            targetRotationDiff > 0.0f)
        {
          targetRotationDiff -= MathHelper.TwoPi;
        }
      }

      _.angularVelocity +=
          (targetRotationDiff * springStiffness - _.angularVelocity * springDamping) * deltaTime;
      _.angularVelocity = MathHelper.Clamp(_.angularVelocity, -rotationSpeed, rotationSpeed);

      _.Rotation += _.angularVelocity * deltaTime;

      rotMidDiff = MathHelper.WrapAngle(_.Rotation - (_.minRotation + _.maxRotation) / 2.0f);

      if (rotMidDiff < -maxDist)
      {
        _.Rotation = _.minRotation;
        _.angularVelocity *= -0.5f;
      }
      else if (rotMidDiff > maxDist)
      {
        _.Rotation = _.maxRotation;
        _.angularVelocity *= -0.5f;
      }

      if (_.aiFindTargetTimer > 0.0f)
      {
        _.aiFindTargetTimer -= deltaTime;
      }

      _.UpdateLightComponents();

      if (_.AutoOperate && _.ActiveUser == null)
      {
        _.UpdateAutoOperate(deltaTime, ignorePower: false);
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Components/Turret.cs#L991
    public static bool Turret_UpdateAutoOperate_Replace(Turret __instance, float deltaTime, bool ignorePower, Identifier friendlyTag = default)
    {
      Turret _ = __instance;

      if (!ignorePower && !_.HasPowerToShoot())
      {
        return false;
      }

      _.IsActive = true;

      if (friendlyTag.IsEmpty)
      {
        friendlyTag = _.FriendlyTag;
      }

      if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
      {
        return false;
      }

      if (_.updatePending)
      {
        if (_.updateTimer < 0.0f)
        {
#if SERVER
          _.item.CreateServerEvent(_);
#endif
          _.prevTargetRotation = _.targetRotation;
          _.updateTimer = 0.25f;
        }
        _.updateTimer -= deltaTime;
      }

      if (_.AimDelay && _.waitTimer > 0)
      {
        _.waitTimer -= deltaTime;
        return false;
      }
      Submarine closestSub = null;
      float maxDistance = 10000.0f;
      float shootDistance = _.AIRange;
      ISpatialEntity target = null;
      float closestDist = shootDistance * shootDistance;
      if (_.TargetCharacters)
      {
        foreach (var character in Character.CharacterList)
        {
          if (!Turret.IsValidTarget(character)) { continue; }
          float priority = _.isSlowTurret ? character.Params.AISlowTurretPriority : character.Params.AITurretPriority;
          if (priority <= 0) { continue; }
          if (!_.IsValidTargetForAutoOperate(character, friendlyTag)) { continue; }
          float dist = Vector2.DistanceSquared(character.WorldPosition, _.item.WorldPosition);
          if (dist > closestDist) { continue; }
          if (!_.IsWithinAimingRadius(character.WorldPosition)) { continue; }
          target = character;
          if (_.currentTarget != null && target == _.currentTarget)
          {
            priority *= _.GetTargetPriorityModifier();
          }
          closestDist = dist / priority;
        }
      }
      if (_.TargetItems)
      {
        foreach (Item targetItem in Item.TurretTargetItems)
        {
          if (!Turret.IsValidTarget(targetItem)) { continue; }
          float priority = _.isSlowTurret ? targetItem.Prefab.AISlowTurretPriority : targetItem.Prefab.AITurretPriority;
          if (priority <= 0) { continue; }
          float dist = Vector2.DistanceSquared(_.item.WorldPosition, targetItem.WorldPosition);
          if (dist > closestDist) { continue; }
          if (dist > shootDistance * shootDistance) { continue; }
          if (!_.IsTargetItemCloseEnough(targetItem, dist)) { continue; }
          if (!_.IsWithinAimingRadius(targetItem.WorldPosition)) { continue; }
          target = targetItem;
          if (_.currentTarget != null && target == _.currentTarget)
          {
            priority *= _.GetTargetPriorityModifier();
          }
          closestDist = dist / priority;
        }
      }
      if (_.TargetSubmarines)
      {
        if (target == null || target.Submarine != null)
        {
          closestDist = maxDistance * maxDistance;
          foreach (Submarine sub in Submarine.Loaded)
          {
            if (sub == _.Item.Submarine) { continue; }
            if (_.item.Submarine != null)
            {
              if (Character.IsOnFriendlyTeam(_.item.Submarine.TeamID, sub.TeamID)) { continue; }
            }
            float dist = Vector2.DistanceSquared(sub.WorldPosition, _.item.WorldPosition);
            if (dist > closestDist) { continue; }
            closestSub = sub;
            closestDist = dist;
          }
          closestDist = shootDistance * shootDistance;
          if (closestSub != null)
          {
            foreach (var hull in Hull.HullList)
            {
              if (!closestSub.IsEntityFoundOnThisSub(hull, true)) { continue; }
              float dist = Vector2.DistanceSquared(hull.WorldPosition, _.item.WorldPosition);
              if (dist > closestDist) { continue; }
              // Don't check the angle, because it doesn't work on Thalamus spike. The angle check wouldn't be very important here anyway.
              target = hull;
              closestDist = dist;
            }
          }
        }
      }

      if (target == null && _.RandomMovement)
      {
        // Random movement while there's no target
        _.waitTimer = Rand.Value(Rand.RandSync.Unsynced) < 0.98f ? 0f : Rand.Range(5f, 20f);
        _.targetRotation = Rand.Range(_.minRotation, _.maxRotation);
        _.updatePending = true;
        return false;
      }

      if (_.AimDelay)
      {
        if (_.RandomAimAmount > 0)
        {
          if (_.randomAimTimer < 0)
          {
            // Random disorder or other flaw in the targeting.
            _.randomAimTimer = Rand.Range(_.RandomAimMinTime, _.RandomAimMaxTime);
            _.waitTimer = Rand.Range(0.25f, 1f);
            float randomAim = MathHelper.ToRadians(_.RandomAimAmount);
            _.targetRotation = MathUtils.WrapAngleTwoPi(_.targetRotation += Rand.Range(-randomAim, randomAim));
            _.updatePending = true;
            return false;
          }
          else
          {
            _.randomAimTimer -= deltaTime;
          }
        }
      }
      if (target == null) { return false; }
      _.currentTarget = target;

      float angle = -MathUtils.VectorToAngle(target.WorldPosition - _.item.WorldPosition);
      _.targetRotation = MathUtils.WrapAngleTwoPi(angle);
      if (Math.Abs(_.targetRotation - _.prevTargetRotation) > 0.1f) { _.updatePending = true; }

      if (target is Hull targetHull)
      {
        Vector2 barrelDir = _.GetBarrelDir();
        Vector2 intersection;
        if (!MathUtils.GetLineWorldRectangleIntersection(_.item.WorldPosition, _.item.WorldPosition + barrelDir * _.AIRange, targetHull.WorldRect, out intersection))
        {
          return false;
        }
      }
      else
      {
        if (!_.IsWithinAimingRadius(angle)) { return false; }
        if (!_.IsPointingTowards(target.WorldPosition)) { return false; }
      }
      Vector2 start = ConvertUnits.ToSimUnits(_.item.WorldPosition);
      Vector2 end = ConvertUnits.ToSimUnits(target.WorldPosition);
      // Check that there's not other entities that shouldn't be targeted (like a friendly sub) between us and the target.
      Body worldTarget = _.CheckLineOfSight(start, end);
      bool shoot;
      if (target.Submarine != null)
      {
        start -= target.Submarine.SimPosition;
        end -= target.Submarine.SimPosition;
        Body transformedTarget = _.CheckLineOfSight(start, end);
        shoot = _.CanShoot(transformedTarget, user: null, friendlyTag, _.TargetSubmarines) && (worldTarget == null || _.CanShoot(worldTarget, user: null, friendlyTag, _.TargetSubmarines));
      }
      else
      {
        shoot = _.CanShoot(worldTarget, user: null, friendlyTag, _.TargetSubmarines);
      }
      if (shoot)
      {
        _.TryLaunch(deltaTime, ignorePower: ignorePower);
      }

      return false;
    }


  }
}