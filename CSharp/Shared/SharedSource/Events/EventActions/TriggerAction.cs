using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;


namespace CleanPatches
{
  public partial class Mod : IAssemblyPlugin
  {

    [ThisIsHowToPatchIt]
    public static void PatchSharedTriggerAction()
    {
      harmony.Patch(
        original: typeof(TriggerAction).GetMethod("Update", AccessTools.all),
        prefix: new HarmonyMethod(typeof(Mod).GetMethod("TriggerAction_Update_Replace"))
      );
    }

    public static bool TriggerAction_Update_Replace(TriggerAction __instance, float deltaTime)
    {
      TriggerAction _ = __instance;

      if (_.isFinished) { return false; }

      _.isRunning = true;

      var targets1 = _.ParentEvent.GetTargets(_.Target1Tag);
      if (!targets1.Any()) { return false; }

      _.triggerers.Clear();
      foreach (Entity e1 in targets1)
      {
        if (_.DisableInCombat && TriggerAction.IsInCombat(e1))
        {
          if (_.CheckAllTargets)
          {
            return false;
          }
          continue;
        }
        if (_.DisableIfTargetIncapacitated && e1 is Character character1 && (character1.IsDead || character1.IsIncapacitated))
        {
          if (_.CheckAllTargets)
          {
            return false;
          }
          continue;
        }
        if (!_.TargetModuleType.IsEmpty)
        {
          if (!_.CheckAllTargets && _.CheckDistanceToHull(e1, out Hull hull))
          {
            _.Trigger(e1, hull);
            return false;
          }
          else if (_.CheckAllTargets)
          {
            if (_.CheckDistanceToHull(e1, out hull))
            {
              _.triggerers.Add((e1, hull));
            }
            else
            {
              return false;
            }
          }
          continue;
        }

        var targets2 = _.ParentEvent.GetTargets(_.Target2Tag);

        foreach (Entity e2 in targets2)
        {
          if (e1 == e2)
          {
            continue;
          }
          if (_.DisableInCombat && TriggerAction.IsInCombat(e2))
          {
            if (_.CheckAllTargets)
            {
              return false;
            }
            continue;
          }
          if (_.DisableIfTargetIncapacitated && e2 is Character character2 && (character2.IsDead || character2.IsIncapacitated))
          {
            if (_.CheckAllTargets)
            {
              return false;
            }
            continue;
          }

          if (_.WaitForInteraction)
          {
            Character player = null;
            Character npc = null;
            Item item = null;
            if (e1 is Character char1)
            {
              if (char1.IsPlayer)
              {
                player = char1;
              }
              else
              {
                npc ??= char1;
              }
            }
            else
            {
              item ??= e1 as Item;
            }
            if (e2 is Character char2)
            {
              if (char2.IsPlayer)
              {
                player = char2;
              }
              else
              {
                npc ??= char2;
              }
            }
            else
            {
              item ??= e2 as Item;
            }

            if (player != null)
            {
              if (npc != null)
              {
                if (!_.npcsOrItems.Any(n => n.TryGet(out Character npc2) && npc2 == npc))
                {
                  _.npcsOrItems.Add(npc);
                }
                if (npc.CampaignInteractionType == CampaignMode.InteractionType.Talk)
                {
                  //if the NPC has a conversation available, don't assign the trigger until the conversation is done
                  continue;
                }
                else if (npc.CampaignInteractionType != CampaignMode.InteractionType.Examine)
                {
                  npc.CampaignInteractionType = CampaignMode.InteractionType.Examine;
                  npc.RequireConsciousnessForCustomInteract = _.DisableIfTargetIncapacitated;
                  npc.SetCustomInteract(
                      (Character npc, Character interactor) =>
                      {
                        //the first character in the CustomInteract callback is always the NPC and the 2nd the character who interacted with it
                        //but the TriggerAction can configure the 1st and 2nd entity in either order,
                        //let's make sure we pass the NPC and the interactor in the intended order
                        if (e1 == npc && _.ParentEvent.GetTargets(_.Target2Tag).Contains(interactor))
                        {
                          _.Trigger(npc, interactor);
                        }
                        else if (_.ParentEvent.GetTargets(_.Target1Tag).Contains(interactor) && e2 == npc)
                        {
                          _.Trigger(interactor, npc);
                        }
                      },
#if CLIENT
                      TextManager.GetWithVariable("CampaignInteraction.Examine", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)));
#else
                      TextManager.Get("CampaignInteraction.Talk"));
                  GameMain.NetworkMember.CreateEntityEvent(npc, new Character.AssignCampaignInteractionEventData());
#endif
                }
                if (!_.AllowMultipleTargets) { return false; }
              }
              else if (item != null)
              {
                if (!_.npcsOrItems.Any(n => n.TryGet(out Item item2) && item2 == item))
                {
                  _.npcsOrItems.Add(item);
                }
                item.AssignCampaignInteractionType(CampaignMode.InteractionType.Examine,
                    GameMain.NetworkMember?.ConnectedClients.Where(c => c.Character != null && targets2.Contains(c.Character)));
                if (player.SelectedItem == item ||
                    player.SelectedSecondaryItem == item ||
                    (player.Inventory != null && player.Inventory.Contains(item)) ||
                    (player.FocusedItem == item && player.IsKeyHit(InputType.Use)))
                {
                  _.Trigger(e1, e2);
                  return false;
                }
              }
            }
          }
          else
          {
            Vector2 pos1 = e1.WorldPosition;
            Vector2 pos2 = e2.WorldPosition;
            _.distance = Vector2.Distance(pos1, pos2);
            if ((_.Type == TriggerAction.TriggerType.Inside) == IsWithinRadius())
            {
              if (!_.CheckAllTargets)
              {
                _.Trigger(e1, e2);
                return false;
              }
              else
              {
                _.triggerers.Add((e1, e2));
              }
            }
            else if (_.CheckAllTargets)
            {
              return false;
            }

            bool IsWithinRadius() =>
                ((e1 is MapEntity m1) && Submarine.RectContains(m1.WorldRect, pos2)) ||
                ((e2 is MapEntity m2) && Submarine.RectContains(m2.WorldRect, pos1)) ||
                Vector2.DistanceSquared(pos1, pos2) < _.Radius * _.Radius;
          }
        }
      }

      foreach (var (e1, e2) in _.triggerers)
      {
        _.Trigger(e1, e2);
      }

      return false;
    }

  }
}