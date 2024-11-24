using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;


using Microsoft.Xna.Framework;
using OpenAL;
using System;
using System.Threading;
using Barotrauma.Sounds;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientSoundChannel()
    {
      harmony.Patch(
        original: typeof(SoundChannel).GetConstructors()[0],
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("SoundChannel_Constuctor_Replace"))
      );
    }

    public static bool SoundChannel_Constuctor_Replace(SoundChannel __instance, Sound sound, float gain, Vector3? position, float freqMult, float near, float far, string category, bool muffle = false)
    {
      SoundChannel _ = __instance;

      _.Sound = sound;

      _.debugName = sound == null ?
          "SoundChannel (null)" :
          $"SoundChannel ({(string.IsNullOrEmpty(sound.Filename) ? "filename empty" : sound.Filename)})";

      _.IsStream = sound.Stream;
      _.FilledByNetwork = sound is VoipSound;
      _.decayTimer = 0;
      _.streamSeekPos = 0; _.reachedEndSample = false;
      _.buffersToRequeue = 4;
      _.muffled = muffle;

      if (_.IsStream)
      {
        //mutex = new object();
        typeof(SoundChannel).GetField("mutex", AccessTools.all).SetValue(_, new object());
      }

#if !DEBUG
      try
      {
#endif
        if (_.mutex != null) { Monitor.Enter(_.mutex); }
        if (sound.Owner.CountPlayingInstances(sound) < sound.MaxSimultaneousInstances)
        {
          _.ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(_);
        }

        if (_.ALSourceIndex >= 0)
        {
          if (!_.IsStream)
          {
            Al.Sourcei(sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex), Al.Buffer, 0);
            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
              throw new Exception("Failed to reset source buffer: " + _.debugName + ", " + Al.GetErrorString(alError));
            }

            _.Sound.FillAlBuffers();
            if (_.Sound.Buffers is not { AlBuffer: not 0, AlMuffledBuffer: not 0 }) { return false; }

            uint alBuffer = sound.Owner.GetCategoryMuffle(category) || _.muffled ? _.Sound.Buffers.AlMuffledBuffer : _.Sound.Buffers.AlBuffer;
            Al.Sourcei(sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex), Al.Buffer, (int)alBuffer);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
              throw new Exception("Failed to bind buffer to source (" + _.ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex) + "," + alBuffer.ToString() + "): " + _.debugName + ", " + Al.GetErrorString(alError));
            }

            SetProperties();

            Al.SourcePlay(sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex));
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
              throw new Exception("Failed to play source: " + _.debugName + ", " + Al.GetErrorString(alError));
            }
          }
          else
          {
            uint alBuffer = 0;
            Al.Sourcei(sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex), Al.Buffer, (int)alBuffer);
            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
              throw new Exception("Failed to reset source buffer: " + _.debugName + ", " + Al.GetErrorString(alError));
            }

            Al.Sourcei(sound.Owner.GetSourceFromIndex(_.Sound.SourcePoolIndex, _.ALSourceIndex), Al.Looping, Al.False);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
              throw new Exception("Failed to set stream looping state: " + _.debugName + ", " + Al.GetErrorString(alError));
            }

            //streamShortBuffer = new short[STREAM_BUFFER_SIZE];
            typeof(SoundChannel).GetField("streamShortBuffer", AccessTools.all).SetValue(_, new short[SoundChannel.STREAM_BUFFER_SIZE]);

            //_.streamBuffers = new uint[4];
            typeof(SoundChannel).GetField("streamBuffers", AccessTools.all).SetValue(_, new uint[4]);
            //_.unqueuedBuffers = new uint[4];
            typeof(SoundChannel).GetField("unqueuedBuffers", AccessTools.all).SetValue(_, new uint[4]);
            //_.streamBufferAmplitudes = new float[4];
            typeof(SoundChannel).GetField("streamBufferAmplitudes", AccessTools.all).SetValue(_, new float[4]);

            for (int i = 0; i < 4; i++)
            {
              uint bufferID;
              Al.GenBuffer(out bufferID);
              _.streamBuffers[i] = bufferID;

              alError = Al.GetError();
              if (alError != Al.NoError)
              {
                throw new Exception("Failed to generate stream buffers: " + _.debugName + ", " + Al.GetErrorString(alError));
              }

              if (!Al.IsBuffer(_.streamBuffers[i]))
              {
                throw new Exception("Generated streamBuffer[" + i.ToString() + "] is invalid! " + _.debugName);
              }
            }
            _.Sound.Owner.InitUpdateChannelThread();
            SetProperties();
          }
        }
#if !DEBUG
      }
      catch
      {
        throw;
      }
      finally
      {
#endif
        if (_.mutex != null) { Monitor.Exit(_.mutex); }
#if !DEBUG
      }
#endif

      void SetProperties()
      {
        _.Position = position;
        _.Gain = gain;
        _.FrequencyMultiplier = freqMult;
        _.Looping = false;
        _.Near = near;
        _.Far = far;
        _.Category = category;
      }

      _.Sound.Owner.Update();

      return false;
    }

  }
}