using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using FarseerPhysics;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedController()
    {
      harmony.Patch(
        original: typeof(Controller).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Controller_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(Controller).GetMethod("Use", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Controller_Use_Replace"))
      );
    }

    public static bool Controller_Update_Replace(float deltaTime, Camera cam, Controller __instance)
    {
      Controller _ = __instance;
      _.cam = cam;
      if (!_.ForceUserToStayAttached) { _.UserInCorrectPosition = false; }

      string signal = _.IsToggle && _.State ? _.output : _.falseOutput;
      if (_.item.Connections != null && _.IsToggle && !string.IsNullOrEmpty(signal) && !_.IsOutOfPower())
      {
        _.item.SendSignal(signal, "signal_out");
        _.item.SendSignal(signal, "trigger_out");
      }

      if (_.forceSelectNextFrame && _.User != null)
      {
        _.User.SelectedItem = _.item;
      }
      _.forceSelectNextFrame = false;
      _.userCanInteractCheckTimer -= deltaTime;

      if (_.User == null
          || _.User.Removed
          || (((_.User.Stun <= 0f && !_.User.IsKnockedDownOrRagdolled && !_.User.LockHands) || !_.ForceUserToStayAttached) && (!_.User.IsAnySelectedItem(_.item) || !_.CheckUserCanInteract()))
          || (_.item.ParentInventory != null && !_.IsAttachedUser(_.User))
          || (_.UsableIn == Controller.UseEnvironment.Water && !_.User.AnimController.InWater)
          || (_.UsableIn == Controller.UseEnvironment.Air && _.User.AnimController.InWater)
          || !_.CheckSpawnItem()
          )
      {
        if (_.User != null)
        {
          _.CancelUsing(_.User);
          _.User = null;
        }
        if (_.item.Connections == null || !_.IsToggle || string.IsNullOrEmpty(signal)) { _.IsActive = false; }
        return false;
      }

      if (_.ForceUserToStayAttached)
      {
        _.teleportTransition = MathF.Min(_.teleportTransition + deltaTime * Controller.TeleportTransitionSpeed, 1f);

        if (_.teleportTransition >= 1f)
        {
          // Transition is complete, if someone was holding this character, force them to deselect
          // so they aren't holding the character that is now attached to the controller
          if (_.User.SelectedBy != null)
          {
            _.User.SelectedBy.SelectedCharacter = null;
          }
        }

        if (_.User == Character.Controlled
            || _.teleportTransition < 1f
            || Vector2.DistanceSquared(_.item.WorldPosition, _.User.WorldPosition) > 0.1f)
        {
          var targetPosition = Vector2.Lerp(_.teleportStartPosition, _.item.WorldPosition, _.teleportTransition);
          _.User.TeleportTo(targetPosition);
          _.User.AnimController.Collider.ResetDynamics();
          foreach (var limb in _.User.AnimController.Limbs)
          {
            if (limb.Removed || limb.IsSevered) { continue; }
            limb.body?.ResetDynamics();
          }
        }
      }

      _.User.AnimController.StartUsingItem();

      if (_.userPos != Vector2.Zero)
      {
        Vector2 diff = (_.item.WorldPosition + _.userPos) - _.User.WorldPosition;

        if (_.User.AnimController.InWater)
        {
          if (diff.LengthSquared() > 30.0f * 30.0f)
          {
            _.User.AnimController.TargetMovement = Vector2.Clamp(diff * 0.01f, -Vector2.One, Vector2.One);
            _.User.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
          }
          else
          {
            _.User.AnimController.TargetMovement = Vector2.Zero;
            _.UserInCorrectPosition = true;
          }
        }
        else
        {
          // Secondary items (like ladders or chairs) will control the character position over primary items
          // Only control the character position if the character doesn't have another secondary item already controlling it
          if (!_.User.HasSelectedAnotherSecondaryItem(_.Item))
          {
            diff.Y = 0.0f;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && _.User != Character.Controlled)
            {
              if (Math.Abs(diff.X) > 20.0f)
              {
                //wait for the character to walk to the correct position
                return false;
              }
              else if (Math.Abs(diff.X) > 0.1f)
              {
                //aim to keep the collider at the correct position once close enough
                _.User.AnimController.Collider.LinearVelocity = new Vector2(
                    diff.X * 0.1f,
                    _.User.AnimController.Collider.LinearVelocity.Y);
              }
            }
            else if (Math.Abs(diff.X) > 10.0f)
            {
              _.User.AnimController.TargetMovement = Vector2.Normalize(diff);
              _.User.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
              return false;
            }
            _.User.AnimController.TargetMovement = Vector2.Zero;
          }
          _.UserInCorrectPosition = true;
        }
      }

      _.ApplyStatusEffects(ActionType.OnActive, deltaTime, _.User);

      if (_.limbPositions.Count == 0) { return false; }

      _.User.AnimController.StartUsingItem();

      if (_.User.SelectedItem != null)
      {
        _.User.AnimController.ResetPullJoints(l => l.IsLowerBody);
      }
      else
      {
        _.User.AnimController.ResetPullJoints();
      }

      if (_.dir != 0) { _.User.AnimController.TargetDir = _.dir; }

      foreach (LimbPos lb in _.limbPositions)
      {
        Limb limb = _.User.AnimController.GetLimb(lb.LimbType);
        if (limb == null || !limb.body.Enabled) { continue; }
        // Don't move lower body limbs if there's another selected secondary item that should control them
        if (limb.IsLowerBody && _.User.HasSelectedAnotherSecondaryItem(_.Item)) { continue; }
        // Don't move hands if there's a selected primary item that should control them
        if (limb.IsArm && _.Item == _.User.SelectedSecondaryItem && _.User.SelectedItem != null) { continue; }
        if (lb.AllowUsingLimb)
        {
          switch (lb.LimbType)
          {
            case LimbType.RightHand:
            case LimbType.RightForearm:
            case LimbType.RightArm:
              if (_.User.Inventory.GetItemInLimbSlot(InvSlotType.RightHand) != null) { continue; }
              break;
            case LimbType.LeftHand:
            case LimbType.LeftForearm:
            case LimbType.LeftArm:
              if (_.User.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand) != null) { continue; }
              break;
          }
        }
        limb.Disabled = true;
        Vector2 worldPosition = new Vector2(_.item.WorldRect.X, _.item.WorldRect.Y) + lb.Position * _.item.Scale;
        Vector2 diff = worldPosition - limb.WorldPosition;
        limb.PullJointEnabled = true;
        limb.PullJointWorldAnchorB = limb.SimPosition + ConvertUnits.ToSimUnits(diff);
      }
      return false;
    }

    public static bool Controller_Use_Replace(Controller __instance, ref bool __result, float deltaTime, Character activator = null)
    {
      Controller _ = __instance;

      if (activator != _.User)
      {
        __result = false; return false;
      }
      if (_.User == null || _.User.Removed || !_.User.IsAnySelectedItem(_.item) || !_.User.CanInteractWith(_.item))
      {
        _.User = null;
        __result = false; return false;
      }

      if (_.IsOutOfPower()) { __result = false; return false; }

      _.ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);
      if (_.IsToggle && (activator == null || _.lastUsed < Timing.TotalTime - 0.1))
      {
        if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
        {
          _.State = !_.State;
#if SERVER
          _.item.CreateServerEvent(_);
#endif
        }
      }
      else if (!string.IsNullOrEmpty(_.output))
      {
        _.item.SendSignal(new Signal(_.output, sender: _.User), "trigger_out");
      }

      _.lastUsed = Timing.TotalTime;

      __result = true; return false;
    }

  }
}