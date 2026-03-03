using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RemoteTech;

/// <summary>
///     Scans a circular area using a sweeping scan and detects pawns that match the current filter.
/// </summary>
public class Building_ProximitySensor : Building, ISwitchable, ISensorSettingsProvider
{
    private const string WirelessUpgrageReferenceId = "WirelessDetonation";
    private const string AIUpgrageReferenceId = "AIController";

    private readonly List<IntVec3> drawnCells = [];
    private CachedValue<float> angleStat;
    private RadialGradientArea area;
    private CompUpgrade brainComp;
    private CompChannelSelector channelsComp;
    private CompGlowerToggleable glowerComp;
    private bool isSelected;
    private float lastTriggeredTick;
    private CompAIPilotLight lightComp;
    private SensorSettings pendingSettings;
    private CompPowerTrader powerComp;
    private CachedValue<float> rangeStat;
    private SensorSettings settings = new();

    // saved
    private Arc slice;
    private CachedValue<float> speedStat;
    private List<Pawn> trackedPawns = [];
    private CompWiredDetonationSender wiredComp;
    private CompWirelessDetonationGridNode wirelessComp;

    private float CooldownTime =>
        Mathf.Max(0, lastTriggeredTick + settings.CooldownTime.SecondsToTicks() - GenTicks.TicksGame) /
        GenTicks.TicksPerRealSecond;

    private bool PowerOn => powerComp == null || powerComp.PowerOn;

    public SensorSettings Settings => pendingSettings ?? settings;

    public bool HasWirelessUpgrade => wirelessComp is { Enabled: true };

    public bool HasAIUpgrade => brainComp is { Complete: true };

    public void OnSettingsChanged(SensorSettings s)
    {
        pendingSettings = s;
        this.UpdateSwitchDesignation();
    }

    public bool WantsSwitch()
    {
        return pendingSettings != null && !settings.Equals(pendingSettings);
    }

    public void DoSwitch()
    {
        settings = pendingSettings?.Clone() ?? new SensorSettings();
        pendingSettings = null;
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        angleStat = this.GetCachedStat(Resources.Stat.rxSensorAngle);
        speedStat = this.GetCachedStat(Resources.Stat.rxSensorSpeed);
        rangeStat = this.GetCachedStat(Resources.Stat.rxSensorRange);
        powerComp = GetComp<CompPowerTrader>();
        wiredComp = GetComp<CompWiredDetonationSender>();
        wirelessComp = GetComp<CompWirelessDetonationGridNode>();
        channelsComp = GetComp<CompChannelSelector>();
        brainComp = this.TryGetUpgrade(AIUpgrageReferenceId);
        lightComp = GetComp<CompAIPilotLight>();
        glowerComp = GetComp<CompGlowerToggleable>();
        UpdateUpgradeableStuff();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref slice, "slice");
        Scribe_Collections.Look(ref trackedPawns, "trackedPawns", false, LookMode.Reference);
        Scribe_Values.Look(ref lastTriggeredTick, "lastTriggered");
        Scribe_Deep.Look(ref settings, "settings");
        Scribe_Deep.Look(ref pendingSettings, "pendingSettings");
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        trackedPawns ??= [];

