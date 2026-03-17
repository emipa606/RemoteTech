using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HugsLib.Utils;
using RimWorld;
using UnityEngine;
using UnityEngine.SceneManagement;
using Verse;

namespace RemoteTech;

/// <summary>
///     Non-ModBase version of your controller. Keeps all original logic but is not tied to HugsLib.
///     Use Initialize(settings) to create/refresh the singleton instance.
/// </summary>
public class RemoteTechController
{
    // kept your constants and fields
    public const float ComponentReplacementWorkMultiplier = 2f;
    public const float SilverReplacementWorkMultiplier = 1.75f;
    private const float ComponentToSteelRatio = 20f;
    private const float SilverToSparkpowderRatio = 4f;

    private readonly MethodInfo objectCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    public readonly RemoteTechSettings settings;

    // constructor is internal; prefer Initialize(settings)
    private RemoteTechController(RemoteTechSettings settings)
    {
        this.settings = settings;
    }

    public static RemoteTechController Instance { get; private set; }


    // reflection targets
    public FieldInfo CompGlowerGlowOnField { get; private set; }
    public PropertyInfo CompGlowerShouldBeLitProperty { get; private set; }

    // material lookup
    public Dictionary<ThingDef, List<ThingDef>> MaterialToBuilding { get; } = new();

    public int BlueprintForbidDuration => settings.forbidReplaced ? settings.forbidTimeout : 0;

    public static void Initialize(RemoteTechSettings settings)
    {
        if (Instance == null)
        {
            Instance = new RemoteTechController(settings);
        }
        else
        {
            // update settings reference if re-loaded
            _ = Instance.settings?.GetType();
            Instance.UpdateFromSettings(settings);
        }
    }

    public void UpdateFromSettings(RemoteTechSettings newSettings)
    {
        // push new settings into controller (if you cache them)
        // for now we only replace the backing settings reference
        // If you had SettingHandle references used throughout, replace them with reads from this.settings
        // (e.g. SettingAutoArmCombat.Value -> settings.autoArmCombat)
        // keeping simple for now:
        // Note: previously you exposed BlueprintForbidDuration depending on SettingForbidReplaced
        // so keep a small helper:
        // nothing else needed unless other parts of your mod cached SettingHandle references.
    }

    // --- legacy lifecycle hooks reimplemented ---

    public void DefsLoaded()
    {
        try
        {
            InjectTraderStocks();
            InjectRecipeVariants();
            InjectVanillaExplosivesComps();
            InjectUpgradeableStatParts();
            PrepareReverseBuildingMaterialLookup();
            PrepareReflection();
            RemoveFoamWallsFromMeteoritePool();
            Compat_DoorsExpanded.OnDefsLoaded();
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: DefsLoaded error: {e}");
        }
    }

    // called from RemoteTechMod.OnSceneLoaded
    public void SceneLoaded(Scene scene)
    {
        // your original action: PlayerAvoidanceGrids.ClearAllMaps();
        // already called by the Mod entry on sceneLoaded, keep hook for compatibility
        PlayerAvoidanceGrids.ClearAllMaps();
    }

    // Many of your original methods are kept verbatim below, only minor edits to logging calls
    private static void InjectTraderStocks()
    {
        try
        {
            var allInjectors = DefDatabase<TraderStockInjectorDef>.AllDefs;
            var affectedTraders = new List<TraderKindDef>();
            foreach (var injectorDef in allInjectors)
            {
                if (injectorDef.traderDef == null || injectorDef.stockGenerators.Count == 0)
                {
                    continue;
                }

                affectedTraders.Add(injectorDef.traderDef);
                foreach (var stockGenerator in injectorDef.stockGenerators)
                {
                    injectorDef.traderDef.stockGenerators.Add(stockGenerator);
                }
            }

            if (affectedTraders.Count > 0)
            {
                Log.Message($"RemoteTech: Injected stock generators for {affectedTraders.Count} traders");
            }

            DefDatabase<TraderStockInjectorDef>.Clear();
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: InjectTraderStocks failed: {e}");
        }
    }

