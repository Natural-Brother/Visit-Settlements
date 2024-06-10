using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using VisitSettlements;

public class HarmonyPatches : Mod
{
	public static bool isIdeologyLoaded = ModsConfig.IdeologyActive;

	public HarmonyPatches(ModContentPack content) : base(content)
	{
		var harmony = new Harmony("alt4s.visitsettlements");
		harmony.PatchAll();
	}
}

[HarmonyPatch(typeof(Settlement), "GetCaravanGizmos")]
public static class VS_GetCaravanGizmos
{
	public static void Postfix(Settlement __instance, Caravan caravan, ref IEnumerable<Gizmo> __result)
	{
		if (__instance.Faction == null || __instance.Faction.HostileTo(Faction.OfPlayer) || __instance.Faction.IsPlayer)
		{
			return;
		}

		var gizmos = __result.ToList();

		Command_Action enterCommand = new Command_Action
		{
			icon = ContentFinder<Texture2D>.Get("UI/Gizmos/EnterSettlement"),
			defaultLabel = "VS_EnterSettlementLabel".Translate(),
			defaultDesc = "VS_EnterSettlementDesc".Translate(),
			action = delegate
			{
				LongEventHandler.QueueLongEvent(delegate
				{
					try
					{
						var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

						if (worldComponent.settlementMaps.ContainsKey(__instance.Tile))
						{
							var existingMap = worldComponent.settlementMaps[__instance.Tile];

							CaravanEnterMapUtility.Enter(caravan, existingMap, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop);
							CameraJumper.TryJump(new GlobalTargetInfo(existingMap.Center, existingMap));
							return;
						}

						MapParent mapParent = Find.WorldObjects.MapParentAt(__instance.Tile);

						worldComponent.settlementMapParents.Add(mapParent.Tile, mapParent);

						mapParent = worldComponent.settlementMapParents[mapParent.Tile];

						var map = GetOrGenerateMapUtility.GetOrGenerateMap(mapParent.Tile, null);

						worldComponent.settlementMaps.Add(mapParent.Tile, map);

						var inventoryItems = new HashSet<ThingWithComps>();
						var colonists = map.mapPawns.FreeColonists;
						foreach (var colonist in colonists)
						{
							foreach (var item in colonist.inventory.innerContainer)
							{
								inventoryItems.Add(item as ThingWithComps);
							}
						}

						var items = map.listerThings.AllThings
							.OfType<ThingWithComps>()
							.Where(t => t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Building && t.def.Minifiable && !inventoryItems.Contains(t))
							.ToHashSet();
						worldComponent.settlementItems.UnionWith(items);

						foreach (var item in items)
						{
							if (item.def.category == ThingCategory.Item)
							{
								item.SetForbidden(true, warnOnFail: false);
							}
						}

						Unfog(map);
						MakeHomeArea(map, __instance);
						MakeStructures(map);

						mapParent.GetComponent<TimedDetectionRaids>().ResetCountdown();

						CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop);

						CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
					}
					catch (Exception ex)
					{
						Log.Error("[Visit Settlements] Exception occurred while entering settlement: " + ex);

						var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
						worldComponent.settlementMaps.Remove(__instance.Tile);
						worldComponent.settlementMapParents.Remove(__instance.Tile);
					}
				}, "GeneratingMapForNewEncounter", false, null);
			}
		};

		gizmos.Add(enterCommand);
		__result = gizmos;
	}

	private static void Unfog(Map map)
	{
		List<Room> allRooms = map.regionGrid.allRooms;

		foreach (var room in allRooms)
		{
			if (room == null) continue;

			foreach (var region in room.Regions)
			{
				if (region == null) continue;

				foreach (var cell in region.Cells)
				{
					map.fogGrid.Unfog(cell);
				}
			}

			foreach (var building in room.ContainedAndAdjacentThings.OfType<Building>())
			{
				map.fogGrid.Unfog(building.Position);

				foreach (var adjCell in GenAdj.CellsAdjacentCardinal(building))
				{
					if (adjCell.InBounds(map))
					{
						map.fogGrid.Unfog(adjCell);
					}
				}
			}
		}
	}

	private static void MakeStructures(Map map)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		var recreationBuildings = map.listerBuildings.allBuildingsNonColonist
			.Where(b => b is Building && b.def.building.joyKind != null);

		foreach (var recBuild in recreationBuildings)
		{
			recBuild.SetFactionDirect(Faction.OfPlayer);
			worldComponent.settlementStructures.Add(recBuild);
		}
	}

	public static int homeAreaRadius = 3;

	private static void MakeHomeArea(Map map, Settlement settlement)
	{
		List<Building> factionBuildings = map.listerBuildings.allBuildingsNonColonist
			.Where(building => building.Faction == settlement.Faction).ToList();

		Area homeArea = map.areaManager.Home;

		foreach (var building in factionBuildings)
		{
			homeArea[building.Position] = true;

			for (int dx = -homeAreaRadius; dx <= homeAreaRadius; dx++)
			{
				for (int dy = -homeAreaRadius; dy <= homeAreaRadius; dy++)
				{
					IntVec3 adjacent = building.Position + new IntVec3(dx, 0, dy);
					if (adjacent.InBounds(map))
					{
						homeArea[adjacent] = true;
					}
				}
			}
		}
	}
}

