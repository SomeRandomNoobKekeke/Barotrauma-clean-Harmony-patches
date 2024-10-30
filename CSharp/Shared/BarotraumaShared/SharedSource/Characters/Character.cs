using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedCharacter()
    {
      harmony.Patch(
        original: typeof(GameScreen).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GameScreen_Update_Replace"))
      );
    }
    public static bool Character_UpdateAll_Replace(float deltaTime, Camera cam)
    {
      if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
      {
        foreach (Character c in Character.CharacterList)
        {
          if (c is not AICharacter && !c.IsRemotePlayer) { continue; }

          if (c.IsPlayer || (c.IsBot && !c.IsDead))
          {
            c.Enabled = true;
          }
          else if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
          {
            //disable AI characters that are far away from all clients and the host's character and not controlled by anyone
            float closestPlayerDist = c.GetDistanceToClosestPlayer();
            if (closestPlayerDist > c.Params.DisableDistance)
            {
              c.Enabled = false;
              if (c.IsDead && c.AIController is EnemyAIController)
              {
                Character.Spawner?.AddEntityToRemoveQueue(c);
              }
            }
            else if (closestPlayerDist < c.Params.DisableDistance * 0.9f)
            {
              c.Enabled = true;
            }
          }
          else if (Submarine.MainSub != null)
          {
            //disable AI characters that are far away from the sub and the controlled character
            float distSqr = Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition);
            if (Character.Controlled != null)
            {
              distSqr = Math.Min(distSqr, Vector2.DistanceSquared(Character.Controlled.WorldPosition, c.WorldPosition));
            }
            else
            {
              distSqr = Math.Min(distSqr, Vector2.DistanceSquared(GameMain.GameScreen.Cam.GetPosition(), c.WorldPosition));
            }

            if (distSqr > MathUtils.Pow2(c.Params.DisableDistance))
            {
              c.Enabled = false;
              if (c.IsDead && c.AIController is EnemyAIController)
              {
                Entity.Spawner?.AddEntityToRemoveQueue(c);
              }
            }
            else if (distSqr < MathUtils.Pow2(c.Params.DisableDistance * 0.9f))
            {
              c.Enabled = true;
            }
          }
        }
      }

      Character.characterUpdateTick++;

      if (Character.characterUpdateTick % Character.CharacterUpdateInterval == 0)
      {
        for (int i = 0; i < Character.CharacterList.Count; i++)
        {
          if (GameMain.LuaCs.Game.UpdatePriorityCharacters.Contains(Character.CharacterList[i])) continue;

          Character.CharacterList[i].Update(deltaTime * Character.CharacterUpdateInterval, cam);
        }
      }

      foreach (Character character in GameMain.LuaCs.Game.UpdatePriorityCharacters)
      {
        if (character.Removed) { continue; }

        character.Update(deltaTime, cam);
      }

#if CLIENT
      Character.UpdateSpeechBubbles(deltaTime);
#endif

      return false;
    }
  }
}