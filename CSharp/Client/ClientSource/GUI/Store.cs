using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using PlayerBalanceElement = Barotrauma.CampaignUI.PlayerBalanceElement;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientStore()
    {
      harmony.Patch(
        original: typeof(Store).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("Store_Update_Replace"))
      );
    }

    public static bool Store_Update_Replace(float deltaTime, Store __instance)
    {
      Store _ = __instance;

      _.updateStopwatch.Restart();

      if (GameMain.DevMode)
      {
        if (PlayerInput.KeyDown(Keys.D0))
        {
          _.CreateUI();
          _.needsRefresh = true;
        }
      }

      if (GameMain.GraphicsWidth != _.resolutionWhenCreated.X || GameMain.GraphicsHeight != _.resolutionWhenCreated.Y)
      {
        _.CreateUI();
        _.needsRefresh = true;
      }
      else
      {
        _.playerBalanceElement = CampaignUI.UpdateBalanceElement(_.playerBalanceElement);

        // Update the owned items at short intervals and check if the interface should be refreshed
        _.ownedItemsUpdateTimer += deltaTime;
        if (_.ownedItemsUpdateTimer >= Store.timerUpdateInterval)
        {
          bool checkForRefresh = !_.needsItemsToSellRefresh || !_.needsRefresh;
          var prevOwnedItems = checkForRefresh ? new Dictionary<ItemPrefab, Store.ItemQuantity>(_.OwnedItems) : null;
          _.UpdateOwnedItems();
          if (checkForRefresh)
          {
            bool refresh = _.OwnedItems.Count != prevOwnedItems.Count ||
                _.OwnedItems.Values.Sum(v => v.Total) != prevOwnedItems.Values.Sum(v => v.Total) ||
                _.OwnedItems.Any(kvp => !prevOwnedItems.TryGetValue(kvp.Key, out Store.ItemQuantity v) || kvp.Value.Total != v.Total) ||
                prevOwnedItems.Any(kvp => !_.OwnedItems.ContainsKey(kvp.Key));
            if (refresh)
            {
              _.needsItemsToSellRefresh = true;
              _.needsRefresh = true;
            }
          }
        }
        // Update the sellable sub items at short intervals and check if the interface should be refreshed
        _.sellableItemsFromSubUpdateTimer += deltaTime;
        if (_.sellableItemsFromSubUpdateTimer >= Store.timerUpdateInterval)
        {
          bool checkForRefresh = !_.needsRefresh;
          var prevSubItems = checkForRefresh ? new List<PurchasedItem>(_.itemsToSellFromSub) : null;
          _.RefreshItemsToSellFromSub();
          if (checkForRefresh)
          {
            _.needsRefresh = _.itemsToSellFromSub.Count != prevSubItems.Count ||
                _.itemsToSellFromSub.Sum(i => i.Quantity) != prevSubItems.Sum(i => i.Quantity) ||
                _.itemsToSellFromSub.Any(i => prevSubItems.FirstOrDefault(prev => prev.ItemPrefab == i.ItemPrefab) is not PurchasedItem prev || i.Quantity != prev.Quantity) ||
                prevSubItems.Any(prev => _.itemsToSellFromSub.None(i => i.ItemPrefab == prev.ItemPrefab));
          }
        }
      }
      // Refresh the interface if balance changes and the buy tab is open
      if (_.activeTab == Store.StoreTab.Buy)
      {
        int currBalance = _.Balance;
        if (_.prevBalance != currBalance)
        {
          _.needsBuyingRefresh = true;
          _.prevBalance = currBalance;
        }
      }
      if (_.ActiveStore != null)
      {
        if (_.needsItemsToSellRefresh)
        {
          _.RefreshItemsToSell();
        }
        if (_.needsItemsToSellFromSubRefresh)
        {
          _.RefreshItemsToSellFromSub();
        }
        if (_.needsRefresh)
        {
          _.Refresh(updateOwned: _.ownedItemsUpdateTimer > 0.0f);
        }
        if (_.needsBuyingRefresh || _.HavePermissionsChanged(Store.StoreTab.Buy))
        {
          _.RefreshBuying(updateOwned: _.ownedItemsUpdateTimer > 0.0f);
        }
        if (_.needsSellingRefresh || _.HavePermissionsChanged(Store.StoreTab.Sell))
        {
          _.RefreshSelling(updateOwned: _.ownedItemsUpdateTimer > 0.0f);
        }
        if (_.needsSellingFromSubRefresh || _.HavePermissionsChanged(Store.StoreTab.SellSub))
        {
          _.RefreshSellingFromSub(updateOwned: _.ownedItemsUpdateTimer > 0.0f, updateItemsToSellFromSub: _.sellableItemsFromSubUpdateTimer > 0.0f);
        }
      }

      _.updateStopwatch.Stop();
      GameMain.PerformanceCounter.AddElapsedTicks("Update:GameSession:Store", _.updateStopwatch.ElapsedTicks);

      return false;
    }
  }
}