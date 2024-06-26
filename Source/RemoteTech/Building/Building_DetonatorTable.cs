﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RemoteTech;

/// <summary>
///     Finds remote explosive charges in range and detonates them on command.
///     Can be upgraded with a component to unlock the ability to use channels.
/// </summary>
public class Building_DetonatorTable : Building, IPawnDetonateable, IRedButtonFeverTarget
{
    private const string ChannelsBasicUpgradeId = "ChannelsBasic";
    private const string ChannelsAdvancedUpgradeId = "ChannelsAdvanced";
    private CompChannelSelector channels;
    private CompUpgrade channelsAdvanced;

    private CompUpgrade channelsBasic;
    private CompPowerTrader power;

    // saved
    private bool wantDetonation;

    private bool IsPowered => power == null || power.PowerOn;

    public bool UseInteractionCell => true;

    public bool WantsDetonation
    {
        get => wantDetonation;
        set => wantDetonation = value;
    }

    public void DoDetonation()
    {
        wantDetonation = false;
        if (!IsPowered)
        {
            PlayNeedPowerEffect();
            return;
        }

        RemoteTechUtility.ReportPowerUse(this, 20f);
        SoundDefOf.FlickSwitch.PlayOneShot(this);
        RemoteTechUtility.TriggerReceiversInNetworkRange(this, channels?.Channel ?? RemoteTechUtility.DefaultChannel);
    }

    public bool RedButtonFeverCanInteract => IsPowered;

    public void RedButtonFeverDoInteraction(Pawn p)
    {
        if (channels != null)
        {
            // switch to a non-empty channel
            var channelWithReceivers = channels.ChannelPopulation.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key)
                .RandomElementWithFallback(-1);
            if (channelWithReceivers > -1)
            {
                channels.Channel = channelWithReceivers;
            }
        }

        DoDetonation();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref wantDetonation, "wantDetonation");
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        channelsBasic = this.TryGetUpgrade(ChannelsBasicUpgradeId);
        channelsAdvanced = this.TryGetUpgrade(ChannelsAdvancedUpgradeId);
        channels = GetComp<CompChannelSelector>();
        power = GetComp<CompPowerTrader>();
        ConfigureChannelComp();
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        Command detonate;
        if (CanDetonateImmediately())
        {
            detonate = new Command_Action
            {
                action = DoDetonation,
                defaultLabel = "Detonator_detonateNow_label".Translate()
            };
        }
        else
        {
            detonate = new Command_Toggle
            {
                toggleAction = DetonateToggleAction,
                isActive = () => wantDetonation,
                defaultLabel = "DetonatorTable_detonate_label".Translate()
            };
        }

        detonate.icon = Resources.Textures.rxUIDetonate;
        detonate.defaultDesc = "DetonatorTable_detonate_desc".Translate();
        detonate.hotKey = Resources.KeyBinging.rxRemoteTableDetonate;
        yield return detonate;

        var c = channels?.GetChannelGizmo();
        if (c != null)
        {
            yield return c;
        }

        foreach (var g in base.GetGizmos())
        {
            yield return g;
        }
    }

    private void DetonateToggleAction()
    {
        wantDetonation = !wantDetonation;
    }

    public override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (signal == CompUpgrade.UpgradeCompleteSignal)
        {
            ConfigureChannelComp();
        }

        if (signal == CompChannelSelector.ChannelChangedSignal)
        {
            RemoteTechUtility.ReportPowerUse(this, 2f);
        }
    }

    private bool CanDetonateImmediately()
    {
        if (!def.hasInteractionCell)
        {
            return false;
        }

        var manningPawn = InteractionCell.GetFirstPawn(Map);
        return manningPawn is { Drafted: true };
    }

    private void ConfigureChannelComp()
    {
        var channelsLevel = RemoteTechUtility.ChannelType.None;
        if (channelsAdvanced is { Complete: true })
        {
            channelsLevel = RemoteTechUtility.ChannelType.Advanced;
        }
        else if (channelsBasic is { Complete: true })
        {
            channelsLevel = RemoteTechUtility.ChannelType.Basic;
        }

        channels?.Configure(false, false, true, channelsLevel);
    }

    public override string GetInspectString()
    {
        if (!Spawned)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder(base.GetInspectString());
        if (channels == null)
        {
            return stringBuilder.ToString();
        }

        channels.ChannelPopulation.TryGetValue(channels.Channel, out var list);
        stringBuilder.AppendLine();
        stringBuilder.Append("DetonatorTable_inrange".Translate());
        stringBuilder.Append($": {list?.Count ?? 0}");
        if (RemoteTechUtility.GetChannelsUnlockLevel() <= RemoteTechUtility.ChannelType.None)
        {
            return stringBuilder.ToString();
        }

        stringBuilder.AppendLine();
        stringBuilder.Append(RemoteTechUtility.GetCurrentChannelInspectString(channels.Channel));

        return stringBuilder.ToString();
    }

    // quick detonation option for drafted pawns
    public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
    {
        var opt = RemoteTechUtility.TryMakeDetonatorFloatMenuOption(selPawn, this);
        if (opt != null)
        {
            yield return opt;
        }

        foreach (var option in base.GetFloatMenuOptions(selPawn))
        {
            yield return option;
        }
    }

    private void PlayNeedPowerEffect()
    {
        var info = SoundInfo.InMap(this);
        info.volumeFactor = 3f;
        SoundDefOf.Power_OffSmall.PlayOneShot(info);
    }
}