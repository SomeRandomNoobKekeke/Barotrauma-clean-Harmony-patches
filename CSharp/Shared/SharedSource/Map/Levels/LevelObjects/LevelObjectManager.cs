using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

#if CLIENT
using Barotrauma.Particles;
#endif
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;
using Barotrauma.Extensions;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedLevelObjectManager()
    {
      harmony.Patch(
        original: typeof(LevelObjectManager).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelObjectManager_Update_Replace"))
      );
    }


    public static bool LevelObjectManager_Update_Replace(LevelObjectManager __instance, float deltaTime, Camera cam)
    {
      LevelObjectManager _ = __instance;

      _.GlobalForceDecreaseTimer += deltaTime;
      if (_.GlobalForceDecreaseTimer > 1000000.0f)
      {
        _.GlobalForceDecreaseTimer = 0.0f;
      }

      if (_.updateableObjects is not null)
      {
        foreach (LevelObject obj in _.updateableObjects)
        {
          if (GameMain.NetworkMember is { IsServer: true })
          {
            obj.NetworkUpdateTimer -= deltaTime;
            if (obj.NeedsNetworkSyncing && obj.NetworkUpdateTimer <= 0.0f)
            {
              GameMain.NetworkMember.CreateEntityEvent(_, new LevelObjectManager.EventData(obj));
              obj.NeedsNetworkSyncing = false;
              obj.NetworkUpdateTimer = NetConfig.LevelObjectUpdateInterval;
            }
          }
          if (obj.Prefab.HideWhenBroken && obj.Health <= 0.0f) { continue; }

          if (obj.Triggers != null)
          {
            obj.ActivePrefab = obj.Prefab;
            for (int i = 0; i < obj.Triggers.Count; i++)
            {
              obj.Triggers[i].Update(deltaTime);
              if (obj.Triggers[i].IsTriggered && obj.Prefab.OverrideProperties[i] != null)
              {
                obj.ActivePrefab = obj.Prefab.OverrideProperties[i];
              }
            }
          }

          if (obj.PhysicsBody != null)
          {
            if (obj.Prefab.PhysicsBodyTriggerIndex > -1) { obj.PhysicsBody.Enabled = obj.Triggers[obj.Prefab.PhysicsBodyTriggerIndex].IsTriggered; }
            /*obj.Position = new Vector3(obj.PhysicsBody.Position, obj.Position.Z);
            obj.Rotation = -obj.PhysicsBody.Rotation;*/
          }
        }
      }
#if CLIENT
      _.UpdateProjSpecific(deltaTime,cam);
#endif

      return false;
    }

  }
}