[HarmonyPatch(typeof(SettlementDefeatUtility), "CheckDefeated")]
internal static class VS_SettlementDefeatUtility_CheckDefeated
{
	private static int tickCounter = 0;

	private static WorldComponent_SettlementData worldComponent;

	static VS_SettlementDefeatUtility_CheckDefeated()
	{
		worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
	}

	private static bool Prefix(Settlement factionBase)
	{
		tickCounter++;

		if (tickCounter >= GenDate.TicksPerHour * 6)
		{
			worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
			tickCounter = 0;
		}

		if (worldComponent == null || worldComponent.settlementMapParents == null)
		{
			return true;
		}

		if (!worldComponent.settlementMapParents.TryGetValue(factionBase.Tile, out var parent))
		{
			return true;
		}

		if (!factionBase.Faction.HostileTo(Faction.OfPlayer))
		{
			return false;
		}

		var map = factionBase.Map;

		if (worldComponent != null)
		{
			worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

			worldComponent.settlementItems.RemoveWhere(item => item?.Map == map);
			worldComponent.settlementStructures.RemoveAll(building => building?.Map == map);
			worldComponent.settlementMaps.Remove(factionBase.Tile);
			worldComponent.settlementMapParents.Remove(factionBase.Tile);

			var bedTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();
			if (bedTracker != null && bedTracker.bedExpirations != null)
			{
				bedTracker.bedExpirations.RemoveAll(beds => beds.Key?.Map == map);
			}
		}

		return true;
	}
}

[HarmonyPatch(typeof(FormCaravanComp), "GetGizmos")]
public static class VS_FormCaravanComp_GetGizmos
{
	public static void Postfix(FormCaravanComp __instance, ref IEnumerable<Gizmo> __result)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementMapParents.ContainsKey(__instance.parent.Tile))
		{
			List<Gizmo> gizmosList = new List<Gizmo>();

			foreach (var gizmo in __result)
			{
				if (gizmo is Command_Action commandAction && commandAction.defaultLabel == "CommandReformCaravan".Translate())
				{
					commandAction.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/ReformSettlement");

					commandAction.defaultLabel = "VS_ReformCaravanLabel".Translate();
					commandAction.defaultDesc = "VS_ReformCaravanDesc".Translate();
				}

				gizmosList.Add(gizmo);
			}

			__result = gizmosList;
		}
	}
}

//prevent allies from getting angry for no reason
[HarmonyPatch(typeof(LordJob_DefendBase), "CreateGraph")]
public static class VS_SettlementLordJob_DefendBase_Patch
{
	public static bool Prefix(LordJob_DefendBase __instance, ref StateGraph __result)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementMapParents.ContainsKey(__instance.Map.Tile))
		{
			var baseCenterField = typeof(LordJob_DefendBase).GetField("baseCenter", BindingFlags.NonPublic | BindingFlags.Instance);

			if (baseCenterField != null)
			{
				IntVec3 baseCenter = (IntVec3)baseCenterField.GetValue(__instance);

				StateGraph stateGraph = new StateGraph();

				LordToil_DefendBase lordToil_DefendBase = new LordToil_DefendBase(baseCenter);
				stateGraph.StartingToil = lordToil_DefendBase;

				Transition noAttackTransition = new Transition(lordToil_DefendBase, new LordToil_DefendBase(baseCenter));
				noAttackTransition.AddTrigger(new Trigger_BecameNonHostileToPlayer());
				stateGraph.AddTransition(noAttackTransition);

				__result = stateGraph;
				return false;
			}
		}

		return true;
	}
}

