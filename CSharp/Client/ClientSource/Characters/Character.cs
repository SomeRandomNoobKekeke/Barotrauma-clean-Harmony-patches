// #define CLIENT
// #define SERVER

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
    public static void PatchClientCharacter()
    {
      harmony.Patch(
        original: typeof(Character).GetMethod("UpdateInteractablesInRange", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_UpdateInteractablesInRange_Replace"))
      );

      harmony.Patch(
        original: typeof(Character).GetMethod("ControlLocalPlayer", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_ControlLocalPlayer_Replace"))
      );

      harmony.Patch(
        original: typeof(Character).GetMethod("UpdateControlled", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Character_UpdateControlled_Replace"))
      );
    }

    public static bool Character_UpdateControlled_Replace(Character __instance, float deltaTime, Camera cam)
    {
      Character _ = __instance;

      if (Character.controlled != _) { return false; }

      _.ControlLocalPlayer(deltaTime, cam);

      Barotrauma.Lights.LightManager.ViewTarget = _;
      CharacterHUD.Update(deltaTime, _, cam);

      if (_.hudProgressBars.Any())
      {
        foreach (var progressBar in _.hudProgressBars)
        {
          if (progressBar.Value.FadeTimer <= 0.0f)
          {
            _.progressBarRemovals.Add(progressBar);
            continue;
          }
          progressBar.Value.Update(deltaTime);
        }
        if (_.progressBarRemovals.Any())
        {
          _.progressBarRemovals.ForEach(pb => _.hudProgressBars.Remove(pb.Key));
          _.progressBarRemovals.Clear();
        }
      }

      return false;
    }

    public static bool Character_ControlLocalPlayer_Replace(Character __instance, float deltaTime, Camera cam, bool moveCam = true)
    {
      Character _ = __instance;

      if (Character.DisableControls || GUI.InputBlockingMenuOpen)
      {
        foreach (Key key in _.keys)
        {
          if (key == null) { continue; }
          key.Reset();
        }
        if (GUI.InputBlockingMenuOpen || ConversationAction.IsDialogOpen)
        {
          _.cursorPosition =
              _.Position + PlayerInput.MouseSpeed.ClampLength(10.0f); //apply a little bit of movement to the cursor pos to prevent AFK kicking
        }
      }
      else
      {
        _.wasFiring |= _.keys[(int)InputType.Aim].Held && _.keys[(int)InputType.Shoot].Held;
        for (int i = 0; i < _.keys.Length; i++)
        {
          _.keys[i].SetState();
        }

        if (CharacterInventory.IsMouseOnInventory &&
            !_.keys[(int)InputType.Aim].Held &&
            CharacterHUD.ShouldDrawInventory(_))
        {
          ResetInputIfPrimaryMouse(InputType.Use);
          ResetInputIfPrimaryMouse(InputType.Shoot);
          ResetInputIfPrimaryMouse(InputType.Select);
          void ResetInputIfPrimaryMouse(InputType inputType)
          {
            if (GameSettings.CurrentConfig.KeyMap.Bindings[inputType].MouseButton == MouseButton.PrimaryMouse)
            {
              _.keys[(int)inputType].Reset();
            }
          }
        }

        _.ShowInteractionLabels = _.keys[(int)InputType.ShowInteractionLabels].Held;

        if (_.ShowInteractionLabels)
        {
          _.focusedItem = InteractionLabelManager.HoveredItem;
        }

        //if we were firing (= pressing the aim and shoot keys at the same time)
        //and the fire key is the same as Select or Use, reset the key to prevent accidentally selecting/using items
        if (_.wasFiring && !_.keys[(int)InputType.Shoot].Held)
        {
          if (GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Shoot] == GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Select])
          {
            _.keys[(int)InputType.Select].Reset();
          }
          if (GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Shoot] == GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Use])
          {
            _.keys[(int)InputType.Use].Reset();
          }
          _.wasFiring = false;
        }

        float targetOffsetAmount = 0.0f;
        if (moveCam)
        {
          if (!_.IsProtectedFromPressure && (_.AnimController.CurrentHull == null || _.AnimController.CurrentHull.LethalPressure > 0.0f))
          {
            //wait until the character has been in pressure for one second so the zoom doesn't
            //"flicker" in and out if the pressure fluctuates around the minimum threshold
            _.pressureEffectTimer += deltaTime;
            if (_.pressureEffectTimer > 1.0f)
            {
              float pressure = _.AnimController.CurrentHull == null ? 100.0f : _.AnimController.CurrentHull.LethalPressure;
              float zoomInEffectStrength = MathHelper.Clamp(pressure / 100.0f, 0.0f, 1.0f);
              cam.Zoom = MathHelper.Lerp(cam.Zoom,
                  cam.DefaultZoom + (Math.Max(pressure, 10) / 150.0f) * Rand.Range(0.9f, 1.1f),
                  zoomInEffectStrength);
            }
          }
          else
          {
            _.pressureEffectTimer = 0.0f;
          }

          if (_.IsHumanoid)
          {
            cam.OffsetAmount = 250.0f;// MathHelper.Lerp(cam.OffsetAmount, 250.0f, deltaTime);
          }
          else
          {
            //increased visibility range when controlling large a non-humanoid
            cam.OffsetAmount = MathHelper.Clamp(_.Mass, 250.0f, 1500.0f);
          }
        }

        _.UpdateLocalCursor(cam);

        if (_.IsKeyHit(InputType.ToggleRun))
        {
          _.ToggleRun = !_.ToggleRun;
        }

        Vector2 mouseSimPos = ConvertUnits.ToSimUnits(_.cursorPosition);
        if (GUI.PauseMenuOpen)
        {
          cam.OffsetAmount = targetOffsetAmount = 0.0f;
        }
        else if (Barotrauma.Lights.LightManager.ViewTarget is Item item && item.Prefab.FocusOnSelected)
        {
          cam.OffsetAmount = targetOffsetAmount = item.Prefab.OffsetOnSelected * item.OffsetOnSelectedMultiplier;
        }
        else if (_.HeldItems.SelectMany(static item => item.GetComponents<Holdable>())
                          .Where(static holdable => holdable.Aimable)
                          .MaxOrNull(static holdable => holdable.CameraAimOffset) is float maxOffset
            && maxOffset > 0f && _.IsKeyDown(InputType.Aim))
        {
          cam.OffsetAmount = targetOffsetAmount = maxOffset;
        }
        else if (_.SelectedItem != null && _.ViewTarget == null && !_.IsIncapacitated &&
            _.SelectedItem.Components.Any(ic => ic?.GuiFrame != null && ic.ShouldDrawHUD(_)))
        {
          cam.OffsetAmount = targetOffsetAmount = 0.0f;
          _.cursorPosition =
              _.Position +
              PlayerInput.MouseSpeed.ClampLength(10.0f); //apply a little bit of movement to the cursor pos to prevent AFK kicking
        }
        else if (!GameSettings.CurrentConfig.EnableMouseLook)
        {
          cam.OffsetAmount = targetOffsetAmount = 0.0f;
        }
        else if (Barotrauma.Lights.LightManager.ViewTarget == _)
        {
          if (GUI.PauseMenuOpen || _.IsIncapacitated)
          {
            if (deltaTime > 0.0f)
            {
              cam.OffsetAmount = targetOffsetAmount = 0.0f;
            }
          }
          else if (Character.IsMouseOnUI)
          {
            targetOffsetAmount = cam.OffsetAmount;
          }
          else if (Vector2.DistanceSquared(_.AnimController.Limbs[0].SimPosition, mouseSimPos) > 1.0f)
          {
            Body body = Submarine.CheckVisibility(_.AnimController.Limbs[0].SimPosition, mouseSimPos);
            Structure structure = body?.UserData as Structure;

            float sightDist = Submarine.LastPickedFraction;
            if (body?.UserData is Structure && !((Structure)body.UserData).CastShadow)
            {
              sightDist = 1.0f;
            }
            targetOffsetAmount = Math.Max(250.0f, sightDist * 500.0f);
          }
        }

        cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, targetOffsetAmount, 0.05f);
        _.DoInteractionUpdate(deltaTime, mouseSimPos);
      }

      if (!GUI.InputBlockingMenuOpen)
      {
        if (_.SelectedItem != null &&
            (_.SelectedItem.ActiveHUDs.Any(ic => ic.GuiFrame != null && ic.CloseByClickingOutsideGUIFrame && HUD.CloseHUD(ic.GuiFrame.Rect)) ||
            ((_.ViewTarget as Item)?.Prefab.FocusOnSelected ?? false) && PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape)))
        {
          if (GameMain.Client != null)
          {
            //emulate a Deselect input to get the character to deselect the item server-side
            _.EmulateInput(InputType.Deselect);
          }
          //reset focus to prevent us from accidentally interacting with another entity
          _.focusedItem = null;
          _.FocusedCharacter = null;
          _.findFocusedTimer = 0.2f;
          _.SelectedItem = null;
        }
      }

      Character.DisableControls = false;

      return false;
    }


    public static bool Character_UpdateInteractablesInRange_Replace(Character __instance)
    {
      Character _ = __instance;

      // keep two lists to detect changes to the current state of interactables in range
      _.previousInteractablesInRange.Clear();
      _.previousInteractablesInRange.AddRange(_.interactablesInRange);

      _.interactablesInRange.Clear();

      //use the list of visible entities if it exists
      var entityList = Submarine.VisibleEntities ?? Item.ItemList;

      foreach (MapEntity entity in entityList)
      {
        if (entity is not Item item) { continue; }

        if (item.body != null && !item.body.Enabled) { continue; }

        if (item.ParentInventory != null) { continue; }

        if (item.Prefab.RequireCampaignInteract &&
            item.CampaignInteractionType == CampaignMode.InteractionType.None)
        {
          continue;
        }

        if (Screen.Selected is SubEditorScreen { WiringMode: true } &&
            item.GetComponent<ConnectionPanel>() == null)
        {
          continue;
        }

        if (_.CanInteractWith(item))
        {
          _.interactablesInRange.Add(item);
        }
      }

      if (!_.interactablesInRange.SequenceEqual(_.previousInteractablesInRange))
      {
        InteractionLabelManager.RefreshInteractablesInRange(_.interactablesInRange);
      }

      return false;
    }

  }
}