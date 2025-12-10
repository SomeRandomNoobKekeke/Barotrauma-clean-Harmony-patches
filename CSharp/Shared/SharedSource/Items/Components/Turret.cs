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

      harmony.Patch(
        original: typeof(Turret).GetMethod("CrewAIOperate", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Turret_CrewAIOperate_Replace"))
      );
    }


    public static bool Turret_CrewAIOperate_Replace(Turret __instance, ref bool __result, float deltaTime, Character character, AIObjectiveOperateItem objective)
    {
      Turret _ = __instance;

      if (character.AIController.SelectedAiTarget?.Entity is Character previousTarget && previousTarget.IsDead)
      {
        if (previousTarget.LastAttacker == null || previousTarget.LastAttacker == character)
        {
          character.Speak(TextManager.Get("DialogTurretTargetDead").Value,
              identifier: $"killedtarget{previousTarget.ID}".ToIdentifier(),
              minDurationBetweenSimilar: 5.0f);
        }
        character.AIController.SelectTarget(null);
      }

      bool canShoot = _.HasPowerToShoot();
      if (!canShoot)
      {
        float lowestCharge = 0.0f;
        PowerContainer batteryToLoad = null;
        foreach (PowerContainer battery in _.GetDirectlyConnectedBatteries())
        {
          if (!battery.Item.IsInteractable(character)) { continue; }
          if (battery.OutputDisabled) { continue; }
          if (batteryToLoad == null || battery.Charge < lowestCharge)
          {
            batteryToLoad = battery;
            lowestCharge = battery.Charge;
          }
          if (battery.Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(battery.Item, character))
          {
            if (battery.Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
            {
              objective.AddSubObjective(new AIObjectiveRepairItem(character, battery.Item, objective.objectiveManager, isPriority: true));
              __result = false; return false;
            }
            else
            {
              character.Speak(TextManager.Get("DialogSupercapacitorIsBroken").Value,
                  identifier: "supercapacitorisbroken".ToIdentifier(),
                  minDurationBetweenSimilar: 30.0f);
            }
          }
        }
        if (batteryToLoad == null) { __result = true; return false; }
        if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed * 0.4f)
        {
          objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, objective.objectiveManager, option: Identifier.Empty, requireEquip: false));
          __result = false; return false;
        }
        if (lowestCharge <= 0 && batteryToLoad.Item.ConditionPercentage > 0)
        {
          character.Speak(TextManager.Get("DialogTurretHasNoPower").Value,
              identifier: "turrethasnopower".ToIdentifier(),
              minDurationBetweenSimilar: 30.0f);
        }
      }

      int usableProjectileCount = 0;
      int maxProjectileCount = 0;
      foreach (MapEntity e in _.item.linkedTo)
      {
        if (!_.item.IsInteractable(character)) { continue; }
        if (!((MapEntity)_.item).Prefab.IsLinkAllowed(e.Prefab)) { continue; }
        if (e is Item projectileContainer)
        {
          var container = projectileContainer.GetComponent<ItemContainer>();
          if (container != null)
          {
            maxProjectileCount += container.Capacity;
            var projectiles = projectileContainer.ContainedItems.Where(it => it.Condition > 0.0f);
            var firstProjectile = projectiles.FirstOrDefault();

            if (firstProjectile?.Prefab != _.previousAmmo?.Prefab)
            {
              //assume the projectiles are infinitely fast (no aiming ahead of the target) if we can't find projectiles to calculate the speed based on,
              //and if the projectile type isn't the same as before
              _.projectileSpeed = float.PositiveInfinity;
            }
            _.previousAmmo = firstProjectile;
            if (projectiles.Any())
            {
              var projectile =
                  firstProjectile.GetComponent<Projectile>() ??
                  firstProjectile.ContainedItems.FirstOrDefault()?.GetComponent<Projectile>();
              _.TryDetermineProjectileSpeed(projectile);
              usableProjectileCount += projectiles.Count();
            }
          }
        }
      }

      if (usableProjectileCount == 0)
      {
        ItemContainer container = null;
        Item containerItem = null;
        foreach (MapEntity e in _.item.linkedTo)
        {
          containerItem = e as Item;
          if (containerItem == null) { continue; }
          if (!containerItem.IsInteractable(character)) { continue; }
          if (character.AIController is HumanAIController aiController && aiController.IgnoredItems.Contains(containerItem)) { continue; }
          container = containerItem.GetComponent<ItemContainer>();
          if (container != null) { break; }
        }
        if (container == null || !container.ContainableItemIdentifiers.Any())
        {
          if (character.IsOnPlayerTeam)
          {
            character.Speak(TextManager.GetWithVariable("DialogCannotLoadTurret", "[itemname]", _.item.Name, formatCapitals: FormatCapitals.Yes).Value,
                identifier: "cannotloadturret".ToIdentifier(),
                minDurationBetweenSimilar: 30.0f);
          }
          __result = true; return false;
        }
        if (objective.SubObjectives.None())
        {
          var loadItemsObjective = _.AIContainItems<Turret>(container, character, objective, usableProjectileCount + 1, equip: true, removeEmpty: true, dropItemOnDeselected: true);
          loadItemsObjective.ignoredContainerIdentifiers = ((MapEntity)containerItem).Prefab.Identifier.ToEnumerable().ToImmutableHashSet();
          if (character.IsOnPlayerTeam)
          {
            character.Speak(TextManager.GetWithVariable("DialogLoadTurret", "[itemname]", _.item.Name, formatCapitals: FormatCapitals.Yes).Value,
                identifier: "loadturret".ToIdentifier(),
                minDurationBetweenSimilar: 30.0f);
          }
          loadItemsObjective.Abandoned += CheckRemainingAmmo;
          loadItemsObjective.Completed += CheckRemainingAmmo;
          __result = false; return false;

          void CheckRemainingAmmo()
          {
            if (!character.IsOnPlayerTeam) { return; }
            if (character.Submarine != Submarine.MainSub) { return; }
            Identifier ammoType = container.ContainableItemIdentifiers.FirstOrNull() ?? "ammobox".ToIdentifier();
            int remainingAmmo = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(ammoType) && i.Condition > 1);
            if (remainingAmmo == 0)
            {
              character.Speak(TextManager.Get($"DialogOutOf{ammoType}", "DialogOutOfTurretAmmo").Value,
                  identifier: "outofammo".ToIdentifier(),
                  minDurationBetweenSimilar: 30.0f);
            }
            else if (remainingAmmo < 3)
            {
              character.Speak(TextManager.Get($"DialogLowOn{ammoType}").Value,
                  identifier: "outofammo".ToIdentifier(),
                  minDurationBetweenSimilar: 30.0f);
            }
          }
        }
        if (objective.SubObjectives.Any())
        {
          __result = false; return false;
        }
      }

      //enough shells and power
      Character closestEnemy = null;
      Vector2? targetPos = null;
      float maxDistance = 10000;
      float shootDistance = _.AIRange * _.item.OffsetOnSelectedMultiplier;
      float closestDistance = maxDistance * maxDistance;
      bool hadCurrentTarget = _.currentTarget != null;
      if (hadCurrentTarget)
      {
        bool isValidTarget = Turret.IsValidTarget(_.currentTarget);
        if (isValidTarget)
        {
          float dist = Vector2.DistanceSquared(_.item.WorldPosition, _.currentTarget.WorldPosition);
          if (dist > closestDistance)
          {
            isValidTarget = false;
          }
          else if (_.currentTarget is Item targetItem)
          {
            if (!_.IsTargetItemCloseEnough(targetItem, dist))
            {
              isValidTarget = false;
            }
          }
        }
        if (!isValidTarget)
        {
          _.currentTarget = null;
          _.aiFindTargetTimer = Turret.CrewAIFindTargetMinInverval;
        }
      }
      if (_.aiFindTargetTimer <= 0.0f)
      {
        foreach (Character enemy in Character.CharacterList)
        {
          if (!Turret.IsValidTarget(enemy)) { continue; }
          float priority = _.isSlowTurret ? enemy.Params.AISlowTurretPriority : enemy.Params.AITurretPriority;
          if (priority <= 0) { continue; }
          if (character.Submarine != null)
          {
            if (enemy.Submarine == character.Submarine) { continue; }
            if (enemy.Submarine != null)
            {
              if (enemy.Submarine.TeamID == character.Submarine.TeamID) { continue; }
              if (enemy.Submarine.Info.IsOutpost) { continue; }
            }
          }
          // Don't aim monsters that are inside any submarine.
          if (!enemy.IsHuman && enemy.CurrentHull != null) { continue; }
          if (HumanAIController.IsFriendly(character, enemy, ignoreHuskDisguising: true)) { continue; }
          // Don't shoot at captured enemies.
          if (enemy.LockHands) { continue; }
          float dist = Vector2.DistanceSquared(enemy.WorldPosition, _.item.WorldPosition);
          if (dist > closestDistance) { continue; }
          if (dist < shootDistance * shootDistance)
          {
            // Only check the angle to targets that are close enough to be shot at
            // We shouldn't check the angle when a long creature is traveling outside of the shooting range, because doing so would not allow us to shoot the limbs that might be close enough to shoot at.
            if (!_.IsWithinAimingRadius(enemy.WorldPosition)) { continue; }
          }
          if (_.currentTarget != null && enemy == _.currentTarget)
          {
            priority *= _.GetTargetPriorityModifier();
          }
          targetPos = enemy.WorldPosition;
          closestEnemy = enemy;
          closestDistance = dist / priority;
          _.currentTarget = closestEnemy;
        }
        foreach (Item targetItem in Item.TurretTargetItems)
        {
          if (!Turret.IsValidTarget(targetItem)) { continue; }
          float priority = _.isSlowTurret ? targetItem.Prefab.AISlowTurretPriority : targetItem.Prefab.AITurretPriority;
          if (priority <= 0) { continue; }
          float dist = Vector2.DistanceSquared(_.item.WorldPosition, targetItem.WorldPosition);
          if (dist > closestDistance) { continue; }
          if (dist > shootDistance * shootDistance) { continue; }
          if (!_.IsTargetItemCloseEnough(targetItem, dist)) { continue; }
          if (!_.IsWithinAimingRadius(targetItem.WorldPosition)) { continue; }
          if (_.currentTarget != null && targetItem == _.currentTarget)
          {
            priority *= _.GetTargetPriorityModifier();
          }
          targetPos = targetItem.WorldPosition;
          closestDistance = dist / priority;
          // Override the target character so that we can target the item instead.
          closestEnemy = null;
          _.currentTarget = targetItem;
        }
        _.aiFindTargetTimer = _.currentTarget == null ? Turret.CrewAiFindTargetMaxInterval : Turret.CrewAIFindTargetMinInverval;
      }
      else if (_.currentTarget != null)
      {
        targetPos = _.currentTarget.WorldPosition;
      }
      bool iceSpireSpotted = false;
      Vector2 targetVelocity = Vector2.Zero;
      // Adjust the target character position (limb or submarine)
      if (_.currentTarget is Character targetCharacter)
      {
        //if the enemy is inside another sub, aim at the room they're in to make it less obvious that the enemy "knows" exactly where the target is
        if (targetCharacter.Submarine != null && targetCharacter.CurrentHull != null && targetCharacter.Submarine != _.item.Submarine && !targetCharacter.CanSeeTarget(_.Item))
        {
          targetPos = targetCharacter.CurrentHull.WorldPosition;
          if (closestDistance > maxDistance * maxDistance)
          {
            ResetTarget();
          }
        }
        else
        {
          // Target the closest limb. Doesn't make much difference with smaller creatures, but enables the bots to shoot longer abyss creatures like the endworm. Otherwise they just target the main body = head.
          float closestDistSqr = closestDistance;
          foreach (Limb limb in targetCharacter.AnimController.Limbs)
          {
            if (limb.IsSevered) { continue; }
            if (limb.Hidden) { continue; }
            if (!_.IsWithinAimingRadius(limb.WorldPosition)) { continue; }
            float distSqr = Vector2.DistanceSquared(limb.WorldPosition, _.item.WorldPosition);
            if (distSqr < closestDistSqr)
            {
              closestDistSqr = distSqr;
              if (limb == targetCharacter.AnimController.MainLimb)
              {
                //prefer main limb (usually a much better target than the extremities that are often the closest limbs)
                closestDistSqr *= 0.5f;
              }
              targetPos = limb.WorldPosition;
            }
          }
          if (_.projectileSpeed < float.PositiveInfinity && targetPos.HasValue)
          {
            //lead the target (aim where the target will be in the future)
            float dist = MathF.Sqrt(closestDistSqr);
            float projectileMovementTime = dist / _.projectileSpeed;

            targetVelocity = targetCharacter.AnimController.Collider.LinearVelocity;
            Vector2 movementAmount = targetVelocity * projectileMovementTime;
            //don't try to compensate more than 10 meters - if the target is so fast or the projectile so slow we need to go beyond that,
            //it'd most likely fail anyway
            movementAmount = ConvertUnits.ToDisplayUnits(movementAmount.ClampLength(Turret.MaximumAimAhead));
            Vector2 futurePosition = targetPos.Value + movementAmount;
            targetPos = Vector2.Lerp(targetPos.Value, futurePosition, _.DegreeOfSuccess(character));
          }
          if (closestDistSqr > shootDistance * shootDistance)
          {
            _.aiFindTargetTimer = Turret.CrewAIFindTargetMinInverval;
            ResetTarget();
          }
        }
        void ResetTarget()
        {
          // Not close enough to shoot.
          _.currentTarget = null;
          closestEnemy = null;
          targetPos = null;
        }
      }
      else if (targetPos == null && _.item.Submarine != null && Level.Loaded != null)
      {
        // Check ice spires
        shootDistance = _.AIRange * _.item.OffsetOnSelectedMultiplier;
        closestDistance = shootDistance;
        foreach (var wall in Level.Loaded.ExtraWalls)
        {
          if (wall is not DestructibleLevelWall destructibleWall || destructibleWall.Destroyed) { continue; }
          foreach (var cell in wall.Cells)
          {
            if (!cell.DoesDamage) { continue; }
            foreach (var edge in cell.Edges)
            {
              Vector2 p1 = edge.Point1 + cell.Translation;
              Vector2 p2 = edge.Point2 + cell.Translation;
              Vector2 closestPoint = MathUtils.GetClosestPointOnLineSegment(p1, p2, _.item.WorldPosition);
              if (!_.IsWithinAimingRadius(closestPoint))
              {
                // The closest point can't be targeted -> get a point directly in front of the turret
                Vector2 barrelDir = new Vector2((float)Math.Cos(_.Rotation), -(float)Math.Sin(_.Rotation));
                if (MathUtils.GetLineSegmentIntersection(p1, p2, _.item.WorldPosition, _.item.WorldPosition + barrelDir * shootDistance, out Vector2 intersection))
                {
                  closestPoint = intersection;
                  if (!_.IsWithinAimingRadius(closestPoint)) { continue; }
                }
                else
                {
                  continue;
                }
              }
              float dist = Vector2.Distance(closestPoint, _.item.WorldPosition);

              //add one px to make sure the visibility raycast doesn't miss the cell due to the end position being right at the edge of the cell
              closestPoint += (closestPoint - _.item.WorldPosition) / Math.Max(dist, 1);

              if (dist > _.AIRange + 1000) { continue; }
              float dot = 0;
              if (!MathUtils.NearlyEqual(_.item.Submarine.Velocity, Vector2.Zero))
              {
                dot = Vector2.Dot(Vector2.Normalize(_.item.Submarine.Velocity), Vector2.Normalize(closestPoint - _.item.Submarine.WorldPosition));
              }
              float minAngle = 0.5f;
              if (dot < minAngle && dist > 1000)
              {
                // The sub is not moving towards the target and it's not very close to the turret either -> ignore
                continue;
              }
              // Allow targeting farther when heading towards the spire (up to 1000 px)
              dist -= MathHelper.Lerp(0, 1000, MathUtils.InverseLerp(minAngle, 1, dot));
              if (dist > closestDistance) { continue; }
              targetPos = closestPoint;
              closestDistance = dist;
              iceSpireSpotted = true;
            }
          }
        }
      }

      if (targetPos == null) { __result = false; return false; }
      // Force the highest priority so that we don't change the objective while targeting enemies.
      objective.ForceHighestPriority = true;
