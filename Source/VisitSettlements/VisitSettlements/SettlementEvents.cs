using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using VisitSettlements;

public class VS_WorldComponent_SettlementEvents : WorldComponent
{
    private int tickCounter = 0;
    private int raidTickCounter = 0;
    private readonly int daysCount = VS_Mod.settings.regularDaysCount;
    private readonly float raidChance = VS_Mod.settings.raidChance; //in %
    private int raidDaysCount = Randomize(VS_Mod.settings.raidDayMin, VS_Mod.settings.raidDayMax);

    public VS_WorldComponent_SettlementEvents(World world) : base(world) { }

    public override void WorldComponentTick()
    {
        base.WorldComponentTick();

        if (VS_Mod.settings.enableSettlementEvents)
        {
            tickCounter++;
            raidTickCounter++;

            if (raidTickCounter >= raidDaysCount * GenDate.TicksPerDay)
            {
                if (Rand.Chance(raidChance))
                {
                    TryTriggerRaidEvent();
                    raidTickCounter = 0;
                }
            }

            if (tickCounter >= daysCount * GenDate.TicksPerDay)
            {
                DropFoodPodsForSettlements();
                tickCounter = 0;
            }
        }
    }

    private static int Randomize(int first, int last)
	{
        return Rand.Range(first, last + 1);
    }

    private void DropFoodPodsForSettlements()
    {
        var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

        if (worldComponent.settlementMaps == null || worldComponent.settlementMaps.Count == 0)
		{
            return;
		}

        foreach (var mapEntry in worldComponent.settlementMaps)
        {
            Map map = mapEntry.Value;
            if (map == null) continue;

            var allyPawns = map.mapPawns.AllPawnsSpawned
                .Where(p =>  p != null && p.Faction != null && !p.Faction.IsPlayer && !p.Faction.HostileTo(Faction.OfPlayer))
                .ToList();

            float totalNutritionNeeded = allyPawns.Sum(pawn => pawn.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * 60000f * daysCount);

            float existingNutrition = map.listerThings.ThingsOfDef(ThingDefOf.MealSimple)
                .Where(thing => worldComponent.settlementItems.Contains(thing))
                .Sum(thing => thing.def.ingestible.CachedNutrition);

            totalNutritionNeeded = Mathf.Max(0f, totalNutritionNeeded - existingNutrition);

            if (totalNutritionNeeded <= 0f) continue;

            float mealNutrition = ThingDefOf.MealSimple.ingestible.CachedNutrition;
            int mealCount = Mathf.RoundToInt(totalNutritionNeeded / mealNutrition);

            IntVec3 near = map.Center;
            IntVec3 dropSpot = near;

            int maxAttempts = 10;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (DropCellFinder.TryFindDropSpotNear(near, map, out dropSpot, allowFogged: false, canRoofPunch: false))
                {
                    if (allyPawns.Any(p => p.CanReach(dropSpot, PathEndMode.ClosestTouch, Danger.Deadly)))
                    {
                        break;
                    }
                }
            }

            var foodQuantities = new Dictionary<ThingDef, int>
            {
                { ThingDef.Named("MealSimple"), mealCount }
            };

            ActiveDropPodInfo dropPodInfo = new ActiveDropPodInfo();

            foreach (var itemEntry in foodQuantities)
            {
                ThingDef itemDef = itemEntry.Key;
                int quantity = itemEntry.Value;

                for (int i = 0; i < quantity; i++)
                {
                    Thing newItem = ThingMaker.MakeThing(itemDef);
                    newItem.SetForbidden(true, warnOnFail: false);
                    dropPodInfo.innerContainer.TryAdd(newItem, 1);
                }
            }

            DropPodUtility.MakeDropPodAt(dropSpot, map, dropPodInfo);

            var itemsToTrack = dropPodInfo.innerContainer.ToList().OfType<ThingWithComps>();
            worldComponent.settlementItems.UnionWith(itemsToTrack);
        }
    }

    private void TryTriggerRaidEvent()
    {
        var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

        if (worldComponent.settlementMaps == null || worldComponent.settlementMaps.Count == 0)
        {
            return;
        }

        var settlements = worldComponent.settlementMaps.Values;

        float settlementRaidChance = 0.5f;
        bool raidOccurred = false;

        foreach (var map in settlements)
        {
            if (map == null) continue;

            if (!Rand.Chance(settlementRaidChance))
            {
                continue;
            }

            if (TriggerRaidEvent(map))
            {
                raidOccurred = true;
            }
        }

        if (!raidOccurred)
        {
            var validSettlements = settlements.Where(m => m != null).ToList();
            if (validSettlements.Any())
            {
                var randomSettlement = validSettlements.RandomElement();
                TriggerRaidEvent(randomSettlement);
            }
        }
    }

    private bool TriggerRaidEvent(Map map)
    {
        Faction settlementFaction = map.ParentFaction;

        var hostileFactions = Find.FactionManager.AllFactionsListForReading
            .Where(f => f != null && !f.defeated && !f.IsPlayer && !f.def.hidden && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Count > 0 && (f.HostileTo(Faction.OfPlayer) || f.HostileTo(settlementFaction)))
            .ToList();

        if (hostileFactions == null || hostileFactions.Count == 0) return false;

        var randomHostileFaction = hostileFactions.RandomElement();

        var parms = new IncidentParms
        {
            target = map,
            faction = randomHostileFaction,
            points = StorytellerUtility.DefaultThreatPointsNow(map),
            raidStrategy = RaidStrategyDefOf.ImmediateAttack,
            raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
        };

        IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        raidDaysCount = Randomize(VS_Mod.settings.raidDayMin, VS_Mod.settings.raidDayMax);

        return true;
    }

	public override void ExposeData()
	{
		base.ExposeData();

        Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
        Scribe_Values.Look(ref raidTickCounter, "raidTickCounter", 0);
    }
}