using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RemoteTech;

/// <summary>
///     Replaces destroyed buildings that have a CompAutoReplaceable new blueprints that are forbidden for a set number of
///     seconds (see mod settings).
///     Buildings and their comps can implement IAutoReplaceExposable to carry over additional data to their rebuilt form.
/// </summary>
/// <see cref="CompAutoReplaceable" />
/// <see cref="IAutoReplaceExposable" />
public class AutoReplaceWatcher : IExposable
{
    private const int TicksBetweenSettingsPruning = GenTicks.TicksPerRealSecond;
    private Dictionary<string, string> currentVars;

    private Map map;

    private List<ReplacementEntry>
        pendingForbiddenBlueprints = []; // acts as a queue for lack of queue saving

    // saved
    private List<ReplacementEntry> pendingSettings = [];

    public LoadSaveMode ExposeMode { get; private set; } = LoadSaveMode.Inactive;

    public void ExposeData()
    {
        Scribe_Collections.Look(ref pendingSettings, "pendingSettings", LookMode.Deep);
        Scribe_Collections.Look(ref pendingForbiddenBlueprints, "pendingForbiddenBlueprints", LookMode.Deep);
        pendingSettings ??= [];

        pendingForbiddenBlueprints ??= [];
    }

    public void SetParentMap(Map parentMap)
    {
        map = parentMap;
    }

    public void ScheduleReplacement(CompAutoReplaceable replaceableComp)
    {
        var building = replaceableComp.parent;
        if (building?.def == null)
        {
            return;
        }

        if (building.Stuff == null && building.def.MadeFromStuff ||
            building.Stuff != null && !building.def.MadeFromStuff)
        {
            Log.Warning(
                "Could not schedule {building} auto-replacement due to Stuff discrepancy.");
            return;
        }

        var report = GenConstruct.CanPlaceBlueprintAt(building.def, replaceableComp.ParentPosition,
            replaceableComp.ParentRotation, map);
        if (!report.Accepted)
        {
            Log.Message(
                $"Could not auto-replace {building.LabelCap}: {report.Reason}");
            return;
        }

        var blueprint = GenConstruct.PlaceBlueprintForBuild(building.def, replaceableComp.ParentPosition, map,
            replaceableComp.ParentRotation, Faction.OfPlayer, building.Stuff);
        var entry = new ReplacementEntry
        {
            position = replaceableComp.ParentPosition,
            unforbidTick = Find.TickManager.TicksGame +
                           (RemoteTechController.Instance.BlueprintForbidDuration * GenTicks.TicksPerRealSecond),
            savedVars = new Dictionary<string, string>()
        };
        InvokeExposableCallbacks(building, entry.savedVars, LoadSaveMode.Saving);
        pendingSettings.Add(entry);
        if (RemoteTechController.Instance.BlueprintForbidDuration <= 0)
        {
            return;
        }

        blueprint.SetForbidden(true, false);
        pendingForbiddenBlueprints.Add(entry);
    }

    public void OnReplaceableThingSpawned(ThingWithComps building)
    {
        for (var i = 0; i < pendingSettings.Count; i++)
        {
            var entry = pendingSettings[i];
            if (building.Position != entry.position)
            {
                continue;
            }

            if (entry.savedVars != null)
            {
                InvokeExposableCallbacks(building, entry.savedVars, LoadSaveMode.LoadingVars);
                InvokeExposableCallbacks(building, null, LoadSaveMode.PostLoadInit);
            }

            var replaceComp = building.TryGetComp<CompAutoReplaceable>();
            replaceComp?.AutoReplaceEnabled = true;

            pendingSettings.RemoveAt(i);
            break;
        }
    }

    public void Tick()
    {
        UnforbidScheduledBlueprints();
        if (Find.TickManager.TicksGame % TicksBetweenSettingsPruning == 0)
        {
            PruneSettingsEntries();
        }
    }

    public void ExposeValue<T>(ref T value, string name, T fallbackValue = default) where T : IConvertible
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (ExposeMode == LoadSaveMode.Inactive)
        {
            throw new InvalidOperationException("Values can only be exposed during IAutoReplaceExposable callbacks"
            );
        }

        if (ExposeMode == LoadSaveMode.LoadingVars)
        {
            if (currentVars.TryGetValue(name, out var storedValue))
            {
                try
                {
                    value = (T)Convert.ChangeType(storedValue, typeof(T));
                    //RemoteTechController.Instance.Logger.Message($"Loaded {value} as {name}");
                    return;
                }
                catch (Exception e)
                {
                    Log.Error(
                        $"Failed to parse value \"{storedValue}\" as {typeof(T).Name}, using fallback value. Exception was: {e}"
                    );
                }
            }

            value = fallbackValue;
            //RemoteTechController.Instance.Logger.Message($"Loaded fallback {value} as {name}");
        }
        else if (ExposeMode == LoadSaveMode.Saving)
        {
            //RemoteTechController.Instance.Logger.Message($"Saving {value} as {name}");
            currentVars[name] = value?.ToString();
        }
    }

    private void InvokeExposableCallbacks(ThingWithComps target, Dictionary<string, string> vars, LoadSaveMode mode)
    {
        ExposeMode = mode;
        currentVars = vars;
        if (target is IAutoReplaceExposable t)
        {
            t.ExposeAutoReplaceValues(this);
        }

        foreach (var comp in target.AllComps)
        {
            if (comp is IAutoReplaceExposable c)
            {
                c.ExposeAutoReplaceValues(this);
            }
        }

        currentVars = null;
        ExposeMode = LoadSaveMode.Inactive;
    }

    private void UnforbidScheduledBlueprints()
    {
        var currentTick = Find.TickManager.TicksGame;
        var anyEntriesExpired = false;
        foreach (var entry in pendingForbiddenBlueprints)
        {
            if (entry.unforbidTick > currentTick)
            {
                continue;
            }

            var blueprint = map.thingGrid.ThingAt<Blueprint_Build>(entry.position);
            blueprint?.SetForbidden(false, false);

            anyEntriesExpired = true;
        }

        if (anyEntriesExpired)
        {
            pendingForbiddenBlueprints.RemoveAll(e => e.unforbidTick <= currentTick);
        }
    }

    // auto-placed blueprints may get canceled. Clean entries up periodically
    private void PruneSettingsEntries()
    {
        for (var i = pendingSettings.Count - 1; i >= 0; i--)
        {
            var entry = pendingSettings[i];
            bool containsBlueprint = false, containsBuildingFrame = false;
            if (map != null)
            {
                containsBlueprint = map.thingGrid.ThingAt<Blueprint_Build>(entry.position) != null;
                var edifice = map.edificeGrid[map.cellIndices.CellToIndex(entry.position)];
                containsBuildingFrame = edifice != null && edifice.def.IsFrame;
            }

            if (!containsBlueprint && !containsBuildingFrame)
            {
                pendingSettings.RemoveAt(i);
            }
        }
    }

    private class ReplacementEntry : IExposable
    {
        public IntVec3 position;
        public Dictionary<string, string> savedVars;
        public int unforbidTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref unforbidTick, "unforbidTick");
            Scribe_Collections.Look(ref savedVars, "vars", LookMode.Value, LookMode.Value);
        }
    }
}