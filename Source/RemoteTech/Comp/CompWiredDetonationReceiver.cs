using System;
using RimWorld;
using Verse;

namespace RemoteTech;

/// <summary>
///     Receives detonation signals from CompWiredDetonationTransmitter and light explosives attached to parent thing.
/// </summary>
public class CompWiredDetonationReceiver : CompDetonationGridNode
{
    private static TickDelayScheduler tickDelayScheduler;

    public void ReceiveSignal(int delayTicks)
    {
        if (parent is Building_RemoteExplosive { IsArmed: false })
        {
            return;
        }


        // get instance
        var gameComponent = Current.Game.GetComponent<GameComponent_TickDelayScheduler>();
        if (gameComponent != null)
        {
            tickDelayScheduler = gameComponent.scheduler;
        }

        // check if valid
        if (tickDelayScheduler == null || tickDelayScheduler.lastProcessedTick < 0)
        {
            //Log.Message($"Last processed tick: {tickDelayScheduler?.lastProcessedTick}");
            //Log.Warning("TickDelayScheduler is either null or not initialized.");
            throw new Exception("TickDelayScheduler is either null or not initialized");
        }

        var customExplosive = parent.GetComp<CompCustomExplosive>();
        var vanillaExplosive = parent.GetComp<CompExplosive>();
        if (customExplosive != null)
        {
            tickDelayScheduler.ScheduleCallback(() => customExplosive.StartWick(true),
                delayTicks, parent);
        }

        if (vanillaExplosive != null)
        {
            tickDelayScheduler.ScheduleCallback(() => vanillaExplosive.StartWick(),
                delayTicks, parent);
        }
    }

    public override void PrintForDetonationGrid(SectionLayer layer)
    {
        PrintEndpoint(layer);
    }
}