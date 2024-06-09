using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

public class VS_ModSettings : ModSettings
{
    public bool enablePenalties = true;
    public bool enableStealingPenalty = true;
    public bool enableDestructionPenalty = true;
    public bool enableEncroachingPenalty = true;
    public bool enableMinifyingPenalty = true;
    public bool enableCaravanPenalty = true;
    public bool enableMiningPenalty = true;

    public int basePenalty = 5;
    public float scalingFactor = 0.1f;

    public bool enableInteractionMenu = true;
    public int baseBedCostPerDay = 30;
    public int maxDaySelection = 30;
    public int refundPercentage = 10;
    public int maxItemQuantity = 60;

    public List<ThingDef> buyableThingDefs = new List<ThingDef>();
    public List<string> buyableThingDefNames = new List<string>();
    public List<string> defaultBuyableThingDefNames = new List<string> { "MedicineHerbal", "MedicineIndustrial", "MealSimple", "MealSurvivalPack", "Beer" };

    public bool enableSettlementEvents = true;
    public int regularDaysCount = 1;
    public float raidChance = 0.7f;
    public int raidDayMin = 2;
    public int raidDayMax = 5;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref enablePenalties, "enablePenalties", true);
        Scribe_Values.Look(ref enableStealingPenalty, "enableStealingPenalty", true);
        Scribe_Values.Look(ref enableDestructionPenalty, "enableDestructionPenalty", true);
        Scribe_Values.Look(ref enableEncroachingPenalty, "enableEncroachingPenalty", true);
        Scribe_Values.Look(ref enableMinifyingPenalty, "enableMinifyingPenalty", true);
        Scribe_Values.Look(ref enableCaravanPenalty, "enableCaravanPenalty", true);
        Scribe_Values.Look(ref enableMiningPenalty, "enableMiningPenalty", true);

        Scribe_Values.Look(ref basePenalty, "basePenalty", 5);
        Scribe_Values.Look(ref scalingFactor, "scalingFactor", 0.1f);

        Scribe_Values.Look(ref enableInteractionMenu, "enableInteractionMenu", true);
        Scribe_Values.Look(ref baseBedCostPerDay, "baseBedCostPerDay", 30);
        Scribe_Values.Look(ref maxDaySelection, "maxDaySelection", 30);
        Scribe_Values.Look(ref refundPercentage, "refundPercentage", 10);
        Scribe_Values.Look(ref maxItemQuantity, "maxItemQuantity", 60);

        Scribe_Collections.Look(ref buyableThingDefNames, "buyableThingDefNames", LookMode.Value);

        Scribe_Values.Look(ref enableSettlementEvents, "enableSettlementEvents", true);
        Scribe_Values.Look(ref regularDaysCount, "regularDaysCount", 1);
        Scribe_Values.Look(ref raidDayMin, "raidDayMin", 2);
        Scribe_Values.Look(ref raidDayMax, "raidDayMax", 5);

        base.ExposeData();
    }
}

[StaticConstructorOnStartup]
public static class Init
{
    static Init()
    {
        if (VS_Mod.settings.buyableThingDefNames == null || VS_Mod.settings.buyableThingDefNames.Count == 0)
        {
            VS_Mod.settings.buyableThingDefNames = VS_Mod.settings.defaultBuyableThingDefNames.ToList();
        }

        if (VS_Mod.settings.buyableThingDefNames != null || VS_Mod.settings.buyableThingDefNames.Count > 0 && VS_Mod.settings.buyableThingDefs != null || VS_Mod.settings.buyableThingDefs.Count > 0)
        {
            foreach (string defName in VS_Mod.settings.buyableThingDefNames)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) is ThingDef def)
                {
                    VS_Mod.settings.buyableThingDefs.Add(def);
                }
                else
                {
                    Log.Warning($"[Visit Settlements] Could not find ThingDef with name: {defName}");
                }
            }
        }
    }
}

public class VS_Mod : Mod
{
    public static VS_ModSettings settings;
    private Vector2 scrollPosition = Vector2.zero;