#if CLIENT
      _.debugDrawTargetPos = targetPos.Value;
#endif
      if (closestEnemy != null && character.AIController.SelectedAiTarget != closestEnemy.AiTarget)
      {
        if (character.IsOnPlayerTeam)
        {
          if (character.AIController.SelectedAiTarget == null && !hadCurrentTarget)
          {
            if (CreatureMetrics.RecentlyEncountered.Contains(closestEnemy.SpeciesName) || closestEnemy.IsHuman)
            {
              character.Speak(TextManager.Get("DialogNewTargetSpotted").Value,
                  identifier: "newtargetspotted".ToIdentifier(),
                  minDurationBetweenSimilar: 30.0f);
            }
            else if (CreatureMetrics.Encountered.Contains(closestEnemy.SpeciesName))
            {
              character.Speak(TextManager.GetWithVariable("DialogIdentifiedTargetSpotted", "[speciesname]", closestEnemy.DisplayName).Value,
                  identifier: "identifiedtargetspotted".ToIdentifier(),
                  minDurationBetweenSimilar: 30.0f);
            }
            else
            {
              character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted").Value,
                  identifier: "unidentifiedtargetspotted".ToIdentifier(),
                  minDurationBetweenSimilar: 5.0f);
            }
          }
          else if (!CreatureMetrics.Encountered.Contains(closestEnemy.SpeciesName))
          {
            character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted").Value,
                identifier: "unidentifiedtargetspotted".ToIdentifier(),
                minDurationBetweenSimilar: 5.0f);
          }
          CreatureMetrics.AddEncounter(closestEnemy.SpeciesName);
        }
        character.AIController.SelectTarget(closestEnemy.AiTarget);
      }
      else if (iceSpireSpotted && character.IsOnPlayerTeam)
      {
        character.Speak(TextManager.Get("DialogIceSpireSpotted").Value,
            identifier: "icespirespotted".ToIdentifier(),
            minDurationBetweenSimilar: 60.0f);
      }

      character.CursorPosition = targetPos.Value;
      if (character.Submarine != null)
      {
        character.CursorPosition -= character.Submarine.Position;
      }

      if (_.IsPointingTowards(targetPos.Value))
      {
        Vector2 barrelDir = _.GetBarrelDir();
        Vector2 aimStartPos = _.item.WorldPosition;
        Vector2 aimEndPos = _.item.WorldPosition + barrelDir * shootDistance;
        bool allowShootingIfNothingInWay = false;
        if (_.currentTarget != null)
        {
          Vector2 targetStartPos = _.currentTarget.WorldPosition;
          Vector2 targetEndPos = _.currentTarget.WorldPosition + targetVelocity * ConvertUnits.ToDisplayUnits(Turret.MaximumAimAhead);

          //if there's nothing in the way (not even the target we're trying to aim towards),
          //shooting should only be allowed if we're aiming ahead of the target, in which case it's to be expected that we're aiming at "thin air"
          allowShootingIfNothingInWay =
              targetVelocity.LengthSquared() > 0.001f &&
              MathUtils.LineSegmentsIntersect(
                 aimStartPos, aimEndPos,
                 targetStartPos, targetEndPos) &&
              //target needs to be moving roughly perpendicular to us for aiming ahead of it to make sense
              Math.Abs(Vector2.Dot(Vector2.Normalize(aimEndPos - aimStartPos), Vector2.Normalize(targetEndPos - targetStartPos))) < 0.5f;
        }

        Vector2 start = ConvertUnits.ToSimUnits(aimStartPos);
        Vector2 end = ConvertUnits.ToSimUnits(aimEndPos);
        // Check that there's not other entities that shouldn't be targeted (like a friendly sub) between us and the target.
        Body worldTarget = _.CheckLineOfSight(start, end);
        if (closestEnemy != null && closestEnemy.Submarine != null)
        {
          start -= closestEnemy.Submarine.SimPosition;
          end -= closestEnemy.Submarine.SimPosition;
          Body transformedTarget = _.CheckLineOfSight(start, end);
          canShoot =
              _.CanShoot(transformedTarget, character, allowShootingIfNothingInWay: allowShootingIfNothingInWay) &&
              (worldTarget == null || _.CanShoot(worldTarget, character, allowShootingIfNothingInWay: allowShootingIfNothingInWay));
        }
        else
        {
          canShoot = _.CanShoot(worldTarget, character, allowShootingIfNothingInWay: allowShootingIfNothingInWay);
        }
        if (!canShoot) { __result = false; return false; }
        if (character.IsOnPlayerTeam)
        {
          character.Speak(TextManager.Get("DialogFireTurret").Value,
              identifier: "fireturret".ToIdentifier(),
              minDurationBetweenSimilar: 30.0f);
        }
        character.SetInput(InputType.Shoot, true, true);
      }
      __result = false; return false;
    }


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