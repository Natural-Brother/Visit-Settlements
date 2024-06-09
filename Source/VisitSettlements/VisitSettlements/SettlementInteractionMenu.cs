using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using VisitSettlements;

public class VS_Dialog_SettlementInteraction : Window
{
    private readonly Pawn interactingPawn;
    private readonly Pawn targetPawn;

    public VS_Dialog_SettlementInteraction(Pawn interactingPawn, Pawn targetPawn)
    {
        this.interactingPawn = interactingPawn;
        this.targetPawn = targetPawn;
        closeOnAccept = true;
        closeOnCancel = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        forcePause = true;
    }

    public override Vector2 InitialSize => new Vector2(500f, 300f);

    public override void DoWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect.ContractedBy(10));

        Text.Font = GameFont.Medium;
        listing.Label($"VS_InteractingWith".Translate(targetPawn.Label));

        Text.Font = GameFont.Small;

        listing.Gap(10);

        if (listing.ButtonText("VS_RentBeds".Translate()))
        {
            Find.WindowStack.Add(new VS_Dialog_RentBeds(interactingPawn, targetPawn));
        }
        if (listing.ButtonText("VS_TradeSupplies".Translate()))
        {
            Find.WindowStack.Add(new VS_Dialog_TradeSupplies(interactingPawn, targetPawn));
        }

        listing.End();

        float iconSize = 128f;
        float bgSize = iconSize - 24f;

        Rect iconRect = new Rect(inRect.center.x - (iconSize / 2f), inRect.yMax - 125f, iconSize, iconSize);
        Rect bgRect = new Rect(iconRect.center.x - (bgSize / 2f), iconRect.center.y - (bgSize / 2f), bgSize, bgSize);
        GUI.DrawTexture(bgRect, ColonistBar.BGTex);
        Widgets.ThingIcon(iconRect, targetPawn);
    }
}

//renting
public class VS_Dialog_RentBeds : Window
{
    private readonly Pawn interactingPawn;
    private readonly Pawn targetPawn;
    private readonly int baseBedCostPerDay = VS_Mod.settings.baseBedCostPerDay;
    private Room rentedRoom;
    private readonly VS_BedOwnershipTracker bedOwnershipTracker;

    private int selectedDays = 1;
    private int totalCost = 0;
    private Vector2 scrollPosition = Vector2.zero;

    public VS_Dialog_RentBeds(Pawn interactingPawn, Pawn targetPawn)
    {
        this.interactingPawn = interactingPawn;
        this.targetPawn = targetPawn;
        closeOnAccept = true;
        closeOnCancel = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        forcePause = true;

        CalculateTotalCost();

        bedOwnershipTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();
    }

    public override Vector2 InitialSize => new Vector2(500f, 350f);

    public override void DoWindowContents(Rect inRect)
    {
        Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, 250);
        Rect viewRect = new Rect(0, 0, outRect.width - 16, Mathf.Max(bedOwnershipTracker.bedExpirations.Count() * (Text.LineHeight * 4), outRect.height));

        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(viewRect.ContractedBy(10));

        Text.Font = GameFont.Medium;
        listing.Label("VS_RentBeds".Translate());

        Text.Font = GameFont.Small;

        float iconSize = 52f;
        float bgSize = iconSize;

        float topRightX = inRect.xMax - iconSize - 42;
        float topRightY = inRect.y;

        Rect iconRect = new Rect(topRightX, topRightY, iconSize, iconSize);
        Rect bgRect = new Rect(iconRect.center.x - (bgSize / 2f), iconRect.center.y - (bgSize / 2f), bgSize, bgSize);
        GUI.DrawTexture(bgRect, ColonistBar.BGTex);
        Widgets.ThingIcon(iconRect, ThingDefOf.Bed);

        listing.Gap(10);

        listing.Label($"VS_SelectNumberDays".Translate(selectedDays));

        selectedDays = Mathf.RoundToInt(listing.Slider(selectedDays, 1, VS_Mod.settings.maxDaySelection));

        CalculateTotalCost();

        listing.Label($"VS_TotalCost".Translate(totalCost));

        foreach (var room in bedOwnershipTracker.bedExpirations.GroupBy(pair => pair.Key.GetRoom().ID))
        {
            var roomMap = room.First().Key.Map;
            if (roomMap != null && roomMap.Tile != -1 && roomMap.Tile == interactingPawn.Map.Tile)
            {

                listing.Gap(10);

                listing.Label($"VS_RoomID".Translate(room.Key));

                int totalBeds = room.Select(pair => pair.Key).Count();
                listing.Label($"VS_TotalBeds".Translate(totalBeds));

                int expirationTick = room.Max(bedPair => bedPair.Value);
                int daysLeft = Mathf.CeilToInt((expirationTick - Find.TickManager.TicksGame) / GenDate.TicksPerDay);
                listing.Label($"VS_BedExpiration".Translate(daysLeft));

                listing.Gap(5);

                Rect cancelButtonRect = new Rect(0f, listing.CurHeight, 200, 25);
                if (Widgets.ButtonText(cancelButtonRect, "VS_CancelRoom".Translate(), true, false, true))
                {
                    CancelRoomRent(room.Key);
                }

                listing.Gap(25);
            }
        }

