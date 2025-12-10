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

      if (_.forceSelectNextFrame && _.user != null)
      {
        _.user.SelectedItem = _.item;
      }
      _.forceSelectNextFrame = false;
      _.userCanInteractCheckTimer -= deltaTime;

      if (_.user == null
          || _.user.Removed
          || !_.user.IsAnySelectedItem(_.item)
          || (_.item.ParentInventory != null && !_.IsAttachedUser(_.user))
          || (_.UsableIn == Controller.UseEnvironment.Water && !_.user.AnimController.InWater)
          || (_.UsableIn == Controller.UseEnvironment.Air && _.user.AnimController.InWater)
          || !_.CheckUserCanInteract())
      {
        if (_.user != null)
        {
          _.CancelUsing(_.user);
          _.user = null;
        }
        if (_.item.Connections == null || !_.IsToggle || string.IsNullOrEmpty(signal)) { _.IsActive = false; }
        return false;
      }

      if (_.ForceUserToStayAttached && Vector2.DistanceSquared(_.item.WorldPosition, _.user.WorldPosition) > 0.1f)
      {
        _.user.TeleportTo(_.item.WorldPosition);
        _.user.AnimController.Collider.ResetDynamics();
        foreach (var limb in _.user.AnimController.Limbs)
        {
          if (limb.Removed || limb.IsSevered) { continue; }
          limb.body?.ResetDynamics();
        }
      }

      _.user.AnimController.StartUsingItem();

      if (_.userPos != Vector2.Zero)
      {
        Vector2 diff = (_.item.WorldPosition + _.userPos) - _.user.WorldPosition;

        if (_.user.AnimController.InWater)
        {
          if (diff.LengthSquared() > 30.0f * 30.0f)
          {
            _.user.AnimController.TargetMovement = Vector2.Clamp(diff * 0.01f, -Vector2.One, Vector2.One);
            _.user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
          }
          else
          {
            _.user.AnimController.TargetMovement = Vector2.Zero;
            _.UserInCorrectPosition = true;
          }
        }
        else
        {
          // Secondary items (like ladders or chairs) will control the character position over primary items
          // Only control the character position if the character doesn't have another secondary item already controlling it
          if (!_.user.HasSelectedAnotherSecondaryItem(_.Item))
          {
            diff.Y = 0.0f;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && _.user != Character.Controlled)
            {
              if (Math.Abs(diff.X) > 20.0f)
              {
                //wait for the character to walk to the correct position
                return false;
              }
              else if (Math.Abs(diff.X) > 0.1f)
              {
                //aim to keep the collider at the correct position once close enough
                _.user.AnimController.Collider.LinearVelocity = new Vector2(
                    diff.X * 0.1f,
                    _.user.AnimController.Collider.LinearVelocity.Y);
              }
            }
            else if (Math.Abs(diff.X) > 10.0f)
            {
              _.user.AnimController.TargetMovement = Vector2.Normalize(diff);
              _.user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
              return false;
            }
            _.user.AnimController.TargetMovement = Vector2.Zero;
          }
          _.UserInCorrectPosition = true;
        }
      }

      _.ApplyStatusEffects(ActionType.OnActive, deltaTime, _.user);

      if (_.limbPositions.Count == 0) { return false; }

      _.user.AnimController.StartUsingItem();

      if (_.user.SelectedItem != null)
      {
        _.user.AnimController.ResetPullJoints(l => l.IsLowerBody);
      }
      else
      {
        _.user.AnimController.ResetPullJoints();
      }

      if (_.dir != 0) { _.user.AnimController.TargetDir = _.dir; }

      foreach (LimbPos lb in _.limbPositions)
      {
        Limb limb = _.user.AnimController.GetLimb(lb.LimbType);
        if (limb == null || !limb.body.Enabled) { continue; }
        // Don't move lower body limbs if there's another selected secondary item that should control them
        if (limb.IsLowerBody && _.user.HasSelectedAnotherSecondaryItem(_.Item)) { continue; }
        // Don't move hands if there's a selected primary item that should control them
        if (limb.IsArm && _.Item == _.user.SelectedSecondaryItem && _.user.SelectedItem != null) { continue; }
        if (lb.AllowUsingLimb)
        {
          switch (lb.LimbType)
          {
            case LimbType.RightHand:
            case LimbType.RightForearm:
            case LimbType.RightArm:
              if (_.user.Inventory.GetItemInLimbSlot(InvSlotType.RightHand) != null) { continue; }
              break;
            case LimbType.LeftHand:
            case LimbType.LeftForearm:
            case LimbType.LeftArm:
              if (_.user.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand) != null) { continue; }
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

      if (activator != _.user)
      {
        __result = false; return false;
      }
      if (_.user == null || _.user.Removed || !_.user.IsAnySelectedItem(_.item) || !_.user.CanInteractWith(_.item))
      {
        _.user = null;
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
        _.item.SendSignal(new Signal(_.output, sender: _.user), "trigger_out");
      }

      _.lastUsed = Timing.TotalTime;

      __result = true; return false;
    }

  }
}