        trackedPawns.RemoveAll(p => p == null);
        settings ??= new SensorSettings();
    }

    protected override void Tick()
    {
        if (!PowerOn)
        {
            return;
        }

        slice = slice.Rotate(speedStat / GenTicks.TicksPerRealSecond);
        if (GenTicks.TicksGame % 6 != 0)
        {
            return;
        }

        drawnCells.Clear();
        var thingGrid = Map.thingGrid;
        // visit all cells in slice
        foreach (var cell in area.CellsInSlice(slice))
        {
            if (!cell.InBounds(Map))
            {
                continue;
            }

            Pawn pawn = null;
            // find first pawn in cell
            var cellThings = thingGrid.ThingsListAtFast(cell);
            foreach (var thing in cellThings)
            {
                lightComp?.ReportTarget(thing);
                if (thing is not Pawn p)
                {
                    continue;
                }

                pawn = p;
                break;
            }

            // track pawn and report them to the authorities
            if (pawn != null && !trackedPawns.Contains(pawn) && GenSight.LineOfSight(Position, cell, Map, true)
                && CooldownTime < float.Epsilon && PawnMatchesFilter(pawn))
            {
                TriggerSensor(pawn);
            }

            // store cell position for drawing
            if (isSelected && GenSight.LineOfSight(Position, cell, Map, true))
            {
                drawnCells.Add(cell);
            }
        }

        // prune tracked pawns that have died or left the area
        for (var i = trackedPawns.Count - 1; i >= 0; i--)
        {
            var pawn = trackedPawns[i];
            if (pawn is { Dead: false } && !(Position.DistanceTo(trackedPawns[i].Position) > rangeStat))
            {
                continue;
            }

            lightComp?.ReportTargetLost(trackedPawns[i]);
            trackedPawns.RemoveAt(i);
        }

        isSelected = false;
    }

    private void TriggerSensor(Pawn pawn)
    {
        lastTriggeredTick = GenTicks.TicksGame;
        trackedPawns.Add(pawn);
        if (settings.SendMessage)
        {
            NotifyPlayer(pawn);
        }

        if (settings.SendWired && wiredComp != null)
        {
            wiredComp.SendNewSignal();
        }

        if (settings.SendWireless && wirelessComp is { Enabled: true } && channelsComp != null)
        {
            RemoteTechUtility.TriggerReceiversInNetworkRange(this, channelsComp.Channel);
        }
    }

    public override void DrawExtraSelectionOverlays()
    {
        if (powerComp is not { PowerOn: true })
        {
            return;
        }

        base.DrawExtraSelectionOverlays();
        isSelected = true;
        GenDraw.DrawFieldEdges(drawnCells);
        foreach (var pawn in trackedPawns)
        {
            if (trackedPawns == null)
            {
                continue;
            }

            GenDraw.DrawCooldownCircle(pawn.DrawPos - Altitudes.AltIncVect, .5f);
        }
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        yield return new Command_Action
        {
            defaultLabel = "proxSensor_settings".Translate() +
                           (WantsSwitch() ? "RemoteExplosive_channel_switching".Translate() : TaggedString.Empty),
            icon = Resources.Textures.rxUISensorSettings,
            action = OpenSettingsDialog
        };
        foreach (var gizmo in base.GetGizmos())
        {
            yield return gizmo;
        }
    }

    public override string GetInspectString()
    {
        var s = new StringBuilder(base.GetInspectString());
        if (!(CooldownTime > 0f))
        {
            return s.ToString();
        }

        s.AppendLine();
        s.AppendFormat("proxSensor_cooldown".Translate(), Math.Round(CooldownTime, 1));

        return s.ToString();
    }

    protected override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        UpdateUpgradeableStuff();
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);
        if (!PowerOn)
        {
            return;
        }

        // draw arc overlay; rotate around lower left corner
        var m = Matrix4x4.TRS(DrawPos - Altitudes.AltIncVect, Quaternion.AngleAxis(slice.StartAngle, Vector3.up),
                    Vector3.one * 2f) *
                Matrix4x4.TRS(new Vector3(0.5f, 0, 0.5f), Quaternion.identity, Vector3.one);
        Graphics.DrawMesh(MeshPool.plane10, m,
            MaterialPool.MatFrom(Resources.Textures.rxProximitySensorArc, ShaderDatabase.TransparentPostLight,
                Color.white), 0);
    }

    private void OpenSettingsDialog()
    {
        Find.WindowStack.Add(new Dialog_SensorSettings(this));
    }

    private bool PawnMatchesFilter(Pawn p)
    {
        if (brainComp is not { Complete: true } || p.RaceProps == null)
        {
            return true;
        }

        return settings.DetectAnimals && p.RaceProps.Animal
               || settings.DetectEnemies && p.HostileTo(Faction)
               || settings.DetectFriendlies && (p.Faction == null || !p.Faction.HostileTo(Faction)) &&
               !p.RaceProps.Animal;
    }

    private void NotifyPlayer(Pawn pawn)
    {
        var message = settings.Name.NullOrEmpty()
            ? "proxSensor_message".Translate(pawn.LabelShortCap)
            : "proxSensor_messageName".Translate(settings.Name, pawn.LabelShort);
        Messages.Message(message, pawn,
            settings.AlternativeSound ? Resources.MessageType.rxSensorTwo : Resources.MessageType.rxSensorOne);
    }

    private void UpdateUpgradeableStuff()
    {
        area = new RadialGradientArea(Position, rangeStat.ValueRecached);
        slice = new Arc(slice.StartAngle, angleStat.ValueRecached);
        speedStat.Recache();
        wirelessComp?.Enabled = this.IsUpgradeCompleted(WirelessUpgrageReferenceId);

        channelsComp?.Configure(true, true, true,
            this.IsUpgradeCompleted(WirelessUpgrageReferenceId)
                ? RemoteTechUtility.ChannelType.Advanced
                : RemoteTechUtility.ChannelType.None);
        var brainIsOn = (brainComp?.Complete ?? false) && PowerOn;
        lightComp?.Enabled = brainIsOn;

        glowerComp?.ToggleGlow(brainIsOn);
    }

    #region support stuff

    private struct Arc(float startAngle, float width) : IExposable
    {
        public float StartAngle = Mathf.Repeat(startAngle, 360f);
        public float Width = Mathf.Min(360f, width);

        public Arc Rotate(float degrees)
        {
            return new Arc(StartAngle + degrees, Width);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref StartAngle, "start");
            Scribe_Values.Look(ref Width, "width");
        }
    }

    /// <summary>
    ///     A performance-friendly way to query a circular area of map cells in a given arc from the starting position.
    ///     Cell angles are pre-calculated, allowing for sublinear time queries.
    /// </summary>
    private class RadialGradientArea
    {
        private readonly CellAngle[] cells;

        public RadialGradientArea(IntVec3 center, float radius)
        {
            cells = GenRadial.RadialCellsAround(center, radius, false)
                .Select(c => new CellAngle(c, (c - center).AngleFlat))
                .OrderBy(c => c.Angle)
                .ToArray();
        }

        public Enumerable CellsInSlice(Arc arc)
        {
            return new Enumerable(cells, arc);
        }

        public readonly struct Enumerable(CellAngle[] cells, Arc arc)
        {
            public Enumerator GetEnumerator()
            {
                return new Enumerator(cells, AngleToIndex(arc.StartAngle), AngleToIndex(arc.StartAngle + arc.Width));
            }

            private int AngleToIndex(float angle)
            {
                return Mathf.FloorToInt(angle / 360f * cells.Length);
            }
        }

        public struct Enumerator(CellAngle[] cells, int startIndex, int endIndex)
        {
            private int index = startIndex - 1;

            public IntVec3 Current => cells[index % cells.Length].Cell;

            public bool MoveNext()
            {
                return ++index <= endIndex;
            }
        }

        public struct CellAngle(IntVec3 cell, float angle)
        {
            public readonly IntVec3 Cell = cell;
            public readonly float Angle = angle;
        }
    }
}

#endregion