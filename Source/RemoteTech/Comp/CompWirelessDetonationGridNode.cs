using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RemoteTech;

/// <summary>
///     Represents a node that can connect to other node-buildings in signal range and recursively form a network.
///     Requires the rxSignalRange stat to be set on the parent thing.
///     Also uses IWirelessDetonationReceiver when looking for receivers in range.
/// </summary>
public class CompWirelessDetonationGridNode : ThingComp
{
    private const int UpdateAdjacentNodesEveryTicks = 30;

    // allows to do a global reset of all adjacency caches
    private static int globalRecacheId;

    // saved
    private bool _enabled = true;
    private List<CompWirelessDetonationGridNode> adjacentNodes;
    private int lastGlobalRecacheId;
    private int lastRecacheTick;

    private CompPowerTrader powerComp;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            RecacheAllNodes();
        }
    }

    private CompProperties_WirelessDetonationGridNode Props => props as CompProperties_WirelessDetonationGridNode;

    private bool CanTransmit => Enabled && powerComp == null || powerComp.PowerOn;

    private float Radius => parent.GetStatValue(Resources.Stat.rxSignalRange);

    public IntVec3 Position => RemoteTechUtility.GetHighestHolderInMap(parent).Position;

    public static IEnumerable<CompWirelessDetonationGridNode> GetPotentialNeighborsFor(ThingDef def, IntVec3 pos,
        Map map)
    {
        var radius = def.GetStatValueAbstract(Resources.Stat.rxSignalRange);
        if (!(radius > 0f))
        {
            yield break;
        }

        var endpoint = (def.GetCompProperties<CompProperties_WirelessDetonationGridNode>()?.endpoint)
            .GetValueOrDefault();
        var candidates = map.listerBuildings.allBuildingsColonist;
        foreach (var candidate in candidates)
        {
            CompWirelessDetonationGridNode comp;
            if (candidate is ThingWithComps building
                && (comp = building.GetComp<CompWirelessDetonationGridNode>()) != null
                && building.Position.DistanceTo(pos) <= Mathf.Min(radius, comp.Radius)
                && (!endpoint || !comp.Props.endpoint))
            {
                yield return comp;
            }
        }
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        powerComp = parent.GetComp<CompPowerTrader>();
        if (Radius < float.Epsilon)
        {
            Log.Error(
                $"CompWirelessDetonationGridNode has zero radius. Missing signal range property on def {parent.def.defName}?");
        }

        if (Props == null)
        {
            Log.Error(
                $"CompWirelessDetonationGridNode needs CompProperties_WirelessDetonationGridNode on def {parent.def.defName}");
        }

        if (!respawningAfterLoad)
        {
            RecacheAllNodes();
        }
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        globalRecacheId = Rand.Int;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref _enabled, "wirelessNodeEnabled", true);
    }

    // enumerates pairs of receivers and the node closest to them
    public IEnumerable<TransmitterReceiverPair> FindReceiversInNetworkRange()
    {
        var receivers = new HashSet<IWirelessDetonationReceiver>();
        var transmitters = new HashSet<CompWirelessDetonationGridNode>();
        foreach (var transmitter in GetReachableNetworkNodes())
        {
            foreach (var receiver in transmitter.FindReceiversInNodeRange())
            {
                transmitters.Add(transmitter);
                receivers.Add(receiver);
            }
        }

        // for each receiver pick the closest transmitter
        return receivers.Select(r =>
        {
            var closest = transmitters.Select(t =>
                new KeyValuePair<CompWirelessDetonationGridNode, float>(t, t.Position.DistanceToSquared(r.Position))
            ).Aggregate((min, pair) => min.Value < float.Epsilon || pair.Value < min.Value ? pair : min);
            return new TransmitterReceiverPair(closest.Key, r);
        });
    }

    // finds buildings as well as their comps
    private IEnumerable<IWirelessDetonationReceiver> FindReceiversInNodeRange()
    {
        if (!CanTransmit)
        {
            yield break;
        }

        var radius = Radius;
        var map = ThingOwnerUtility.GetRootMap(parent.ParentHolder);
        var sample = map.listerBuildings.allBuildingsColonist;
        var ownPos = Position;
        foreach (var building in sample)
        {
            if (building.Position.DistanceTo(ownPos) > radius)
            {
                continue;
            }

            if (building is IWirelessDetonationReceiver br)
            {
                yield return br;
            }
            else
            {
                foreach (var thingComp in building.AllComps)
                {
                    // ReSharper disable once SuspiciousTypeConversion.Global
                    if (thingComp is IWirelessDetonationReceiver comp)
                    {
                        yield return comp;
                    }
                }
            }
        }
    }

    private IEnumerable<NetworkGraphLink> GetAllNetworkLinks()
    {
        var links = new HashSet<NetworkGraphLink>();
        TraverseNetwork(false, null, l => links.Add(l));
        return links;
    }

    private IEnumerable<CompWirelessDetonationGridNode> GetReachableNetworkNodes()
    {
        var nodes = new HashSet<CompWirelessDetonationGridNode>();
        TraverseNetwork(true, n => nodes.Add(n));
        return nodes;
    }

    private List<CompWirelessDetonationGridNode> GetAdjacentNodes()
    {
        RecacheAdjacentNodesIfNeeded();
        return adjacentNodes;
    }

    private void TraverseNetwork(bool reachableOnly, Action<CompWirelessDetonationGridNode> nodeCallback,
        Action<NetworkGraphLink> linkCallback = null)
    {
        var nodes = new HashSet<CompWirelessDetonationGridNode>();
        var queue = new Queue<CompWirelessDetonationGridNode>();
        queue.Enqueue(this);
        while (queue.Count > 0)
        {
            var comp = queue.Dequeue();
            // don't walk through endpoints, unless we're starting from one
            if (!nodes.Add(comp) || comp != this && comp.Props.endpoint)
            {
                continue;
            }

            nodeCallback?.Invoke(comp);
            var compAdjacent = comp.GetAdjacentNodes();
            foreach (var adjacent in compAdjacent)
            {
                var canTraverse = comp.CanTransmit && adjacent.CanTransmit;
                if (!canTraverse && reachableOnly)
                {
                    continue;
                }

                linkCallback?.Invoke(new NetworkGraphLink(comp, adjacent, canTraverse));
                queue.Enqueue(adjacent);
            }
        }
    }

    public void DrawNetworkLinks()
    {
        foreach (var link in GetAllNetworkLinks())
        {
            IntVec3 pos1 = link.First.Position, pos2 = link.Second.Position;
            var linkColor = link.CanTraverse ? SimpleColor.White : SimpleColor.Red;
            if (link.CanTraverse || Time.realtimeSinceStartup % 1f > .5f)
            {
                GenDraw.DrawLineBetween(pos1.ToVector3Shifted(), pos2.ToVector3Shifted(), linkColor);
            }
        }
    }

    public void DrawRadiusRing(bool drawReceivers = false)
    {
        var radius = Radius;
        if (!(radius <= GenRadial.MaxRadialPatternRadius))
        {
            return;
        }

        var ownPos = Position;
        GenDraw.DrawRadiusRing(ownPos, radius);
        if (!drawReceivers)
        {
            return;
        }

        foreach (var receiver in FindReceiversInNodeRange())
        {
            // highlight explosives in range
            var drawPos = receiver.Position.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            Graphics.DrawMesh(MeshPool.plane10, drawPos, Quaternion.identity, GenDraw.InteractionCellMaterial,
                0);
        }
    }

    private void RecacheAdjacentNodesIfNeeded()
    {
        if (lastRecacheTick + UpdateAdjacentNodesEveryTicks > Find.TickManager.TicksGame &&
            globalRecacheId == lastGlobalRecacheId)
        {
            return;
        }

        lastGlobalRecacheId = globalRecacheId;
        var map = ThingOwnerUtility.GetRootMap(parent.ParentHolder);
        var center = Position;
        var radius = Radius;
        adjacentNodes ??= [];
        adjacentNodes.Clear();
        lastRecacheTick = Find.TickManager.TicksGame;
        var candidates = map.listerBuildings.allBuildingsColonist;
        foreach (var candidate in candidates)
        {
            CompWirelessDetonationGridNode comp;
            if (candidate is not ThingWithComps building
                || building == parent
                || (comp = building.GetComp<CompWirelessDetonationGridNode>()) == null)
            {
                continue;
            }

            var mutualMaxRange = Mathf.Min(radius, comp.Radius);
            if (building.Position.DistanceTo(center) <= mutualMaxRange
                && (!Props.endpoint || Props.endpoint != comp.Props.endpoint))
            {
                adjacentNodes.Add(comp);
            }
        }
    }

    private static void RecacheAllNodes()
    {
        globalRecacheId = Rand.Int;
    }

    public struct TransmitterReceiverPair(
        CompWirelessDetonationGridNode transmitter,
        IWirelessDetonationReceiver receiver)
    {
        public readonly CompWirelessDetonationGridNode Transmitter = transmitter;
        public readonly IWirelessDetonationReceiver Receiver = receiver;
    }

    // order-invariant node pair
    public readonly struct NetworkGraphLink(
        CompWirelessDetonationGridNode first,
        CompWirelessDetonationGridNode second,
        bool canTraverse)
        : IEquatable<NetworkGraphLink>
    {
        public readonly CompWirelessDetonationGridNode First = first;
        public readonly CompWirelessDetonationGridNode Second = second;
        public readonly bool CanTraverse = canTraverse;

        public override bool Equals(object obj)
        {
            return obj is NetworkGraphLink pair && Equals(pair);
        }

        public bool Equals(NetworkGraphLink other)
        {
            return First == other.First && Second == other.Second || First == other.Second && Second == other.First;
        }

        public override int GetHashCode()
        {
            var one = First != null ? First.GetHashCode() : 0;
            var two = Second != null ? Second.GetHashCode() : 0;
            return Gen.HashCombineInt(one < two ? one : two, one < two ? two : one);
        }
    }
}