//track settlement structures
[HarmonyPatch(typeof(Building), "Destroy")]
public static class VS_PenalizeForDestruction
{
	public static void Prefix(Building __instance, DestroyMode mode)
	{
		if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableDestructionPenalty)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementStructures.Contains(__instance) && mode == DestroyMode.Deconstruct)
		{
			var VandalizedInSettlement = DefDatabase<HistoryEventDef>.GetNamed("VandalizedInSettlement");
			__instance.Map.Parent.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -5, true, reason: VandalizedInSettlement);

			worldComponent.settlementStructures.Remove(__instance);
		}
	}
}

[HarmonyPatch(typeof(MinifyUtility), "MakeMinified")]
public static class VS_PenalizeForStructureTheft
{
	public static void Prefix(Thing thing)
	{
		if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableMinifyingPenalty)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		Building building = thing as Building;

		if (worldComponent.settlementStructures.Contains(building))
		{
			var ItemStolenFromSettlement = DefDatabase<HistoryEventDef>.GetNamed("ItemStolenFromSettlement");
			thing.Map.Parent.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -5, true, reason: ItemStolenFromSettlement);

			worldComponent.settlementStructures.Remove(building);
		}
	}
}

//track stealing (on map)
[HarmonyPatch(typeof(JobDriver), "ReadyForNextToil")]
public static class VS_PenalizeForStealing
{
	public static void Postfix(JobDriver __instance)
	{
		if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableStealingPenalty)
		{
			return;
		}
		var map = __instance.pawn?.Map;

		if (map == null)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementMaps.ContainsKey(map.Tile))
		{
			if (__instance.pawn.Faction == Faction.OfPlayer)
			{
				if (__instance.job?.targetA.HasThing == true)
				{
					var thing = __instance.job.targetA.Thing as ThingWithComps;
					var trackedItems = worldComponent.settlementItems;

					if ((trackedItems.Contains(thing) && !(thing is Building)) || (trackedItems.Contains(thing) && thing is MinifiedThing))
					{
						if (thing.def.BaseMarketValue > 0)
						{
							int goodwillImpact = CalculateGoodwillImpact(thing.def.BaseMarketValue, thing.stackCount);
							var itemStolenEvent = DefDatabase<HistoryEventDef>.GetNamed("ItemStolenFromSettlement");
							map.Parent.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -goodwillImpact, true, reason: itemStolenEvent);

							trackedItems.Remove(thing);
						}
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

//track stealing when reforming
[HarmonyPatch(typeof(Dialog_FormCaravan), "TrySend")]
public static class VS_CheckForSettlementTheftOnTrySend
{
	public static void Prefix(Dialog_FormCaravan __instance)
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

//track building
[HarmonyPatch(typeof(Frame), "CompleteConstruction")]
public static class VS_PenalizeForBuilding
{
	public static void Postfix(Frame __instance)
	{
		if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableEncroachingPenalty)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementMaps.TryGetValue(Find.CurrentMap.Tile, out var map))
		{
			if (map.Parent is Settlement settlement && settlement.Faction != null && !settlement.Faction.IsPlayer)
			{
				if (__instance.Faction == Faction.OfPlayer)
				{
					var EncroachedInSettlement = DefDatabase<HistoryEventDef>.GetNamed("EncroachedInSettlement");
					settlement.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -5, reason: EncroachedInSettlement);
				}
			}
		}
	}
}

//track mining rocks
[HarmonyPatch(typeof(JobDriver), "EndJobWith")]
public static class VS_PenalizeForMining
{
	public static void Postfix(JobDriver __instance, JobCondition condition)
	{
		if (!VS_Mod.settings.enablePenalties || !VS_Mod.settings.enableMiningPenalty)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
		var map = __instance.pawn?.Map;

		if (map != null && worldComponent.settlementMaps.ContainsKey(map.Tile))
		{
			if (__instance is JobDriver_Mine && condition == JobCondition.Succeeded)
			{
				if (map.Parent is Settlement settlement && settlement.Faction != null && !settlement.Faction.IsPlayer)
				{
					var VandalizedInSettlement = DefDatabase<HistoryEventDef>.GetNamed("VandalizedInSettlement");
					settlement.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -5, true, reason: VandalizedInSettlement);
				}
			}
		}
	}
}

