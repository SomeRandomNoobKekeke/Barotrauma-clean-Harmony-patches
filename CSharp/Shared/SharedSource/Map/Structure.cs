using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Networking;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Immutable;
using Barotrauma.Abilities;
#if CLIENT
using System.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Lights;
#endif



namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedStructure()
    {
      harmony.Patch(
        original: typeof(Structure).GetMethod("AddDamage", AccessTools.all, new Type[]{
          typeof(int),
          typeof(float),
          typeof(Character),
          typeof(bool),
          typeof(bool),
        }),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Structure_AddDamage_Replace"))
      );

      harmony.Patch(
        original: typeof(Structure).GetMethod("SetDamage", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Structure_SetDamage_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Structure.cs#L968
    public static bool Structure_AddDamage_Replace(Structure __instance, int sectionIndex, float damage, Character attacker = null, bool emitParticles = true, bool createWallDamageProjectiles = false)
    {
      Structure _ = __instance;

      if (!_.HasBody || _.Prefab.Platform || _.Indestructible) { return false; }

      if (sectionIndex < 0 || sectionIndex > _.Sections.Length - 1) { return false; }

      var section = _.Sections[sectionIndex];
      float prevDamage = section.damage;
      if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
      {
        _.SetDamage(sectionIndex, section.damage + damage, attacker, createWallDamageProjectiles: createWallDamageProjectiles);
      }
#if CLIENT
      if (damage > 0 && emitParticles)
      {
        float dmg = Math.Min(section.damage - prevDamage, damage);
        float particleAmount = MathHelper.Lerp(0, 25, MathUtils.InverseLerp(0, 100, dmg * Rand.Range(0.75f, 1.25f)));
        // Special case for very low but frequent dmg like plasma cutter: 10% chance for emitting a particle
        if (particleAmount < 1 && Rand.Value() < 0.10f)
        {
          particleAmount = 1;
        }
        for (int i = 1; i <= particleAmount; i++)
        {
          var worldRect = section.WorldRect;
          var directionUnitX = MathUtils.RotatedUnitXRadians(_.BodyRotation);
          var directionUnitY = directionUnitX.YX().FlipX();
          Vector2 particlePos = new Vector2(
              Rand.Range(0, worldRect.Width + 1),
              Rand.Range(-worldRect.Height, 1));
          particlePos -= worldRect.Size.ToVector2().FlipY() * 0.5f;

          var particlePosFinal = _.SectionPosition(sectionIndex, world: true);
          particlePosFinal += particlePos.X * directionUnitX + particlePos.Y * directionUnitY;

          var particle = GameMain.ParticleManager.CreateParticle(_.Prefab.DamageParticle,
              position: particlePosFinal,
              velocity: Rand.Vector(Rand.Range(1.0f, 50.0f)), collisionIgnoreTimer: 1f);
          if (particle == null) { break; }
        }
      }
#endif

      return false;
    }


    //TODO i didn't test it in multiplayer
    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Structure.cs#L1171
    public static bool Structure_SetDamage_Replace(Structure __instance, int sectionIndex, float damage, Character attacker = null,
              bool createNetworkEvent = true,
              bool isNetworkEvent = true,
              bool createExplosionEffect = true,
              bool createWallDamageProjectiles = false)
    {
      Structure _ = __instance;

      if (_.Submarine != null && _.Submarine.GodMode || (_.Indestructible && !isNetworkEvent)) { return false; }
      if (!_.HasBody) { return false; }
      if (!MathUtils.IsValid(damage)) { return false; }

      damage = MathHelper.Clamp(damage, 0.0f, _.MaxHealth - _.Prefab.MinHealth);

      if (_.Sections[sectionIndex].NoPhysicsBody) { return false; }

#if SERVER
      if (GameMain.Server != null && createNetworkEvent && damage != _.Sections[sectionIndex].damage)
      {
        GameMain.Server.CreateEntityEvent(_);
      }
      bool noGaps = true;
      for (int i = 0; i < _.Sections.Length; i++)
      {
        if (i != sectionIndex && _.SectionIsLeaking(i))
        {
          noGaps = false;
          break;
        }
      }
#endif

      if (damage < _.MaxHealth * Structure.LeakThreshold)
      {
        if (_.Sections[sectionIndex].gap != null)
        {
#if SERVER
          //the structure doesn't have any other gap, log the structure being fixed
          if (noGaps && attacker != null)
          {
            GameServer.Log((_.Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall repaired by " + GameServer.CharacterLogName(attacker), ServerLog.MessageType.ItemInteraction);
          }
#endif
          DebugConsole.Log("Removing gap (ID " + _.Sections[sectionIndex].gap.ID + ", section: " + sectionIndex + ") from wall " + _.ID);

          //remove existing gap if damage is below leak threshold
          _.Sections[sectionIndex].gap.Open = 0.0f;
          _.Sections[sectionIndex].gap.Remove();
          _.Sections[sectionIndex].gap = null;
        }
      }
      //do not create gaps on damaged walls in editors,
      //they're created at the start of a round and "pre-creating" them in the editors causes issues (see #12998)
      else if (Screen.Selected is not { IsEditor: true })
      {
        float prevGapOpenState = _.Sections[sectionIndex].gap?.Open ?? 0.0f;
        if (_.Sections[sectionIndex].gap == null)
        {
          Rectangle gapRect = _.Sections[sectionIndex].rect;
          float diffFromCenter;
          if (_.IsHorizontal)
          {
            diffFromCenter = (gapRect.Center.X - _.rect.Center.X) / (float)_.rect.Width * _.BodyWidth;
            if (_.BodyWidth > 0.0f) { gapRect.Width = (int)(_.BodyWidth * (gapRect.Width / (float)_.rect.Width)); }
            if (_.BodyHeight > 0.0f)
            {
              gapRect.Y = (gapRect.Y - gapRect.Height / 2) + (int)(_.BodyHeight / 2 + _.BodyOffset.Y * _.scale);
              gapRect.Height = (int)_.BodyHeight;
            }
            if (_.FlippedX) { diffFromCenter = -diffFromCenter; }
          }
          else
          {
            diffFromCenter = ((gapRect.Y - gapRect.Height / 2) - (_.rect.Y - _.rect.Height / 2)) / (float)_.rect.Height * _.BodyHeight;
            if (_.BodyWidth > 0.0f)
            {
              gapRect.X = gapRect.Center.X + (int)(-_.BodyWidth / 2 + _.BodyOffset.X * _.scale);
              gapRect.Width = (int)_.BodyWidth;
            }
            if (_.BodyHeight > 0.0f) { gapRect.Height = (int)(_.BodyHeight * (gapRect.Height / (float)_.rect.Height)); }
            if (_.FlippedY) { diffFromCenter = -diffFromCenter; }
          }

          if (Math.Abs(_.BodyRotation) > 0.01f)
          {
            Vector2 structureCenter = _.Position;
            Vector2 gapPos = structureCenter + new Vector2(
                (float)Math.Cos(_.IsHorizontal ? -_.BodyRotation : MathHelper.PiOver2 - _.BodyRotation),
                (float)Math.Sin(_.IsHorizontal ? -_.BodyRotation : MathHelper.PiOver2 - _.BodyRotation)) * diffFromCenter + _.BodyOffset * _.scale;
            gapRect = new Rectangle((int)(gapPos.X - gapRect.Width / 2), (int)(gapPos.Y + gapRect.Height / 2), gapRect.Width, gapRect.Height);
          }

          gapRect.X -= 10;
          gapRect.Y += 10;
          gapRect.Width += 20;
          gapRect.Height += 20;

          bool rotatedEnoughToChangeOrientation = (MathUtils.WrapAngleTwoPi(_.RotationRad - MathHelper.PiOver4) % MathHelper.Pi < MathHelper.PiOver2);
          if (rotatedEnoughToChangeOrientation)
          {
            var center = gapRect.Location + gapRect.Size.FlipY() / new Point(2);
            var topLeft = gapRect.Location;
            var diff = topLeft - center;
            diff = diff.FlipY().YX().FlipY();
            var newTopLeft = diff + center;
            gapRect = new Rectangle(newTopLeft, gapRect.Size.YX());
          }
          bool horizontalGap = rotatedEnoughToChangeOrientation
              ? _.IsHorizontal
              : !_.IsHorizontal;
          bool diagonalGap = false;
          if (!MathUtils.NearlyEqual(_.BodyRotation, 0f))
          {
            //rotation within a 90 deg sector (e.g. 100 -> 10, 190 -> 10, -10 -> 80)
            float sectorizedRotation = MathUtils.WrapAngleTwoPi(_.BodyRotation) % MathHelper.PiOver2;
            //diagonal if 30 < angle < 60
            diagonalGap = sectorizedRotation is > MathHelper.Pi / 6 and < MathHelper.Pi / 3;
            //gaps on the lower half of a diagonal wall are horizontal, ones on the upper half are vertical
            if (diagonalGap)
            {
              horizontalGap = gapRect.Y - gapRect.Height / 2 < _.Position.Y;
              if (_.FlippedY) { horizontalGap = !horizontalGap; }
            }
          }

          _.Sections[sectionIndex].gap = new Gap(gapRect, horizontalGap, _.Submarine, isDiagonal: diagonalGap);

          //free the ID, because if we give gaps IDs we have to make sure they always match between the clients and the server and
          //that clients create them in the correct order along with every other entity created/removed during the round
          //which COULD be done via entityspawner, but it's unnecessary because we never access these gaps by ID
          _.Sections[sectionIndex].gap.FreeID();
          _.Sections[sectionIndex].gap.ShouldBeSaved = false;
          _.Sections[sectionIndex].gap.ConnectedWall = _;
          DebugConsole.Log("Created gap (ID " + _.Sections[sectionIndex].gap.ID + ", section: " + sectionIndex + ") on wall " + _.ID);
          //AdjustKarma(attacker, 300);

#if SERVER
          //the structure didn't have any other gaps yet, log the breach
          if (noGaps && attacker != null)
          {
            GameServer.Log((_.Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall breached by " + GameServer.CharacterLogName(attacker), ServerLog.MessageType.ItemInteraction);
          }
#endif
        }

        var gap = _.Sections[sectionIndex].gap;
        float damageRatio = _.MaxHealth <= 0.0f ? 0 : damage / _.MaxHealth;
        float gapOpen = 0;
        if (damageRatio > Structure.BigGapThreshold)
        {
          gapOpen = MathHelper.Lerp(0.35f, 0.75f, MathUtils.InverseLerp(Structure.BigGapThreshold, 1.0f, damageRatio));
        }
        else if (damageRatio > Structure.LeakThreshold)
        {
          gapOpen = MathHelper.Lerp(0f, 0.35f, MathUtils.InverseLerp(Structure.LeakThreshold, Structure.BigGapThreshold, damageRatio));
        }
        gap.Open = gapOpen;

        //gap appeared or became much larger -> explosion effect
        if (gapOpen - prevGapOpenState > 0.25f && createExplosionEffect && !gap.IsRoomToRoom)
        {
          Structure.CreateWallDamageExplosion(gap, attacker, createWallDamageProjectiles);
#if CLIENT
          SteamTimelineManager.OnHullBreached(_);
#endif
        }
      }

      float damageDiff = damage - _.Sections[sectionIndex].damage;
      bool hadHole = _.SectionBodyDisabled(sectionIndex);
      _.Sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, _.MaxHealth);
      _.HasDamage = _.Sections.Any(s => s.damage > 0.0f);

      if (attacker != null && damageDiff != 0.0f)
      {
        HumanAIController.StructureDamaged(_, damageDiff, attacker);

#if SERVER
        _.OnHealthChangedProjSpecific(attacker, damageDiff);
#endif

        if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
        {
          if (damageDiff < 0.0f)
          {
            attacker.Info?.ApplySkillGain(Barotrauma.Tags.MechanicalSkill,
                -damageDiff * SkillSettings.Current.SkillIncreasePerRepairedStructureDamage);
          }
        }
      }

      bool hasHole = _.SectionBodyDisabled(sectionIndex);

      if (hadHole == hasHole) { return false; }

      _.UpdateSections();

      return false;
    }


  }
}