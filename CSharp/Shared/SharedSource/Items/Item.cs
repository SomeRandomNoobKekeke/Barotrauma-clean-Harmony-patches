using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;


using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;
using MoonSharp.Interpreter;
using System.Collections.Immutable;
using Barotrauma.Abilities;

#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedItem()
    {
      harmony.Patch(
        original: typeof(Item).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Item_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(Item).GetMethod("Use", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Item_Use_Replace"))
      );

      harmony.Patch(
        original: typeof(Item).GetMethod("SendSignal", AccessTools.all, new Type[]{
          typeof(Signal),
          typeof(Connection),
        }),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Item_SendSignal_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Item.cs#L2299
    public static bool Item_Update_Replace(float deltaTime, Camera cam, Item __instance)
    {
      Item _ = __instance;

      if (!_.isActive || _.IsLayerHidden) { return false; }

      if (_.impactQueue != null)
      {
        while (_.impactQueue.TryDequeue(out float impact))
        {
          _.HandleCollision(impact);
        }
      }
      if (_.isDroppedStackOwner && _.body != null)
      {
        foreach (var item in _.droppedStack)
        {
          if (item != _)
          {
            item.body.Enabled = false;
            item.body.SetTransformIgnoreContacts(_.SimPosition, _.body.Rotation);
          }
        }
      }

      if (_.aiTarget != null && _.aiTarget.NeedsUpdate)
      {
        _.aiTarget.Update(deltaTime);
      }

      var containedEffectType = _.parentInventory == null ? ActionType.OnNotContained : ActionType.OnContained;

      _.ApplyStatusEffects(ActionType.Always, deltaTime, character: (_.parentInventory as CharacterInventory)?.Owner as Character);
      _.ApplyStatusEffects(containedEffectType, deltaTime, character: (_.parentInventory as CharacterInventory)?.Owner as Character);

      for (int i = 0; i < _.updateableComponents.Count; i++)
      {
        ItemComponent ic = _.updateableComponents[i];

        bool isParentInActive = ic.InheritParentIsActive && ic.Parent is { IsActive: false };

        if (ic.IsActiveConditionals != null && !isParentInActive)
        {
          if (ic.IsActiveConditionalComparison == PropertyConditional.LogicalOperatorType.And)
          {
            bool shouldBeActive = true;
            foreach (var conditional in ic.IsActiveConditionals)
            {
              if (!_.ConditionalMatches(conditional))
              {
                shouldBeActive = false;
                break;
              }
            }
            ic.IsActive = shouldBeActive;
          }
          else
          {
            bool shouldBeActive = false;
            foreach (var conditional in ic.IsActiveConditionals)
            {
              if (_.ConditionalMatches(conditional))
              {
                shouldBeActive = true;
                break;
              }
            }
            ic.IsActive = shouldBeActive;
          }
        }
#if CLIENT
        if (ic.HasSounds)
        {
          ic.PlaySound(ActionType.Always);
          ic.UpdateSounds();
          if (!ic.WasUsed) { ic.StopSounds(ActionType.OnUse); }
          if (!ic.WasSecondaryUsed) { ic.StopSounds(ActionType.OnSecondaryUse); }
        }
#endif
        ic.WasUsed = false;
        ic.WasSecondaryUsed = false;

        if (ic.IsActive || ic.UpdateWhenInactive)
        {
          if (_.condition <= 0.0f)
          {
            ic.UpdateBroken(deltaTime, cam);
          }
          else
          {
            ic.Update(deltaTime, cam);
#if CLIENT
            if (ic.IsActive)
            {
              if (ic.IsActiveTimer > 0.02f)
              {
                ic.PlaySound(ActionType.OnActive);
              }
              ic.IsActiveTimer += deltaTime;
            }
#endif
          }
        }
      }

      if (_.Removed) { return false; }

      bool needsWaterCheck = _.hasInWaterStatusEffects || _.hasNotInWaterStatusEffects;
      if (_.body != null && _.body.Enabled)
      {
        System.Diagnostics.Debug.Assert(_.body.FarseerBody.FixtureList != null);

        if (Math.Abs(_.body.LinearVelocity.X) > 0.01f || Math.Abs(_.body.LinearVelocity.Y) > 0.01f || _.transformDirty)
        {
          if (_.body.CollisionCategories != Category.None)
          {
            _.UpdateTransform();
          }
          if (_.CurrentHull == null && Level.Loaded != null && _.body.SimPosition.Y < ConvertUnits.ToSimUnits(Level.MaxEntityDepth))
          {
            Item.Spawner?.AddItemToRemoveQueue(_);
            return false;
          }
        }
        needsWaterCheck = true;
        _.UpdateNetPosition(deltaTime);
        if (_.inWater)
        {
          _.ApplyWaterForces();
          _.CurrentHull?.ApplyFlowForces(deltaTime, _);
        }
      }

      if (needsWaterCheck)
      {
        bool wasInWater = _.inWater;
        _.inWater = !_.inWaterProofContainer && _.IsInWater();
        if (_.inWater)
        {
          //the item has gone through the surface of the water
          if (!wasInWater && _.CurrentHull != null && _.body != null && _.body.LinearVelocity.Y < -1.0f)
          {

            // Note: #if CLIENT is needed because on server side Splash isn't compiled 
#if CLIENT
            _.Splash();
#endif

            if (_.GetComponent<Projectile>() is not { IsActive: true })
            {
              //slow the item down (not physically accurate, but looks good enough)
              _.body.LinearVelocity *= 0.2f;
            }
          }
        }
        if ((_.hasInWaterStatusEffects || _.hasNotInWaterStatusEffects) && _.condition > 0.0f)
        {
          _.ApplyStatusEffects(_.inWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
        }
        if (_.inWaterProofContainer && !_.hasNotInWaterStatusEffects)
        {
          needsWaterCheck = false;
        }
      }

      if (!needsWaterCheck &&
          _.updateableComponents.Count == 0 &&
          (_.aiTarget == null || !_.aiTarget.NeedsUpdate) &&
          !_.hasStatusEffectsOfType[(int)ActionType.Always] &&
          !_.hasStatusEffectsOfType[(int)containedEffectType] &&
          (_.body == null || !_.body.Enabled))
      {
#if CLIENT
        _.positionBuffer.Clear();
#endif
        _.isActive = false;
      }

      return false;
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Item.cs#L3233
    public static bool Item_Use_Replace(Item __instance, float deltaTime, Character user = null, Limb targetLimb = null, Entity useTarget = null, Character userForOnUsedEvent = null)
    {
      Item _ = __instance;

      if (_.RequireAimToUse && (user == null || !user.IsKeyDown(InputType.Aim)))
      {
        return false;
      }

      if (_.condition <= 0.0f) { return false; }

      var should = GameMain.LuaCs.Hook.Call<bool?>("item.use", new object[] { _, user, targetLimb, useTarget });

      if (should != null && should.Value) { return false; }

      bool remove = false;

      foreach (ItemComponent ic in _.components)
      {
        bool isControlled = false;
#if CLIENT
        isControlled = user == Character.Controlled;
#endif
        if (!ic.HasRequiredContainedItems(user, isControlled)) { continue; }
        if (ic.Use(deltaTime, user))
        {
          ic.WasUsed = true;
#if CLIENT
          ic.PlaySound(ActionType.OnUse, user);
#endif
          ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, user, targetLimb, useTarget: useTarget, user: user);
          ic.OnUsed.Invoke(new ItemComponent.ItemUseInfo(_, user ?? userForOnUsedEvent));
          if (ic.DeleteOnUse) { remove = true; }
        }
      }

      if (remove)
      {
        Item.Spawner.AddItemToRemoveQueue(_);
      }

      return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Item.cs#L2946
    public static bool Item_SendSignal_Replace(Signal signal, Connection connection, Item __instance)
    {
      Item _ = __instance;

      _.LastSentSignalRecipients.Clear();
      if (_.connections == null || connection == null) { return false; }

      signal.stepsTaken++;

      //if the signal has been passed through this item multiple times already, interrupt it to prevent infinite loops
      if (signal.stepsTaken > 5 && signal.source != null)
      {
        int duplicateRecipients = 0;
        foreach (var recipient in signal.source.LastSentSignalRecipients)
        {
          if (recipient == connection)
          {
            duplicateRecipients++;
            if (duplicateRecipients > 2) { return false; }
          }
        }
      }

      //use a coroutine to prevent infinite loops by creating a one 
      //frame delay if the "signal chain" gets too long
      if (signal.stepsTaken > 10)
      {
        //if there's an equal signal waiting to be sent
        //to the same connection, don't add a new one
        signal.stepsTaken = 0;
        bool duplicateFound = false;
        foreach (var s in _.delayedSignals)
        {
          if (s.Connection == connection && s.Signal.source == signal.source && s.Signal.value == signal.value && s.Signal.sender == signal.sender)
          {
            duplicateFound = true;
            break;
          }
        }
        if (!duplicateFound)
        {
          _.delayedSignals.Add((signal, connection));
          CoroutineManager.StartCoroutine(_.DelaySignal(signal, connection));
        }
      }
      else
      {
        if (connection.Effects != null && signal.value != "0" && !string.IsNullOrEmpty(signal.value))
        {
          foreach (StatusEffect effect in connection.Effects)
          {
            if (_.condition <= 0.0f && effect.type != ActionType.OnBroken) { continue; }
            _.ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step);
          }
        }
        signal.source ??= _;
        connection.SendSignal(signal);
      }

      return false;
    }


  }
}