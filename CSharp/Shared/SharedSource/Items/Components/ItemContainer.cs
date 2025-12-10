using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Abilities;
using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchSharedItemContainer()
    {
      harmony.Patch(
        original: typeof(ItemContainer).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("ItemContainer_Update_Replace"))
      );
    }

    public static bool ItemContainer_Update_Replace(ItemContainer __instance, float deltaTime, Camera cam)
    {
      ItemContainer _ = __instance;

      if (!string.IsNullOrEmpty(_.SpawnWithId) && !_.alwaysContainedItemsSpawned)
      {
        _.SpawnAlwaysContainedItems();
        _.alwaysContainedItemsSpawned = true;
      }

      if (_.hasSignalConnections)
      {
        float totalConditionValue = 0, totalConditionPercentage = 0; int totalItems = 0;
        foreach (var item in _.Inventory.AllItems)
        {
          if (!MathUtils.NearlyEqual(item.Condition, 0))
          {
            totalConditionValue += item.Condition;
            totalConditionPercentage += item.ConditionPercentage;
            totalItems++;
          }
        }

        if (!MathUtils.NearlyEqual(totalConditionValue, _.prevTotalConditionValue))
        {
          _.totalConditionValueString = ((int)totalConditionValue).ToString(CultureInfo.InvariantCulture);
          _.prevTotalConditionValue = totalConditionValue;
        }

        if (!MathUtils.NearlyEqual(totalConditionPercentage, _.prevTotalConditionPercentage))
        {
          _.totalConditionPercentageString = ((int)totalConditionPercentage).ToString(CultureInfo.InvariantCulture);
          _.prevTotalConditionPercentage = totalConditionPercentage;
        }

        if (totalItems != _.prevTotalItems)
        {
          _.totalItemsString = totalItems.ToString(CultureInfo.InvariantCulture);
          _.prevTotalItems = totalItems;
        }

        _.item.SendSignal(_.totalConditionValueString, "contained_conditions");
        _.item.SendSignal(_.totalConditionPercentageString, "contained_conditions_percentage");
        _.item.SendSignal(_.totalItemsString, "contained_items");
      }

      if (_.item.ParentInventory is CharacterInventory ownerInventory)
      {
        _.SetContainedItemPositionsIfNeeded();

        if (_.AutoInject || _.subContainersCanAutoInject)
        {
          //normally autoinjection should delete the (medical) item, so it only gets applied once
          //but in multiplayer clients aren't allowed to remove items themselves, so they may be able to trigger this dozens of times
          //before the server notifies them of the item being removed, leading to a sharp lag spike.
          //this can also happen with mods, if there's a way to autoinject something that doesn't get removed On Use.
          //so let's ensure the item is only applied once per second at most.

          _.autoInjectCooldown -= deltaTime;
          if (_.autoInjectCooldown <= 0.0f &&
              ownerInventory?.Owner is Character ownerCharacter &&
              ownerCharacter.HealthPercentage / 100f <= _.AutoInjectThreshold &&
              ownerCharacter.HasEquippedItem(_.item))
          {
            if (_.AutoInject)
            {
              _.Inventory.AllItemsMod.ForEach(i => Inject(i));
            }
            else
            {
              for (int i = 0; i < _.slotRestrictions.Length; i++)
              {
                if (_.slotRestrictions[i].AutoInject)
                {
                  _.Inventory.GetItemsAt(i).ForEachMod(i => Inject(i));
                }
              }
            }
            void Inject(Item item)
            {
              item.ApplyStatusEffects(ActionType.OnSuccess, 1.0f, ownerCharacter, useTarget: ownerCharacter);
              item.ApplyStatusEffects(ActionType.OnUse, 1.0f, ownerCharacter, useTarget: ownerCharacter);
              item.GetComponent<GeneticMaterial>()?.Equip(ownerCharacter);
            }
            _.autoInjectCooldown = ItemContainer.AutoInjectInterval;
          }
        }

      }
      else if (_.item.body != null && _.item.body.Enabled)
      {
        if (_.item.body.FarseerBody.Awake)
        {
          _.SetContainedItemPositionsIfNeeded();
        }
      }
      else if (!_.hasSignalConnections && _.activeContainedItems.Count == 0)
      {
        _.IsActive = false;
        return false;
      }

      foreach (ItemContainer.ActiveContainedItem activeContainedItem in _.activeContainedItems)
      {
        if (!_.ShouldApplyEffects(activeContainedItem)) continue;

        StatusEffect effect = activeContainedItem.StatusEffect;
        effect.Apply(ActionType.OnActive, deltaTime, _.item, _.targets);
        effect.Apply(ActionType.OnContaining, deltaTime, _.item, _.targets);
        if (_.item.GetComponent<Wearable>() is Wearable { IsActive: true })
        {
          effect.Apply(ActionType.OnWearing, deltaTime, _.item, _.targets);
        }
      }

      return false;
    }

  }
}