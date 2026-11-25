using System;
using HarmonyLib;
using UnityEngine;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 回合结束流程探针：把候选方法按调用顺序 + 时间戳打出来，
    /// 用来找出这场战斗真正的"回合结束时间闸门"。
    ///
    /// 这些补丁只记录，不改变任何行为，可安全常驻。
    /// </summary>
    [HarmonyPatch]
    public static class RoundEndProbe
    {
        private static float T0 = -1f;

        private static float Now()
        {
            if (T0 < 0f) T0 = Time.unscaledTime;
            return Time.unscaledTime - T0;
        }

        private static void Log(string where, string extra = "")
        {
            try { Plugin.L.LogInfo("[CBRA][probe] " + Now().ToString("0.000").PadLeft(8) + "s " + where + " " + extra); } catch { }
        }

        // ---------- BattleObjectManager ----------
        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd() => Log("BattleObjectManager.OnRoundEnd");

        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_Before_View")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_Before_View() => Log("OnRoundEnd_Before_View");

        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_After_View")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_After_View() => Log("OnRoundEnd_After_View");

        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_After_Model")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_After_Model() => Log("OnRoundEnd_After_Model");

        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_ChecKRetreat")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_ChecKRetreat() => Log("OnRoundEnd_ChecKRetreat");

        // ---------- BattleUnitView：回合结束动作通道 ----------
        [HarmonyPatch(typeof(BattleUnitView), "OnRoundEnd")]
        [HarmonyPrefix]
        public static void Pre_View_OnRoundEnd(BattleUnitView __instance)
        {
            int uid = -1; try { uid = __instance._unitModel?.GetUnitID() ?? -1; } catch { }
            Log("BattleUnitView.OnRoundEnd", $"unit={uid}");
        }

        [HarmonyPatch(typeof(BattleUnitView), "OnRoundEndAction")]
        [HarmonyPrefix]
        public static void Pre_View_OnRoundEndAction(BattleUnitView __instance, MOTION_DETAIL motion)
        {
            int uid = -1; try { uid = __instance._unitModel?.GetUnitID() ?? -1; } catch { }
            Log("BattleUnitView.OnRoundEndAction", $"unit={uid} motion={motion}");
        }

        [HarmonyPatch(typeof(BattleUnitView), "ReserveRoundEndAction")]
        [HarmonyPrefix]
        public static void Pre_View_ReserveRoundEndAction(BattleUnitView __instance, MOTION_DETAIL motion)
        {
            int uid = -1; try { uid = __instance._unitModel?.GetUnitID() ?? -1; } catch { }
            Log("BattleUnitView.ReserveRoundEndAction", $"unit={uid} motion={motion}");
        }

        [HarmonyPatch(typeof(BattleUnitView), "PlayReservedRoundEndAction")]
        [HarmonyPrefix]
        public static void Pre_View_PlayReserved(BattleUnitView __instance)
        {
            int uid = -1; try { uid = __instance._unitModel?.GetUnitID() ?? -1; } catch { }
            Log("BattleUnitView.PlayReservedRoundEndAction", $"unit={uid}");
        }

        // ---------- TurnTransition（若存在） ----------
        [HarmonyPatch(typeof(ProduceAddOn_TurnTransition), "PlayProduce_Before")]
        [HarmonyPrefix]
        public static void Pre_TurnTransition_PlayProduce_Before() => Log("TurnTransition.PlayProduce_Before");

        [HarmonyPatch(typeof(ProduceAddOn_TurnTransition), "SetProduceEnd")]
        [HarmonyPrefix]
        public static void Pre_TurnTransition_SetProduceEnd() => Log("TurnTransition.SetProduceEnd");
    }
}
