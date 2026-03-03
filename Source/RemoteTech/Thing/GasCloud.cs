using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RemoteTech;

/// <summary>
///     A self-replicating Thing with a concentration property.
///     Will spread in cardinal directions when the concentration is high enough, and loose concentration over time.
///     See MoteProperties_GasCloud for settings.
/// </summary>
public class GasCloud : Thing
{
    public delegate bool TraversibilityTest(Building b, GasCloud g);

    private const float AlphaEasingDivider = 10f;
    private const float SpreadingAnimationDuration = 1f;
    private const float DisplacingConcentrationFraction = .33f;

    private const int AvoidanceGridPathCost = 10;

    // this can be used to inject open/closed behavior for
    public static readonly Dictionary<Type, TraversibilityTest> TraversibleBuildings =
        new()
        {
            { typeof(Building_Vent), (_, _) => true },
            { typeof(Building_Door), (d, _) => ((Building_Door)d).Open }
        };

    private static int GlobalOffsetCounter;
    private static readonly List<GasCloud> adjacentBuffer = new(4);
    private static readonly List<IntVec3> positionBuffer = new(4);

    private static TickDelayScheduler tickDelayScheduler;
    private static DistributedTickScheduler distributedTickScheduler;
    private readonly ValueInterpolator interpolatedOffsetX;
    private readonly ValueInterpolator interpolatedOffsetY;
    private readonly ValueInterpolator interpolatedRotation;
    private readonly ValueInterpolator interpolatedScale;

    //saved fields
    private float concentration;
    private MoteProperties_GasCloud gasProps;
    protected int gasTicksProcessed;

    private float interpolatedAlpha;
    private float mouseoverLabelCacheTime;
    private bool needsInitialFill;
    public int relativeZOrder; // to avoid z fighting among clouds
    public float spriteAlpha = 1f;

    public Vector2 spriteOffset;
    public float spriteRotation;
    public Vector2 spriteScaleMultiplier = new(1f, 1f);

    protected GasCloud()
    {
        interpolatedOffsetX = new ValueInterpolator();
        interpolatedOffsetY = new ValueInterpolator();
        interpolatedScale = new ValueInterpolator();
        interpolatedRotation = new ValueInterpolator();
    }
    //

    public float Concentration => concentration;

    private bool IsBlocked => !TileIsGasTraversible(Position, Map, this);

    public override string LabelMouseover
    {
        get
        {
            if (field != null && !(mouseoverLabelCacheTime < Time.realtimeSinceStartup - .5f))
            {
                return field;
            }

            var effectivenessPercent =
                Mathf.Round(Mathf.Clamp01(Concentration / gasProps.FullAlphaConcentration) * 100f);
            if (concentration >= 1000)
            {
                var concentrationThousands = Math.Round(concentration / 1000, 1);
                field =
                    "GasCloud_statusReadout_high".Translate(LabelCap, concentrationThousands, effectivenessPercent);
            }
            else
            {
                field = "GasCloud_statusReadout_low".Translate(LabelCap, Mathf.Round(concentration),
                    effectivenessPercent);
            }

            mouseoverLabelCacheTime = Time.realtimeSinceStartup;

            return field;
        }
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        gasProps = def.mote as MoteProperties_GasCloud;
        relativeZOrder = ++GlobalOffsetCounter % 80;
        if (gasProps == null)
        {
            throw new Exception($"Missing required gas mote properties in {def.defName}");
        }

        // get instance
        var gameComponent = Current.Game.GetComponent<GameComponent_TickDelayScheduler>();
        if (gameComponent != null)
        {
            tickDelayScheduler = gameComponent.scheduler;
            distributedTickScheduler = gameComponent.distScheduler;
        }

        // check if valid
        if (tickDelayScheduler == null || tickDelayScheduler.lastProcessedTick < 0)
        {
            //Log.Message($"Last processed tick: {tickDelayScheduler?.lastProcessedTick}");
            //Log.Warning("TickDelayScheduler is either null or not initialized.");
            throw new Exception("TickDelayScheduler is either null or not initialized");
        }


        interpolatedScale.value = GetRandomGasScale();
        interpolatedRotation.value = GetRandomGasRotation();
        // uniformly distribute gas ticks to reduce per frame workload
        // wait for next tick to avoid registering while DistributedTickScheduler is mid-tick
        tickDelayScheduler.ScheduleCallback(() =>
                distributedTickScheduler.RegisterTickability(GasTick, gasProps.GastickInterval,
                    this)
            , 1, this);
        //PlayerAvoidanceGrids.AddAvoidanceSource(this, AvoidanceGridPathCost);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref concentration, "concentration");
        Scribe_Values.Look(ref gasTicksProcessed, "ticks");
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        if (!Find.TickManager.Paused)
        {
            UpdateInterpolatedValues();
        }

        var targetAlpha = Mathf.Min(1f, concentration / gasProps.FullAlphaConcentration);
        spriteAlpha = interpolatedAlpha =
            DoAdditiveEasing(interpolatedAlpha, targetAlpha, AlphaEasingDivider, Time.deltaTime);
        spriteOffset = new Vector2(interpolatedOffsetX, interpolatedOffsetY);
        spriteScaleMultiplier = new Vector2(interpolatedScale, interpolatedScale);
        spriteRotation = interpolatedRotation;
        base.DrawAt(drawLoc, flip);
    }

