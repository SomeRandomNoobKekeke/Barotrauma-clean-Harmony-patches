using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;

using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.Tutorials;
#endif
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedCampaignMode()
    {
      harmony.Patch(
        original: typeof(CampaignMode).GetMethod("NPCInteract", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CampaignMode_NPCInteract_Replace"))
      );
    }

    public static bool CampaignMode_NPCInteract_Replace(CampaignMode __instance, Character npc, Character interactor)
    {
      CampaignMode _ = __instance;

      if (!npc.AllowCustomInteract) { return false; }
      if (npc.AIController is HumanAIController humanAi && !humanAi.AllowCampaignInteraction()) { return false; }
#if CLIENT // not compiled on server
      _.NPCInteractProjSpecific(npc, interactor);
#endif
      string coroutineName = "DoCharacterWait." + (npc?.ID ?? Entity.NullEntityID);
      if (!CoroutineManager.IsCoroutineRunning(coroutineName))
      {
        CoroutineManager.StartCoroutine(_.DoCharacterWait(npc, interactor), coroutineName);
      }

      return false;
    }

  }
}