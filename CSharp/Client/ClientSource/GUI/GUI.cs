using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using EventInput;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Immutable;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientGUI()
    {
      harmony.Patch(
        original: typeof(GUI).GetMethod("Draw", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GUI_Draw_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/GUI/GUI.cs#L253
    public static bool GUI_Draw_Replace(Camera cam, SpriteBatch spriteBatch)
    {
      lock (GUI.mutex)
      {
        GUI.usedIndicatorAngles.Clear();

        if (GUI.ScreenChanged)
        {
          GUI.updateList.Clear();
          GUI.updateListSet.Clear();
          Screen.Selected?.AddToGUIUpdateList();
          GUI.ScreenChanged = false;
        }

        foreach (GUIComponent c in GUI.updateList)
        {
          c.DrawAuto(spriteBatch);
        }

        // always draw IME preview on top of everything else
        foreach (GUIComponent c in GUI.updateList)
        {
          if (c is not GUITextBox box) { continue; }
          box.DrawIMEPreview(spriteBatch);
        }

        if (GUI.ScreenOverlayColor.A > 0.0f)
        {
          GUI.DrawRectangle(
              spriteBatch,
              new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
              GUI.ScreenOverlayColor, true);
        }

#if UNSTABLE
        string line1 = "Barotrauma Unstable v" + GameMain.Version;
        string line2 = "(" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")";

        Rectangle watermarkRect = new Rectangle(-50, GameMain.GraphicsHeight - 80, 50 + (int)(Math.Max(GUIStyle.LargeFont.MeasureString(line1).X, GUIStyle.Font.MeasureString(line2).X) * 1.2f), 100);
        float alpha = 1.0f;

        int yOffset = 0;

        if (Screen.Selected == GameMain.GameScreen)
        {
          yOffset = (int)(-HUDLayoutSettings.ChatBoxArea.Height * 1.2f);
          watermarkRect.Y += yOffset;
        }

        if (Screen.Selected == GameMain.GameScreen || Screen.Selected == GameMain.SubEditorScreen)
        {
          alpha = 0.2f;
        }

        GUIStyle.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
            spriteBatch, watermarkRect, Color.Black * 0.8f * alpha);
        GUIStyle.LargeFont.GUI.DrawString(spriteBatch, line1,
        new Vector2(10, GameMain.GraphicsHeight - 30 - GUIStyle.LargeFont.MeasureString(line1).Y + yOffset), Color.White * 0.6f * alpha);
        GUIStyle.Font.GUI.DrawString(spriteBatch, line2,
        new Vector2(10, GameMain.GraphicsHeight - 30 + yOffset), Color.White * 0.6f * alpha);

        if (Screen.Selected != GameMain.GameScreen)
        {
          var buttonRect =
              new Rectangle(20 + (int)Math.Max(GUIStyle.LargeFont.MeasureString(line1).X, GUIStyle.Font.MeasureString(line2).X), GameMain.GraphicsHeight - (int)(45 * GUI.Scale) + yOffset, (int)(150 * GUI.Scale), (int)(40 * GUI.Scale));
          if (DrawButton(spriteBatch, buttonRect, "Report Bug", GUIStyle.GetComponentStyle("GUIBugButton").Color * 0.8f))
          {
            GameMain.Instance.ShowBugReporter();
          }
        }
#endif

        if (GUI.DisableHUD)
        {
          GUI.DrawSavingIndicator(spriteBatch);
          return false;
        }

        float startY = 10.0f;
        float yStep = GUI.AdjustForTextScale(18) * GUI.yScale;
        if (GameMain.ShowFPS || GameMain.DebugDraw || GameMain.ShowPerf)
        {
          float y = startY;
          GUI.DrawString(spriteBatch, new Vector2(10, y),
              "FPS: " + Math.Round(GameMain.PerformanceCounter.AverageFramesPerSecond),
              Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          if (GameMain.GameSession != null && GameMain.GameSession.RoundDuration > 1.0)
          {
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                $"Physics: {GameMain.CurrentUpdateRate}",
                (GameMain.CurrentUpdateRate < Timing.FixedUpdateRate) ? Color.Red : Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }
          if (GameMain.DebugDraw || GameMain.ShowPerf)
          {
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Active lights: " + Barotrauma.Lights.LightManager.ActiveLightCount,
                Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Physics: " + GameMain.World.UpdateTime.TotalMilliseconds + " ms",
                Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            y += yStep;
            try
            {
              GUI.DrawString(spriteBatch, new Vector2(10, y),
                  $"Bodies: {GameMain.World.BodyList.Count} ({GameMain.World.BodyList.Count(b => b != null && b.Awake && b.Enabled)} awake, {GameMain.World.BodyList.Count(b => b != null && b.Awake && b.BodyType == BodyType.Dynamic && b.Enabled)} dynamic)",
                  Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            }
            catch (InvalidOperationException)
            {
              DebugConsole.AddWarning("Exception while rendering debug info. Physics bodies may have been created or removed while rendering.");
            }
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Particle count: " + GameMain.ParticleManager.ParticleCount + "/" + GameMain.ParticleManager.MaxParticles,
                Color.Lerp(GUIStyle.Green, GUIStyle.Red, (GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles)), Color.Black * 0.5f, 0, GUIStyle.SmallFont);

          }
        }

        if (GameMain.ShowPerf)
        {
          float x = 400;
          float y = startY;
          GUI.DrawString(spriteBatch, new Vector2(x, y),
              "Draw - Avg: " + GameMain.PerformanceCounter.DrawTimeGraph.Average().ToString("0.00") + " ms" +
              " Max: " + GameMain.PerformanceCounter.DrawTimeGraph.LargestValue().ToString("0.00") + " ms",
              GUIStyle.Green, Color.Black * 0.8f, font: GUIStyle.SmallFont);
          y += yStep;
          GameMain.PerformanceCounter.DrawTimeGraph.Draw(spriteBatch, new Rectangle((int)x, (int)y, 170, 50), color: GUIStyle.Green);
          y += yStep * 4;

          GUI.DrawString(spriteBatch, new Vector2(x, y),
              "Update - Avg: " + GameMain.PerformanceCounter.UpdateTimeGraph.Average().ToString("0.00") + " ms" +
              " Max: " + GameMain.PerformanceCounter.UpdateTimeGraph.LargestValue().ToString("0.00") + " ms",
              Color.LightBlue, Color.Black * 0.8f, font: GUIStyle.SmallFont);
          y += yStep;
          GameMain.PerformanceCounter.UpdateTimeGraph.Draw(spriteBatch, new Rectangle((int)x, (int)y, 170, 50), color: Color.LightBlue);
          y += yStep * 4;
          foreach (string key in GameMain.PerformanceCounter.GetSavedIdentifiers.OrderBy(i => i))
          {
            float elapsedMillisecs = GameMain.PerformanceCounter.GetAverageElapsedMillisecs(key);

            int categoryDepth = key.Count(c => c == ':');
            //color the more fine-grained counters red more easily (ok for the whole Update to take a longer time than specific part of the update)
            float runningSlowThreshold = 10.0f / categoryDepth;
            GUI.DrawString(spriteBatch, new Vector2(x + categoryDepth * 15, y),
                key.Split(':').Last() + ": " + elapsedMillisecs.ToString("0.00"),
                ToolBox.GradientLerp(elapsedMillisecs / runningSlowThreshold, Color.LightGreen, GUIStyle.Yellow, GUIStyle.Orange, GUIStyle.Red, Color.Magenta), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            y += yStep;
          }
          if (Powered.Grids != null)
          {
            GUI.DrawString(spriteBatch, new Vector2(x, y), "Grids: " + Powered.Grids.Count, Color.LightGreen, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            y += yStep;
          }
          if (Settings.EnableDiagnostics)
          {
            x += yStep * 2;
            GUI.DrawString(spriteBatch, new Vector2(x, y), "ContinuousPhysicsTime: " + GameMain.World.ContinuousPhysicsTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ContinuousPhysicsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(x, y + yStep), "ControllersUpdateTime: " + GameMain.World.ControllersUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ControllersUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(x, y + yStep * 2), "AddRemoveTime: " + GameMain.World.AddRemoveTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.AddRemoveTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(x, y + yStep * 3), "NewContactsTime: " + GameMain.World.NewContactsTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.NewContactsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(x, y + yStep * 4), "ContactsUpdateTime: " + GameMain.World.ContactsUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ContactsUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(x, y + yStep * 5), "SolveUpdateTime: " + GameMain.World.SolveUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.SolveUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }
        }

        if (GameMain.DebugDraw && !Submarine.Unloading && !(Screen.Selected is RoundSummaryScreen))
        {
          float y = startY + yStep * 6;

          if (Screen.Selected.Cam != null)
          {
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Camera pos: " + Screen.Selected.Cam.Position.ToPoint() + ", zoom: " + Screen.Selected.Cam.Zoom,
                Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }

          if (Submarine.MainSub != null)
          {
            y += yStep;
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Sub pos: " + Submarine.MainSub.WorldPosition.ToPoint(),
                Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }

          if (GUI.loadedSpritesText == null || DateTime.Now > GUI.loadedSpritesUpdateTime)
          {
            GUI.loadedSpritesText = "Loaded sprites: " + Sprite.LoadedSprites.Count() + "\n(" + Sprite.LoadedSprites.Select(s => s.FilePath).Distinct().Count() + " unique textures)";
            GUI.loadedSpritesUpdateTime = DateTime.Now + new TimeSpan(0, 0, seconds: 5);
          }
          y += yStep * 2;
          GUI.DrawString(spriteBatch, new Vector2(10, y), GUI.loadedSpritesText, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

          if (GUI.debugDrawSounds)
          {
            float soundTextY = 0;
            GUI.DrawString(spriteBatch, new Vector2(500, soundTextY),
                "Sounds (Ctrl+S to hide): ", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            soundTextY += yStep;

            GUI.DrawString(spriteBatch, new Vector2(500, soundTextY),
                "Current playback amplitude: " + GameMain.SoundManager.PlaybackAmplitude.ToString(), Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

            soundTextY += yStep;

            GUI.DrawString(spriteBatch, new Vector2(500, soundTextY),
                "Compressed dynamic range gain: " + GameMain.SoundManager.CompressionDynamicRangeGain.ToString(), Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

            soundTextY += yStep;

            GUI.DrawString(spriteBatch, new Vector2(500, soundTextY),
                "Loaded sounds: " + GameMain.SoundManager.LoadedSoundCount + " (" + GameMain.SoundManager.UniqueLoadedSoundCount + " unique)", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            soundTextY += yStep;

            for (int i = 0; i < SoundManager.SourceCount; i++)
            {
              Color clr = Color.White;
              string soundStr = i + ": ";
              SoundChannel playingSoundChannel = GameMain.SoundManager.GetSoundChannelFromIndex(SoundManager.SourcePoolIndex.Default, i);
              if (playingSoundChannel == null)
              {
                soundStr += "none";
                clr *= 0.5f;
              }
              else
              {
                soundStr += Path.GetFileNameWithoutExtension(playingSoundChannel.Sound.Filename);

#if DEBUG
                if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.G))
                {
                  if (PlayerInput.MousePosition.Y >= soundTextY && PlayerInput.MousePosition.Y <= soundTextY + 12)
                  {
                    GameMain.SoundManager.DebugSource(i);
                  }
                }
#endif

                if (playingSoundChannel.Looping)
                {
                  soundStr += " (looping)";
                  clr = Color.Yellow;
                }
                if (playingSoundChannel.IsStream)
                {
                  soundStr += " (streaming)";
                  clr = Color.Lime;
                }
                if (!playingSoundChannel.IsPlaying)
                {
                  soundStr += " (stopped)";
                  clr *= 0.5f;
                }
                else
                {
                  if (playingSoundChannel.Muffled)
                  {
                    soundStr += " (muffled)";
                    clr = Color.Lerp(clr, Color.LightGray, 0.5f);
                  }
                  if (playingSoundChannel.FadingOutAndDisposing)
                  {
                    soundStr += ". Fading out...";
                    clr = Color.Lerp(clr, Color.Black, 0.15f);
                  }
                }
              }

              GUI.DrawString(spriteBatch, new Vector2(500, soundTextY), soundStr, clr, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
              soundTextY += yStep;
            }
          }
          else
          {
            GUI.DrawString(spriteBatch, new Vector2(500, 0),
                "Ctrl+S to show sound debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }


          y += 185 * GUI.yScale;
          if (GUI.debugDrawEvents)
          {
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Ctrl+E to hide EventManager debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            GameMain.GameSession?.EventManager?.DebugDrawHUD(spriteBatch, y + 15 * GUI.yScale);
          }
          else
          {
            GUI.DrawString(spriteBatch, new Vector2(10, y),
                "Ctrl+E to show EventManager debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
          }

          if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
          {
            // TODO: TEST THIS
            if (GUI.debugDrawMetaData.Enabled)
            {
              string text = "Ctrl+M to hide campaign metadata debug info\n\n" +
                            $"Ctrl+1 to {(GUI.debugDrawMetaData.FactionMetadata ? "hide" : "show")} faction reputations, \n" +
                            $"Ctrl+2 to {(GUI.debugDrawMetaData.UpgradeLevels ? "hide" : "show")} upgrade levels, \n" +
                            $"Ctrl+3 to {(GUI.debugDrawMetaData.UpgradePrices ? "hide" : "show")} upgrade prices";
              Vector2 textSize = GUIStyle.SmallFont.MeasureString(text);
              Vector2 pos = new Vector2(GameMain.GraphicsWidth - (textSize.X + 10), 300);
              GUI.DrawString(spriteBatch, pos, text, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
              pos.Y += textSize.Y + 8;
              campaignMode.CampaignMetadata?.DebugDraw(spriteBatch, pos, campaignMode, GUI.debugDrawMetaData);
            }
            else
            {
              const string text = "Ctrl+M to show campaign metadata debug info";
              GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - (GUIStyle.SmallFont.MeasureString(text).X + 10), 300),
                  text, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
            }
          }

          IEnumerable<string> strings;
          if (GUI.MouseOn != null)
          {
            RectTransform mouseOnRect = GUI.MouseOn.RectTransform;
            bool isAbsoluteOffsetInUse = mouseOnRect.AbsoluteOffset != Point.Zero || mouseOnRect.RelativeOffset == Vector2.Zero;

            strings = new string[]
            {
                            $"Selected UI Element: {GUI.MouseOn.GetType().Name} ({ GUI.MouseOn.Style?.Element.Name.LocalName ?? "no style" }, {GUI.MouseOn.Rect}",
                            $"Relative Offset: {mouseOnRect.RelativeOffset} | Absolute Offset: {(isAbsoluteOffsetInUse ? mouseOnRect.AbsoluteOffset : mouseOnRect.ParentRect.MultiplySize(mouseOnRect.RelativeOffset))}{(isAbsoluteOffsetInUse ? "" : " (Calculated from RelativeOffset)")}",
                            $"Anchor: {mouseOnRect.Anchor} | Pivot: {mouseOnRect.Pivot}"
            };
          }
          else
          {
            strings = new string[]
            {
                            $"GUI.Scale: {GUI.Scale}",
                            $"GUI.xScale: {GUI.xScale}",
                            $"GUI.yScale: {GUI.yScale}",
                            $"RelativeHorizontalAspectRatio: {GUI.RelativeHorizontalAspectRatio}",
                            $"RelativeVerticalAspectRatio: {GUI.RelativeVerticalAspectRatio}",
            };
          }

          strings = strings.Concat(new string[] { $"Cam.Zoom: {Screen.Selected.Cam?.Zoom ?? 0f}" });

          int padding = GUI.IntScale(10);
          int yPos = padding;

          foreach (string str in strings)
          {
            Vector2 stringSize = GUIStyle.SmallFont.MeasureString(str);

            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - (int)stringSize.X - padding, yPos), str, Color.LightGreen, Color.Black, 0, GUIStyle.SmallFont);
            yPos += (int)stringSize.Y + padding / 2;
          }
        }

        GameMain.GameSession?.EventManager?.DrawPinnedEvent(spriteBatch);

        if (HUDLayoutSettings.DebugDraw) { HUDLayoutSettings.Draw(spriteBatch); }

        GameMain.Client?.Draw(spriteBatch);

        if (Character.Controlled?.Inventory != null)
        {
          if (Character.Controlled.Stun < 0.1f && !Character.Controlled.IsDead)
          {
            Inventory.DrawFront(spriteBatch);
          }
        }

        GUI.DrawMessages(spriteBatch, cam);

        if (GUI.MouseOn != null)
        {
          if (!GUI.MouseOn.ToolTip.IsNullOrWhiteSpace())
          {
            GUI.MouseOn.DrawToolTip(spriteBatch);
          }
          GUI.MouseOn.OnDrawToolTip?.Invoke(GUI.MouseOn);
        }

        if (SubEditorScreen.IsSubEditor())
        {
          // Draw our "infinite stack" on the cursor
          switch (SubEditorScreen.DraggedItemPrefab)
          {
            case ItemPrefab itemPrefab:
              {
                var sprite = itemPrefab.InventoryIcon ?? itemPrefab.Sprite;
                sprite?.Draw(spriteBatch, PlayerInput.MousePosition, scale: Math.Min(64 / sprite.size.X, 64 / sprite.size.Y) * GUI.Scale);
                break;
              }
            case ItemAssemblyPrefab itemAssemblyPrefab:
              {
                itemAssemblyPrefab.Draw(spriteBatch, PlayerInput.MousePosition.FlipY());
                break;
              }
          }
        }

        GUI.DrawSavingIndicator(spriteBatch);
        GUI.DrawCursor(spriteBatch);
        GUI.HideCursor = false;
      }

      return false;
    }
  }
}