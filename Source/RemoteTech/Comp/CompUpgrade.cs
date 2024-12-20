﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using HugsLib.Utils;
using RimWorld;
using UnityEngine;
using Verse;

namespace RemoteTech;

/// <summary>
///     A universal upgrade that modifies stats on the thing it is applied to in exchange for building materials.
///     Displays a toggle gizmo and requires a pawn to perform work to bring in materials and complete the upgrade.
///     Just add a CompProperties_Upgrade to your ThingDef to use, everything else is handled automatically.
/// </summary>
public class CompUpgrade : ThingComp, IThingHolder
{
    public static readonly string UpgradeCompleteSignal = "UpgradeComplete";

    // saved
    private bool complete;
    private ThingOwner ingredients;
    private bool wantsWork;
    private float workDone;

    public CompUpgrade()
    {
        ingredients = new ThingOwner<Thing>(this);
    }

    public CompProperties_Upgrade Props => props as CompProperties_Upgrade ?? new CompProperties_Upgrade();

    public bool Complete => complete;

    public float WorkProgress => Mathf.Clamp01(workDone / Mathf.Max(Props.workAmount, 1f));

    public bool WantsWork => wantsWork && !complete;

    private bool CompletedPrerequisites =>
        (Props.researchPrerequisite == null || Props.researchPrerequisite.IsFinished)
        && (Props.prerequisiteUpgradeId == null || parent.IsUpgradeCompleted(Props.prerequisiteUpgradeId));

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return ingredients;
    }

    public override string GetDescriptionPart()
    {
        return Props.GetDescriptionPart(parent.def);
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        this.RequireComponent(props as CompProperties_Upgrade);
        UpdateDesignation();
    }

    public override void PostDeSpawn(Map map)
    {
        base.PostDeSpawn(map);
        ingredients.TryDropAll(parent.Position, map, ThingPlaceMode.Near);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe.EnterNode($"CompUpgrade_{Props.referenceId}");
        Scribe_Values.Look(ref complete, "complete");
        if (!(Scribe.mode == LoadSaveMode.Saving && complete))
        {
            Scribe_Values.Look(ref workDone, "workDone");
            Scribe_Values.Look(ref wantsWork, "wantsWork");
            Scribe_Deep.Look(ref ingredients, "ingredients", this);
        }

        if (ingredients == null)
        {
            ingredients = new ThingOwner<Thing>(this);
        }

        Scribe.ExitNode();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (!complete && CompletedPrerequisites)
        {
            yield return new Command_Toggle
            {
                defaultLabel = Props.label,
                defaultDesc =
                    $"<b>{"Upgrade_labelPrefix".Translate(Props.label)}</b>\n{Props.EffectDescription}\n{Props.MaterialsDescription}",
                toggleAction = () =>
                {
                    if (Prefs.DevMode && HugsLibUtility.ShiftIsHeld)
                    {
                        CompleteUpgrade();
                        return;
                    }

                    if (!wantsWork && parent.ParentHolder is not Map)
                    {
                        Messages.Message("Upgrade_uneqip_message".Translate(), parent, MessageTypeDefOf.RejectInput);
                        return;
                    }

                    if (!wantsWork && parent.Map.mapPawns.FreeColonists.All(p => !PawnMeetsSkillRequirement(p)))
                    {
                        Messages.Message("Upgrade_needSkills_message".Translate(Props.constructionSkillPrerequisite),
                            MessageTypeDefOf.RejectInput);
                        return;
                    }

                    wantsWork = !wantsWork;
                    if (!wantsWork)
                    {
                        ingredients.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near);
                    }

                    UpdateDesignation();
                },
                isActive = () => wantsWork,
                icon = Resources.Textures.rxUIUpgrade
            };
        }
    }

    public override string CompInspectStringExtra()
    {
        var s = new StringBuilder();
        if (WantsWork)
        {
            s.AppendFormat("Upgrade_labelPrefix".Translate(), Props.label);
            s.AppendLine();
            s.AppendFormat("Upgrade_workProgress".Translate(), Mathf.Round(WorkProgress * 100f));
            if (Props.costList.Any())
            {
                s.Append("; ");
                s.AppendFormat("Upgrade_deliveredIngredients".Translate(), ingredients.ContentsString);
            }
        }

        CompUpgrade firstUpgrade = null;
        var anyComplete = false;
        foreach (var comp in parent.AllComps)
        {
            var upgrade = comp as CompUpgrade;
            if (firstUpgrade == null)
            {
                firstUpgrade = upgrade;
            }

            if (upgrade is { Complete: true })
            {
                anyComplete = true;
            }
        }

        if (firstUpgrade != this || !anyComplete)
        {
            return s.ToString();
        }

        if (s.Length > 0)
        {
            s.AppendLine();
        }

        s.Append("Upgrade_installedUpgrades".Translate());
        var numEntries = 0;
        foreach (var comp in parent.AllComps)
        {
            if (comp is not CompUpgrade { Complete: true } upgrade)
            {
                continue;
            }

            if (numEntries > 0)
            {
                s.Append(", ");
            }

            s.Append(upgrade.Props.label);
            numEntries++;
        }

        return s.ToString();
    }

    public StatModifier TryGetStatModifier(StatDef forStat)
    {
        foreach (var modifier in Props.statModifiers)
        {
            if (modifier.stat == forStat)
            {
                return modifier;
            }
        }

        return null;
    }

    public void DoWork(float workAmount)
    {
        workDone += workAmount;
        if (workDone >= Props.workAmount)
        {
            CompleteUpgrade();
        }
    }

    public ThingDefCount TryGetNextMissingIngredient()
    {
        if (!WantsWork)
        {
            return new ThingDefCount();
        }

        foreach (var required in Props.costList)
        {
            var missingCount = required.count;
            for (var j = 0; j < ingredients.Count; j++)
            {
                var filled = ingredients.GetAt(j);
                if (filled.def == required.thingDef)
                {
                    missingCount -= filled.stackCount;
                }
            }

            if (missingCount > 0)
            {
                return new ThingDefCount(required.thingDef, missingCount);
            }
        }

        return new ThingDefCount();
    }

    public bool PawnMeetsSkillRequirement(Pawn p)
    {
        return p.skills.GetSkill(SkillDefOf.Construction).Level >= Props.constructionSkillPrerequisite;
    }

    private void CompleteUpgrade()
    {
        if (complete)
        {
            return;
        }

        complete = true;
        ingredients.ClearAndDestroyContents();
        UpdateDesignation();
        if (Props.statModifiers.Any(m => m.stat == StatDefOf.MaxHitPoints))
        {
            parent.HitPoints = parent.MaxHitPoints;
        }

        parent.BroadcastCompSignal(UpgradeCompleteSignal);
    }

    private void UpdateDesignation()
    {
        if (parent.Map == null)
        {
            return;
        }

        var anyWantsWork = parent.AllComps.OfType<CompUpgrade>().Any(c => c.WantsWork);
        parent.ToggleDesignation(Resources.Designation.rxInstallUpgrade, anyWantsWork);
    }
}