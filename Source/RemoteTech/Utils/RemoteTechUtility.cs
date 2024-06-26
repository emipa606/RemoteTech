﻿using System;
using System.Collections.Generic;
using System.Linq;
using HugsLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RemoteTech;

/// <summary>
///     A place for common functions and utilities used by the mod.
/// </summary>
[StaticConstructorOnStartup]
public static class RemoteTechUtility
{
    public enum ChannelType
    {
        None,
        Basic,
        Advanced
    }

    public const int DefaultChannel = 1;

    // how long it will take to trigger an additional explosive
    private const int TicksBetweenTriggers = 2;

    public static ChannelType GetChannelsUnlockLevel()
    {
        if (Resources.Research.rxChannelsAdvanced.IsFinished)
        {
            return ChannelType.Advanced;
        }

        return Resources.Research.rxChannels.IsFinished ? ChannelType.Basic : ChannelType.None;
    }

    public static Gizmo GetChannelGizmo(int desiredChannel, int currentChannel, Action<int> activateCallback,
        ChannelType gizmoType, Dictionary<int, List<IWirelessDetonationReceiver>> channelPopulation = null)
    {
        var switching = desiredChannel != currentChannel;
        if (gizmoType == ChannelType.Basic)
        {
            return new Command_ChannelsBasic(desiredChannel, switching, activateCallback);
        }

        return gizmoType == ChannelType.Advanced
            ? new Command_ChannelsKeypad(desiredChannel, switching, activateCallback, channelPopulation)
            : null;
    }

    public static string GetCurrentChannelInspectString(int currentChannel)
    {
        return "RemoteExplosive_currentChannel".Translate(currentChannel);
    }

    public static void TriggerReceiversInNetworkRange(ThingWithComps origin, int channel, bool noTargetsMessage = true)
    {
        var comp = origin.GetComp<CompWirelessDetonationGridNode>();
        if (comp == null)
        {
            throw new Exception("Missing CompWirelessDetonationGridNode on sender");
        }

        var sample = comp.FindReceiversInNetworkRange().Where(pair => pair.Receiver.CurrentChannel == channel);
        // closer ones to their transmitters will go off first. This is used to simulate a bit of signal delay
        var sortedByDistance = sample
            .OrderBy(pair => pair.Transmitter.Position.DistanceToSquared(pair.Receiver.Position))
            .Select(pair => pair.Receiver);
        var counter = 0;
        foreach (var receiver in sortedByDistance)
        {
            HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(() =>
            {
                if (receiver.CanReceiveWirelessSignal)
                {
                    receiver.ReceiveWirelessSignal(origin);
                }
            }, TicksBetweenTriggers * counter++, GetHighestHolderInMap(origin));
        }

        if (counter == 0 && noTargetsMessage)
        {
            Messages.Message("Detonator_notargets".Translate(), origin, MessageTypeDefOf.RejectInput);
        }
    }

    public static Dictionary<int, List<IWirelessDetonationReceiver>> FindReceiversInNetworkRange(ThingWithComps origin)
    {
        var comp = origin.GetComp<CompWirelessDetonationGridNode>();
        if (comp == null)
        {
            throw new Exception("Missing CompWirelessDetonationGridNode on sender");
        }

        var results = new Dictionary<int, List<IWirelessDetonationReceiver>>();
        var sample = comp.FindReceiversInNetworkRange();
        foreach (var pair in sample)
        {
            if (!pair.Receiver.CanReceiveWirelessSignal)
            {
                continue;
            }

            results.TryGetValue(pair.Receiver.CurrentChannel, out var list);
            if (list == null)
            {
                list = new List<IWirelessDetonationReceiver>();
                results[pair.Receiver.CurrentChannel] = list;
            }

            list.Add(pair.Receiver);
        }

        return results;
    }

