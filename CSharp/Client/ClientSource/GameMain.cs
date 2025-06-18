// #define DEBUG
// Note: i didn't really test it with debug

using System;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Collections.Immutable;

using HarmonyLib;

using Barotrauma;
using Barotrauma.IO;
using Barotrauma.Media;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Steam;
using Barotrauma.Transition;
using Barotrauma.Tutorials;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

// If it's not enough GameMain.Update will throw names of assemblies to add
// you can try using reflection, but i'm a bit too lazy for that 
[assembly: IgnoresAccessChecksTo("MonoGame.Framework.Windows.NetStandard")]
[assembly: IgnoresAccessChecksTo("MonoGame.Framework.Linux.NetStandard")]

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientGameMain()
    {
      harmony.Patch(
        original: typeof(GameMain).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameMain_Update_Replace"))
      );

      harmony.Patch(
        original: typeof(GameMain).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameMain_Draw_Replace"))
      );
    }

    //https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/GameMain.cs#L1057
    public static bool GameMain_Draw_Replace(GameTime gameTime, GameMain __instance)
    {
      GameMain _ = __instance;

      Stopwatch sw = new Stopwatch();
      sw.Start();

      _.FixRazerCortex();

      double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

      if (Timing.FrameLimit > 0)
      {
        double step = 1.0 / Timing.FrameLimit;
        while (!GameSettings.CurrentConfig.Graphics.VSync && sw.Elapsed.TotalSeconds + deltaTime < step)
        {
          Thread.Sleep(1);
        }
      }

      GameMain.PerformanceCounter.Update(sw.Elapsed.TotalSeconds + deltaTime);

      if (_.loadingScreenOpen)
      {
        GameMain.TitleScreen.Draw(GameMain.spriteBatch, _.GraphicsDevice, (float)deltaTime);
      }
      else if (_.HasLoaded)
      {
        Screen.Selected.Draw(deltaTime, _.GraphicsDevice, GameMain.spriteBatch);
      }

      if (GameMain.DebugDraw && GUI.MouseOn != null)
      {
        GameMain.spriteBatch.Begin();
        if (PlayerInput.IsCtrlDown() && PlayerInput.KeyDown(Keys.G))
        {
          List<GUIComponent> hierarchy = new List<GUIComponent>();
          var currComponent = GUI.MouseOn;
          while (currComponent != null)
          {
            hierarchy.Add(currComponent);
            currComponent = currComponent.Parent;
          }

          Color[] colors = { Color.Lime, Color.Yellow, Color.Aqua, Color.Red };
          for (int index = 0; index < hierarchy.Count; index++)
          {
            var component = hierarchy[index];
            if (component is { MouseRect: var mouseRect, Rect: var rect })
            {
              if (mouseRect.IsEmpty) { mouseRect = rect; }
              mouseRect.Location += (index % 2, (index % 4) / 2);
              GUI.DrawRectangle(GameMain.spriteBatch, mouseRect, colors[index % 4]);
            }
          }
        }
        else
        {
          GUI.DrawRectangle(GameMain.spriteBatch, GUI.MouseOn.MouseRect, Color.Lime);
          GUI.DrawRectangle(GameMain.spriteBatch, GUI.MouseOn.Rect, Color.Cyan);
        }
        GameMain.spriteBatch.End();
      }

      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Draw", sw.ElapsedTicks);
      GameMain.PerformanceCounter.DrawTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);

      return false;
    }



    // Note: it doesn't work in dll mods because of that base.Update(fixedTime);
    // -> don't use it in dll mods

    //https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/GameMain.cs#L692
    public static bool GameMain_Update_Replace(GameTime gameTime, GameMain __instance)
    {
      GameMain _ = __instance;

      Timing.Accumulator += gameTime.ElapsedGameTime.TotalSeconds;
      if (Timing.Accumulator > Timing.AccumulatorMax)
      {
        //prevent spiral of death:
        //if the game's running too slowly then we have no choice but to skip a bunch of steps
        //otherwise it snowballs and becomes unplayable
        Timing.Accumulator = Timing.Step;
      }

      CrossThread.ProcessTasks();

      PlayerInput.UpdateVariable();

      if (GameMain.SoundManager != null)
      {
        if (GameMain.WindowActive || !GameSettings.CurrentConfig.Audio.MuteOnFocusLost)
        {
          GameMain.SoundManager.ListenerGain = GameMain.SoundManager.CompressionDynamicRangeGain;
        }
        else
        {
          GameMain.SoundManager.ListenerGain = 0.0f;
        }
      }

      while (Timing.Accumulator >= Timing.Step)
      {
        Timing.TotalTime += Timing.Step;
        if (!_.Paused)
        {
          Timing.TotalTimeUnpaused += Timing.Step;
        }
        Stopwatch sw = new Stopwatch();
        sw.Start();

        _.fixedTime.IsRunningSlowly = gameTime.IsRunningSlowly;
        TimeSpan addTime = new TimeSpan(0, 0, 0, 0, 16);
        _.fixedTime.ElapsedGameTime = addTime;
        _.fixedTime.TotalGameTime = _.fixedTime.TotalGameTime.Add(addTime);

        //  instead of base.Update(fixedTime);
        try
        {
          _._updateables.ForEachFilteredItem(Game.UpdateAction, _.fixedTime);
        }
        catch (Exception e)
        {
          log(Assembly.GetAssembly(typeof(Game)).FullName, Color.Yellow);
          log(e, Color.Orange);
        }
        //typeof(Game).GetMethod("Update", AccessTools.all).Invoke(_, new object[] { _.fixedTime });


        PlayerInput.Update(Timing.Step);

        SocialOverlay.Instance?.Update();

        if (_.loadingScreenOpen)
        {
          //reset accumulator if loading
          // -> less choppy loading screens because the screen is rendered after each update
          // -> no pause caused by leftover time in the accumulator when starting a new shift
          GameMain.ResetFrameTime();

          if (!GameMain.TitleScreen.PlayingSplashScreen)
          {
            SoundPlayer.Update((float)Timing.Step);
            GUI.ClearUpdateList();
            GUI.UpdateGUIMessageBoxesOnly((float)Timing.Step);
          }

          if (GameMain.TitleScreen.LoadState >= 100.0f && !GameMain.TitleScreen.PlayingSplashScreen &&
              (!GameMain.waitForKeyHit || ((PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0 || PlayerInput.PrimaryMouseButtonClicked()) && GameMain.WindowActive)))
          {
            _.loadingScreenOpen = false;
          }

#if DEBUG
          if (PlayerInput.KeyHit(Keys.LeftShift))
          {
            GameMain.CancelQuickStart = !GameMain.CancelQuickStart;
          }

          if (GameMain.TitleScreen.LoadState >= 100.0f && !GameMain.TitleScreen.PlayingSplashScreen &&
              (GameSettings.CurrentConfig.AutomaticQuickStartEnabled ||
               GameSettings.CurrentConfig.AutomaticCampaignLoadEnabled ||
               GameSettings.CurrentConfig.TestScreenEnabled) && FirstLoad && !GameMain.CancelQuickStart)
          {
            _.loadingScreenOpen = false;
            FirstLoad = false;

            if (GameSettings.CurrentConfig.TestScreenEnabled)
            {
              TestScreen.Select();
            }
            else if (GameSettings.CurrentConfig.AutomaticQuickStartEnabled)
            {
              GameMain.MainMenuScreen.QuickStart();
            }
            else if (GameSettings.CurrentConfig.AutomaticCampaignLoadEnabled)
            {
              var saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);
              if (saveFiles.Count() > 0)
              {
                try
                {
                  SaveUtil.LoadGame(CampaignDataPath.CreateRegular(saveFiles.OrderBy(file => file.SaveTime).Last().FilePath));
                }
                catch (Exception e)
                {
                  DebugConsole.ThrowError("Loading save \"" + saveFiles.Last() + "\" failed", e);
                  return;
                }
              }
            }
          }
#endif

          GameMain.Client?.Update((float)Timing.Step);
        }
        else if (_.HasLoaded)
        {
          if (_.ConnectCommand.TryUnwrap(out var connectCommand))
          {
            DebugConsole.NewMessage($"Processing connect command: {connectCommand}...", Color.Lime);
            if (GameMain.Client != null)
            {
              GameMain.Client.Quit();
              GameMain.Client = null;
            }
            GameMain.MainMenuScreen.Select();

            string clientNameString = _.clientName ?? MultiplayerPreferences.Instance.PlayerName.FallbackNullOrEmpty(SteamManager.GetUsername());

            if (connectCommand.SteamLobbyIdOption.TryUnwrap(out var lobbyId))
            {
              DebugConsole.NewMessage($"Connecting to lobby ID {lobbyId}...", Color.Lime);
              SteamManager.JoinLobby(lobbyId.Value, joinServer: true);
            }
            else if ((connectCommand.NameAndP2PEndpointsOption.TryUnwrap(out var nameAndEndpoint) && nameAndEndpoint is { ServerName: var serverName, Endpoints: var endpoints }))
            {
              GameMain.Client = new GameClient(clientNameString,
                  endpoints.Cast<Endpoint>().ToImmutableArray(),
                  string.IsNullOrWhiteSpace(serverName) ? endpoints.First().StringRepresentation : serverName,
                  Option<int>.None());
              DebugConsole.NewMessage($"Connecting to endpoint {endpoints.First().StringRepresentation}...", Color.Lime);
            }
            else if ((connectCommand.NameAndLidgrenEndpointOption.TryUnwrap(out var nameAndLidgrenEndpoint) && nameAndLidgrenEndpoint is { ServerName: var lidgrenServerName, Endpoint: var endpoint }))
            {
              GameMain.Client = new GameClient(
                  clientNameString,
                  endpoint,
                  string.IsNullOrWhiteSpace(lidgrenServerName) ? endpoint.StringRepresentation : lidgrenServerName,
                  Option<int>.None());
            }
            else
            {
              DebugConsole.NewMessage($"Cannot connect: unrecognized connect command.", Color.Lime);
            }

            _.ConnectCommand = Option<ConnectCommand>.None();
          }

          SoundPlayer.Update((float)Timing.Step);

          if ((PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
              && (PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift))
              && PlayerInput.KeyHit(Keys.Tab)
              && SocialOverlay.Instance is { } socialOverlay)
          {
            socialOverlay.IsOpen = !socialOverlay.IsOpen;
            if (socialOverlay.IsOpen)
            {
              socialOverlay.RefreshFriendList();
            }
          }

          if (PlayerInput.KeyHit(Keys.Escape) && GameMain.WindowActive)
          {
            // Check if a text input is selected.
            if (GUI.KeyboardDispatcher.Subscriber != null)
            {
              if (GUI.KeyboardDispatcher.Subscriber is GUITextBox textBox)
              {
                textBox.Deselect();
              }
              GUI.KeyboardDispatcher.Subscriber = null;
            }
            else if (SocialOverlay.Instance is { IsOpen: true })
            {
              SocialOverlay.Instance.IsOpen = false;
            }
            //if a verification prompt (are you sure you want to x) is open, close it
            else if (GUIMessageBox.VisibleBox is GUIMessageBox { UserData: "verificationprompt" })
            {
              ((GUIMessageBox)GUIMessageBox.VisibleBox).Close();
            }
            else if (GUIMessageBox.VisibleBox?.UserData is RoundSummary { ContinueButton.Visible: true })
            {
              GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
            }
            else if (ObjectiveManager.ContentRunning)
            {
              ObjectiveManager.CloseActiveContentGUI();
            }
            else if (GameSession.IsTabMenuOpen)
            {
              GameMain.gameSession.ToggleTabMenu();
            }
            else if (GUIMessageBox.VisibleBox is GUIMessageBox { UserData: "bugreporter" })
            {
              ((GUIMessageBox)GUIMessageBox.VisibleBox).Close();
            }
            else if (GUI.PauseMenuOpen)
            {
              GUI.TogglePauseMenu();
            }
            else if (GameMain.GameSession?.Campaign is { ShowCampaignUI: true, ForceMapUI: false })
            {
              GameMain.GameSession.Campaign.ShowCampaignUI = false;
            }
            //open the pause menu if not controlling a character OR if the character has no UIs active that can be closed with ESC
            else if ((Character.Controlled == null || !itemHudActive())
                && CharacterHealth.OpenHealthWindow == null
                && !CrewManager.IsCommandInterfaceOpen
                && !(Screen.Selected is SubEditorScreen editor && !editor.WiringMode && Character.Controlled?.SelectedItem != null))
            {
              // Otherwise toggle pausing, unless another window/interface is open.
              GUI.TogglePauseMenu();
            }

            static bool itemHudActive()
            {
              if (Character.Controlled?.SelectedItem == null) { return false; }
              return
                  Character.Controlled.SelectedItem.ActiveHUDs.Any(ic => ic.GuiFrame != null) ||
                  ((Character.Controlled.ViewTarget as Item)?.Prefab?.FocusOnSelected ?? false);
            }
          }

#if DEBUG
          if (GameMain.NetworkMember == null)
          {
            if (PlayerInput.KeyHit(Keys.P) && !(GUI.KeyboardDispatcher.Subscriber is GUITextBox))
            {
              DebugConsole.Paused = !DebugConsole.Paused;
            }
          }
#endif

          GUI.ClearUpdateList();
          _.Paused =
              (DebugConsole.IsOpen || DebugConsole.Paused ||
                  GUI.PauseMenuOpen || GUI.SettingsMenuOpen ||
                  (GameMain.GameSession?.GameMode is TutorialMode && ObjectiveManager.ContentRunning)) &&
              (GameMain.NetworkMember == null || !GameMain.NetworkMember.GameStarted);
          if (GameMain.GameSession?.GameMode != null && GameMain.GameSession.GameMode.Paused)
          {
            _.Paused = true;
            GameMain.GameSession.GameMode.UpdateWhilePaused((float)Timing.Step);
          }

#if !DEBUG
          if (GameMain.NetworkMember == null && !GameMain.WindowActive && !_.Paused && true && GameSettings.CurrentConfig.PauseOnFocusLost &&
              Screen.Selected != GameMain.MainMenuScreen && Screen.Selected != GameMain.ServerListScreen && Screen.Selected != GameMain.NetLobbyScreen &&
              Screen.Selected != GameMain.SubEditorScreen && Screen.Selected != GameMain.LevelEditorScreen)
          {
            GUI.TogglePauseMenu();
            _.Paused = true;
          }
#endif

          Screen.Selected.AddToGUIUpdateList();

          LuaCsLogger.AddToGUIUpdateList();

          GameMain.Client?.AddToGUIUpdateList();

          SubmarinePreview.AddToGUIUpdateList();

          FileSelection.AddToGUIUpdateList();

          DebugConsole.AddToGUIUpdateList();

          DebugConsole.Update((float)Timing.Step);

          if (!_.Paused)
          {
            Screen.Selected.Update(Timing.Step);
          }
          else if (ObjectiveManager.ContentRunning && GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
          {
            ObjectiveManager.VideoPlayer.Update();
            tutorialMode.Update((float)Timing.Step);
          }
          else
          {
            if (Screen.Selected.Cam == null)
            {
              DebugConsole.Paused = false;
            }
            else
            {
              Screen.Selected.Cam.MoveCamera((float)Timing.Step, allowMove: DebugConsole.Paused, allowZoom: DebugConsole.Paused);
            }
          }

          GameMain.Client?.Update((float)Timing.Step);

          GUI.Update((float)Timing.Step);

#if DEBUG
          if (DebugDraw && GUI.MouseOn != null && PlayerInput.IsCtrlDown() && PlayerInput.KeyHit(Keys.G))
          {
            List<GUIComponent> hierarchy = new List<GUIComponent>();
            var currComponent = GUI.MouseOn;
            while (currComponent != null)
            {
              hierarchy.Add(currComponent);
              currComponent = currComponent.Parent;
            }
            DebugConsole.NewMessage("*********************");
            foreach (var component in hierarchy)
            {
              if (component is { MouseRect: var mouseRect, Rect: var rect })
              {
                DebugConsole.NewMessage($"{component.GetType().Name} {component.Style?.Name ?? "[null]"} {rect.Bottom} {mouseRect.Bottom}", mouseRect != rect ? Color.Lime : Color.Red);
              }
            }
          }
#endif
        }

        CoroutineManager.Update(_.Paused, (float)Timing.Step);

        SteamManager.Update((float)Timing.Step);
        EosInterface.Core.Update();

        TaskPool.Update();

        GameMain.SoundManager?.Update();

        GameMain.LuaCs.Update();

        Timing.Accumulator -= Timing.Step;

        GameMain.updateCount++;

        sw.Stop();
        GameMain.PerformanceCounter.AddElapsedTicks("Update", sw.ElapsedTicks);
        GameMain.PerformanceCounter.UpdateTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);
      }

      if (!_.Paused)
      {
        Timing.Alpha = Timing.Accumulator / Timing.Step;
      }

      if (GameMain.performanceCounterTimer.ElapsedMilliseconds > 1000)
      {
        GameMain.CurrentUpdateRate = (int)Math.Round(GameMain.updateCount / (double)(GameMain.performanceCounterTimer.ElapsedMilliseconds / 1000.0));
        GameMain.performanceCounterTimer.Restart();
        GameMain.updateCount = 0;
      }

      return false;
    }

  }
}