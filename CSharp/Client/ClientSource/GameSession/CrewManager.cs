using System;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Collections.Immutable;

using HarmonyLib;
using Barotrauma;

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {
    [ThisIsHowToPatchIt]
    public static void PatchClientCrewManager()
    {
      harmony.Patch(
        original: typeof(CrewManager).GetMethod("GetManualOrderPriority", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_GetManualOrderPriority_Replace"))
      );

      harmony.Patch(
        original: typeof(CrewManager).GetMethod("CreateOrder", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_CreateOrder_Replace"))
      );

      harmony.Patch(
        original: typeof(CrewManager).GetMethod("OnOrdersRearranged", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_OnOrdersRearranged_Replace"))
      );

      harmony.Patch(
        original: typeof(CrewManager).GetMethod("AddCurrentOrderIcon", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_AddCurrentOrderIcon_Replace"))
      );

      harmony.Patch(
        original: typeof(CrewManager).GetMethod("AddPreviousOrderIcon", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("CrewManager_AddPreviousOrderIcon_Replace"))
      );
    }

    public static bool CrewManager_GetManualOrderPriority_Replace(CrewManager __instance, ref int __result, Character character, Order order)
    {
      __result = character?.Info?.GetManualOrderPriority(order) ?? CharacterInfo.HighestManualOrderPriority;
      return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaClient/ClientSource/GameSession/CrewManager.cs#L1411
    public static bool CrewManager_CreateOrder_Replace(CrewManager __instance, ref bool __result, OrderPrefab orderPrefab, Hull targetHull = null)
    {
      CrewManager _ = __instance;

      var sub = Character.Controlled?.Submarine;

      if (sub == null || sub.TeamID != Character.Controlled.TeamID || sub.Info.IsWreck) { __result = false; return false; }

      var order = new Order(orderPrefab, targetHull, null, Character.Controlled)
          .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
      _.SetCharacterOrder(null, order);

      if (_.IsSinglePlayer)
      {
        HumanAIController.ReportProblem(Character.Controlled, order);
      }

      __result = true; return false;
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaClient/ClientSource/GameSession/CrewManager.cs#L1223
    public static void CrewManager_OnOrdersRearranged_Replace(CrewManager __instance, ref bool __runOriginal, GUIListBox orderList, object userData)
    {
      CrewManager _ = __instance;
      __runOriginal = false;


      var orderComponent = orderList.Content.GetChildByUserData(userData);
      if (orderComponent == null) { return; }
      var orderInfo = (Order)userData;
      var priority = Math.Max(CharacterInfo.HighestManualOrderPriority - orderList.Content.GetChildIndex(orderComponent), 1);
      if (orderInfo.ManualPriority == priority) { return; }
      var character = (Character)orderList.UserData;
      _.SetCharacterOrder(character, orderInfo.WithManualPriority(priority), isNewOrder: false);
    }

    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaClient/ClientSource/GameSession/CrewManager.cs#L964
    public static void CrewManager_AddCurrentOrderIcon_Replace(CrewManager __instance, ref bool __runOriginal, Character character, Order order)
    {
      CrewManager _ = __instance;
      __runOriginal = false;


      if (character == null) { return; }

      var characterComponent = _.crewList.Content.GetChildByUserData(character);

      if (characterComponent == null) { return; }

      var currentOrderIconList = _.GetCurrentOrderIconList(characterComponent);
      var currentOrderIcons = currentOrderIconList.Content.Children;
      var iconsToRemove = new List<GUIComponent>();
      var newPreviousOrders = new List<Order>();
      bool updatedExistingIcon = false;

      foreach (var icon in currentOrderIcons)
      {
        var orderInfo = (Order)icon.UserData;
        var matchingOrder = character.GetCurrentOrder(orderInfo);
        if (matchingOrder is null)
        {
          iconsToRemove.Add(icon);
          newPreviousOrders.Add(orderInfo);
        }
        else if (orderInfo.MatchesOrder(order))
        {
          icon.UserData = order.Clone();
          if (icon is GUIImage image)
          {
            image.Sprite = _.GetOrderIconSprite(order);
            image.ToolTip = _.CreateOrderTooltip(order);
          }
          updatedExistingIcon = true;
        }
      }
      iconsToRemove.ForEach(c => currentOrderIconList.RemoveChild(c));

      // Remove a previous order icon if it matches the new order
      // We don't want the same order as both a current order and a previous order
      var previousOrderIconGroup = _.GetPreviousOrderIconGroup(characterComponent);
      var previousOrderIcons = previousOrderIconGroup.Children;
      foreach (var icon in previousOrderIcons)
      {
        var orderInfo = (Order)icon.UserData;
        if (orderInfo.MatchesOrder(order))
        {
          previousOrderIconGroup.RemoveChild(icon);
          break;
        }
      }

      // Rearrange the icons before adding anything
      if (updatedExistingIcon)
      {
        RearrangeIcons();
      }

      for (int i = newPreviousOrders.Count - 1; i >= 0; i--)
      {
        _.AddPreviousOrderIcon(character, characterComponent, newPreviousOrders[i]);
      }

      if (order == null || order.Identifier == _.dismissedOrderPrefab.Identifier || updatedExistingIcon)
      {
        RearrangeIcons();
        return;
      }

      int orderIconCount = currentOrderIconList.Content.CountChildren + previousOrderIconGroup.CountChildren;
      if (orderIconCount >= CharacterInfo.MaxCurrentOrders)
      {
        _.RemoveLastOrderIcon(characterComponent);
      }

      float nodeWidth = ((1.0f / CharacterInfo.MaxCurrentOrders) * currentOrderIconList.Parent.Rect.Width) - ((CharacterInfo.MaxCurrentOrders - 1) * currentOrderIconList.Spacing);
      Point size = new Point((int)nodeWidth, currentOrderIconList.RectTransform.NonScaledSize.Y);
      var nodeIcon = _.CreateNodeIcon(size, currentOrderIconList.Content.RectTransform, _.GetOrderIconSprite(order), order.Color, tooltip: _.CreateOrderTooltip(order));
      nodeIcon.UserData = order.Clone();
      nodeIcon.OnSecondaryClicked = (image, userData) =>
      {
        if (!CrewManager.CanIssueOrders) { return false; }
        var orderInfo = (Order)userData;
        var order = orderInfo.GetDismissal().WithManualPriority(character.GetCurrentOrder(orderInfo)?.ManualPriority ?? 0).WithOrderGiver(Character.Controlled);
        _.SetCharacterOrder(character, order);
        return true;
      };

      new GUIFrame(new RectTransform(new Point((int)(1.5f * nodeWidth)), parent: nodeIcon.RectTransform, Anchor.Center), "OuterGlowCircular")
      {
        CanBeFocused = false,
        Color = order.Color,
        UserData = "glow",
        Visible = false
      };

      int hierarchyIndex = Math.Clamp(CharacterInfo.HighestManualOrderPriority - order.ManualPriority, 0, Math.Max(currentOrderIconList.Content.CountChildren - 1, 0));
      if (hierarchyIndex != currentOrderIconList.Content.GetChildIndex(nodeIcon))
      {
        nodeIcon.RectTransform.RepositionChildInHierarchy(hierarchyIndex);
      }

      RearrangeIcons();

      void RearrangeIcons()
      {
        if (character.CurrentOrders != null)
        {
          // Make sure priority values are up-to-date
          foreach (var currentOrderInfo in character.CurrentOrders)
          {
            var component = currentOrderIconList.Content.FindChild(c => c?.UserData is Order componentOrderInfo &&
                componentOrderInfo.MatchesOrder(currentOrderInfo));
            if (component == null) { continue; }
            var componentOrderInfo = (Order)component.UserData;
            int newPriority = currentOrderInfo.ManualPriority;
            if (componentOrderInfo.ManualPriority != newPriority)
            {
              component.UserData = componentOrderInfo.WithManualPriority(newPriority);
            }
          }

          currentOrderIconList.Content.RectTransform.SortChildren((x, y) =>
          {
            var xOrder = (Order)x.GUIComponent.UserData;
            var yOrder = (Order)y.GUIComponent.UserData;
            return yOrder.ManualPriority.CompareTo(xOrder.ManualPriority);
          });

          if (currentOrderIconList.Parent is GUILayoutGroup parentGroup)
          {
            int iconCount = currentOrderIconList.Content.CountChildren;
            float nonScaledWidth = ((float)iconCount / CharacterInfo.MaxCurrentOrders) * parentGroup.Rect.Width + (iconCount * currentOrderIconList.Spacing);
            currentOrderIconList.RectTransform.NonScaledSize = new Point((int)nonScaledWidth, currentOrderIconList.RectTransform.NonScaledSize.Y);
            parentGroup.Recalculate();
            previousOrderIconGroup.Recalculate();
          }
        }
      }
    }


    // https://github.com/evilfactory/LuaCsForBarotrauma/blob/ad837423a8d71666dc0a5621713e2ab1fe7e2802/Barotrauma/BarotraumaClient/ClientSource/GameSession/CrewManager.cs#L1103
    public static void CrewManager_AddPreviousOrderIcon_Replace(CrewManager __instance, ref bool __runOriginal, Character character, GUIComponent characterComponent, Order orderInfo)
    {
      CrewManager _ = __instance;
      __runOriginal = false;


      if (orderInfo == null || orderInfo.Identifier == _.dismissedOrderPrefab.Identifier) { return; }

      var currentOrderIconList = _.GetCurrentOrderIconList(characterComponent);
      int maxPreviousOrderIcons = CharacterInfo.MaxCurrentOrders - currentOrderIconList.Content.CountChildren;

      if (maxPreviousOrderIcons < 1) { return; }

      var previousOrderIconGroup = _.GetPreviousOrderIconGroup(characterComponent);
      if (previousOrderIconGroup.CountChildren >= maxPreviousOrderIcons)
      {
        _.RemoveLastPreviousOrderIcon(previousOrderIconGroup);
      }

      float nodeWidth = ((1.0f / CharacterInfo.MaxCurrentOrders) * previousOrderIconGroup.Parent.Rect.Width) - ((CharacterInfo.MaxCurrentOrders - 1) * currentOrderIconList.Spacing);
      Point size = new Point((int)nodeWidth, previousOrderIconGroup.Rect.Height);
      var previousOrderInfo = orderInfo.WithType(Order.OrderType.Previous);
      var prevOrderFrame = new GUIButton(new RectTransform(size, parent: previousOrderIconGroup.RectTransform), style: null)
      {
        UserData = previousOrderInfo,
        OnClicked = (button, userData) =>
        {
          if (!CrewManager.CanIssueOrders) { return false; }
          var orderInfo = (Order)userData;
          int priority = _.GetManualOrderPriority(character, orderInfo);
          _.SetCharacterOrder(character, orderInfo.WithManualPriority(priority).WithOrderGiver(Character.Controlled));
          return true;
        },
        OnSecondaryClicked = (button, userData) =>
        {
          if (previousOrderIconGroup == null) { return false; }
          previousOrderIconGroup.RemoveChild(button);
          previousOrderIconGroup.Recalculate();
          return true;
        }
      };
      prevOrderFrame.RectTransform.IsFixedSize = true;

      var prevOrderIconFrame = new GUIFrame(
          new RectTransform(new Vector2(0.8f), prevOrderFrame.RectTransform, anchor: Anchor.BottomLeft),
          style: null);

      _.CreateNodeIcon(Vector2.One,
          prevOrderIconFrame.RectTransform,
          _.GetOrderIconSprite(previousOrderInfo),
          previousOrderInfo.Color,
          tooltip: _.CreateOrderTooltip(previousOrderInfo));

      foreach (GUIComponent c in prevOrderIconFrame.Children)
      {
        c.HoverColor = c.Color;
        c.PressedColor = c.Color;
        c.SelectedColor = c.Color;
      }

      new GUIImage(
          new RectTransform(new Vector2(0.8f), prevOrderFrame.RectTransform, anchor: Anchor.TopRight),
          _.previousOrderArrow,
          scaleToFit: true)
      {
        CanBeFocused = false
      };

      prevOrderFrame.SetAsFirstChild();
    }
  }
}