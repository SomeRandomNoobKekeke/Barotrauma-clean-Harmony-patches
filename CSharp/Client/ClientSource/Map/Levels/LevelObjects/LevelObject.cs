using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Barotrauma.SpriteDeformations;
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
    public static void PatchClientLevelObject()
    {
      harmony.Patch(
        original: typeof(LevelObject).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("LevelObject_Update_Replace"))
      );
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaClient/ClientSource/Map/Levels/LevelObjects/LevelObject.cs#L170
    public static bool LevelObject_Update_Replace(LevelObject __instance, float deltaTime, Camera cam)
    {
      LevelObject _ = __instance;

      _.CurrentRotation = _.Rotation;
      if (_.ActivePrefab.SwingFrequency > 0.0f)
      {
        _.SwingTimer += deltaTime * _.ActivePrefab.SwingFrequency;
        _.SwingTimer = _.SwingTimer % MathHelper.TwoPi;
        //lerp the swing amount to the correct value to prevent it from abruptly changing to a different value
        //when a trigger changes the swing amoung
        _.CurrentSwingAmount = MathHelper.Lerp(_.CurrentSwingAmount, _.ActivePrefab.SwingAmountRad, deltaTime * 10.0f);

        if (_.ActivePrefab.SwingAmountRad > 0.0f)
        {
          _.CurrentRotation += (float)Math.Sin(_.SwingTimer) * _.CurrentSwingAmount;
        }
      }

      _.CurrentScale = Vector2.One * _.Scale;
      if (_.ActivePrefab.ScaleOscillationFrequency > 0.0f)
      {
        _.ScaleOscillateTimer += deltaTime * _.ActivePrefab.ScaleOscillationFrequency;
        _.ScaleOscillateTimer = _.ScaleOscillateTimer % MathHelper.TwoPi;
        _.CurrentScaleOscillation = Vector2.Lerp(_.CurrentScaleOscillation, _.ActivePrefab.ScaleOscillation, deltaTime * 10.0f);

        float sin = (float)Math.Sin(_.ScaleOscillateTimer);
        _.CurrentScale *= new Vector2(
            1.0f + sin * _.CurrentScaleOscillation.X,
            1.0f + sin * _.CurrentScaleOscillation.Y);
      }

      if (_.LightSources != null)
      {
        Vector2 position2D = new Vector2(_.Position.X, _.Position.Y);
        Vector2 camDiff = position2D - cam.WorldViewCenter;
        for (int i = 0; i < _.LightSources.Length; i++)
        {
          if (_.LightSourceTriggers[i] != null)
          {
            _.LightSources[i].Enabled = _.LightSourceTriggers[i].IsTriggered;
          }
          _.LightSources[i].Rotation = -_.CurrentRotation;
          _.LightSources[i].SpriteScale = _.CurrentScale;
          _.LightSources[i].Position = position2D - camDiff * _.Position.Z * LevelObjectManager.ParallaxStrength;
        }
      }

      if (_.spriteDeformations.Count > 0)
      {
        _.UpdateDeformations(deltaTime);
      }

      if (_.ParticleEmitters != null)
      {
        for (int i = 0; i < _.ParticleEmitters.Length; i++)
        {
          if (_.ParticleEmitterTriggers[i] != null && !_.ParticleEmitterTriggers[i].IsTriggered) { continue; }
          Vector2 emitterPos = _.LocalToWorld(_.Prefab.EmitterPositions[i]);
          _.ParticleEmitters[i].Emit(deltaTime, emitterPos, hullGuess: null,
              angle: _.ParticleEmitters[i].Prefab.Properties.CopyEntityAngle ? -_.CurrentRotation + MathHelper.Pi : 0.0f);
        }
      }

      for (int i = 0; i < _.Sounds.Length; i++)
      {
        if (_.Sounds[i] == null) { continue; }
        if (_.SoundTriggers[i] == null || _.SoundTriggers[i].IsTriggered)
        {
          RoundSound roundSound = _.Sounds[i];
          Vector2 soundPos = _.LocalToWorld(new Vector2(_.Prefab.Sounds[i].Position.X, _.Prefab.Sounds[i].Position.Y));
          if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), soundPos) <
              roundSound.Range * roundSound.Range)
          {
            if (_.SoundChannels[i] == null || !_.SoundChannels[i].IsPlaying)
            {
              _.SoundChannels[i] = roundSound.Sound.Play(roundSound.Volume, roundSound.Range, roundSound.GetRandomFrequencyMultiplier(), soundPos);
            }
            if (_.SoundChannels[i] != null)
            {
              _.SoundChannels[i].Position = new Vector3(soundPos.X, soundPos.Y, 0.0f);
            }
          }
        }
        else if (_.SoundChannels[i] != null && _.SoundChannels[i].IsPlaying)
        {
          _.SoundChannels[i].FadeOutAndDispose();
          _.SoundChannels[i] = null;
        }
      }

      return false;
    }

  }
}