using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedRepairTool()
    {
      harmony.Patch(
        original: typeof(RepairTool).GetMethod("Use", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("RepairTool_Use_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Components/Holdable/RepairTool.cs#L185
    public static bool RepairTool_Use_Replace(RepairTool __instance, ref bool __result, float deltaTime, Character character = null)
    {
      RepairTool _ = __instance;

      if (character != null)
      {
        if (_.item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) { __result = false; return false; }
      }

      float degreeOfSuccess = character == null ? 0.5f : _.DegreeOfSuccess(character);

      bool failed = false;
      if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess)
      {
        _.ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
        failed = true;
      }
      if (_.UsableIn == RepairTool.UseEnvironment.None)
      {
        _.ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
        failed = true;
      }
      if (_.item.InWater)
      {
        if (_.UsableIn == RepairTool.UseEnvironment.Air)
        {
          _.ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
          failed = true;
        }
      }
      else
      {
        if (_.UsableIn == RepairTool.UseEnvironment.Water)
        {
          _.ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
          failed = true;
        }
      }
      if (failed)
      {
        // Always apply ActionType.OnUse. If doesn't fail, the effect is called later.
        _.ApplyStatusEffects(ActionType.OnUse, deltaTime, character);
        __result = false; return false;
      }

      Vector2 rayStart;
      Vector2 rayStartWorld;
      Vector2 sourcePos = character?.AnimController == null ? _.item.SimPosition : character.AnimController.AimSourceSimPos;
      Vector2 barrelPos = _.item.SimPosition + ConvertUnits.ToSimUnits(_.TransformedBarrelPos);
      //make sure there's no obstacles between the base of the item (or the shoulder of the character) and the end of the barrel
      if (Submarine.PickBody(sourcePos, barrelPos, collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking) == null)
      {
        //no obstacles -> we start the raycast at the end of the barrel
        rayStart = ConvertUnits.ToSimUnits(_.item.Position + _.TransformedBarrelPos);
        rayStartWorld = ConvertUnits.ToSimUnits(_.item.WorldPosition + _.TransformedBarrelPos);
      }
      else
      {
        rayStart = rayStartWorld = Submarine.LastPickedPosition + Submarine.LastPickedNormal * 0.1f;
        if (_.item.Submarine != null) { rayStartWorld += _.item.Submarine.SimPosition; }
      }

      //if the calculated barrel pos is in another hull, use the origin of the item to make sure the particles don't end up in an incorrect hull
      if (_.item.CurrentHull != null)
      {
        var barrelHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(rayStartWorld), _.item.CurrentHull, useWorldCoordinates: true);
        if (barrelHull != null && barrelHull != _.item.CurrentHull)
        {
          if (MathUtils.GetLineRectangleIntersection(ConvertUnits.ToDisplayUnits(sourcePos), ConvertUnits.ToDisplayUnits(rayStart), _.item.CurrentHull.Rect, out Vector2 hullIntersection))
          {
            if (!_.item.CurrentHull.ConnectedGaps.Any(g => g.Open > 0.0f && Submarine.RectContains(g.Rect, hullIntersection)))
            {
              Vector2 rayDir = rayStart.NearlyEquals(sourcePos) ? Vector2.Zero : Vector2.Normalize(rayStart - sourcePos);
              rayStartWorld = ConvertUnits.ToSimUnits(hullIntersection - rayDir * 5.0f);
              if (_.item.Submarine != null) { rayStartWorld += _.item.Submarine.SimPosition; }
            }
          }
        }
      }

      float spread = MathHelper.ToRadians(MathHelper.Lerp(_.UnskilledSpread, _.Spread, degreeOfSuccess));

      float angle = MathHelper.ToRadians(_.BarrelRotation) + spread * Rand.Range(-0.5f, 0.5f);
      float dir = 1;
      if (_.item.body != null)
      {
        angle += _.item.body.Rotation;
        dir = _.item.body.Dir;
      }
      Vector2 rayEnd = rayStartWorld + ConvertUnits.ToSimUnits(new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * _.Range * dir);

      _.ignoredBodies.Clear();
      if (character != null)
      {
        foreach (Limb limb in character.AnimController.Limbs)
        {
          if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess) continue;
          _.ignoredBodies.Add(limb.body.FarseerBody);
        }
        _.ignoredBodies.Add(character.AnimController.Collider.FarseerBody);
      }

      _.IsActive = true;
      _.activeTimer = 0.1f;

      _.debugRayStartPos = ConvertUnits.ToDisplayUnits(rayStartWorld);
      _.debugRayEndPos = ConvertUnits.ToDisplayUnits(rayEnd);

      Submarine parentSub = character?.Submarine ?? _.item.Submarine;
      if (parentSub == null)
      {
        foreach (Submarine sub in Submarine.Loaded)
        {
          Rectangle subBorders = sub.Borders;
          subBorders.Location += new Point((int)sub.WorldPosition.X, (int)sub.WorldPosition.Y - sub.Borders.Height);
          if (!MathUtils.CircleIntersectsRectangle(_.item.WorldPosition, _.Range * 5.0f, subBorders))
          {
            continue;
          }
          _.Repair(rayStartWorld - sub.SimPosition, rayEnd - sub.SimPosition, deltaTime, character, degreeOfSuccess, _.ignoredBodies);
        }
        _.Repair(rayStartWorld, rayEnd, deltaTime, character, degreeOfSuccess, _.ignoredBodies);
      }
      else
      {
        _.Repair(rayStartWorld - parentSub.SimPosition, rayEnd - parentSub.SimPosition, deltaTime, character, degreeOfSuccess, _.ignoredBodies);
      }

      //TODO test in multiplayer, this is probably not compiled on server side
      _.UseProjSpecific(deltaTime, rayStartWorld);

      __result = true; return false;
    }


  }
}