using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedEventManager()
    {
      harmony.Patch(
        original: typeof(EventManager).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("EventManager_Update_Replace"))
      );
    }


    public static bool EventManager_Update_Replace(EventManager __instance, float deltaTime)
    {
      EventManager _ = __instance;

      if (!_.Enabled) { return false; }
      if (GameMain.GameSession.Campaign?.DisableEvents ?? false) { return false; }

      if (!_.eventsInitialized)
      {
        foreach (var eventSet in _.selectedEvents.Keys)
        {
          foreach (var ev in _.selectedEvents[eventSet])
          {
            ev.Init(eventSet);
          }
        }
        _.eventsInitialized = true;
      }

      //clients only calculate the intensity but don't create any events
      //(the intensity is used for controlling the background music)
      _.CalculateCurrentIntensity(deltaTime);

#if DEBUG && SERVER
      if (DateTime.Now > nextIntensityLogTime)
      {
          DebugConsole.NewMessage("EventManager intensity: " + (int)Math.Round(_.currentIntensity * 100) + " %");
          nextIntensityLogTime = DateTime.Now + new TimeSpan(0, minutes: 1, seconds: 0);
      }
#endif

      if (_.isClient) { return false; }

      _.roundDuration += deltaTime;

      if (_.settings == null)
      {
        DebugConsole.ThrowError("Event settings not set before updating EventManager. Attempting to select...");
        _.SelectSettings();
        if (_.settings == null)
        {
          DebugConsole.ThrowError("Could not select EventManager settings. Disabling EventManager for the round...");
#if SERVER
          GameMain.Server?.SendChatMessage("Could not select EventManager settings. Disabling EventManager for the round...", Barotrauma.Networking.ChatMessageType.Error);
#endif
          _.Enabled = false;
          return false;
        }
      }

      if (_.IsCrewAway())
      {
        _.isCrewAway = true;
        _.crewAwayResetTimer = EventManager.CrewAwayResetDelay;
        _.crewAwayDuration += deltaTime;
      }
      else if (_.crewAwayResetTimer > 0.0f)
      {
        _.isCrewAway = false;
        _.crewAwayResetTimer -= deltaTime;
      }
      else
      {
        _.isCrewAway = false;
        _.crewAwayDuration = 0.0f;
        _.eventThreshold += _.settings.EventThresholdIncrease * deltaTime;
        _.eventThreshold = Math.Min(_.eventThreshold, 1.0f);
        _.eventCoolDown -= deltaTime;
      }

      _.calculateDistanceTraveledTimer -= deltaTime;
      if (_.calculateDistanceTraveledTimer <= 0.0f)
      {
        _.distanceTraveled = _.CalculateDistanceTraveled();
        _.calculateDistanceTraveledTimer = EventManager.CalculateDistanceTraveledInterval;
      }

      bool recheck = false;
      do
      {
        recheck = false;
        //activate pending event sets that can be activated
        for (int i = _.pendingEventSets.Count - 1; i >= 0; i--)
        {
          var eventSet = _.pendingEventSets[i];
          if (_.eventCoolDown > 0.0f && !eventSet.IgnoreCoolDown) { continue; }
          if (_.currentIntensity > _.eventThreshold && !eventSet.IgnoreIntensity) { continue; }
          if (!_.CanStartEventSet(eventSet)) { continue; }

          _.pendingEventSets.RemoveAt(i);

          if (_.selectedEvents.ContainsKey(eventSet))
          {
            //start events in this set
            foreach (Event ev in _.selectedEvents[eventSet])
            {
              _.activeEvents.Add(ev);
              _.eventThreshold = _.settings.DefaultEventThreshold;
              if (eventSet.TriggerEventCooldown && _.selectedEvents[eventSet].Any(e => e.Prefab.TriggerEventCooldown))
              {
                _.eventCoolDown = _.settings.EventCooldown;
              }
              if (eventSet.ResetTime > 0)
              {
                ev.Finished += () =>
                {
                  _.pendingEventSets.Add(eventSet);
                  _.CreateEvents(eventSet);
                  foreach (Event newEvent in _.selectedEvents[eventSet])
                  {
                    if (!newEvent.Initialized) { newEvent.Init(eventSet); }
                  }
                };
              }
            }
          }

          //add child event sets to pending
          foreach (EventSet childEventSet in eventSet.ChildSets)
          {
            _.pendingEventSets.Add(childEventSet);
            recheck = true;
          }
        }
      } while (recheck);

      foreach (Event ev in _.activeEvents)
      {
        if (!ev.IsFinished)
        {
          ev.Update(deltaTime);
        }
        else if (ev.Prefab != null && !_.finishedEvents.Any(e => e.Prefab == ev.Prefab))
        {
          if (_.level?.LevelData != null && _.level.LevelData.Type == LevelData.LevelType.Outpost)
          {
            if (!_.level.LevelData.EventHistory.Contains(ev.Prefab.Identifier)) { _.level.LevelData.EventHistory.Add(ev.Prefab.Identifier); }
          }
          _.finishedEvents.Add(ev);
        }
      }

      if (_.QueuedEvents.Count > 0)
      {
        _.activeEvents.Add(_.QueuedEvents.Dequeue());
      }

      return false;
    }


  }
}