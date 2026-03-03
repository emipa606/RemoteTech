using System;
using Verse;

namespace RemoteTech;

/// <summary>
///     Stores the AutoReplaceWatcher for an individual map
/// </summary>
public class MapComponent_RemoteTech : MapComponent
{
    private AutoReplaceWatcher replaceWatcher;

    public MapComponent_RemoteTech(Map map) : base(map)
    {
        replaceWatcher = new AutoReplaceWatcher();
        replaceWatcher.SetParentMap(map);
    }

    public AutoReplaceWatcher ReplaceWatcher => replaceWatcher;

    public override void ExposeData()
    {
        Scribe_Deep.Look(ref replaceWatcher, "replaceWatcher");
        replaceWatcher ??= new AutoReplaceWatcher();

        replaceWatcher.SetParentMap(map);
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        replaceWatcher.Tick();
    }

    public override void MapRemoved()
    {
        base.MapRemoved();
        try
        {
            PlayerAvoidanceGrids.DiscardMap(map);
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: Error in MapComponent_RemoteTech.MapRemoved: {e}");
        }
    }
}