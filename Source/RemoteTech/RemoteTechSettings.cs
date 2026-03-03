using Verse;

namespace RemoteTech;

public class RemoteTechSettings : ModSettings
{
    // defaults reflect previous defaults you used with HugsLib handles
    public bool autoArmCombat = true;
    public bool autoArmMining = true;
    public bool autoArmUtility = true;

    public bool forbidReplaced = true;
    public int forbidTimeout = 30;
    public bool lowerStandingCap;
    public bool miningChargesForbid = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref autoArmCombat, "autoArmCombat", true);
        Scribe_Values.Look(ref autoArmMining, "autoArmMining", true);
        Scribe_Values.Look(ref autoArmUtility, "autoArmUtility", true);
        Scribe_Values.Look(ref miningChargesForbid, "miningChargesForbid", true);
        Scribe_Values.Look(ref lowerStandingCap, "lowerStandingCap");

        Scribe_Values.Look(ref forbidReplaced, "forbidReplaced", true);
        Scribe_Values.Look(ref forbidTimeout, "forbidTimeout", 30);
    }
}