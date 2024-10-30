using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;
using FarseerPhysics.Dynamics;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedGameScreen()
    {
      harmony.Patch(
        original: typeof(GameScreen).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameScreen_Update_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Screens/GameScreen.cs#L99
    public static bool GameScreen_Update_Replace(double deltaTime, GameScreen __instance)
    {
      GameScreen _ = __instance;

#if RUN_PHYSICS_IN_SEPARATE_THREAD
      physicsTime += deltaTime;
      lock (updateLock)
      {
#endif


#if DEBUG && CLIENT
      if (GameMain.GameSession != null && !DebugConsole.IsOpen && GUI.KeyboardDispatcher.Subscriber == null)
      {
        if (GameMain.GameSession.Level != null && GameMain.GameSession.Submarine != null)
        {
          Submarine closestSub = Submarine.FindClosest(cam.WorldViewCenter) ?? GameMain.GameSession.Submarine;

          Vector2 targetMovement = Vector2.Zero;
          if (PlayerInput.KeyDown(Keys.I)) { targetMovement.Y += 1.0f; }
          if (PlayerInput.KeyDown(Keys.K)) { targetMovement.Y -= 1.0f; }
          if (PlayerInput.KeyDown(Keys.J)) { targetMovement.X -= 1.0f; }
          if (PlayerInput.KeyDown(Keys.L)) { targetMovement.X += 1.0f; }

          if (targetMovement != Vector2.Zero)
          {
            closestSub.ApplyForce(targetMovement * closestSub.SubBody.Body.Mass * 100.0f);
          }
        }
      }
#endif

#if CLIENT
      GameMain.LightManager?.Update((float)deltaTime);
#endif

      _.GameTime += deltaTime;

      foreach (PhysicsBody body in PhysicsBody.List)
      {
        if (body.Enabled && body.BodyType != FarseerPhysics.BodyType.Static) { body.Update(); }
      }
      MapEntity.ClearHighlightedEntities();

#if CLIENT
      var sw = new System.Diagnostics.Stopwatch();
      sw.Start();
#endif

      GameMain.GameSession?.Update((float)deltaTime);

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:GameSession", sw.ElapsedTicks);
      sw.Restart();

      GameMain.ParticleManager.Update((float)deltaTime);

      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Particles", sw.ElapsedTicks);
      sw.Restart();

      if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime, _.cam);

      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Level", sw.ElapsedTicks);

      if (Character.Controlled is { } controlled)
      {
        if (controlled.SelectedItem != null && controlled.CanInteractWith(controlled.SelectedItem))
        {
          controlled.SelectedItem.UpdateHUD(_.cam, controlled, (float)deltaTime);
        }
        if (controlled.Inventory != null)
        {
          foreach (Item item in controlled.Inventory.AllItems)
          {
            if (controlled.HasEquippedItem(item))
            {
              item.UpdateHUD(_.cam, controlled, (float)deltaTime);
            }
          }
        }
      }

      sw.Restart();

      Character.UpdateAll((float)deltaTime, _.cam);
#elif SERVER
            if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime, Camera.Instance);
            Character.UpdateAll((float)deltaTime, Camera.Instance);
#endif


#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Character", sw.ElapsedTicks);
      sw.Restart();
#endif

      StatusEffect.UpdateAll((float)deltaTime);

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:StatusEffects", sw.ElapsedTicks);
      sw.Restart();

      if (Character.Controlled != null &&
          Barotrauma.Lights.LightManager.ViewTarget != null)
      {
        Vector2 targetPos = Barotrauma.Lights.LightManager.ViewTarget.WorldPosition;
        if (Barotrauma.Lights.LightManager.ViewTarget == Character.Controlled &&
            (CharacterHealth.OpenHealthWindow != null || CrewManager.IsCommandInterfaceOpen || ConversationAction.IsDialogOpen))
        {
          Vector2 screenTargetPos = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) * 0.5f;
          if (CharacterHealth.OpenHealthWindow != null)
          {
            screenTargetPos.X = GameMain.GraphicsWidth * (CharacterHealth.OpenHealthWindow.Alignment == Alignment.Left ? 0.6f : 0.4f);
          }
          else if (ConversationAction.IsDialogOpen)
          {
            screenTargetPos.Y = GameMain.GraphicsHeight * 0.4f;
          }
          Vector2 screenOffset = screenTargetPos - new Vector2(GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight / 2);
          screenOffset.Y = -screenOffset.Y;
          targetPos -= screenOffset / _.cam.Zoom;
        }
        _.cam.TargetPos = targetPos;
      }

      _.cam.MoveCamera((float)deltaTime, allowZoom: GUI.MouseOn == null && !Inventory.IsMouseOnInventory);

      Character.Controlled?.UpdateLocalCursor(_.cam);
#endif

      foreach (Submarine sub in Submarine.Loaded)
      {
        sub.SetPrevTransform(sub.Position);
      }

      foreach (PhysicsBody body in PhysicsBody.List)
      {
        if (body.Enabled && body.BodyType != FarseerPhysics.BodyType.Static)
        {
          body.SetPrevTransform(body.SimPosition, body.Rotation);
        }
      }

#if CLIENT
      MapEntity.UpdateAll((float)deltaTime, _.cam);
#elif SERVER
            MapEntity.UpdateAll((float)deltaTime, Camera.Instance);
#endif

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:MapEntity", sw.ElapsedTicks);
      sw.Restart();
#endif
      Character.UpdateAnimAll((float)deltaTime);

#if CLIENT
      Ragdoll.UpdateAll((float)deltaTime, _.cam);
#elif SERVER
            Ragdoll.UpdateAll((float)deltaTime, Camera.Instance);
#endif

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Ragdolls", sw.ElapsedTicks);
      sw.Restart();
#endif

      foreach (Submarine sub in Submarine.Loaded)
      {
        sub.Update((float)deltaTime);
      }

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Submarine", sw.ElapsedTicks);
      sw.Restart();
#endif

#if !RUN_PHYSICS_IN_SEPARATE_THREAD
      try
      {
        GameMain.World.Step((float)Timing.Step);
      }
      catch (WorldLockedException e)
      {
        string errorMsg = "Attempted to modify the state of the physics simulation while a time step was running.";
        DebugConsole.ThrowError(errorMsg, e);
        GameAnalyticsManager.AddErrorEventOnce("GameScreen.Update:WorldLockedException" + e.Message, GameAnalyticsManager.ErrorSeverity.Critical, errorMsg);
      }
#endif


#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Physics", sw.ElapsedTicks);
      _.UpdateProjSpecific(deltaTime);
#endif
      // it seems that on server side this method is not even compiled because it's empty
      // _.UpdateProjSpecific(deltaTime);

#if RUN_PHYSICS_IN_SEPARATE_THREAD
      }
#endif

      return false;
    }
  }
}