        listing.End();
        Widgets.EndScrollView();

        Rect rentButtonRect = new Rect(inRect.width / 2 - 100, inRect.height - 40, 200, 35);
        if (Widgets.ButtonText(rentButtonRect, "VS_RentBeds".Translate(), true, false, true))
        {
            TryRentBeds();
        }
    }

    private void CalculateTotalCost()
    {
        totalCost = baseBedCostPerDay * selectedDays;
    }

    private void CancelRoomRent(int roomId)
    {
        var bedsInRoom = bedOwnershipTracker.bedExpirations.Where(pair => pair.Key.GetRoom().ID == roomId).ToList();

        int refundedAmount = 0;

        foreach (var bedPair in bedsInRoom)
        {
            if (bedPair.Key.Faction == Faction.OfPlayer)
            {
                var map = bedPair.Key.Map;

                int expirationTick = bedPair.Value;
                int daysLeft = Mathf.CeilToInt((expirationTick - Find.TickManager.TicksGame) / GenDate.TicksPerDay);
                int refundPercentage = Mathf.Clamp(daysLeft * VS_Mod.settings.refundPercentage, 0, 100); //% refund per day left before expiration
                int refundAmount = Mathf.RoundToInt(totalCost * refundPercentage / 100);

                refundedAmount += refundAmount;

                TryDeductSilver(targetPawn, refundAmount);
                TransferSilver(interactingPawn, refundAmount);

                bedPair.Key.SetFactionDirect(map.ParentFaction);
                if (bedPair.Key.OwnersForReading.Any())
                {
                    bedPair.Key.OwnersForReading.Clear();
                }
                bedOwnershipTracker.RemoveBed(bedPair.Key);
            }
        }

        Messages.Message($"VS_RoomRentCancelled".Translate(roomId, refundedAmount), MessageTypeDefOf.NegativeEvent);
    }

    private bool TryDeductSilver(Pawn pawn, int amount)
    {
        var silverItems = pawn.inventory.innerContainer.Where(item => item.def == ThingDefOf.Silver).ToList();
        int totalSilver = silverItems.Sum(item => item.stackCount);

        if (totalSilver >= amount)
        {
            int remainingAmount = amount;

            foreach (var silver in silverItems)
            {
                int deducted = Mathf.Min(silver.stackCount, remainingAmount);
                silver.stackCount -= deducted;
                remainingAmount -= deducted;

                if (remainingAmount <= 0)
                    break;
            }

            pawn.inventory.innerContainer.RemoveAll(item => item.stackCount == 0);
            return true;
        }

        return false;
    }

    private void TransferSilver(Pawn pawn, int amount)
    {
        var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
        silver.stackCount = amount;
        pawn.inventory.innerContainer.TryAdd(silver);
    }

    private bool AreThereValidRooms(Map map)
    {
        var filteredBeds = FilterBeds(map);

        var bedsByRoom = filteredBeds.GroupBy(bed => bed.GetRoom());

        return bedsByRoom.Any(group => group.All(bed => bed.Faction != Faction.OfPlayer && map.reachability.CanReach(map.Center, bed.Position, Verse.AI.PathEndMode.OnCell, TraverseMode.PassDoors)));
    }

    private IEnumerable<Building_Bed> FilterBeds(Map map)
	{
        var beds = map.listerBuildings.allBuildingsNonColonist
            .Where(t => t is Building_Bed)
            .Cast<Building_Bed>();

        var filteredBeds = beds
            .Where(b => (!HarmonyPatches.isIdeologyLoaded || !b.ForSlaves) && !b.ForPrisoners && map.reachability.CanReach(map.Center, b.Position, Verse.AI.PathEndMode.OnCell, TraverseMode.PassDoors));

        return filteredBeds;
    }

    private void TryRentBeds()
    {
        if (TryDeductSilver(interactingPawn, totalCost))
        {
            var map = interactingPawn.Map;

            if (!AreThereValidRooms(map))
            {
                Messages.Message("VS_NoValidRooms".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            TransferSilver(targetPawn, totalCost);

            MakeBeds(map);

            ScheduleOwnershipReturn(map, selectedDays);

            Messages.Message($"VS_UsedSilverRent".Translate(totalCost, selectedDays, rentedRoom.ID), MessageTypeDefOf.PositiveEvent);
            Close();
        }
        else
        {
            Messages.Message($"VS_NotEnoughSilverReq".Translate(totalCost), MessageTypeDefOf.RejectInput);
        }
    }

    public void MakeBeds(Map map)
    {
        var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

        var filteredBeds = FilterBeds(map);

        var bedsByRoom = filteredBeds.GroupBy(bed => bed.GetRoom());

        var roomWithMostBeds = bedsByRoom
            .Where(group => group.All(bed => bed.Faction != Faction.OfPlayer))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        rentedRoom = roomWithMostBeds.Key;

        if (roomWithMostBeds != null)
        {
            foreach (var bed in roomWithMostBeds)
            {
                bed.SetFactionDirect(Faction.OfPlayer);
                worldComponent.settlementStructures.Add(bed);
            }
        }
    }

    private void ScheduleOwnershipReturn(Map map, int days)
    {
        var bedOwnershipTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();

        int ticksUntilReversion = days * GenDate.TicksPerDay;
        int currentTick = Find.TickManager.TicksGame;

        var beds = map.listerBuildings.allBuildingsNonColonist
             .Where(t => t is Building_Bed)
             .Cast<Building_Bed>();

        foreach (var bed in beds)
        {
            if (bed.Faction == Faction.OfPlayer)
            {
                bedOwnershipTracker.AddBed(bed, currentTick + ticksUntilReversion);
            }
        }
    }
}

public class VS_BedOwnershipTracker : WorldComponent
{
    public Dictionary<Building_Bed, int> bedExpirations = new Dictionary<Building_Bed, int>();

    List<Building_Bed> bedExpirationsValues = new List<Building_Bed>();
    List<int> bedExpirationsKeys = new List<int>();

    public VS_BedOwnershipTracker(World world) : base(world) { }

    public void AddBed(Building_Bed bed, int expirationTicks)
    {
        if (bedExpirations.ContainsKey(bed))
        {
            return;
        }
        else
        {
            bedExpirations.Add(bed, expirationTicks);
        }
    }

    public void RemoveBed(Building_Bed bed)
    {
        bedExpirations.Remove(bed);
    }

    public void CheckAndRevertBedOwnership()
    {
        int currentTick = Find.TickManager.TicksGame;

        var expiredBeds = bedExpirations
            .Where(pair => pair.Value <= currentTick)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var bed in expiredBeds)
        {
            if (bed.Faction == Faction.OfPlayer)
            {
                var map = bed.Map;
                if (map != null)
                {
                    var parentFaction = map.ParentFaction;
                    if (parentFaction != null)
                    {
                        if (bed.OwnersForReading.Any())
                        {
                            bed.OwnersForReading.Clear();
                        }

                        bed.SetFactionDirect(parentFaction);

                        var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();
                        if (worldComponent.settlementStructures.Contains(bed))
                        {
                            worldComponent.settlementStructures.Remove(bed);
                        }
                    }
                }

                bedExpirations.Remove(bed);
                Messages.Message("VS_BedsExpired".Translate(), MessageTypeDefOf.NegativeEvent);
            }
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref bedExpirations, "bedExpirations", LookMode.Reference, LookMode.Value, ref bedExpirationsValues, ref bedExpirationsKeys);
    }
}

