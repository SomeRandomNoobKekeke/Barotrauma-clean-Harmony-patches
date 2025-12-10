using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientSonar()
    {
      harmony.Patch(
        original: typeof(Sonar).GetMethod("DrawSonar", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Sonar_DrawSonar_Replace"))
      );
    }

    public static bool Sonar_DrawSonar_Replace(Sonar __instance, SpriteBatch spriteBatch, Rectangle rect)
    {
      Sonar _ = __instance;

      _.displayBorderSize = 0.2f;
      _.center = rect.Center.ToVector2();
      _.DisplayRadius = (rect.Width / 2.0f) * (1.0f - _.displayBorderSize);
      _.DisplayScale = _.DisplayRadius / _.range * _.zoom;

      _.screenBackground?.Draw(spriteBatch, _.center, 0.0f, rect.Width / _.screenBackground.size.X);

      if (_.useDirectionalPing)
      {
        _.directionalPingBackground?.Draw(spriteBatch, _.center, 0.0f, rect.Width / _.directionalPingBackground.size.X);
        if (_.directionalPingButton != null)
        {
          int buttonSprIndex = 0;
          if (_.pingDragDirection != null)
          {
            buttonSprIndex = 2;
          }
          else if (_.MouseInDirectionalPingRing(rect, true))
          {
            buttonSprIndex = 1;
          }
          _.directionalPingButton[buttonSprIndex]?.Draw(spriteBatch, _.center, MathUtils.VectorToAngle(_.pingDirection), rect.Width / _.directionalPingBackground.size.X);
        }
      }

      if (_.currentPingIndex != -1)
      {
        var activePing = _.activePings[_.currentPingIndex];
        if (activePing.IsDirectional && _.directionalPingCircle != null)
        {
          _.directionalPingCircle.Draw(spriteBatch, _.center, Color.White * (1.0f - activePing.State),
              rotate: MathUtils.VectorToAngle(activePing.Direction),
              scale: _.DisplayRadius / _.directionalPingCircle.size.X * activePing.State);
        }
        else
        {
          _.pingCircle.Draw(spriteBatch, _.center, Color.White * (1.0f - activePing.State), 0.0f, (_.DisplayRadius * 2 / _.pingCircle.size.X) * activePing.State);
        }
      }

      float signalStrength = 1.0f;
      if (_.UseTransducers)
      {
        signalStrength = 0.0f;
        foreach (Sonar.ConnectedTransducer connectedTransducer in _.connectedTransducers)
        {
          signalStrength = Math.Max(signalStrength, connectedTransducer.SignalStrength);
        }
      }

      Vector2 transducerCenter = _.GetTransducerPos();// + DisplayOffset;

      if (_.sonarBlips.Count > 0)
      {
        float blipScale = 0.08f * (float)Math.Sqrt(_.zoom) * (rect.Width / 700.0f);
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

        foreach (SonarBlip sonarBlip in _.sonarBlips)
        {
          _.DrawBlip(spriteBatch, sonarBlip, transducerCenter + _.DisplayOffset, _.center, sonarBlip.FadeTimer / 2.0f * signalStrength, blipScale);
        }

        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
      }

      if (_.item.Submarine != null && !_.DetectSubmarineWalls)
      {
        transducerCenter += _.DisplayOffset;
        _.DrawDockingPorts(spriteBatch, transducerCenter, signalStrength);
        _.DrawOwnSubmarineBorders(spriteBatch, transducerCenter, signalStrength);
      }
      else
      {
        _.DisplayOffset = Vector2.Zero;
      }

      float directionalPingVisibility = _.useDirectionalPing && _.currentMode == Sonar.Mode.Active ? 1.0f : _.showDirectionalIndicatorTimer;
      if (directionalPingVisibility > 0.0f)
      {
        Vector2 sector1 = MathUtils.RotatePointAroundTarget(_.pingDirection * _.DisplayRadius, Vector2.Zero, MathHelper.ToRadians(Sonar.DirectionalPingSector * 0.5f));
        Vector2 sector2 = MathUtils.RotatePointAroundTarget(_.pingDirection * _.DisplayRadius, Vector2.Zero, MathHelper.ToRadians(-Sonar.DirectionalPingSector * 0.5f));
        _.DrawLine(spriteBatch, Vector2.Zero, sector1, Color.LightCyan * 0.2f * directionalPingVisibility, width: 3);
        _.DrawLine(spriteBatch, Vector2.Zero, sector2, Color.LightCyan * 0.2f * directionalPingVisibility, width: 3);
      }

      if (GameMain.DebugDraw)
      {
        GUI.DrawString(spriteBatch, rect.Location.ToVector2(), _.sonarBlips.Count.ToString(), Color.White);
      }

      _.screenOverlay?.Draw(spriteBatch, _.center, 0.0f, rect.Width / _.screenOverlay.size.X);

      if (signalStrength <= 0.5f)
      {
        _.signalWarningText.Text = TextManager.Get(signalStrength <= 0.0f ? "SonarNoSignal" : "SonarSignalWeak");
        _.signalWarningText.Color = signalStrength <= 0.0f ? _.negativeColor : _.warningColor;
        _.signalWarningText.Visible = true;
        return false;
      }
      else
      {
        _.signalWarningText.Visible = false;
      }

      foreach (AITarget aiTarget in AITarget.List)
      {
        if (aiTarget.InDetectable) { continue; }
        if (aiTarget.SonarLabel.IsNullOrEmpty() || aiTarget.SoundRange <= 0.0f) { continue; }

        if (Vector2.DistanceSquared(aiTarget.WorldPosition, transducerCenter) < aiTarget.SoundRange * aiTarget.SoundRange)
        {
          _.DrawMarker(spriteBatch,
              aiTarget.SonarLabel.Value,
              aiTarget.SonarIconIdentifier,
              aiTarget,
              aiTarget.WorldPosition, transducerCenter,
              _.DisplayScale, _.center, _.DisplayRadius * 0.975f);
        }
      }

      if (GameMain.GameSession == null) { return false; }

      if (Level.Loaded != null)
      {
        if (Level.Loaded.StartLocation?.Type is { ShowSonarMarker: true })
        {
          _.DrawMarker(spriteBatch,
              Level.Loaded.StartLocation.DisplayName.Value,
              (Level.Loaded.StartOutpost != null ? "outpost" : "location").ToIdentifier(),
              "startlocation",
              Level.Loaded.StartExitPosition, transducerCenter,
              _.DisplayScale, _.center, _.DisplayRadius);
        }

        if (Level.Loaded is { EndLocation.Type.ShowSonarMarker: true, Type: LevelData.LevelType.LocationConnection })
        {
          _.DrawMarker(spriteBatch,
              Level.Loaded.EndLocation.DisplayName.Value,
              (Level.Loaded.EndOutpost != null ? "outpost" : "location").ToIdentifier(),
              "endlocation",
              Level.Loaded.EndExitPosition, transducerCenter,
              _.DisplayScale, _.center, _.DisplayRadius);
        }

        for (int i = 0; i < Level.Loaded.Caves.Count; i++)
        {
          var cave = Level.Loaded.Caves[i];
          if (cave.MissionsToDisplayOnSonar.None()) { continue; }
          _.DrawMarker(spriteBatch,
              Sonar.caveLabel.Value,
              "cave".ToIdentifier(),
              "cave" + i,
              cave.StartPos.ToVector2(), transducerCenter,
              _.DisplayScale, _.center, _.DisplayRadius);
        }

        if (GameMain.NetworkMember is { } networkMember && GameMain.GameSession?.GameMode is PvPMode)
        {
          if (networkMember.ServerSettings.TrackOpponentInPvP
              && Submarine.MainSubs[0] is { } coalitionSub
              && Submarine.MainSubs[1] is { } separatistSub
              && Character.Controlled is { } player)
          {
            Submarine whichSubToDraw = player.TeamID switch
            {
              CharacterTeamType.Team1 => separatistSub,
              CharacterTeamType.Team2 => coalitionSub,
              _ => null
            };

            if (whichSubToDraw != null)
            {
              _.DrawOffsetMarker(spriteBatch,
                         Sonar.enemyLabel.Value,
                         Tags.Submarine,
                         Tags.Enemy,
                         whichSubToDraw.WorldPosition,
                         transducerCenter,
                         distanceThresholds: new Range<float>(start: MetersToUnits(150), end: MetersToUnits(1600)),
                         offset: new Range<float>(start: MetersToUnits(100), end: MetersToUnits(400)),
                         minOffset: MetersToUnits(10));

              static float MetersToUnits(float m)
                  => m / Physics.DisplayToRealWorldRatio;
            }
          }
        }
      }

      int missionIndex = 0;
      foreach (Mission mission in GameMain.GameSession.Missions)
      {
        if (!mission.Prefab.ShowSonarLabels) { continue; }
        int i = 0;
        foreach ((LocalizedString label, Vector2 position) in mission.SonarLabels)
        {
          if (!string.IsNullOrEmpty(label.Value))
          {
            _.DrawMarker(spriteBatch,
                label.Value,
                mission.SonarIconIdentifier,
                "mission" + missionIndex + ":" + i,
                position, transducerCenter,
                _.DisplayScale, _.center, _.DisplayRadius * 0.95f);
          }
          i++;
        }
        missionIndex++;
      }

      if (_.HasMineralScanner && _.UseMineralScanner && _.CurrentMode == Sonar.Mode.Active && _.MineralClusters != null &&
          (_.item.CurrentHull == null || !_.DetectSubmarineWalls) &&
          _.HasPower)
      {
        foreach (var c in _.MineralClusters)
        {
          var unobtainedMinerals = c.resources.Where(i => i != null && i.GetComponent<Holdable>() is { Attached: true });
          if (unobtainedMinerals.None()) { continue; }
          if (!_.CheckResourceMarkerVisibility(c.center, transducerCenter)) { continue; }
          var i = unobtainedMinerals.FirstOrDefault();
          if (i == null) { continue; }

          bool disrupted = false;
          foreach ((Vector2 disruptPos, float disruptStrength) in _.disruptedDirections)
          {
            float dot = Vector2.Dot(Vector2.Normalize(c.center - transducerCenter), disruptPos);
            if (dot > 1.0f - disruptStrength)
            {
              disrupted = true;
              break;
            }
          }
          if (disrupted) { continue; }

          _.DrawMarker(spriteBatch,
              i.Name, "mineral".ToIdentifier(), "mineralcluster" + i,
              c.center, transducerCenter,
              _.DisplayScale, _.center, _.DisplayRadius * 0.95f,
              onlyShowTextOnMouseOver: true);
        }
      }

      foreach (Submarine sub in Submarine.Loaded)
      {
        if (!sub.ShowSonarMarker) { continue; }
        if (_.connectedSubs.Contains(sub)) { continue; }
        if (sub.IsAboveLevel) { continue; }

        if (_.item.Submarine != null || Character.Controlled != null)
        {
          //hide enemy team
          if (sub.TeamID == CharacterTeamType.Team1 && (_.item.Submarine?.TeamID == CharacterTeamType.Team2 || Character.Controlled?.TeamID == CharacterTeamType.Team2))
          {
            continue;
          }
          else if (sub.TeamID == CharacterTeamType.Team2 && (_.item.Submarine?.TeamID == CharacterTeamType.Team1 || Character.Controlled?.TeamID == CharacterTeamType.Team1))
          {
            continue;
          }
        }

        _.DrawMarker(spriteBatch,
            sub.Info.DisplayName.Value,
            (sub.Info.HasTag(SubmarineTag.Shuttle) ? "shuttle" : "submarine").ToIdentifier(),
            sub,
            sub.WorldPosition, transducerCenter,
            _.DisplayScale, _.center, _.DisplayRadius * 0.95f);
      }

      if (GameMain.DebugDraw)
      {
        var steering = _.item.GetComponent<Steering>();
        steering?.DebugDrawHUD(spriteBatch, transducerCenter, _.DisplayScale, _.DisplayRadius, _.center);
      }

      return false;
    }

  }
}