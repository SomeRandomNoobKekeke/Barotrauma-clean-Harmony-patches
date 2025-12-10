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
    [ThisIsHowToPatchIt]
    public static void PatchClientGUIStyle()
    {
      harmony.Patch(
        original: typeof(GUIStyle).GetMethod("Apply", AccessTools.all, new Type[]{
          typeof(GUIComponent),
          typeof(Identifier),
          typeof(GUIComponent),
        }),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("GUIStyle_Apply_Replace"))
      );
    }

    public static bool GUIStyle_Apply_Replace(GUIComponent targetComponent, Identifier styleName, GUIComponent parent)
    {
      GUIComponentStyle componentStyle;
      if (parent != null)
      {
        GUIComponentStyle parentStyle = parent.Style;

        if (parentStyle == null)
        {
          Identifier parentStyleName = ReflectionUtils.GetTypeNameWithoutGenericArity(parent.GetType());
          if (!GUIStyle.ComponentStyles.ContainsKey(parentStyleName))
          {
            DebugConsole.ThrowError($"Couldn't find a GUI style \"{parentStyleName}\"");
            return false;
          }
          parentStyle = GUIStyle.ComponentStyles[parentStyleName];
        }
        Identifier childStyleName = styleName.IsEmpty ? ReflectionUtils.GetTypeNameWithoutGenericArity(targetComponent.GetType()) : styleName;
        parentStyle.ChildStyles.TryGetValue(childStyleName, out componentStyle);
      }
      else
      {
        Identifier styleIdentifier = styleName.ToIdentifier();
        if (styleIdentifier == Identifier.Empty)
        {
          styleIdentifier = ReflectionUtils.GetTypeNameWithoutGenericArity(targetComponent.GetType());
        }
        if (!GUIStyle.ComponentStyles.ContainsKey(styleIdentifier))
        {
          DebugConsole.ThrowError($"Couldn't find a GUI style \"{styleIdentifier}\"");
          return false;
        }
        componentStyle = GUIStyle.ComponentStyles[styleIdentifier];
      }

      targetComponent.ApplyStyle(componentStyle);

      return false;
    }
  }
}