public class VS_BedOwnershipCheckComponent : MapComponent
{
    private int lastCheckTick = 0;
    private const int checkIntervalTicks = GenDate.TicksPerDay;

    public VS_BedOwnershipCheckComponent(Map map) : base(map) { }

    public override void MapComponentTick()
    {
        base.MapComponentTick();

        int currentTick = Find.TickManager.TicksGame;

        if (currentTick - lastCheckTick >= checkIntervalTicks)
        {
            var worldComponent = Find.World.GetComponent<WorldComponent_SettlementData>();

            if (worldComponent.settlementMaps.ContainsKey(map.Tile))
            {
                var bedOwnershipTracker = Find.World.GetComponent<VS_BedOwnershipTracker>();

                if (bedOwnershipTracker != null && bedOwnershipTracker.bedExpirations.Count > 0)
                {
                    bedOwnershipTracker.CheckAndRevertBedOwnership();
                }
            }

            lastCheckTick = currentTick;
        }
    }
}

//trading
public class VS_Dialog_TradeSupplies : Window
{
    private readonly Pawn interactingPawn;
    private readonly Pawn targetPawn;
    private readonly Dictionary<ThingDef, int> selectedQuantities;
    private Vector2 scrollPosition = Vector2.zero;

    public VS_Dialog_TradeSupplies(Pawn interactingPawn, Pawn targetPawn)
    {
        this.interactingPawn = interactingPawn;
        this.targetPawn = targetPawn;
        closeOnAccept = true;
        closeOnCancel = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        forcePause = true;

        selectedQuantities = new Dictionary<ThingDef, int>();

        foreach (var itemDef in VS_Mod.settings.buyableThingDefs)
        {
            selectedQuantities[itemDef] = 0;
        }
    }

