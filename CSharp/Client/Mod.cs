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
    public void PatchOnClient()
    {
      // harmony.Patch(
      //   original: typeof(GUIStyle).GetMethod("Apply", AccessTools.all, new Type[]{
      //     typeof(GUIComponent),
      //     typeof(Identifier),
      //     typeof(GUIComponent),
      //   }),
      //   prefix: new HarmonyMethod(typeof(Mod).GetMethod("GUIStyle_Apply_Replace"))
      // );
    }
  }
}