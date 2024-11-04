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
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
#if CLIENT
using Barotrauma.Sounds;
#endif
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedPowered()
    {
      harmony.Patch(
        original: typeof(Powered).GetMethod("UpdatePower", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Powered_UpdatePower_Replace"))
      );
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/master/Barotrauma/BarotraumaShared/SharedSource/Items/Components/Power/Powered.cs#L425
    public static bool Powered_UpdatePower_Replace(float deltaTime)
    {
      //Don't update the power if the round is ending
      if (GameMain.GameSession != null && GameMain.GameSession.RoundEnding)
      {
        return false;
      }

      //Only update the power at the given update interval
      /*
      //Not use currently as update interval of 1/60
      if (updateTimer > 0.0f)
      {
          updateTimer -= deltaTime;
          return false;
      }
      updateTimer = UpdateInterval;
      */

#if CLIENT
      System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
      sw.Start();
#endif
      //Ensure all grids are updated correctly and have the correct connections
      Powered.UpdateGrids();

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Power", sw.ElapsedTicks);
      sw.Restart();
#endif

      //Reset all grids
      foreach (GridInfo grid in Powered.Grids.Values)
      {
        //Wipe priority groups as connections can change to not be outputting -- Can be improved caching wise --
        grid.PowerSourceGroups.Clear();
        grid.Power = 0;
        grid.Load = 0;
      }

      //Determine if devices are adding a load or providing power, also resolve solo nodes
      foreach (Powered powered in Powered.poweredList)
      {
        //Make voltage decay to ensure the device powers down.
        //This only effects devices with no power input (whose voltage is set by other means, e.g. status effects from a contained battery)
        //or devices that have been disconnected from the power grid - other devices use the voltage of the grid instead.
        powered.Voltage -= deltaTime;

        //Handle the device if it's got a power connection
        if (powered.powerIn != null && powered.powerOut != powered.powerIn)
        {
          //Get the new load for the connection
          float currLoad = powered.GetCurrentPowerConsumption(powered.powerIn);

          //If its a load update its grid load
          if (currLoad >= 0)
          {
            if (powered.PoweredByTinkering) { currLoad = 0.0f; }
            powered.CurrPowerConsumption = currLoad;
            if (powered.powerIn.Grid != null)
            {
              powered.powerIn.Grid.Load += currLoad;
            }
          }
          else if (powered.powerIn.Grid != null)
          {
            //If connected to a grid add as a source to be processed
            powered.powerIn.Grid.AddSrc(powered.powerIn);
          }
          else
          {
            powered.CurrPowerConsumption = -powered.GetConnectionPowerOut(powered.powerIn, 0, powered.MinMaxPowerOut(powered.powerIn, 0), 0);
            powered.GridResolved(powered.powerIn);
          }
        }

        //Handle the device power depending on if its powerout
        if (powered.powerOut != null)
        {
          //Get the connection's load
          float currLoad = powered.GetCurrentPowerConsumption(powered.powerOut);

          //Update the device's output load to the correct variable
          if (powered is PowerTransfer pt)
          {
            pt.PowerLoad = currLoad;
          }
          else if (powered is PowerContainer pc)
          {
            // PowerContainer handle its own output value
          }
          else
          {
            powered.CurrPowerConsumption = currLoad;
          }

          if (currLoad >= 0)
          {
            //Add to the grid load if possible
            if (powered.powerOut.Grid != null)
            {
              powered.powerOut.Grid.Load += currLoad;
            }
          }
          else if (powered.powerOut.Grid != null)
          {
            //Add connection as a source to be processed
            powered.powerOut.Grid.AddSrc(powered.powerOut);
          }
          else
          {
            //Perform power calculations for the singular connection
            float loadOut = -powered.GetConnectionPowerOut(powered.powerOut, 0, powered.MinMaxPowerOut(powered.powerOut, 0), 0);
            if (powered is PowerTransfer pt2)
            {
              pt2.PowerLoad = loadOut;
            }
            else if (powered is PowerContainer pc)
            {
              //PowerContainer handles its own output value
            }
            else
            {
              powered.CurrPowerConsumption = loadOut;
            }

            //Indicate grid is resolved as it was the only device
            powered.GridResolved(powered.powerOut);
          }
        }
      }

      //Iterate through all grids to determine the power on the grid
      foreach (GridInfo grid in Powered.Grids.Values)
      {
        //Iterate through the priority src groups lowest first
        foreach (PowerSourceGroup scrGroup in grid.PowerSourceGroups.Values)
        {
          scrGroup.MinMaxPower = PowerRange.Zero;

          //Iterate through all connections in the group to get their minmax power and sum them
          foreach (Connection c in scrGroup.Connections)
          {
            foreach (var device in c.Item.GetComponents<Powered>())
            {
              scrGroup.MinMaxPower += device.MinMaxPowerOut(c, grid.Load);
            }
          }

          //Iterate through all connections to get their final power out provided the min max information
          float addedPower = 0;
          foreach (Connection c in scrGroup.Connections)
          {
            foreach (var device in c.Item.GetComponents<Powered>())
            {
              addedPower += device.GetConnectionPowerOut(c, grid.Power, scrGroup.MinMaxPower, grid.Load);
            }
          }

          //Add the power to the grid
          grid.Power += addedPower;
        }

        //Calculate Grid voltage, limit between 0 - 1000
        float newVoltage = MathHelper.Min(grid.Power / MathHelper.Max(grid.Load, 1E-10f), 1000);
        if (float.IsNegative(newVoltage))
        {
          newVoltage = 0.0f;
        }

        grid.Voltage = newVoltage;

        //Iterate through all connections on that grid and run their gridResolved function
        foreach (Connection c in grid.Connections)
        {
          foreach (var device in c.Item.GetComponents<Powered>())
          {
            device?.GridResolved(c);
          }
        }
      }

#if CLIENT
      sw.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:Power", sw.ElapsedTicks);
#endif
      return false;
    }

  }
}