    private enum Tab
    {
        Penalty_Settings,
        Interaction_Menu,
        Settlement_Events,
        Credits,
    }

    private Tab currentTab = Tab.Penalty_Settings;

    public VS_Mod(ModContentPack content) : base(content)
    {
        settings = GetSettings<VS_ModSettings>();
    }

    public override string SettingsCategory() => "Visit Settlements";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listingStandard = new Listing_Standard();
        listingStandard.Begin(inRect);

        Text.Font = GameFont.Small;

        DrawTabs(listingStandard);

        listingStandard.Gap(50f);

        Rect settingsAreaRect = new Rect(inRect.x, listingStandard.CurHeight, inRect.width, inRect.height - listingStandard.CurHeight);

        DrawSettingsBackground(settingsAreaRect);

        settingsAreaRect = settingsAreaRect.ContractedBy(20f);

        switch (currentTab)
        {
            case Tab.Penalty_Settings:
                DrawPenalty_Settings(listingStandard, settingsAreaRect);
                break;
            case Tab.Interaction_Menu:
                DrawInteractionMenuSettings(listingStandard, settingsAreaRect);
                break;
            case Tab.Settlement_Events:
                DrawSettlementEventsSettings(listingStandard, settingsAreaRect);
                break;
            case Tab.Credits:
                DrawCredits(listingStandard, settingsAreaRect);
                break;

        }

        listingStandard.Gap(20f);

        listingStandard.End();