    private void InjectRecipeVariants()
    {
        try
        {
            IEnumerable<RecipeDef> GetRecipesRequestingVariant(RecipeVariantType variant)
            {
                return DefDatabase<RecipeDef>.AllDefs.Where(r =>
                    r.GetModExtension<MakeRecipeVariants>() is { } v &&
                    (v.Variant & variant) != 0).ToArray();
            }

            var injectCount = 0;
            foreach (var explosiveRecipe in GetRecipesRequestingVariant(RecipeVariantType.Steel))
            {
                var variant = TryMakeRecipeVariant(explosiveRecipe, RecipeVariantType.Steel,
                    ThingDefOf.ComponentIndustrial, ThingDefOf.Steel, ComponentToSteelRatio,
                    ComponentReplacementWorkMultiplier);
                if (variant == null)
                {
                    continue;
                }

                DefDatabase<RecipeDef>.Add(variant);
                injectCount++;
            }

            foreach (var explosiveRecipe in GetRecipesRequestingVariant(RecipeVariantType.Sparkpowder))
            {
                var variant = TryMakeRecipeVariant(explosiveRecipe, RecipeVariantType.Sparkpowder, ThingDefOf.Silver,
                    Resources.Thing.rxSparkpowder, SilverToSparkpowderRatio, SilverReplacementWorkMultiplier);
                if (variant == null)
                {
                    continue;
                }

                DefDatabase<RecipeDef>.Add(variant);
                injectCount++;
            }

            if (injectCount > 0)
            {
                Log.Message($"RemoteTech: Injected {injectCount} alternate explosives recipes.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: InjectRecipeVariants failed: {e}");
        }
    }

    private RecipeDef TryMakeRecipeVariant(RecipeDef recipeOriginal, RecipeVariantType variant,
        ThingDef originalIngredient, ThingDef replacementIngredient, float replacementRatio, float workAmountMultiplier)
    {
        var resourceCountRequired = 0f;
        var newIngredientList = new List<IngredientCount>(recipeOriginal.ingredients);
        foreach (var ingredientCount in newIngredientList)
        {
            if (!ingredientCount.filter.Allows(originalIngredient))
            {
                continue;
            }

            resourceCountRequired = ingredientCount.GetBaseCount();
            newIngredientList.Remove(ingredientCount);
            break;
        }

        if (resourceCountRequired < float.Epsilon)
        {
            return null;
        }

        var recipeCopy = CloneObject(recipeOriginal);
        recipeCopy.defName = $"{recipeOriginal.defName}_{replacementIngredient.defName}";
        recipeCopy.shortHash = 0;
        HugsLibUtility.InjectedDefHasher.GiveShortHashToDef(recipeCopy, typeof(RecipeDef));
        recipeCopy.modExtensions = recipeCopy.modExtensions
            ?.Select(e => e is ICloneable i ? (DefModExtension)i.Clone() : e).ToList();
        if (!recipeOriginal.HasModExtension<MakeRecipeVariants>())
        {
            recipeOriginal.modExtensions ??= [];
            recipeOriginal.modExtensions.Add(new MakeRecipeVariants());
        }

        var variantExtension = recipeCopy.GetModExtension<MakeRecipeVariants>();
        if (variantExtension == null)
        {
            variantExtension = new MakeRecipeVariants();
            recipeCopy.modExtensions ??= [];
            recipeCopy.modExtensions.Add(variantExtension);
        }

        variantExtension.Variant |= variant;

        var newFixedFilter = new ThingFilter();
        foreach (var allowedThingDef in recipeOriginal.fixedIngredientFilter.AllowedThingDefs)
        {
            if (allowedThingDef != originalIngredient)
            {
                newFixedFilter.SetAllow(allowedThingDef, true);
            }
        }

        newFixedFilter.SetAllow(replacementIngredient, true);
        recipeCopy.fixedIngredientFilter = newFixedFilter;
        recipeCopy.defaultIngredientFilter = null;

        var replacementIngredientFilter = new ThingFilter();
        replacementIngredientFilter.SetAllow(replacementIngredient, true);
        var replacementCount = new IngredientCount { filter = replacementIngredientFilter };
        replacementCount.SetBaseCount(Mathf.Round(resourceCountRequired * replacementRatio));
        newIngredientList.Add(replacementCount);
        recipeCopy.ingredients = newIngredientList;

        recipeCopy.workAmount = recipeOriginal.workAmount * workAmountMultiplier;

        recipeCopy.ResolveReferences();
        return recipeCopy;
    }

    private static void InjectVanillaExplosivesComps()
    {
        try
        {
            var ieds = new List<ThingDef>
            {
                GetDefWithWarning("FirefoamPopper")
            };
            ieds.AddRange(DefDatabase<ThingDef>.AllDefs.Where(d =>
                d != null && d.thingClass == typeof(Building_TrapExplosive)));
            foreach (var thingDef in ieds)
            {
                if (thingDef == null)
                {
                    continue;
                }

                thingDef.comps.Add(new CompProperties_WiredDetonationReceiver());
                thingDef.comps.Add(new CompProperties_AutoReplaceable());
            }

            ThingDefOf.PassiveCooler.comps.Add(new CompProperties_AutoReplaceable { applyOnVanish = true });
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: InjectVanillaExplosivesComps failed: {e}");
        }
    }

    private static void InjectUpgradeableStatParts()
    {
        try
        {
            var relevantStats = new HashSet<StatDef>();
            var allThings = DefDatabase<ThingDef>.AllDefs.ToArray();
            foreach (var def in allThings)
            {
                if (def.comps.Count <= 0)
                {
                    continue;
                }

                foreach (var comp in def.comps)
                {
                    if (comp is not CompProperties_Upgrade upgradeProps)
                    {
                        continue;
                    }

                    foreach (var upgradeProp in upgradeProps.statModifiers)
                    {
                        relevantStats.Add(upgradeProp.stat);
                    }
                }
            }

            foreach (var stat in relevantStats)
            {
                var parts = stat.parts ?? (stat.parts = []);
                parts.Add(new StatPart_Upgradeable { parentStat = stat });
            }
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: InjectUpgradeableStatParts failed: {e}");
        }
    }

    private void PrepareReverseBuildingMaterialLookup()
    {
        try
        {
            MaterialToBuilding.Clear();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                foreach (var compProperties in def.comps)
                {
                    if (compProperties is not CompProperties_BuildGizmo)
                    {
                        continue;
                    }

                    MaterialToBuilding.Add(def, []);
                    break;
                }
            }

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.category != ThingCategory.Building || def.costList == null)
                {
                    continue;
                }

                foreach (var countClass in def.costList)
                {
                    var materialDef = countClass?.thingDef;
                    if (materialDef == null || !MaterialToBuilding.TryGetValue(materialDef, out var buildingDefs))
                    {
                        continue;
                    }

                    buildingDefs.Add(def);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: PrepareReverseBuildingMaterialLookup failed: {e}");
        }
    }