    public static FloatMenuOption TryMakeDetonatorFloatMenuOption(Pawn pawn, IPawnDetonateable detonator)
    {
        if (pawn == null || detonator is not Thing detonatorThing || !pawn.IsColonistPlayerControlled ||
            pawn.drafter == null)
        {
            return null;
        }

        var entry = new FloatMenuOption("Detonator_detonatenow".Translate(), () =>
        {
            if (!pawn.IsColonistPlayerControlled)
            {
                return;
            }

            if (!detonator.WantsDetonation)
            {
                detonator.WantsDetonation = true;
            }

            var job = JobMaker.MakeJob(Resources.Job.rxDetonateExplosives, detonatorThing);
            pawn.jobs.TryTakeOrderedJob(job);
        });
        if (!pawn.Map.reservationManager.IsReservedAndRespected(detonatorThing, pawn))
        {
            return entry;
        }

        entry.Disabled = true;
        var reservedByName = pawn.Map.reservationManager.FirstRespectedReserver(detonatorThing, pawn).Name
            .ToStringShort;
        entry.Label += " " + "Detonator_detonatenow_reserved".Translate(reservedByName);

        return entry;
    }

    // Determines if by being placed in the given cell the roof breaker has both a
    // thick roof within its radius, and a thin roof/no roof adjacent to it
    public static bool IsEffectiveRoofBreakerPlacement(float explosiveRadius, IntVec3 center, Map map, bool respectFog)
    {
        if (explosiveRadius <= 0)
        {
            return false;
        }

        var roofGrid = map.roofGrid;
        var fogGrid = map.fogGrid;
        var cardinals = GenAdj.CardinalDirections;
        var effectiveRadiusNumCells = GenRadial.NumCellsInRadius(explosiveRadius);
        var adjacentWeakRoofFound = false;
        var thickRoofInEffectiveRadius = false;
        for (var i = 0; i < effectiveRadiusNumCells; i++)
        {
            var radiusCell = center + GenRadial.RadialPattern[i];
            if (!radiusCell.InBounds(map))
            {
                continue;
            }

            var roof = roofGrid.RoofAt(radiusCell);
            if (roof is { isThickRoof: true })
            {
                thickRoofInEffectiveRadius = true;
            }

            if (adjacentWeakRoofFound)
            {
                continue;
            }

            foreach (var intVec3 in cardinals)
            {
                var cardinalCell = intVec3 + radiusCell;
                if (!cardinalCell.InBounds(map))
                {
                    continue;
                }

                var cardinalRoof = roofGrid.RoofAt(cardinalCell);
                if (respectFog && fogGrid.IsFogged(cardinalCell)
                    || cardinalRoof is { isThickRoof: true })
                {
                    continue;
                }

                adjacentWeakRoofFound = true;
                break;
            }
        }

        return thickRoofInEffectiveRadius && adjacentWeakRoofFound;
    }

    public static float TryGetExplosiveRadius(ThingDef def)
    {
        if (def?.comps == null)
        {
            return 0;
        }

        foreach (var compProperties in def.comps)
        {
            if (compProperties is CompProperties_Explosive props)
            {
                return props.explosiveRadius;
            }
        }

        return 0;
    }

    public static GasCloud TryFindGasCloudAt(Map map, IntVec3 pos, ThingDef matchingDef = null)
    {
        if (!pos.InBounds(map))
        {
            return null;
        }

        var thingList = map.thingGrid.ThingsListAtFast(map.cellIndices.CellToIndex(pos));
        foreach (var thing in thingList)
        {
            if (thing is GasCloud cloud && (matchingDef == null || cloud.def == matchingDef))
            {
                return cloud;
            }
        }

        return null;
    }

    public static void DeployGas(Map map, IntVec3 pos, ThingDef gasDef, int amount)
    {
        if (gasDef == null)
        {
            RemoteTechController.Instance.Logger.Error($"Tried to deploy null GasDef: {Environment.StackTrace}");
            return;
        }

        var cloud = TryFindGasCloudAt(map, pos, gasDef);
        if (cloud == null)
        {
            cloud = ThingMaker.MakeThing(gasDef) as GasCloud;
            if (cloud == null)
            {
                RemoteTechController.Instance.Logger.Error($"Deployed thing was not a GasCloud: {gasDef}");
                return;
            }

            GenPlace.TryPlaceThing(cloud, pos, map, ThingPlaceMode.Direct);
        }

        cloud.ReceiveConcentration(amount);
    }

