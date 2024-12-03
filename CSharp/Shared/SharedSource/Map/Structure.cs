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
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Map/Structure.cs#L968
    public static bool Structure_AddDamage_Replace(Structure __instance, int sectionIndex, float damage, Character attacker = null, bool emitParticles = true, bool createWallDamageProjectiles = false)
    {
      Structure _ = __instance;

      if (!_.Prefab.Body || _.Prefab.Platform || _.Indestructible) { return false; }

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

  }
}