    public override Vector2 InitialSize => new Vector2(500f, 350f);

    public override void DoWindowContents(Rect inRect)
    {
        Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, 215);
        Rect viewRect = new Rect(0, 0, outRect.width - 16, Mathf.Max(selectedQuantities.Count() * (Text.LineHeight * 4) + 20, outRect.height));

        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(viewRect.ContractedBy(10));

        Text.Font = GameFont.Medium;
        listing.Label("VS_TradeSupplies".Translate());

        Text.Font = GameFont.Small;

        float iconSize = 52f;
        float bgSize = iconSize;

        float topRightX = inRect.xMax - iconSize - 42;
        float topRightY = inRect.y;

        Rect iconSilverRect = new Rect(topRightX, topRightY, iconSize, iconSize);
        Rect bgRect = new Rect(iconSilverRect.center.x - (bgSize / 2f), iconSilverRect.center.y - (bgSize / 2f), bgSize, bgSize);
        GUI.DrawTexture(bgRect, ColonistBar.BGTex);
        Widgets.ThingIcon(iconSilverRect, ThingDefOf.Silver);

        listing.Gap(10);

        int totalCost = 0;

        var itemKeys = selectedQuantities.Keys.ToList();

        bool hasSelectedItems = false;

        foreach (var itemDef in itemKeys)
        {
            int baseCost = Mathf.CeilToInt(itemDef.BaseMarketValue);

            Texture2D icon = itemDef.uiIcon;
            Rect iconRect = listing.GetRect(24f);
            Widgets.DrawTextureFitted(iconRect, icon, 1f);

            listing.Gap(4f);

            listing.Label($"VS_QuantitiesSelectedCost".Translate(itemDef.label, selectedQuantities[itemDef], baseCost));

            int currentQuantity = selectedQuantities[itemDef];

            selectedQuantities[itemDef] = Mathf.RoundToInt(listing.Slider(currentQuantity, 0, VS_Mod.settings.maxItemQuantity));

            totalCost += selectedQuantities[itemDef] * baseCost;

            if (selectedQuantities[itemDef] > 0)
            {
                hasSelectedItems = true;
            }
        }

        listing.Gap(10);

        listing.End();
        Widgets.EndScrollView();

        Rect totalCostRect = new Rect(inRect.x, outRect.yMax + 10, inRect.width, 30);
        Widgets.Label(totalCostRect, $"VS_TotalCost".Translate(totalCost));

        Rect completeButtonRect = new Rect(inRect.width / 2 - 125, inRect.height - 40, 250, 35);
        if (Widgets.ButtonText(completeButtonRect, "VS_CompleteTransaction".Translate(), true, false, true))
        {
            if (!hasSelectedItems)
            {
                Messages.Message("VS_NoItemsSelected".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (TryDeductSilver(interactingPawn, totalCost))
            {
                TransferSilver(targetPawn, totalCost);

                foreach (var item in selectedQuantities)
                {
                    if (item.Value > 0)
                    {
                        var thing = ThingMaker.MakeThing(item.Key);
                        thing.stackCount = item.Value;
                        interactingPawn.inventory.innerContainer.TryAdd(thing);
                    }
                }

                Messages.Message($"VS_TransactionCompleted".Translate(totalCost), MessageTypeDefOf.PositiveEvent);
                Close();
            }
            else
            {
                Messages.Message($"VS_NotEnoughSilverReq".Translate(totalCost), MessageTypeDefOf.RejectInput);
            }
        }
    }

    private bool TryDeductSilver(Pawn pawn, int amount)
    {
        var silverItems = pawn.inventory.innerContainer.Where(item => item.def == ThingDefOf.Silver).ToList();
        int totalSilver = silverItems.Sum(item => item.stackCount);

        if (totalSilver >= amount)
        {
            int remainingAmount = amount;

            foreach (var silver in silverItems)
            {
                int deducted = Mathf.Min(silver.stackCount, remainingAmount);
                silver.stackCount -= deducted;
                remainingAmount -= deducted;

                if (remainingAmount <= 0)
                    break;
            }

            pawn.inventory.innerContainer.RemoveAll(item => item.stackCount == 0);
            return true;
        }

        return false;
    }

    private void TransferSilver(Pawn pawn, int amount)
    {
        var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
        silver.stackCount = amount;
        pawn.inventory.innerContainer.TryAdd(silver);
    }
}