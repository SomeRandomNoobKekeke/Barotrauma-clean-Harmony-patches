using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using System.Runtime.CompilerServices;
[assembly: IgnoresAccessChecksTo("Barotrauma")]
[assembly: IgnoresAccessChecksTo("DedicatedServer")]
[assembly: IgnoresAccessChecksTo("BarotraumaCore")]

namespace CleanPatches
{
  public class ThisIsHowToPatchIt : System.Attribute { }

  public partial class Mod : IAssemblyPlugin
  {
    public static Harmony harmony;

    public void Initialize()
    {
      harmony = new Harmony("clean.patches");

      ApplyAllHarmonyPatches();

      PatchOnBothSides();
#if CLIENT
      PatchOnClient();
#elif SERVER
      PatchOnServer();
#endif

      log("Clean harmony patches compiled without errors");
    }

    public void PatchOnBothSides()
    {

    }

    // Note: i'm aware that Harmony supports Annotational patching
    // But i want to show an example of manual patching
    // It's more flexible and i usually prefer it
    public void ApplyAllHarmonyPatches()
    {
      Assembly CallingAssembly = Assembly.GetCallingAssembly();

      foreach (Type type in CallingAssembly.GetTypes())
      {
        foreach (MethodInfo mi in type.GetMethods(AccessTools.all))
        {
          if (Attribute.IsDefined(mi, typeof(ThisIsHowToPatchIt)))
          {
            mi.Invoke(null, new object[] { });
          }
        }
      }

      // var originalMethods = Harmony.GetAllPatchedMethods();
      // foreach (MethodBase method in originalMethods)
      // {
      //   log($"{method.DeclaringType}.{method}");
      // }
    }

    public static void log(object msg, Color? cl = null)
    {
      cl ??= Color.Cyan;
      LuaCsLogger.LogMessage($"{msg ?? "null"}", cl * 0.8f, cl);
    }

    public void OnLoadCompleted() { }
    public void PreInitPatching() { }
    public void Dispose()
    {
      harmony.UnpatchSelf();
    }
  }
}