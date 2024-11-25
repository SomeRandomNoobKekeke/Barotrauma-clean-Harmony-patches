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

      harmony.Patch(
        original: typeof(RepairTool).GetMethod("Repair", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("RepairTool_Repair_Replace"))
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


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Components/Holdable/RepairTool.cs#L321
    public static bool RepairTool_Repair_Replace(RepairTool __instance, Vector2 rayStart, Vector2 rayEnd, float deltaTime, Character user, float degreeOfSuccess, List<Body> ignoredBodies)
    {
      RepairTool _ = __instance;

      var collisionCategories = Physics.CollisionWall | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionRepairableWall;
      if (!_.IgnoreCharacters)
      {
        collisionCategories |= Physics.CollisionCharacter;
      }

      //if the item can cut off limbs, activate nearby bodies to allow the raycast to hit them
      if (_.statusEffectLists != null)
      {
        static bool CanSeverJoints(ActionType type, Dictionary<ActionType, List<StatusEffect>> effectList) =>
            effectList.TryGetValue(type, out List<StatusEffect> effects) && effects.Any(e => e.SeverLimbsProbability > 0);

        if (CanSeverJoints(ActionType.OnUse, _.statusEffectLists) || CanSeverJoints(ActionType.OnSuccess, _.statusEffectLists))
        {
          float rangeSqr = ConvertUnits.ToSimUnits(_.Range);
          rangeSqr *= rangeSqr;
          foreach (Character c in Character.CharacterList)
          {
            if (!c.Enabled || !c.AnimController.BodyInRest) { continue; }
            //do a broad check first
            if (Math.Abs(c.WorldPosition.X - _.item.WorldPosition.X) > 1000.0f) { continue; }
            if (Math.Abs(c.WorldPosition.Y - _.item.WorldPosition.Y) > 1000.0f) { continue; }
            foreach (Limb limb in c.AnimController.Limbs)
            {
              if (Vector2.DistanceSquared(limb.SimPosition, _.item.SimPosition) < rangeSqr && Vector2.Dot(rayEnd - rayStart, limb.SimPosition - rayStart) > 0)
              {
                c.AnimController.BodyInRest = false;
                break;
              }
            }
          }
        }
      }

      float lastPickedFraction = 0.0f;
      if (_.RepairMultiple)
      {
        var bodies = Submarine.PickBodies(rayStart, rayEnd, ignoredBodies, collisionCategories,
            ignoreSensors: false,
            customPredicate: (Fixture f) =>
            {
              if (f.IsSensor)
              {
                if (_.RepairThroughHoles && f.Body?.UserData is Structure) { return false; }
                if (f.Body?.UserData is PhysicsBody) { return false; }
              }
              if (f.Body?.UserData is Item it && it.GetComponent<Planter>() != null) { return false; }
              if (f.Body?.UserData as string == "ruinroom") { return false; }
              if (f.Body?.UserData is VineTile && !(_.FireDamage > 0)) { return false; }
              return true;
            },
            allowInsideFixture: true);

        RepairTool.hitBodies.Clear();
        RepairTool.hitBodies.AddRange(bodies.Distinct());

        lastPickedFraction = Submarine.LastPickedFraction;
        Type lastHitType = null;
        _.hitCharacters.Clear();
        foreach (Body body in RepairTool.hitBodies)
        {
          Type bodyType = body.UserData?.GetType();
          if (!_.RepairThroughWalls && bodyType != null && bodyType != lastHitType)
          {
            //stop the ray if it already hit a door/wall and is now about to hit some other type of entity
            if (lastHitType == typeof(Item) || lastHitType == typeof(Structure)) { break; }
          }
          if (!_.RepairMultipleWalls && (bodyType == typeof(Structure) || (body.UserData as Item)?.GetComponent<Door>() != null)) { break; }

          Character hitCharacter = null;
          if (body.UserData is Limb limb)
          {
            hitCharacter = limb.character;
          }
          else if (body.UserData is Character character)
          {
            hitCharacter = character;
          }
          //only do damage once to each character even if they ray hit multiple limbs
          if (hitCharacter != null)
          {
            if (_.hitCharacters.Contains(hitCharacter)) { continue; }
            _.hitCharacters.Add(hitCharacter);
          }

          //if repairing through walls is not allowed and the next wall is more than 100 pixels away from the previous one, stop here
          //(= repairing multiple overlapping walls is allowed as long as the edges of the walls are less than MaxOverlappingWallDist pixels apart)
          float thisBodyFraction = Submarine.LastPickedBodyDist(body);
          if (!_.RepairThroughWalls && lastHitType == typeof(Structure) && _.Range * (thisBodyFraction - lastPickedFraction) > _.MaxOverlappingWallDist)
          {
            break;
          }
          _.pickedPosition = rayStart + (rayEnd - rayStart) * thisBodyFraction;
          if (_.FixBody(user, _.pickedPosition, deltaTime, degreeOfSuccess, body))
          {
            lastPickedFraction = thisBodyFraction;
            if (bodyType != null) { lastHitType = bodyType; }
          }
        }
      }
      else
      {
        var pickedBody = Submarine.PickBody(rayStart, rayEnd,
            ignoredBodies, collisionCategories,
            ignoreSensors: false,
            customPredicate: (Fixture f) =>
            {
              if (f.IsSensor)
              {
                if (_.RepairThroughHoles && f.Body?.UserData is Structure) { return false; }
                if (f.Body?.UserData is PhysicsBody) { return false; }
              }
              if (f.Body?.UserData as string == "ruinroom") { return false; }
              if (f.Body?.UserData is VineTile && !(_.FireDamage > 0)) { return false; }

              if (f.Body?.UserData is Item targetItem)
              {
                if (!_.HitItems) { return false; }
                if (_.HitBrokenDoors)
                {
                  if (targetItem.GetComponent<Door>() == null && targetItem.Condition <= 0) { return false; }
                }
                else
                {
                  if (targetItem.Condition <= 0) { return false; }
                }
              }
              return f.Body?.UserData != null;
            },
            allowInsideFixture: true);
        _.pickedPosition = Submarine.LastPickedPosition;
        _.FixBody(user, _.pickedPosition, deltaTime, degreeOfSuccess, pickedBody);
        lastPickedFraction = Submarine.LastPickedFraction;
      }

      if (_.ExtinguishAmount > 0.0f && _.item.CurrentHull != null)
      {
        _.fireSourcesInRange.Clear();
        //step along the ray in 10% intervals, collecting all fire sources in the range
        for (float x = 0.0f; x <= lastPickedFraction; x += 0.1f)
        {
          Vector2 displayPos = ConvertUnits.ToDisplayUnits(rayStart + (rayEnd - rayStart) * x);
          if (_.item.CurrentHull.Submarine != null) { displayPos += _.item.CurrentHull.Submarine.Position; }

          Hull hull = Hull.FindHull(displayPos, _.item.CurrentHull);
          if (hull == null) continue;
          foreach (FireSource fs in hull.FireSources)
          {
            if (fs.IsInDamageRange(displayPos, 100.0f) && !_.fireSourcesInRange.Contains(fs))
            {
              _.fireSourcesInRange.Add(fs);
            }
          }
          foreach (FireSource fs in hull.FakeFireSources)
          {
            if (fs.IsInDamageRange(displayPos, 100.0f) && !_.fireSourcesInRange.Contains(fs))
            {
              _.fireSourcesInRange.Add(fs);
            }
          }
        }

        foreach (FireSource fs in _.fireSourcesInRange)
        {
          fs.Extinguish(deltaTime, _.ExtinguishAmount);
#if SERVER
          if (!(fs is DummyFireSource))
          {
            GameMain.Server.KarmaManager.OnExtinguishingFire(user, deltaTime);
          }
#endif
        }
      }

      if (_.WaterAmount > 0.0f && _.item.Submarine != null)
      {
        Vector2 pos = ConvertUnits.ToDisplayUnits(rayStart + _.item.Submarine.SimPosition);

        // Could probably be done much efficiently here
        foreach (Item it in Item.ItemList)
        {
          if (it.Submarine == _.item.Submarine && it.GetComponent<Planter>() is { } planter)
          {
            if (it.GetComponent<Holdable>() is { } holdable && holdable.Attachable && !holdable.Attached) { continue; }

            Rectangle collisionRect = it.WorldRect;
            collisionRect.Y -= collisionRect.Height;
            if (collisionRect.Left < pos.X && collisionRect.Right > pos.X && collisionRect.Bottom < pos.Y)
            {
              Body collision = Submarine.PickBody(rayStart, it.SimPosition, ignoredBodies, collisionCategories);
              if (collision == null)
              {
                for (var i = 0; i < planter.GrowableSeeds.Length; i++)
                {
                  Growable seed = planter.GrowableSeeds[i];
                  if (seed == null || seed.Decayed) { continue; }

                  seed.Health += _.WaterAmount * deltaTime;

#if CLIENT
                  float barOffset = 10f * GUI.Scale;
                  Vector2 offset = planter.PlantSlots.ContainsKey(i) ? planter.PlantSlots[i].Offset : Vector2.Zero;
                  user?.UpdateHUDProgressBar(planter, planter.Item.DrawPosition + new Vector2(barOffset, 0) + offset, seed.Health / seed.MaxHealth, GUIStyle.Blue, GUIStyle.Blue, "progressbar.watering");
#endif
                }
              }
            }
          }
        }
      }

      if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
      {
        if (Rand.Range(0.0f, 1.0f) < _.FireProbability * deltaTime && _.item.CurrentHull != null)
        {
          Vector2 displayPos = ConvertUnits.ToDisplayUnits(rayStart + (rayEnd - rayStart) * lastPickedFraction * 0.9f);
          if (_.item.CurrentHull.Submarine != null) { displayPos += _.item.CurrentHull.Submarine.Position; }
          new FireSource(displayPos, sourceCharacter: user);
        }
      }

      return false;
    }

  }
}