//ally interaction
[HarmonyPatch(typeof(FloatMenuMakerMap), "AddHumanlikeOrders")]
public static class VS_SettlementInteractionMenu
{
	public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
	{
		if (VS_Mod.settings.enableInteractionMenu)
		{
			var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

			if (worldComponent.settlementMaps.ContainsKey(pawn.Map.Tile))
			{
				List<Pawn> targetPawns = GenUI.TargetsAt(clickPos, new TargetingParameters
				{
					canTargetPawns = true,
					mapObjectTargetsMustBeAutoAttackable = false,
					validator = target => target.HasThing && target.Thing is Pawn && target.Thing.Faction != null && target.Thing.Faction != Faction.OfPlayer
				}, false)
				.Select(target => target.Thing as Pawn)
				.ToList();

				if (pawn.jobs == null || pawn.jobs.curJob == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
				{
					return;
				}

				foreach (var targetPawn in targetPawns)
				{
					if (targetPawn == null || targetPawn.Faction == null)
					{
						continue;
					}

					if (targetPawn.Faction.HostileTo(Faction.OfPlayer))
					{
						continue;
					}

					if (!pawn.CanReach(targetPawn, PathEndMode.OnCell, Danger.Deadly))
					{
						opts.Add(new FloatMenuOption($"VS_FloatMenu_CannotReach".Translate(targetPawn.LabelShort), null));
						continue;
					}

					var floatMenuOption = new FloatMenuOption($"VS_InteractWithAllyLabel".Translate(targetPawn.LabelShort), delegate
					{
						var job = JobMaker.MakeJob(SettlementJobDefOf.InteractWithAlly, targetPawn);
						pawn.jobs.StartJob(job, JobCondition.InterruptForced);
					});

					opts.Add(FloatMenuUtility.DecoratePrioritizedTask(floatMenuOption, pawn, targetPawn));
				}
			}
		}
	}
}

//deinit if all colonists downed
[HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
public static class VS_Pawn_Downed_Patch
{
	public static void Postfix(Pawn_HealthTracker __instance)
	{
		Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
		Map map = pawn.Map;

		if (pawn == null || map == null || __instance == null)
		{
			return;
		}

		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
		if (worldComponent.settlementMaps.ContainsKey(map.Tile))
		{
			if (map.mapPawns.FreeColonistsSpawned.All(p => p.Downed))
			{
				DeinitializeMap(map);
			}
		}
	}

	private static void DeinitializeMap(Map map)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		int tile = map.Tile;

		worldComponent.settlementItems.RemoveWhere(item => item.Map == map);
		worldComponent.settlementStructures.RemoveAll(building => building.Map == map);
		worldComponent.settlementMaps.Remove(tile);
		worldComponent.settlementMapParents.Remove(tile);

		var bedTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();
		bedTracker.bedExpirations.RemoveAll(beds => beds.Key?.Map == map);

		Current.Game.DeinitAndRemoveMap(map, true);
	}
}

[HarmonyPatch(typeof(MapDeiniter), "Deinit")]
public static class VS_MapDeiniter_Deinit
{
	public static void Prefix(Map map)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (map != null && worldComponent.settlementMaps.ContainsValue(map))
		{
			var exitFromTile = map.Tile;
			var colonists = map.mapPawns?.FreeColonists;

			if (colonists == null || colonists.Any())
			{
				return;
			}

			worldComponent.settlementItems.RemoveWhere(item => item?.Map == map);
			worldComponent.settlementStructures.RemoveAll(building => building?.Map == map);
			worldComponent.settlementMaps.Remove(exitFromTile);
			worldComponent.settlementMapParents.Remove(exitFromTile);

			var bedTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();
			bedTracker.bedExpirations.RemoveAll(beds => beds.Key?.Map == map);
		}
	}
}

[HarmonyPatch(typeof(CaravanExitMapUtility), "ExitMapAndCreateCaravan", new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(int), typeof(int), typeof(int), typeof(bool) })]
public static class VS_CaravanExitMapUtility_Overload2
{
	public static void Postfix(int exitFromTile)
	{
		var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

		if (worldComponent.settlementMaps.TryGetValue(exitFromTile, out var exitingMap) && exitingMap != null)
		{
			exitingMap.Parent?.CheckRemoveMapNow();
		}
	}
}