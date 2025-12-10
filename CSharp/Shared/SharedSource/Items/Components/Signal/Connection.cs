using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedConnection()
    {
      harmony.Patch(
        original: typeof(Connection).GetMethod("SendSignal", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Connection_SendSignal_Replace"))
      );
    }

    public static bool Connection_SendSignal_Replace(Signal signal, Connection __instance)
    {
      Connection _ = __instance;

      _.LastSentSignal = signal;
      _.enumeratingWires = true;
      foreach (var wire in _.wires)
      {
        Connection recipient = wire.OtherConnection(_);
        if (recipient == null) { continue; }
        if (recipient.item == _.item || signal.source?.LastSentSignalRecipients.LastOrDefault() == recipient) { continue; }

        signal.source?.LastSentSignalRecipients.Add(recipient);
#if CLIENT
                wire.RegisterSignal(signal, source: _);
#endif
        Connection.SendSignalIntoConnection(signal, recipient);
        GameMain.LuaCs.Hook.Call("signalReceived", signal, recipient);
        GameMain.LuaCs.Hook.Call("signalReceived." + recipient.item.Prefab.Identifier, signal, recipient);
      }

      foreach (CircuitBoxConnection connection in _.CircuitBoxConnections)
      {
        connection.ReceiveSignal(signal);
        GameMain.LuaCs.Hook.Call("signalReceived", signal, connection.Connection);
        GameMain.LuaCs.Hook.Call("signalReceived." + connection.Connection.Item.Prefab.Identifier, signal, connection);
      }
      _.enumeratingWires = false;
      foreach (var removedWire in _.removedWires)
      {
        _.wires.Remove(removedWire);
      }
      _.removedWires.Clear();

      return false;
    }

  }
}