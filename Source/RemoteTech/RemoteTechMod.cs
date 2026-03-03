using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Mlie;
using UnityEngine;
using UnityEngine.SceneManagement;
using Verse;
using static HugsLib.Utils.HugsLibUtility;

namespace RemoteTech;

// This is the new Mod entrypoint. It creates/loads ModSettings and
// queues a LongEvent to call the old DefsLoaded logic after defs are available.
public class RemoteTechMod : Mod
{
    public static RemoteTechMod Instance;
    private static string currentVersion;
    public readonly RemoteTechSettings Settings;

    public RemoteTechMod(ModContentPack content) : base(content)
    {
        // load or create settings object
        Settings = GetSettings<RemoteTechSettings>();
        Instance = this;
        currentVersion = VersionFromManifest.GetVersionFromModMetaData(content.ModMetaData);
        importOldHugsLibSettings();
        new Harmony("Mlie.RemoteTech").PatchAll(Assembly.GetExecutingAssembly());

        InjectedDefHasher.PrepareReflection();

        // Queue the legacy DefsLoaded work to run as a long event (ensures defs are ready).
        LongEventHandler.QueueLongEvent(() =>
        {
            try
            {
                // instantiate controller (previously ModBase instance) and call DefsLoaded
                RemoteTechController.Initialize(Settings);
                RemoteTechController.Instance.DefsLoaded();
            }
            catch (Exception e)
            {
                Log.Error($"RemoteTech: DefsLoaded initialization failed: {e}");
            }
        }, "RemoteTech: loading", false, null);

        // Hook sceneLoaded to approximate ModBase.SceneLoaded
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void importOldHugsLibSettings()
    {
        var hugsLibConfig = Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml");
        if (!new FileInfo(hugsLibConfig).Exists)
        {
            return;
        }

        var xml = XDocument.Load(hugsLibConfig);
        var modNodeName = "RemoteTech";

        var modSettings = xml.Root?.Element(modNodeName);
        if (modSettings == null)
        {
            return;
        }

        foreach (var modSetting in modSettings.Elements())
        {
            if (modSetting.Name == "forbidReplaced")
            {
                Instance.Settings.forbidReplaced = bool.Parse(modSetting.Value);
            }

            if (modSetting.Name == "forbidTimeout")
            {
                Instance.Settings.forbidTimeout = int.Parse(modSetting.Value);
            }

            if (modSetting.Name == "autoArmCombat")
            {
                Instance.Settings.autoArmCombat = bool.Parse(modSetting.Value);
            }

            if (modSetting.Name == "autoArmMining")
            {
                Instance.Settings.autoArmMining = bool.Parse(modSetting.Value);
            }

            if (modSetting.Name == "autoArmUtility")
            {
                Instance.Settings.autoArmUtility = bool.Parse(modSetting.Value);
            }

            if (modSetting.Name == "miningChargesForbid")
            {
                Instance.Settings.miningChargesForbid = bool.Parse(modSetting.Value);
            }

            if (modSetting.Name == "lowerStandingCap")
            {
                Instance.Settings.lowerStandingCap = bool.Parse(modSetting.Value);
            }
        }

        Instance.Settings.Write();
        xml.Root.Element(modNodeName)?.Remove();
        xml.Save(hugsLibConfig);

        Log.Message($"[{modNodeName}]: Imported old HugLib-settings");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            // replicate previous SceneLoaded behaviour
            PlayerAvoidanceGrids.ClearAllMaps();
            RemoteTechController.Instance?.SceneLoaded(scene);
        }
        catch (Exception e)
        {
            Log.Error($"RemoteTech: sceneLoaded handler error: {e}");
        }
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        // Draw settings UI similar to HugsLib settings handles
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.Label((TaggedString)"RemoteTech Settings");
        listing.Gap();

        // draw toggles bound to settings object
        listing.CheckboxLabeled("Setting_autoArmCombat_label".Translate(), ref Settings.autoArmCombat,
            "Setting_autoArmCombat_desc".Translate());
        listing.CheckboxLabeled("Setting_autoArmMining_label".Translate(), ref Settings.autoArmMining,
            "Setting_autoArmMining_desc".Translate());
        listing.CheckboxLabeled("Setting_autoArmUtility_label".Translate(), ref Settings.autoArmUtility,
            "Setting_autoArmUtility_desc".Translate());
        listing.CheckboxLabeled("Setting_miningChargesForbid_label".Translate(), ref Settings.miningChargesForbid,
            "Setting_miningChargesForbid_desc".Translate());

        listing.Gap();

        // forbid replaced / timeout
        listing.CheckboxLabeled("Setting_forbidReplaced_label".Translate(), ref Settings.forbidReplaced,
            "Setting_forbidReplaced_desc".Translate());

        if (Settings.forbidReplaced)
        {
            listing.Label("Setting_forbidTimeout_label".Translate() + $": {Settings.forbidTimeout}");
            // integer field
            var temp = Settings.forbidTimeout;
            var s = listing.TextEntry(temp.ToString());
            if (int.TryParse(s, out var parsed))
            {
                Settings.forbidTimeout = parsed;
            }
        }

        // developer-only setting visibility
        if (Prefs.DevMode)
        {
            listing.Gap();
            listing.CheckboxLabeled("Setting_lowerStandingCap_label".Translate(), ref Settings.lowerStandingCap,
                "Setting_lowerStandingCap_desc".Translate());
        }

        if (currentVersion != null)
        {
            listing.Gap();
            GUI.contentColor = Color.gray;
            listing.Label("Setting_currentVersion_label".Translate(currentVersion));
            GUI.contentColor = Color.white;
        }

        listing.End();

        // Save changes
        WriteSettings();
    }

    public override string SettingsCategory()
    {
        return "RemoteTech";
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        // ensure the controller reads the latest settings
        RemoteTechController.Instance?.UpdateFromSettings(Settings);
    }
}