using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RemoteTech;

/// <summary>
///     The base class for all wireless remote explosives.
///     Requires a CompCustomExplosive to work correctly. Can be armed and assigned to a channel.
///     Will blink with an overlay texture when armed.
/// </summary>
public class Building_RemoteExplosive : Building, ISwitchable, IWirelessDetonationReceiver, IAutoReplaceExposable
{
    private static readonly string ArmButtonLabel = "RemoteExplosive_arm_label".Translate();
    private static readonly string ArmButtonDesc = "RemoteExplosive_arm_desc".Translate();

    protected bool beepWhenLit = true;
    private CompChannelSelector channelsComp;

    private bool desiredArmState;

    private CompCustomExplosive explosiveComp;
    private bool isArmed;
    private CompAutoReplaceable replaceComp;
    private int ticksSinceFlare;

    private BuildingProperties_RemoteExplosive CustomProps
    {
        get
        {
            field ??= def.building as BuildingProperties_RemoteExplosive ??
                      new BuildingProperties_RemoteExplosive();

            return field;
        }
    }

    private GraphicData_Blinker BlinkerData => Graphic.data as GraphicData_Blinker;

    public bool IsArmed => isArmed;

    protected bool FuseLit => explosiveComp.WickStarted;

    public void ExposeAutoReplaceValues(AutoReplaceWatcher watcher)
    {
        var armed = IsArmed;
        watcher.ExposeValue(ref armed, "armed");
        if (watcher.ExposeMode != LoadSaveMode.LoadingVars)
        {
            return;
        }

        if (armed)
        {
            Arm();
        }
        else
        {
            Disarm();
        }
    }

    public bool WantsSwitch()
    {
        return isArmed != desiredArmState;
    }

    public void DoSwitch()
    {
        if (isArmed == desiredArmState)
        {
            return;
        }

        if (!isArmed)
        {
            Arm();
        }
        else
        {
            Disarm();
        }
    }

    public bool CanReceiveWirelessSignal => IsArmed && !FuseLit;

    public int CurrentChannel => channelsComp?.Channel ?? RemoteTechUtility.DefaultChannel;

    public void ReceiveWirelessSignal(Thing sender)
    {
        LightFuse();
    }

    protected virtual void LightFuse()
    {
        if (FuseLit)
        {
            return;
        }

        explosiveComp.StartWick(true);
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        this.UpdateSwitchDesignation();
        explosiveComp = GetComp<CompCustomExplosive>();
        replaceComp = GetComp<CompAutoReplaceable>()?.DisableGizmoAutoDisplay();
        channelsComp = GetComp<CompChannelSelector>()?.Configure(true);
        this.RequireComponent(CustomProps);
        this.RequireComponent(BlinkerData);
        if (respawningAfterLoad || CustomProps == null)
        {
            return;
        }

        var typeShouldAutoArm =
            CustomProps.explosiveType == RemoteExplosiveType.Combat &&
            RemoteTechController.Instance.settings.autoArmCombat ||
            CustomProps.explosiveType == RemoteExplosiveType.Mining &&
            RemoteTechController.Instance.settings.autoArmMining ||
            CustomProps.explosiveType == RemoteExplosiveType.Utility &&
            RemoteTechController.Instance.settings.autoArmUtility;
        var builtFromAutoReplacedBlueprint = replaceComp?.WasAutoReplaced ?? false;
        if (typeShouldAutoArm && !builtFromAutoReplacedBlueprint)
        {
            Arm();
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref isArmed, "isArmed");
        Scribe_Values.Look(ref ticksSinceFlare, "ticksSinceFlare");
        Scribe_Values.Look(ref desiredArmState, "desiredArmState");
    }

