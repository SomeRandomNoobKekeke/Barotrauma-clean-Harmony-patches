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
    public static void PatchSharedAICharacter()
    {
      harmony.Patch(
        original: typeof(AICharacter).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("AICharacter_Update_Replace"))
      );
    }

    // ACHTUNG! Base call detected!
    // if you want this then you also need Character_Update_Replace
    public static bool AICharacter_Update_Replace(float deltaTime, Camera cam, AICharacter __instance)
    {
      AICharacter _ = __instance;

      //base.Update(deltaTime, cam);
      Character_Update_Replace(_, deltaTime, cam);

      if (!_.Enabled) { return false; }
      if (!_.IsRemotePlayer && _.AIController is EnemyAIController enemyAi)
      {
        enemyAi.PetBehavior?.Update(deltaTime);
      }
      if (_.IsDead || _.IsUnconscious || _.Stun > 0.0f || _.IsIncapacitated)
      {
        //don't enable simple physics on dead/incapacitated characters
        //the ragdoll controls the movement of incapacitated characters instead of the collider,
        //but in simple physics mode the ragdoll would get disabled, causing the character to not move at all
        _.AnimController.SimplePhysicsEnabled = false;
        return false;
      }

      if (!_.IsRemotePlayer && _.AIController is not HumanAIController)
      {
        float characterDistSqr = _.GetDistanceSqrToClosestPlayer();
        if (characterDistSqr > MathUtils.Pow2(_.Params.DisableDistance * 0.5f))
        {
          _.AnimController.SimplePhysicsEnabled = true;
        }
        else if (characterDistSqr < MathUtils.Pow2(_.Params.DisableDistance * 0.5f * 0.9f))
        {
          _.AnimController.SimplePhysicsEnabled = false;
        }
      }
      else
      {
        _.AnimController.SimplePhysicsEnabled = false;
      }

      if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return false; }
      if (Character.Controlled == _) { return false; }

      if (!_.IsRemotelyControlled && _.aiController != null && _.aiController.Enabled)
      {
        _.aiController.Update(deltaTime);
      }

      return false;
    }

  }
}