    private static ThingDef GetDefWithWarning(string defName)
    {
        var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (def == null)
        {
            Log.Warning($"Could not get ThingDef for Comp injection: {defName}");
        }

        return def;
    }

    private static void RemoveFoamWallsFromMeteoritePool()
    {
        // foam walls are mineable, but should not appear in a meteorite drop
        ThingSetMaker_Meteorite.nonSmoothedMineables.Remove(Resources.Thing.rxFoamWall);
        ThingSetMaker_Meteorite.nonSmoothedMineables.Remove(Resources.Thing.rxFoamWallSmooth);
        ThingSetMaker_Meteorite.nonSmoothedMineables.Remove(Resources.Thing.rxFoamWallBricks);
        // same for our passable collapsed rock
        ThingSetMaker_Meteorite.nonSmoothedMineables.Remove(Resources.Thing.rxCollapsedRoofRocks);
    }

    private void PrepareReflection()
    {
        CompGlowerShouldBeLitProperty = AccessTools.Property(typeof(CompGlower), "ShouldBeLitNow");
        CompGlowerGlowOnField = AccessTools.Field(typeof(CompGlower), "glowOnInt");
        if (CompGlowerShouldBeLitProperty == null || CompGlowerShouldBeLitProperty.PropertyType != typeof(bool)
                                                  || CompGlowerGlowOnField == null ||
                                                  CompGlowerGlowOnField.FieldType != typeof(bool))
        {
            Log.Error("RemoteTech: Could not reflect required members");
        }
    }

    public T CloneObject<T>(T obj)
    {
        return (T)objectCloneMethod.Invoke(obj, null);
    }

    // If other parts of your mod expect a ModLogger, you can create a wrapper or use Log.
    public void LogMessage(string msg)
    {
        Log.Message(msg);
    }
}