        DrawResetButton(inRect);
    }

    private void DrawSettingsBackground(Rect rect)
    {
        GUI.color = new Color(0.07f, 0.07f, 0.07f);
        Widgets.DrawBoxSolid(rect, GUI.color);
        GUI.color = Color.white;

        Widgets.DrawBox(rect, 2);
    }

    private void DrawTabs(Listing_Standard listingStandard)
    {
        float tabWidth = listingStandard.ColumnWidth / Enum.GetValues(typeof(Tab)).Length;
        Rect tabsRect = new Rect(listingStandard.GetRect(5f).x, listingStandard.CurHeight, tabWidth, 30f);

        foreach (Tab tab in Enum.GetValues(typeof(Tab)))
        {
            Rect tabRect = new Rect(tabsRect.x, tabsRect.y, Mathf.Min(tabWidth, listingStandard.ColumnWidth / Enum.GetValues(typeof(Tab)).Length), tabsRect.height);

            bool mouseOver = Mouse.IsOver(tabRect);
            bool isSelected = (tab == currentTab);

            GUI.color = isSelected ? new Color(0.1f, 0.1f, 0.1f) : (mouseOver ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.05f, 0.05f, 0.05f));
            Widgets.DrawBoxSolid(tabRect, GUI.color);
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(tabRect))
            {
                currentTab = tab;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            if (isSelected)
            {
                Widgets.DrawHighlightSelected(tabRect);
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(tabRect, tab.ToString().Replace("_", " "));
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.DrawBox(tabRect, 2);

            tabsRect.x += Mathf.Min(tabWidth, listingStandard.ColumnWidth / Enum.GetValues(typeof(Tab)).Length);
        }
    }

    private void DrawPenalty_Settings(Listing_Standard listingStandard, Rect settingsAreaRect)
    {
        listingStandard.Begin(settingsAreaRect);

        Text.Font = GameFont.Medium;

        Text.Anchor = TextAnchor.MiddleCenter;
        listingStandard.Label("VS_PenaltySettings".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        listingStandard.Gap(15f);

        listingStandard.CheckboxLabeled("VS_EnablePenalties".Translate(), ref settings.enablePenalties);

        Text.Font = GameFont.Small;

        if (settings.enablePenalties)
        {
            listingStandard.Gap(25f);

            Rect sliderRect1 = listingStandard.GetRect(22f);
            settings.basePenalty = Mathf.Max(0, Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect1, settings.basePenalty, 0, 40, true, "VS_BasePenalty".Translate(settings.basePenalty.ToString()), "0", "40")));
            TooltipHandler.TipRegion(sliderRect1, "VS_BasePenaltyTooltip".Translate(settings.basePenalty));

            listingStandard.Gap(25f);

            Rect sliderRect2 = listingStandard.GetRect(22f);
            settings.scalingFactor = Mathf.Clamp(Widgets.HorizontalSlider(sliderRect2, settings.scalingFactor, 0, 5, true, "VS_ScalingFactor".Translate(settings.scalingFactor.ToString("0.00")), "0", "5"), 0.01f, 5f);
            TooltipHandler.TipRegion(sliderRect2, "VS_ScalingFactorTooltip".Translate(settings.scalingFactor.ToString("0.00")));

            listingStandard.Gap(20f);

            listingStandard.CheckboxLabeled("VS_EnableStealingPenalty".Translate(), ref settings.enableStealingPenalty, "VS_EnableStealingPenaltyTooltip".Translate());
            listingStandard.CheckboxLabeled("VS_EnableDestructionPenalty".Translate(), ref settings.enableDestructionPenalty, "VS_EnableDestructionPenaltyTooltip".Translate());
            listingStandard.CheckboxLabeled("VS_EnableEncroachingPenalty".Translate(), ref settings.enableEncroachingPenalty, "VS_EnableEncroachingPenaltyTooltip".Translate());
            listingStandard.CheckboxLabeled("VS_EnableMinifyingPenalty".Translate(), ref settings.enableMinifyingPenalty, "VS_EnableMinifyingPenaltyTooltip".Translate());
            listingStandard.CheckboxLabeled("VS_EnableCaravanPenalty".Translate(), ref settings.enableCaravanPenalty, "VS_EnableCaravanPenaltyTooltip".Translate());
            listingStandard.CheckboxLabeled("VS_EnableMiningPenalty".Translate(), ref settings.enableMiningPenalty, "VS_EnableMiningPenaltyTooltip".Translate());
        }

        listingStandard.End();
    }

    private void DrawInteractionMenuSettings(Listing_Standard listingStandard, Rect settingsAreaRect)
    {
        listingStandard.Begin(settingsAreaRect);

        Text.Font = GameFont.Medium;

        Text.Anchor = TextAnchor.MiddleCenter;
        listingStandard.Label("VS_InteractionMenu".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        listingStandard.Gap(15f);

        listingStandard.CheckboxLabeled("VS_EnableInteractionMenu".Translate(), ref settings.enableInteractionMenu);

        Text.Font = GameFont.Small;

        if (settings.enableInteractionMenu)
        {
            listingStandard.Gap(25f);

            Rect sliderRect1 = listingStandard.GetRect(22f);
            settings.baseBedCostPerDay = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect1, settings.baseBedCostPerDay, 0, 100, true, "VS_BaseBedCostPerDay".Translate(settings.baseBedCostPerDay.ToString()), "0", "100")), 0, 100);
            TooltipHandler.TipRegion(sliderRect1, "VS_BaseBedCostPerDayTooltip".Translate(settings.baseBedCostPerDay));

            listingStandard.Gap(25f);

            Rect sliderRect2 = listingStandard.GetRect(22f);
            settings.maxDaySelection = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect2, settings.maxDaySelection, 1, 70, true, "VS_MaxDaySelection".Translate(settings.maxDaySelection.ToString()), "1", "70")), 1, 70);
            TooltipHandler.TipRegion(sliderRect2, "VS_MaxDaySelectionTooltip".Translate(settings.maxDaySelection));

            listingStandard.Gap(25f);

            Rect sliderRect3 = listingStandard.GetRect(22f);
            settings.refundPercentage = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect3, settings.refundPercentage, 0, 100, true, "VS_RefundPercentage".Translate(settings.refundPercentage.ToString()), "0", "100")), 0, 100);
            TooltipHandler.TipRegion(sliderRect3, "VS_RefundPercentageTooltip".Translate(settings.refundPercentage.ToString()));

            listingStandard.Gap(25f);

            Rect sliderRect4 = listingStandard.GetRect(22f);
            settings.maxItemQuantity = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect4, settings.maxItemQuantity, 0, 200, true, "VS_MaxItemQuantity".Translate(settings.maxItemQuantity.ToString()), "0", "200")), 0, 200);
            TooltipHandler.TipRegion(sliderRect4, "VS_MaxItemQuantityTooltip".Translate(settings.maxItemQuantity.ToString()));

            listingStandard.Gap(25f);

            IEnumerable<ThingDef> availableThingDefs = AvailableThingDefs();

            Rect addButtonRect = new Rect(listingStandard.GetRect(5f).x, listingStandard.CurHeight, settingsAreaRect.width, 30f);
            DrawStyledButton(addButtonRect, "VS_SelectTradeableItems".Translate(), () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (ThingDef thingDef in availableThingDefs)
                {
                    if (settings.buyableThingDefs == null || !settings.buyableThingDefs.Contains(thingDef))
                    {
                        options.Add(new FloatMenuOption(thingDef.LabelCap, () =>
                        {
                            settings.buyableThingDefNames.Add(thingDef.ToString());
                            settings.buyableThingDefs.Add(thingDef);
                        }));
                    }
                }
                Find.WindowStack.Add(new FloatMenu(options));
            });

            listingStandard.Gap(50f);

            if (settings.buyableThingDefs != null && settings.buyableThingDefs.Count > 0)
            {
                Rect scrollRect = listingStandard.GetRect(settingsAreaRect.height - listingStandard.CurHeight - 70);
                Rect viewRect = new Rect(0, 0, scrollRect.width - 16f, Mathf.Max(settings.buyableThingDefs.Count * Text.LineHeight, scrollRect.height));
                Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);

                float y = 0f;
                for (int i = 0; i < settings.buyableThingDefs.Count; i++)
                {
                    ThingDef thingDef = settings.buyableThingDefs[i];
                    Rect rowRect = new Rect(0, y, viewRect.width - 50, Text.LineHeight);

                    Rect iconRect = new Rect(rowRect.x, rowRect.y, Text.LineHeight, Text.LineHeight);
                    Widgets.ThingIcon(iconRect, thingDef);
                    Rect labelRect = new Rect(iconRect.xMax + 5, rowRect.y, rowRect.width - iconRect.width - 5, rowRect.height);
                    Widgets.Label(labelRect, thingDef.LabelCap);

                    Rect buttonRect = new Rect(rowRect.xMax, rowRect.y, 25, rowRect.height - 5);
                    DrawStyledButton(buttonRect, "X", () =>
                    {
                        settings.buyableThingDefNames.RemoveAt(i);
                        settings.buyableThingDefs.RemoveAt(i);
                    });

                    y += Text.LineHeight;
                }

                Widgets.EndScrollView();
            }
        }

        listingStandard.End();
    }

    private IEnumerable<ThingDef> AvailableThingDefs()
    {
        return DefDatabase<ThingDef>.AllDefs
            .Where(def => def.tradeability == Tradeability.Buyable || def.IsDrug || def.IsRangedWeapon || def.IsMeleeWeapon || def.IsStuff && def.category == ThingCategory.Item)
            .OrderBy(def => !def.IsIngestible || def.IsDrug);
    }

    private void DrawSettlementEventsSettings(Listing_Standard listingStandard, Rect settingsAreaRect)
    {
        listingStandard.Begin(settingsAreaRect);

        Text.Font = GameFont.Medium;

        Text.Anchor = TextAnchor.MiddleCenter;
        listingStandard.Label("VS_SettlementEvents".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        listingStandard.Gap(15f);

        listingStandard.CheckboxLabeled("VS_EnableSettlementEvents".Translate(), ref settings.enableSettlementEvents);

        Text.Font = GameFont.Small;

        if (settings.enableSettlementEvents)
        {

            listingStandard.Gap(25f);

            Rect sliderRect1 = listingStandard.GetRect(22f);
            settings.regularDaysCount = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect1, settings.regularDaysCount, 0, 20, true, "VS_RegularDaysCount".Translate(settings.regularDaysCount.ToString()), "0", "20")), 0, 20);
            TooltipHandler.TipRegion(sliderRect1, "VS_RegularDaysCountTooltip".Translate());

            listingStandard.Gap(25f);

            Rect sliderRect2 = listingStandard.GetRect(22f);
            settings.raidDayMin = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect2, settings.raidDayMin, 1, settings.raidDayMax, true, $"VS_RaidDayMin".Translate(settings.raidDayMin), "1", settings.raidDayMax.ToString()));
            TooltipHandler.TipRegion(sliderRect2, $"VS_RaidDayMinTooltip".Translate());

            listingStandard.Gap(25f);

            Rect sliderRect3 = listingStandard.GetRect(22f);
            settings.raidDayMax = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect3, settings.raidDayMax, 1, 20, true, "VS_RaidDayMax".Translate(settings.raidDayMax), "1", "20"));
            TooltipHandler.TipRegion(sliderRect3, $"VS_RaidDayMaxTooltip".Translate());

            settings.raidDayMax = Mathf.Max(settings.raidDayMin, settings.raidDayMax);
        }

        listingStandard.End();
    }

    private void DrawCredits(Listing_Standard listingStandard, Rect settingsAreaRect)
    {
        listingStandard.Begin(settingsAreaRect);

        listingStandard.Gap(40);

        Text.Font = GameFont.Medium;

        Text.Anchor = TextAnchor.MiddleCenter;
        listingStandard.Label("VS_CreditsTranslation".Translate());

        listingStandard.Gap(20);

        listingStandard.Label("VS_CreditsTranslation2".Translate());

        Text.Font = GameFont.Small;

        listingStandard.End();
    }

    private void DrawStyledButton(Rect rect, string label, Action onClick, bool isSelected = false)
    {
        bool mouseOver = Mouse.IsOver(rect);

        Color backgroundColor = isSelected ? new Color(0.1f, 0.1f, 0.1f) : (mouseOver ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.05f, 0.05f, 0.05f));
        GUI.color = backgroundColor;
        Widgets.DrawBoxSolid(rect, GUI.color);
        GUI.color = Color.white;

        Widgets.DrawBox(rect, 2);

        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, label);
        Text.Anchor = TextAnchor.UpperLeft;

        if (Widgets.ButtonInvisible(rect))
        {
            onClick?.Invoke();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }
    }

    private void DrawResetButton(Rect inRect)
    {
        float buttonWidth = 192f;
        float buttonHeight = 35f;

        float buttonX = inRect.x + (inRect.width - buttonWidth) / 2f;
        float buttonY = inRect.height - buttonHeight;

        Rect buttonRect = new Rect(buttonX, buttonY - 5, buttonWidth, buttonHeight);
        DrawStyledButton(buttonRect, "VS_ResetSettings".Translate(), () => { ResetToDefaults(); }, false);
    }

    public void ResetToDefaults()
    {
        settings.enablePenalties = true;
        settings.enableStealingPenalty = true;
        settings.enableDestructionPenalty = true;
        settings.enableEncroachingPenalty = true;
        settings.enableMinifyingPenalty = true;
        settings.enableCaravanPenalty = true;
        settings.enableMiningPenalty = true;

        settings.basePenalty = 5;
        settings.scalingFactor = 0.1f;

        settings.enableInteractionMenu = true;
        settings.baseBedCostPerDay = 30;
        settings.maxDaySelection = 30;
        settings.refundPercentage = 10;
        settings.maxItemQuantity = 60;

        settings.enableSettlementEvents = true;
        settings.regularDaysCount = 1;
        settings.raidDayMin = 2;
        settings.raidDayMax = 5;

        settings.buyableThingDefs.Clear();
        settings.buyableThingDefNames.Clear();
        foreach (string defName in settings.defaultBuyableThingDefNames)
        {
            if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) is ThingDef def)
            {
                settings.buyableThingDefNames.Add(defName);
                settings.buyableThingDefs.Add(def);
            }
            else
            {
                Log.Warning($"[Visit Settlements] Could not find default ThingDef with name: {defName}");
            }
        }
    }
}