    protected override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (signal == CompChannelSelector.ChannelChangedSignal)
        {
            Resources.Sound.rxChannelChange.PlayOneShot(this);
        }
    }

    private void Arm()
    {
        if (IsArmed)
        {
            return;
        }

        DrawFlareOverlay(true);
        Resources.Sound.rxArmed.PlayOneShot(this);
        desiredArmState = true;
        isArmed = true;
    }

    private void Disarm()
    {
        if (!IsArmed)
        {
            return;
        }

        desiredArmState = false;
        isArmed = false;
        explosiveComp.StopWick();
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        var armGizmo = new Command_Toggle
        {
            toggleAction = ArmGizmoAction,
            isActive = () => desiredArmState,
            icon = Resources.Textures.rxUIArm,
            defaultLabel = ArmButtonLabel,
            defaultDesc = ArmButtonDesc,
            hotKey = Resources.KeyBinging.rxArm
        };
        yield return armGizmo;

        if (channelsComp != null)
        {
            channelsComp.Configure(true, false, false, RemoteTechUtility.GetChannelsUnlockLevel());
            var gz = channelsComp.GetChannelGizmo();
            if (gz != null)
            {
                yield return gz;
            }
        }

        if (replaceComp != null)
        {
            yield return replaceComp.MakeGizmo();
        }

        if (DebugSettings.godMode)
        {
            yield return new Command_Action
            {
                action = () =>
                {
                    if (isArmed)
                    {
                        Disarm();
                    }
                    else
                    {
                        Arm();
                    }
                },
                icon = Resources.Textures.rxUIArm,
                defaultLabel = "DEV: Toggle armed"
            };
            yield return new Command_Action
            {
                action = () =>
                {
                    Arm();
                    LightFuse();
                },
                icon = Resources.Textures.rxUIDetonate,
                defaultLabel = "DEV: Detonate now"
            };
        }

        foreach (var g in base.GetGizmos())
        {
            yield return g;
        }
    }

    protected override void Tick()
    {
        base.Tick();
        ticksSinceFlare++;
        // beep in sync with the flash
        if (!beepWhenLit || !FuseLit || ticksSinceFlare != 1)
        {
            return;
        }

        // raise pitch with each beep
        const float maxAdditionalPitch = .15f;
        var pitchRamp = (1 - (explosiveComp.WickTicksLeft / (float)explosiveComp.WickTotalTicks)) *
                        maxAdditionalPitch;
        EmitBeep(1f + pitchRamp);
    }

    public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
    {
        base.PostApplyDamage(dinfo, totalDamageDealt);
        if (dinfo.Def == DamageDefOf.EMP)
        {
            Disarm();
        }
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);
        if (!isArmed)
        {
            return;
        }

        if (FuseLit)
        {
            if (ticksSinceFlare >= BlinkerData.blinkerIntervalActive)
            {
                DrawFlareOverlay(true);
            }
        }
        else
        {
            if (ticksSinceFlare >= BlinkerData.blinkerIntervalNormal)
            {
                DrawFlareOverlay(false);
            }
        }
    }

    public override string GetInspectString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(base.GetInspectString());
        stringBuilder.Append(IsArmed ? "RemoteExplosive_armed".Translate() : "RemoteExplosive_notArmed".Translate());

        if (channelsComp == null || RemoteTechUtility.GetChannelsUnlockLevel() <= RemoteTechUtility.ChannelType.None)
        {
            return stringBuilder.ToString();
        }

        stringBuilder.AppendLine();
        stringBuilder.Append(RemoteTechUtility.GetCurrentChannelInspectString(channelsComp.Channel));

        return stringBuilder.ToString();
    }

    private void ArmGizmoAction()
    {
        desiredArmState = !desiredArmState;
        this.UpdateSwitchDesignation();
    }

    private void DrawFlareOverlay(bool useStrong)
    {
        ticksSinceFlare = 0;
        var overlay = useStrong ? Resources.Graphics.FlareOverlayStrong : Resources.Graphics.FlareOverlayNormal;
        RemoteTechUtility.DrawFlareOverlay(overlay, DrawPos, BlinkerData);
    }

    private void EmitBeep(float pitch)
    {
        var beepInfo = SoundInfo.InMap(this);
        beepInfo.pitchFactor = pitch;
        Resources.Sound.rxBeep.PlayOneShot(beepInfo);
    }
}