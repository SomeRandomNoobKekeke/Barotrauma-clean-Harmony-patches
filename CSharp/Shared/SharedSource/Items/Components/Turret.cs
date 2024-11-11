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

      _.UpdateProjSpecific(deltaTime);

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

  }
}