    public static CompUpgrade FirstUpgradeableComp(this Thing t)
    {
        if (t is not ThingWithComps comps)
        {
            return null;
        }

        foreach (var thingComp in comps.AllComps)
        {
            if (thingComp is CompUpgrade { WantsWork: true } comp)
            {
                return comp;
            }
        }

        return null;
    }

    public static CompUpgrade TryGetUpgrade(this Thing t, string upgradeReferenceId)
    {
        if (t is not ThingWithComps comps)
        {
            return null;
        }

        foreach (var thingComp in comps.AllComps)
        {
            if (thingComp is CompUpgrade comp && comp.Props.referenceId == upgradeReferenceId)
            {
                return comp;
            }
        }

        return null;
    }

    public static bool IsUpgradeCompleted(this Thing t, string upgradeReferenceId)
    {
        var upgrade = t.TryGetUpgrade(upgradeReferenceId);
        return upgrade is { Complete: true };
    }

    public static void DrawFlareOverlay(Graphic overlay, Vector3 drawPos, GraphicData_Blinker props, float alpha = 1f,
        float scaleZ = 1f)
    {
        var color = new Color(props.blinkerColor.r, props.blinkerColor.g, props.blinkerColor.b,
            props.blinkerColor.a * alpha);
        var flareMat = overlay.MatSingle;
        var material =
            MaterialPool.MatFrom(new MaterialRequest((Texture2D)flareMat.mainTexture, flareMat.shader, color));
        var matrix = Matrix4x4.TRS(drawPos + Altitudes.AltIncVect + props.blinkerOffset, Quaternion.identity,
            new Vector3(props.blinkerScale.x, props.blinkerScale.y, props.blinkerScale.z * scaleZ));
        Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
    }

    public static Thing GetHighestHolderInMap(Thing heldThing)
    {
        // step upwards through containers until we can get a valid root thing for inventory items, etc.
        var rootHolder = heldThing.ParentHolder;
        var lastSeenThing = heldThing;
        while (rootHolder != null && rootHolder is not Map)
        {
            var rootHeld = rootHolder;
            if (rootHeld is Thing t)
            {
                lastSeenThing = t;
            }

            rootHolder = rootHeld.ParentHolder;
        }

        return lastSeenThing;
    }

    public static void ReportPowerUse(ThingWithComps thing, float duration = 1f)
    {
        foreach (var thingComp in thing.AllComps)
        {
            if (thingComp is IPowerUseNotified comp)
            {
                comp.ReportPowerUse(duration);
            }
        }
    }

    public static T RequireComp<T>(this ThingWithComps thing) where T : ThingComp
    {
        var c = thing.GetComp<T>();
        if (c == null)
        {
            RemoteTechController.Instance.Logger.Error(
                $"{thing.GetType().Name} requires ThingComp of type {nameof(T)} in def {thing.def.defName}");
        }

        return c;
    }

    public static T RequireComponent<T>(this ThingWithComps thing, T component)
    {
        if (component == null)
        {
            RemoteTechController.Instance.Logger.Error(
                $"{thing.GetType().Name} requires {nameof(T)} in def {thing.def.defName}");
        }

        return component;
    }

    public static void RequireComponent<T>(this ThingComp comp, T component)
    {
        if (component == null)
        {
            RemoteTechController.Instance.Logger.Error(
                $"{comp.GetType().Name} requires {nameof(T)} in def {comp.parent.def.defName}");
        }
    }

    public static void RequireTicker(this ThingComp comp, TickerType type)
    {
        if (comp.parent.def.tickerType != type)
        {
            RemoteTechController.Instance.Logger.Error(
                $"{comp.GetType().Name} requires tickerType:{type} in def {comp.parent.def.defName}");
        }
    }

    public static CachedValue<float> GetCachedStat(this Thing thing, StatDef stat,
        int recacheInterval = GenTicks.TicksPerRealSecond)
    {
        return new CachedValue<float>(() => thing.GetStatValue(stat), recacheInterval);
    }

    public static bool ApproximatelyEquals(this float value1, float value2, float tolerance = float.Epsilon)
    {
        return Math.Abs(value1 - value2) < tolerance;
    }
}