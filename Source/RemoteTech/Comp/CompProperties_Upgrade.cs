using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using HugsLib.Utils;
using RimWorld;
using Verse;
// ReSharper disable CollectionNeverUpdated.Global

namespace RemoteTech;

public class CompProperties_Upgrade : CompProperties
{
    // minimum construction skill level on the builder who can install the upgrade
    public readonly int constructionSkillPrerequisite = 3;

    // an optional list of materials required for the upgrade
    public readonly List<ThingDefCountClass> costList = [];

    // multipliers to stats that will come into effect once the upgrade has been completed
    public readonly List<StatModifier> statModifiers = [];

    // number of ticks to complete the upgrade work. Reduced by construction skill
    public readonly int workAmount = 1000;

    private string _prerequisitesDescription;

    // a description of what this upgrade does. Used when no stats are modified
    public string effectDescription;

    // a readable identifier for the player
    public string label;

    // optional referenceId of another upgrade on the same thing that must be installed before this upgrade can be installed
    public string prerequisiteUpgradeId;

    // an internal identifier to be used during saving, referencing from code, and specifying dependencies for other upgrades
    public string referenceId;

    // optional research project that will allow this upgrade to be installed
    public ResearchProjectDef researchPrerequisite;

    public CompProperties_Upgrade()
    {
        compClass = typeof(CompUpgrade);
    }

    public string EffectDescription
    {
        get
        {
            if (field != null)
            {
                return field;
            }

            var s = new StringBuilder("Upgrade_descriptionEffects".Translate());
            if (effectDescription != null)
            {
                s.Append(effectDescription);
            }

            if (statModifiers.Count > 0)
            {
                if (effectDescription != null)
                {
                    s.AppendLine();
                }

                for (var i = 0; i < statModifiers.Count; i++)
                {
                    var effect = statModifiers[i];
                    s.Append(effect.stat.LabelCap);
                    s.Append(": ");
                    s.Append(effect.ToStringAsFactor);
                    if (i < statModifiers.Count - 1)
                    {
                        s.Append(", ");
                    }
                }
            }

            field = s.ToString();

            return field;
        }
    }

    public string MaterialsDescription
    {
        get
        {
            if (field != null)
            {
                return field;
            }

            var s = new StringBuilder("Upgrade_descriptionCost".Translate());
            for (var i = 0; i < costList.Count; i++)
            {
                var thingCount = costList[i];
                s.Append(thingCount.count.ToString());
                s.Append("x ");
                s.Append(thingCount.thingDef.label);
                if (i < costList.Count - 1)
                {
                    s.Append(", ");
                }
            }

            field = costList.Count > 0 ? s.ToString() : string.Empty;

            return field;
        }
    }

    private string GetPrerequisitesDescription(ThingDef parentDef)
    {
        if (_prerequisitesDescription != null)
        {
            return _prerequisitesDescription;
        }

        var reqList = new List<string>(3);
        if (researchPrerequisite != null)
        {
            reqList.Add("Upgrade_prerequisitesResearch".Translate(researchPrerequisite.label));
        }

        if (prerequisiteUpgradeId != null)
        {
            var prereqLabel = parentDef.comps.OfType<CompProperties_Upgrade>()
                .FirstOrDefault(u => u.referenceId == prerequisiteUpgradeId)?.label;
            reqList.Add("Upgrade_prerequisitesUpgrade".Translate(prereqLabel ?? prerequisiteUpgradeId));
        }

        if (constructionSkillPrerequisite > 0)
        {
            reqList.Add("Upgrade_prerequisitesSkill".Translate(constructionSkillPrerequisite));
        }

        _prerequisitesDescription = reqList.Count > 0
            ? "Upgrade_prerequisites".Translate() + reqList.Join(", ")
            : TaggedString.Empty;

        return _prerequisitesDescription;
    }

    public string GetDescriptionPart(ThingDef parentDef)
    {
        return
            $"<b>{"Upgrade_labelPrefix".Translate(label)}</b>{MakeSection(EffectDescription)}{MakeSection(MaterialsDescription)}{MakeSection(GetPrerequisitesDescription(parentDef))}";
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        if (label.NullOrEmpty())
        {
            yield return $"CompProperties_Upgrade needs a label in def {parentDef.defName}";
        }

        if (statModifiers.NullOrEmpty() && effectDescription.NullOrEmpty())
        {
            yield return
                $"CompProperties_Upgrade must have stat effects or effectDescription in def {parentDef.defName}";
        }

        if (referenceId.NullOrEmpty())
        {
            yield return
                $"CompProperties_Upgrade needs a referenceId in def {parentDef.defName}";
        }

        Exception ex = null;
        try
        {
            XmlConvert.VerifyName(referenceId);
        }
        catch (Exception e)
        {
            ex = e;
        }

        if (ex != null)
        {
            yield return $"CompProperties_Upgrade needs a valid referenceId in def {parentDef.defName}: {ex.Message}";
        }

        if (parentDef.comps.OfType<CompProperties_Upgrade>().Count(u => u.referenceId == referenceId) > 1)
        {
            yield return $"CompProperties_Upgrade requires a unique referenceId in def {parentDef.defName}";
        }
    }

    private static string MakeSection(string str)
    {
        return str.NullOrEmpty() ? str : $"\n{str}";
    }
}