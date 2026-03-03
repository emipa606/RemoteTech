using System.Collections.Generic;
using HugsLib.Utils;
using RimWorld;
using Verse;

namespace RemoteTech;

/// <summary>
///     A designator that selects only detonation wire
/// </summary>
public class Designator_SelectDetonatorWire : Designator
{
    public Designator_SelectDetonatorWire()
    {
        hotKey = KeyBindingDefOf.Misc10;
        icon = Resources.Textures.rxUISelectWire;
        useMouseIcon = true;
        defaultLabel = "WireDesignator_label".Translate();
        defaultDesc = "WireDesignator_desc".Translate();
        soundDragSustain = SoundDefOf.Designate_DragStandard;
        soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
        soundSucceeded = SoundDefOf.ThingSelected;
        hasDesignateAllFloatMenuOption = true;
    }

    public override string Label => "WireDesignator_label".Translate();

    public override string Desc => "WireDesignator_desc".Translate();

    public override DrawStyleCategoryDef DrawStyleCategory => DefDatabase<DrawStyleCategoryDef>.GetNamed("Conduits");

    public override bool DragDrawMeasurements => true;

    private void CellDesignate(IntVec3 cell)
    {
        var contents = Map.thingGrid.ThingsListAt(cell);
        var selector = Find.Selector;
        if (contents == null)
        {
            return;
        }

        foreach (var thing in contents)
        {
            if (!IsSelectable(thing) || selector.SelectedObjects.Contains(thing))
            {
                continue;
            }

            selector.SelectedObjects.Add(thing);
            SelectionDrawer.Notify_Selected(thing);
        }
    }

    private static bool IsSelectable(Thing t)
    {
        return t.def?.building is BuildingProperties_DetonatorWire;
    }

    private static void TryCloseArchitectMenu()
    {
        if (Find.Selector.NumSelected == 0)
        {
            return;
        }

        if (Find.MainTabsRoot.OpenTab != MainButtonDefOf.Architect)
        {
            return;
        }

        Find.MainTabsRoot.EscapeCurrentTab();
    }

    public override AcceptanceReport CanDesignateCell(IntVec3 loc)
    {
        var contents = Map.thingGrid.ThingsListAt(loc);
        if (contents == null)
        {
            return false;
        }

        foreach (var thing in contents)
        {
            if (IsSelectable(thing))
            {
                return true;
            }
        }

        return false;
    }

    public override AcceptanceReport CanDesignateThing(Thing t)
    {
        return IsSelectable(t);
    }

    public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
    {
        if (!HugsLibUtility.ShiftIsHeld)
        {
            Find.Selector.ClearSelection();
        }

        foreach (var cell in cells)
        {
            CellDesignate(cell);
        }

        TryCloseArchitectMenu();
    }

    public override void DesignateSingleCell(IntVec3 c)
    {
        if (!HugsLibUtility.ShiftIsHeld)
        {
            Find.Selector.ClearSelection();
        }

        CellDesignate(c);
        TryCloseArchitectMenu();
    }
}