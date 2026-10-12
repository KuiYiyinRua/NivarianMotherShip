using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class NutrientTowerSupply
    {
        static bool Owned(Map map, Building_NutrientEnergyTower tower)
            => map != null && tower != null && tower.Spawned && !tower.Destroyed
                && tower.Map == map && tower.Faction == Faction.OfPlayer;
        internal static float ClampTarget(float amount)
            => float.IsNaN(amount) || float.IsInfinity(amount) ? Building_NutrientEnergyTower.Capacity
                : Mathf.Clamp(amount, 0f, Building_NutrientEnergyTower.Capacity);
        // Synchronize committed UI outcomes, keeping a drag preview local.
        public static void SetTarget(Map map, Building_NutrientEnergyTower tower, float amount)
        { if (Owned(map, tower)) tower.nutritionTarget = ClampTarget(amount); }
        public static void SetAutoFill(Map map, Building_NutrientEnergyTower tower, bool enabled)
        { if (Owned(map, tower)) tower.autoFillNutrition = enabled; }
    }

    public sealed class Gizmo_NutrientTowerNutrition : Gizmo_Slider
    {
        readonly Building_NutrientEnergyTower tower;
        bool dragging, commitDrag;
        internal bool IsDragging => dragging;
        public Gizmo_NutrientTowerNutrition(Building_NutrientEnergyTower tower) { this.tower = tower; }
        protected override float Width => 180f;
        protected override float Target
        {
            get => tower.nutritionTarget / Building_NutrientEnergyTower.Capacity;
            set
            {
                if (!commitDrag) return;
                commitDrag = false;
                NutrientTowerSupply.SetTarget(tower.Map, tower, value * Building_NutrientEnergyTower.Capacity);
            }
        }
        protected override bool DraggingBar
        {
            get => dragging;
            set { commitDrag = dragging && !value; dragging = value; }
        }
        protected override float ValuePercent => Mathf.Clamp01((float)tower.NutritionStored / Building_NutrientEnergyTower.Capacity);
        protected override string Title => NmcText.Get("NMC_Text0749");
        protected override string BarLabel => tower.NutritionStored.ToString("0.#") + " / " + Building_NutrientEnergyTower.Capacity.ToString("0");
        protected override bool IsDraggable => true;
        protected override int Increments => Mathf.RoundToInt(Building_NutrientEnergyTower.Capacity);
        protected override Color BarColor => new Color(.27f, .54f, .43f);
        protected override string GetTooltip() => NmcText.Get("NMC_Text0750", tower.nutritionTarget.ToString("0.#"));
        protected override void DrawHeader(Rect headerRect, ref bool mouseOverElement)
        {
            headerRect.xMax -= 24f;
            var toggle = new Rect(headerRect.xMax, headerRect.y, 24f, 24f);
            GUI.DrawTexture(toggle, ThingDefOf.MealSimple.uiIcon);
            GUI.DrawTexture(new Rect(toggle.center.x, toggle.y, 12f, 12f),
                tower.autoFillNutrition ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);
            if (Widgets.ButtonInvisible(toggle))
                NutrientTowerSupply.SetAutoFill(tower.Map, tower, !tower.autoFillNutrition);
            if (Mouse.IsOver(toggle))
            {
                Widgets.DrawHighlight(toggle);
                TooltipHandler.TipRegion(toggle, NmcText.Get("NMC_Text0751"));
                mouseOverElement = true;
            }
            base.DrawHeader(headerRect, ref mouseOverElement);
        }
    }
}