    private void UpdateInterpolatedValues()
    {
        interpolatedOffsetX.Update();
        interpolatedOffsetY.Update();
        if (!(gasProps.AnimationAmplitude > 0))
        {
            return;
        }

        interpolatedScale.Update();
        interpolatedRotation.Update();
        if (interpolatedOffsetX.finished)
        {
            // start offset interpolation
            var newX = Rand.Range(-gasProps.AnimationAmplitude, gasProps.AnimationAmplitude);
            var newY = Rand.Range(-gasProps.AnimationAmplitude, gasProps.AnimationAmplitude);
            var duration = gasProps.AnimationPeriod.RandomInRange;
            interpolatedOffsetX.StartInterpolation(newX, duration, CurveType.CubicInOut);
            interpolatedOffsetY.StartInterpolation(newY, duration, CurveType.CubicInOut);
        }

        if (interpolatedScale.finished)
        {
            // start scale interpolation
            interpolatedScale.StartInterpolation(GetRandomGasScale(), gasProps.AnimationPeriod.RandomInRange,
                CurveType.CubicInOut);
        }

        if (!interpolatedRotation.finished)
        {
            return;
        }

        // start rotation interpolation
        const float MaxRotationDelta = 90f;
        var newRotation = interpolatedRotation.value +
                          (Rand.Range(-MaxRotationDelta, MaxRotationDelta) * gasProps.AnimationAmplitude);
        interpolatedRotation.StartInterpolation(newRotation, gasProps.AnimationPeriod.RandomInRange,
            CurveType.CubicInOut);
    }

    public void ReceiveConcentration(float amount)
    {
        concentration += amount;
        if (concentration < 0)
        {
            concentration = 0;
        }
    }

    private void BeginSpreadingTransition(GasCloud parentCloud, IntVec3 targetPosition)
    {
        interpolatedOffsetX.value = parentCloud.Position.x - targetPosition.x;
        interpolatedOffsetY.value = parentCloud.Position.z - targetPosition.z;
        interpolatedOffsetX.StartInterpolation(0, SpreadingAnimationDuration, CurveType.QuinticOut);
        interpolatedOffsetY.StartInterpolation(0, SpreadingAnimationDuration, CurveType.QuinticOut);
    }

    protected virtual void GasTick()
    {
        if (!Spawned)
        {
            return;
        }

        gasTicksProcessed++;
        // dissipate
        var underRoof = Map.roofGrid.Roofed(Position);
        concentration -= underRoof ? gasProps.RoofedDissipation : gasProps.UnroofedDissipation;
        if (concentration <= 0)
        {
            Destroy(DestroyMode.KillFinalize);
            return;
        }

        //spread
        var gasTickFitForSpreading = gasTicksProcessed % gasProps.SpreadInterval == 0;
        if (gasTickFitForSpreading)
        {
            TryCreateNewNeighbors();
        }

        // if filled in
        if (IsBlocked)
        {
            ForcePushConcentrationToNeighbors();
        }

        // balance concentration
        ShareConcentrationWithMinorNeighbors();
    }

    private float GetRandomGasScale()
    {
        return 1f + Rand.Range(-gasProps.AnimationAmplitude, gasProps.AnimationAmplitude);
    }

    private static float GetRandomGasRotation()
    {
        return Rand.Value * 360f;
    }

    // this is just a "current + difference / divider", but adjusted for frame rate
    private static float DoAdditiveEasing(float currentValue, float targetValue, float easingDivider,
        float frameDeltaTime)
    {
        const float nominalFrameRate = 60f;
        var dividerMultiplier = frameDeltaTime < float.Epsilon ? 0 : 1f / nominalFrameRate / frameDeltaTime;
        easingDivider *= dividerMultiplier;
        if (easingDivider < 1)
        {
            easingDivider = 1;
        }

        var easingStep = (targetValue - currentValue) / easingDivider;
        return currentValue + easingStep;
    }

