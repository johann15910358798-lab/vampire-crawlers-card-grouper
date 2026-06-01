using HarmonyLib;
using Nosebleed.Pancake.Models;

namespace VcCardGrouper;

[HarmonyPatch]
internal static class HandGroupingPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerModel), "Awake")]
    private static void PlayerModel_Awake_Postfix(PlayerModel __instance)
    {
        HandGroupingController.SetPlayer(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerModel), "OnDestroy")]
    private static void PlayerModel_OnDestroy_Postfix(PlayerModel __instance)
    {
        HandGroupingController.ClearPlayer(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerModel), nameof(PlayerModel.TryPlayCard))]
    private static void PlayerModel_TryPlayCard_Postfix(bool __result)
    {
        HandGroupingController.NotifyCardPlayed(__result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.OnDrawCard))]
    private static void CardModel_OnDrawCard_Postfix()
    {
        HandGroupingController.RequestRefresh();
    }
}

