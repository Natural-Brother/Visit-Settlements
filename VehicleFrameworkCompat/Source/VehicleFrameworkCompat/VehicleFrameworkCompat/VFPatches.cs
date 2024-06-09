using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vehicles;
using Verse;
using VisitSettlements;

public class HarmonyPatches_VF : Mod
{
    public HarmonyPatches_VF(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("alt4s.visitsettlements_vfcompat");
        harmony.PatchAll();
    }
}

[HarmonyPatch(typeof(Dialog_FormVehicleCaravan), "TryReformCaravan")]
public static class VS_CheckForSettlementTheftOnReform_Vehicles
{
    public static void Prefix(Dialog_FormVehicleCaravan __instance)
    {
        if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableCaravanPenalty)
        {
            return;
        }

        var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

        if (worldComponent.settlementMaps.TryGetValue(__instance.CurrentTile, out var map))
        {
            var inventoryItems = new HashSet<ThingWithComps>();
            var colonists = map.mapPawns.FreeColonists;
            foreach (var colonist in colonists)
            {
                foreach (var item in colonist.inventory.innerContainer)
                {
                    inventoryItems.Add(item as ThingWithComps);
                }
            }

            foreach (var transferable in __instance.transferables)
            {
                if (transferable.CountToTransfer > 0)
                {
                    int itemCount = transferable.CountToTransfer;

                    foreach (var item in inventoryItems)
                    {
                        if (item.def == transferable.ThingDef)
                        {
                            itemCount -= item.stackCount;

                            if (itemCount <= 0)
                            {
                                return;
                            }
                        }
                    }

                    if (worldComponent.settlementItems.Contains(transferable.AnyThing))
                    {
                        int goodwillImpact = CalculateGoodwillImpact(transferable.ThingDef.BaseMarketValue, itemCount);

                        var itemStolenEvent = DefDatabase<HistoryEventDef>.GetNamed("ItemStolenFromSettlement");
                        map.Parent.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -goodwillImpact, true, reason: itemStolenEvent);

                        worldComponent.settlementItems.Remove(transferable.AnyThing as ThingWithComps);
                    }
                }
            }
        }
    }

    private static int CalculateGoodwillImpact(float marketValue, int itemCount)
    {
        int basePenalty = VS_Mod.settings.basePenalty;
        float scalingFactor = VS_Mod.settings.scalingFactor;

        return Mathf.RoundToInt(basePenalty + scalingFactor * marketValue * itemCount);
    }
}