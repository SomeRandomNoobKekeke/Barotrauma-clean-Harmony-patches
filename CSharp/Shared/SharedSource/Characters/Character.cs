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
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
#if SERVER
using System.Text;
#endif


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedCharacter()
    {
      harmony.Patch(
        original: typeof(Character).GetMethod("UpdateAll", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_UpdateAll_Replace"))
      );

      harmony.Patch(
        original: typeof(Character).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(Character).GetMethod("Control", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_Control_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/Character.cs#L3366
    public static bool Character_UpdateAll_Replace(float deltaTime, Camera cam)
    {
      if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
      {
        foreach (Character c in Character.CharacterList)
        {
          if (c is not AICharacter && !c.IsRemotePlayer) { continue; }

          if (c.IsPlayer || (c.IsBot && !c.IsDead))
          {
            c.Enabled = true;
          }
          else if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
          {
            //disable AI characters that are far away from all clients and the host's character and not controlled by anyone
            float closestPlayerDist = c.GetDistanceToClosestPlayer();
            if (closestPlayerDist > c.Params.DisableDistance)
            {
              c.Enabled = false;
              if (c.IsDead && c.AIController is EnemyAIController)
              {
                Character.Spawner?.AddEntityToRemoveQueue(c);
              }
            }
            else if (closestPlayerDist < c.Params.DisableDistance * 0.9f)
            {
              c.Enabled = true;
            }
          }
          else if (Submarine.MainSub != null)
          {
            //disable AI characters that are far away from the sub and the controlled character
            float distSqr = Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition);
            if (Character.Controlled != null)
            {
              distSqr = Math.Min(distSqr, Vector2.DistanceSquared(Character.Controlled.WorldPosition, c.WorldPosition));
            }
            else
            {
              distSqr = Math.Min(distSqr, Vector2.DistanceSquared(GameMain.GameScreen.Cam.GetPosition(), c.WorldPosition));
            }

            if (distSqr > MathUtils.Pow2(c.Params.DisableDistance))
            {
              c.Enabled = false;
              if (c.IsDead && c.AIController is EnemyAIController)
              {
                Entity.Spawner?.AddEntityToRemoveQueue(c);
              }
            }
            else if (distSqr < MathUtils.Pow2(c.Params.DisableDistance * 0.9f))
            {
              c.Enabled = true;
            }
          }
        }
      }

      Character.characterUpdateTick++;

      if (Character.characterUpdateTick % Character.CharacterUpdateInterval == 0)
      {
        for (int i = 0; i < Character.CharacterList.Count; i++)
        {
          if (GameMain.LuaCs.Game.UpdatePriorityCharacters.Contains(Character.CharacterList[i])) continue;

          Character.CharacterList[i].Update(deltaTime * Character.CharacterUpdateInterval, cam);
        }
      }

      foreach (Character character in GameMain.LuaCs.Game.UpdatePriorityCharacters)
      {
        if (character.Removed) { continue; }

        character.Update(deltaTime, cam);
      }

#if CLIENT
      Character.UpdateSpeechBubbles(deltaTime);
#endif

      return false;
    }



    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/Character.cs#L3450
    public static bool Character_Update_Replace(float deltaTime, Camera cam, Character __instance)
    {
      Character _ = __instance;

      // Note: #if CLIENT is needed because on server side UpdateProjSpecific isn't compiled 
#if CLIENT
      _.UpdateProjSpecific(deltaTime, cam);
#endif

      if (_.TextChatVolume > 0)
      {
        _.TextChatVolume -= 0.2f * deltaTime;
      }

      if (_.InvisibleTimer > 0.0f)
      {
        if (Character.Controlled == null || Character.Controlled == _ || (Character.Controlled.CharacterHealth.GetAffliction("psychosis")?.Strength ?? 0.0f) <= 0.0f)
        {
          _.InvisibleTimer = Math.Min(_.InvisibleTimer, 1.0f);
        }
        _.InvisibleTimer -= deltaTime;
      }

      _.KnockbackCooldownTimer -= deltaTime;

      if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && _ == Character.Controlled && !_.isSynced) { return false; }

      _.UpdateDespawn(deltaTime);

      if (!_.Enabled) { return false; }

      if (Level.Loaded != null)
      {
        if (_.WorldPosition.Y < Level.MaxEntityDepth ||
            (_.Submarine != null && _.Submarine.WorldPosition.Y < Level.MaxEntityDepth))
        {
          _.Enabled = false;
          _.Kill(CauseOfDeathType.Pressure, null);
          return false;
        }
      }

      _.ApplyStatusEffects(ActionType.Always, deltaTime);

      _.PreviousHull = _.CurrentHull;
      _.CurrentHull = Hull.FindHull(_.WorldPosition, _.CurrentHull, useWorldCoordinates: true);

      _.obstructVisionAmount = Math.Max(_.obstructVisionAmount - deltaTime, 0.0f);

      if (_.Inventory != null)
      {
        //do not check for duplicates: _ is code is called very frequently, and duplicates don't matter here,
        //so it's better just to avoid the relatively expensive duplicate check
        foreach (Item item in _.Inventory.GetAllItems(checkForDuplicates: false))
        {
          if (item.body == null || item.body.Enabled) { continue; }
          item.SetTransform(_.SimPosition, 0.0f);
          item.Submarine = _.Submarine;
        }
      }

      _.HideFace = false;
      _.IgnoreMeleeWeapons = false;

      _.UpdateSightRange(deltaTime);
      _.UpdateSoundRange(deltaTime);

      _.UpdateAttackers(deltaTime);

      foreach (var characterTalent in _.characterTalents)
      {
        characterTalent.UpdateTalent(deltaTime);
      }

      if (_.IsDead) { return false; }

      if (GameMain.NetworkMember != null)
      {
        _.UpdateNetInput();
      }
      else
      {
        _.AnimController.Frozen = false;
      }

      _.DisableImpactDamageTimer -= deltaTime;

      if (!_.speechImpedimentSet)
      {
        //if no statuseffect or anything else has set a speech impediment, allow speaking normally
        _.speechImpediment = 0.0f;
      }
      _.speechImpedimentSet = false;

      if (_.NeedsAir)
      {
        //implode if not protected from pressure, and either outside or in a high-pressure hull
        if (!_.IsProtectedFromPressure && (_.AnimController.CurrentHull == null || _.AnimController.CurrentHull.LethalPressure >= 80.0f))
        {
          if (_.PressureTimer > _.CharacterHealth.PressureKillDelay * 0.1f)
          {
            //after a brief delay, start doing increasing amounts of organ damage
            _.CharacterHealth.ApplyAffliction(
                targetLimb: _.AnimController.MainLimb,
                new Affliction(AfflictionPrefab.OrganDamage, _.PressureTimer / 10.0f * deltaTime));
          }

          if (_.CharacterHealth.PressureKillDelay <= 0.0f)
          {
            _.PressureTimer = 100.0f;
          }
          else
          {
            _.PressureTimer += ((_.AnimController.CurrentHull == null) ?
                100.0f : _.AnimController.CurrentHull.LethalPressure) / _.CharacterHealth.PressureKillDelay * deltaTime;
          }

          if (_.PressureTimer >= 100.0f)
          {
            if (Character.Controlled == _) { cam.Zoom = 5.0f; }
            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
            {
              _.Implode();
              if (_.IsDead) { return false; }
            }
          }
        }
        else
        {
          _.PressureTimer = 0.0f;
        }
      }
      else if ((GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) && !_.IsProtectedFromPressure)
      {
        float realWorldDepth = Level.Loaded?.GetRealWorldDepth(_.WorldPosition.Y) ?? 0.0f;
        if (_.PressureProtection < realWorldDepth && realWorldDepth > _.CharacterHealth.CrushDepth)
        {
          //implode if below crush depth, and either outside or in a high-pressure hull                
          if (_.AnimController.CurrentHull == null || _.AnimController.CurrentHull.LethalPressure >= 80.0f)
          {
            _.Implode();
            if (_.IsDead) { return false; }
          }
        }
      }

      _.ApplyStatusEffects(_.AnimController.InWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
      _.ApplyStatusEffects(ActionType.OnActive, deltaTime);

      //wait 0.1 seconds so status effects that continuously set InDetectable to true can keep the character InDetectable
      if (_.aiTarget != null && Timing.TotalTime > _.aiTarget.InDetectableSetTime + 0.1f)
      {
        _.aiTarget.InDetectable = false;
      }

      // Note: #if CLIENT is needed because on server side UpdateControlled isn't compiled 
#if CLIENT
      _.UpdateControlled(deltaTime, cam);
#endif

      //Health effects
      if (_.NeedsOxygen)
      {
        _.UpdateOxygen(deltaTime);
      }

      _.CalculateHealthMultiplier();
      _.CharacterHealth.Update(deltaTime);

      if (_.IsIncapacitated)
      {
        _.Stun = Math.Max(5.0f, _.Stun);
        _.AnimController.ResetPullJoints();
        _.SelectedItem = _.SelectedSecondaryItem = null;
        return false;
      }

      _.UpdateAIChatMessages(deltaTime);

      bool wasRagdolled = _.IsRagdolled;
      if (_.IsForceRagdolled)
      {
        _.IsRagdolled = _.IsForceRagdolled;
      }
      else if (_ != Character.Controlled)
      {
        wasRagdolled = _.IsRagdolled;
        _.IsRagdolled = _.IsKeyDown(InputType.Ragdoll);
        if (_.IsRagdolled && _.IsBot && GameMain.NetworkMember is not { IsClient: true })
        {
          _.ClearInput(InputType.Ragdoll);
        }
      }
      else
      {
        bool tooFastToUnragdoll = bodyMovingTooFast(_.AnimController.Collider) || bodyMovingTooFast(_.AnimController.MainLimb.body);
        bool bodyMovingTooFast(PhysicsBody body)
        {
          return
              body.LinearVelocity.LengthSquared() > 8.0f * 8.0f ||
              //falling down counts as going too fast
              (!_.InWater && body.LinearVelocity.Y < -5.0f);
        }
        if (_.ragdollingLockTimer > 0.0f)
        {
          _.ragdollingLockTimer -= deltaTime;
        }
        else if (!tooFastToUnragdoll)
        {
          _.IsRagdolled = _.IsKeyDown(InputType.Ragdoll); //Handle _ here instead of Control because we can stop being ragdolled ourselves
          if (wasRagdolled != _.IsRagdolled && !_.AnimController.IsHangingWithRope)
          {
            _.ragdollingLockTimer = 0.2f;
          }
        }
        _.SetInput(InputType.Ragdoll, false, _.IsRagdolled);
      }
      if (!wasRagdolled && _.IsRagdolled && !_.AnimController.IsHangingWithRope)
      {
        _.CheckTalents(AbilityEffectType.OnRagdoll);
      }

      _.lowPassMultiplier = MathHelper.Lerp(_.lowPassMultiplier, 1.0f, 0.1f);

      if (_.IsRagdolled || !_.CanMove)
      {
        if (_.AnimController is HumanoidAnimController humanAnimController)
        {
          humanAnimController.Crouching = false;
        }
        if (_.IsRagdolled) { _.AnimController.IgnorePlatforms = true; }
        _.AnimController.ResetPullJoints();
        _.SelectedItem = _.SelectedSecondaryItem = null;
        return false;
      }

      //AI and control stuff

      _.Control(deltaTime, cam);

      bool isNotControlled = Character.Controlled != _;

      if (isNotControlled && (!(_ is AICharacter) || _.IsRemotePlayer))
      {
        Vector2 mouseSimPos = ConvertUnits.ToSimUnits(_.cursorPosition);
        _.DoInteractionUpdate(deltaTime, mouseSimPos);
      }

      if (MustDeselect(_.SelectedItem))
      {
        _.SelectedItem = null;
      }
      if (MustDeselect(_.SelectedSecondaryItem))
      {
        _.ReleaseSecondaryItem();
      }

      if (!_.IsDead) { _.LockHands = false; }

      bool MustDeselect(Item item)
      {
        if (item == null) { return false; }
        if (!_.CanInteractWith(item)) { return true; }
        bool hasSelectableComponent = false;
        foreach (var component in item.Components)
        {
          //the "selectability" of an item can change e.g. if the player unequips another item that's required to access it
          if (component.CanBeSelected && component.HasRequiredItems(_, addMessage: false))
          {
            hasSelectableComponent = true;
            break;
          }
        }
        return !hasSelectableComponent;
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Characters/Character.cs#L2154
    public static bool Character_Control_Replace(float deltaTime, Camera cam, Character __instance)
    {
      Character _ = __instance;

      _.ViewTarget = null;
      if (!_.AllowInput) { return false; }

      if (Character.Controlled == _ || (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer))
      {
        _.SmoothedCursorPosition = _.cursorPosition;
      }
      else
      {
        //apply some smoothing to the cursor positions of remote players when playing as a client
        //to make aiming look a little less choppy
        Vector2 smoothedCursorDiff = _.cursorPosition - _.SmoothedCursorPosition;
        smoothedCursorDiff = NetConfig.InterpolateCursorPositionError(smoothedCursorDiff);
        _.SmoothedCursorPosition = _.cursorPosition - smoothedCursorDiff;
      }

      bool aiControlled = _ is AICharacter && Character.Controlled != _ && !_.IsRemotelyControlled;
      if (!aiControlled)
      {
        Vector2 targetMovement = _.GetTargetMovement();
        _.AnimController.TargetMovement = targetMovement;
        _.AnimController.IgnorePlatforms = _.AnimController.TargetMovement.Y < -0.1f;
      }

      if (_.AnimController is HumanoidAnimController humanAnimController)
      {
        humanAnimController.Crouching =
            humanAnimController.ForceSelectAnimationType == AnimationType.Crouch ||
            _.IsKeyDown(InputType.Crouch);
        if (Screen.Selected is not { IsEditor: true })
        {
          humanAnimController.ForceSelectAnimationType = AnimationType.NotDefined;
        }
      }

      if (!aiControlled &&
          !_.AnimController.IsUsingItem &&
          _.AnimController.Anim != AnimController.Animation.CPR &&
          (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient || Character.Controlled == _) &&
          ((!_.IsClimbing && _.AnimController.OnGround) || (_.IsClimbing && _.IsKeyDown(InputType.Aim))) &&
          !_.AnimController.InWater)
      {
        if (!_.FollowCursor)
        {
          _.AnimController.TargetDir = Direction.Right;
        }
        else
        {
          if (_.CursorPosition.X < _.AnimController.Collider.Position.X - Character.cursorFollowMargin)
          {
            _.AnimController.TargetDir = Direction.Left;
          }
          else if (_.CursorPosition.X > _.AnimController.Collider.Position.X + Character.cursorFollowMargin)
          {
            _.AnimController.TargetDir = Direction.Right;
          }
        }
      }

      if (GameMain.NetworkMember != null)
      {
        if (GameMain.NetworkMember.IsServer)
        {
          if (!aiControlled)
          {
            if (_.dequeuedInput.HasFlag(Character.InputNetFlags.FacingLeft))
            {
              _.AnimController.TargetDir = Direction.Left;
            }
            else
            {
              _.AnimController.TargetDir = Direction.Right;
            }
          }
        }
        else if (GameMain.NetworkMember.IsClient && Character.Controlled != _)
        {
          if (_.memState.Count > 0)
          {
            _.AnimController.TargetDir = _.memState[0].Direction;
          }
        }
      }

#if DEBUG && CLIENT
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.F))
            {
                _.AnimController.ReleaseStuckLimbs();
                if (AIController != null && AIController is EnemyAIController enemyAI)
                {
                    enemyAI.LatchOntoAI?.DeattachFromBody(reset: true);
                }
            }
#endif

      if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Character.Controlled != _ && _.IsKeyDown(InputType.Aim))
      {
        if (_.currentAttackTarget.AttackLimb?.attack is Attack { Ranged: true } attack && _.AIController is EnemyAIController enemyAi)
        {
          enemyAi.AimRangedAttack(attack, _.currentAttackTarget.DamageTarget as Entity);
        }
      }

      if (_.attackCoolDown > 0.0f)
      {
        _.attackCoolDown -= deltaTime;
      }
      else if (_.IsKeyDown(InputType.Attack))
      {
        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Character.Controlled != _)
        {
          if ((_.currentAttackTarget.DamageTarget as Entity)?.Removed ?? false)
          {
            _.currentAttackTarget = default;
          }

          AttackResult attackResult;
          _.currentAttackTarget.AttackLimb?.UpdateAttack(deltaTime, _.currentAttackTarget.AttackPos, _.currentAttackTarget.DamageTarget, out attackResult);
        }
        else if (_.IsPlayer)
        {
          float dist = -1;
          Vector2 attackPos = _.SimPosition + ConvertUnits.ToSimUnits(_.cursorPosition - _.Position);
          List<Body> ignoredBodies = _.AnimController.Limbs.Select(l => l.body.FarseerBody).ToList();
          ignoredBodies.Add(_.AnimController.Collider.FarseerBody);

          var body = Submarine.PickBody(
              _.SimPosition,
              attackPos,
              ignoredBodies,
              Physics.CollisionCharacter | Physics.CollisionWall);

          IDamageable attackTarget = null;
          if (body != null)
          {
            attackPos = Submarine.LastPickedPosition;

            if (body.UserData is Submarine sub)
            {
              body = Submarine.PickBody(
                  _.SimPosition - ((Submarine)body.UserData).SimPosition,
                  attackPos - ((Submarine)body.UserData).SimPosition,
                  ignoredBodies,
                  Physics.CollisionWall);

              if (body != null)
              {
                attackPos = Submarine.LastPickedPosition + sub.SimPosition;
                attackTarget = body.UserData as IDamageable;
              }
            }
            else
            {
              if (body.UserData is IDamageable damageable)
              {
                attackTarget = damageable;
              }
              else if (body.UserData is Limb limb)
              {
                attackTarget = limb.character;
              }
            }
          }
          var currentContexts = _.GetAttackContexts();
          var validLimbs = _.AnimController.Limbs.Where(l =>
          {
            if (l.IsSevered || l.IsStuck) { return false; }
            if (l.Disabled) { return false; }
            var attack = l.attack;
            if (attack == null) { return false; }
            if (attack.CoolDownTimer > 0) { return false; }
            if (!attack.IsValidContext(currentContexts)) { return false; }
            if (attackTarget != null)
            {
              if (!attack.IsValidTarget(attackTarget as Entity)) { return false; }
              if (attackTarget is ISerializableEntity se and Character)
              {
                if (attack.Conditionals.Any(c => !c.TargetSelf && !c.Matches(se))) { return false; }
              }
            }
            if (attack.Conditionals.Any(c => c.TargetSelf && !c.Matches(_))) { return false; }
            return true;
          });
          var sortedLimbs = validLimbs.OrderBy(l => Vector2.DistanceSquared(ConvertUnits.ToDisplayUnits(l.SimPosition), _.cursorPosition));
          // Select closest
          var attackLimb = sortedLimbs.FirstOrDefault();
          if (attackLimb != null)
          {
            if (attackTarget is Character targetCharacter)
            {
              dist = ConvertUnits.ToDisplayUnits(Vector2.Distance(Submarine.LastPickedPosition, attackLimb.SimPosition));
              foreach (Limb limb in targetCharacter.AnimController.Limbs)
              {
                if (limb.IsSevered || limb.Removed) { continue; }
                float tempDist = ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackLimb.SimPosition));
                if (tempDist < dist)
                {
                  dist = tempDist;
                }
              }
            }
            attackLimb.UpdateAttack(deltaTime, attackPos, attackTarget, out AttackResult attackResult, dist);
            if (!attackLimb.attack.IsRunning)
            {
              _.attackCoolDown = 1.0f;
            }
          }
        }
      }

      if (_.Inventory != null)
      {
        if (_.IsKeyHit(InputType.DropItem) && Screen.Selected is { IsEditor: false })
        {
          foreach (Item item in _.HeldItems)
          {
            if (!_.CanInteractWith(item)) { continue; }

            if (_.SelectedItem?.OwnInventory != null && _.SelectedItem.OwnInventory.CanBePut(item))
            {
              _.SelectedItem.OwnInventory.TryPutItem(item, _);
            }
            else
            {
              item.Drop(_);
            }
            //only drop one held item per key hit
            break;
          }
        }

        bool CanUseItemsWhenSelected(Item item) => item == null || !item.Prefab.DisableItemUsageWhenSelected;
        if (CanUseItemsWhenSelected(_.SelectedItem) && CanUseItemsWhenSelected(_.SelectedSecondaryItem))
        {
          foreach (Item item in _.HeldItems)
          {
            tryUseItem(item, deltaTime);
          }
          foreach (Item item in _.Inventory.AllItems)
          {
            if (item.GetComponent<Wearable>() is { AllowUseWhenWorn: true } && _.HasEquippedItem(item))
            {
              tryUseItem(item, deltaTime);
            }
          }
        }
      }

      void tryUseItem(Item item, float deltaTime)
      {
        if (_.IsKeyDown(InputType.Aim) || !item.RequireAimToSecondaryUse)
        {
          item.SecondaryUse(deltaTime, _);
        }
        if (_.IsKeyDown(InputType.Use) && !item.IsShootable)
        {
          if (!item.RequireAimToUse || _.IsKeyDown(InputType.Aim))
          {
            item.Use(deltaTime, user: _);
          }
        }
        if (_.IsKeyDown(InputType.Shoot) && item.IsShootable)
        {
          if (!item.RequireAimToUse || _.IsKeyDown(InputType.Aim))
          {
            item.Use(deltaTime, user: _);
          }
#if CLIENT
          else if (item.RequireAimToUse && !_.IsKeyDown(InputType.Aim))
          {
              HintManager.OnShootWithoutAiming(_, item);
          }
#endif
        }
      }

      if (_.SelectedItem != null)
      {
        tryUseItem(_.SelectedItem, deltaTime);
      }

      if (_.SelectedCharacter != null)
      {
        if (!_.SelectedCharacter.CanBeSelected ||
            (Vector2.DistanceSquared(_.SelectedCharacter.WorldPosition, _.WorldPosition) > Character.MaxDragDistance * Character.MaxDragDistance &&
            _.SelectedCharacter.GetDistanceToClosestLimb(_.GetRelativeSimPosition(_.selectedCharacter, _.WorldPosition)) > ConvertUnits.ToSimUnits(Character.MaxDragDistance)))
        {
          _.DeselectCharacter();
        }
      }

      if (_.IsRemotelyControlled && _.keys != null)
      {
        foreach (Key key in _.keys)
        {
          key.ResetHit();
        }
      }

      return false;
    }

  }
}