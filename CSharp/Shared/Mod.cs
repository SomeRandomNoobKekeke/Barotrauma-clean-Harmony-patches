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
    public Harmony harmony;

    public void Initialize()
    {
      harmony = new Harmony("clean.patches");

      PatchOnBothSides();

#if CLIENT
  PatchOnClient();
#elif SERVER
  PatchOnServer();
#endif
    }

    public void PatchOnBothSides()
    {

    }

    public static void log(object msg, Color? cl = null)
    {
      cl ??= Color.Cyan;
      LuaCsLogger.LogMessage($"{msg ?? "null"}", cl * 0.8f, cl);
    }

    public void OnLoadCompleted() { }
    public void PreInitPatching() { }
    public void Dispose() { }
  }
}