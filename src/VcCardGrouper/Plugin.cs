using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace VcCardGrouper;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "johann.vampirecrawlers.cardgrouper";
    public const string PluginName = "VC Card Grouper";
    public const string PluginVersion = "0.1.0";

    private readonly Harmony _harmony = new(PluginGuid);

    internal static ManualLogSource Logger { get; private set; }

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");

        HandGroupingController.Configure(Config);
        CardFaceReplacement.Configure(Config);

        ClassInjector.RegisterTypeInIl2Cpp<HandGroupingController>();
        AddComponent<HandGroupingController>();

        _harmony.PatchAll(typeof(Plugin).Assembly);
    }
}