    private List<IntVec3> GetSpreadableAdjacentCells()
    {
        positionBuffer.Clear();
        for (var i = 0; i < 4; i++)
        {
            var adjPosition = GenAdj.CardinalDirections[i] + Position;
            if (!TileIsGasTraversible(adjPosition, Map, this))
            {
                continue;
            }

            var neighborThings = Map.thingGrid.ThingsListAtFast(adjPosition);
            var anyPreventingClouds = false;
            foreach (var thing in neighborThings)
            {
                // check if a cloud of same type already exists or another type of cloud is too concentrated to expand into
                if (thing is GasCloud cloud && (cloud.def == def ||
                                                cloud.concentration > concentration *
                                                DisplacingConcentrationFraction))
                {
                    anyPreventingClouds = true;
                }
            }

            if (!anyPreventingClouds)
            {
                positionBuffer.Add(adjPosition);
            }
        }

        positionBuffer.Shuffle();
        return positionBuffer;
    }

    private List<GasCloud> GetAdjacentGasCloudsOfSameDef()
    {
        adjacentBuffer.Clear();
        for (var i = 0; i < 4; i++)
        {
            var adjPosition = GenAdj.CardinalDirections[i] + Position;
            if (!adjPosition.InBounds(Map))
            {
                continue;
            }

            if (adjPosition.GetFirstThing(Map, def) is GasCloud cloud)
            {
                adjacentBuffer.Add(cloud);
            }
        }

        return adjacentBuffer;
    }

    private void ShareConcentrationWithMinorNeighbors()
    {
        var neighbors = GetAdjacentGasCloudsOfSameDef();
        var numSharingNeighbors = 0;
        for (var i = 0; i < neighbors.Count; i++)
        {
            var neighbor = neighbors[i];
            // do not push to a blocked cloud, unless it's one we created this tick
            if (neighbor.Concentration >= concentration || !neighbor.needsInitialFill && neighbor.IsBlocked)
            {
                neighbors[i] = null;
            }
            else
            {
                numSharingNeighbors++;
            }
        }

        if (numSharingNeighbors <= 0)
        {
            return;
        }

        foreach (var neighbor in neighbors)
        {
            if (neighbor == null)
            {
                continue;
            }

            var neighborConcentration = neighbor.concentration > 0 ? neighbor.Concentration : 1;
            var amountToShare = (concentration - neighborConcentration) / (numSharingNeighbors + 1) *
                                gasProps.SpreadAmountMultiplier;
            neighbor.ReceiveConcentration(amountToShare);
            neighbor.needsInitialFill = false;
            concentration -= amountToShare;
        }
    }

    private void ForcePushConcentrationToNeighbors()
    {
        var neighbors = GetAdjacentGasCloudsOfSameDef();
        foreach (var neighbor in neighbors)
        {
            if (neighbor.IsBlocked)
            {
                continue;
            }

            var pushAmount = concentration / neighbors.Count;
            neighbor.ReceiveConcentration(pushAmount);
            concentration -= pushAmount;
        }
    }

    private void TryCreateNewNeighbors()
    {
        var spreadsLeft = Mathf.FloorToInt(concentration / gasProps.SpreadMinConcentration);
        if (spreadsLeft <= 0)
        {
            return;
        }

        var viableCells = GetSpreadableAdjacentCells();
        foreach (var intVec3 in viableCells)
        {
            if (spreadsLeft <= 0)
            {
                break;
            }

            // place on next Normal tick. We cannot register while DistributedTickScheduler is ticking
            var newCloud = (GasCloud)ThingMaker.MakeThing(def);
            newCloud.needsInitialFill = true;
            newCloud.BeginSpreadingTransition(this, intVec3);
            GenPlace.TryPlaceThing(newCloud, intVec3, Map, ThingPlaceMode.Direct);
            spreadsLeft--;
        }
    }

    private static bool TileIsGasTraversible(IntVec3 pos, Map map, GasCloud sourceCloud)
    {
        if (!pos.InBounds(map) || !map.pathing.Normal.pathGrid.WalkableFast(pos))
        {
            return false;
        }

        var thingList = map.thingGrid.ThingsListAtFast(pos);
        foreach (var thing in thingList)
        {
            // check for conditionally traversable buildings
            if (thing is Building building)
            {
                TraversibleBuildings.TryGetValue(building.GetType(), out var travTest);
                if (travTest != null && !travTest(building, sourceCloud))
                {
                    return false;
                }
            }

            // check for more concentrated gases of a different def
            if (thing is GasCloud cloud && cloud.def != sourceCloud.def &&
                sourceCloud.concentration < cloud.concentration)
            {
                return false;
            }
        }

        return true;
    }
}