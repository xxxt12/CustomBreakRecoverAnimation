using System;
using HarmonyLib;
using UnityEngine;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 探针：记录「原版解混人格」在解除混乱时到底调用了哪些动作切换。
    /// 目的：搞清楚原版实现，而不是继续猜。
    ///
    /// 只记录关注单位（10115/10614/10916/11115）以及我们自己（10310），避免刷屏。
    /// </summary>
    [HarmonyPatch]
    public static class VanillaMotionProbe
    {
        private static readonly int[] Watch = { 10115, 10614, 10916, 11115, 10310, 10114, 10513, 11210, 11111, 10212, 10714 };

        private static float T0 = -1f;
        private static string NowStr()
        {
            if (T0 < 0f) T0 = Time.unscaledTime;
            return (Time.unscaledTime - T0).ToString("0.000").PadLeft(8);
        }

        private static bool IsWatched(int uid)
        {
            for (int i = 0; i < Watch.Length; i++) if (Watch[i] == uid) return true;
            return false;
        }

        private static int UidOf(SD.CharacterAppearance ca)
        {
            try
            {
                var v = ca?.GetView();
                var m = v?._unitModel;
                return m?.GetUnitID() ?? -1;
            }
            catch { return -1; }
        }

        [HarmonyPatch(typeof(SD.CharacterAppearance), "ChangeMotion")]
        [HarmonyPrefix]
        public static void Pre_ChangeMotion(SD.CharacterAppearance __instance, MOTION_DETAIL motiondetail, bool forcely, int index)
        {
            try
            {
                int uid = UidOf(__instance);
                if (!IsWatched(uid)) return;

                string cur = "?";
                try { cur = __instance._currentMotiondetail.ToString(); } catch { }

                Plugin.L.LogInfo($"[CBRA][motion] {NowStr()}s unit={uid} ChangeMotion({motiondetail}, forcely={forcely}, index={index}) cur={cur}");
            }
            catch { }
        }

        [HarmonyPatch(typeof(SD.CharacterAppearance), "StopMotion")]
        [HarmonyPrefix]
        public static void Pre_StopMotion(SD.CharacterAppearance __instance, bool value, float timelineSpeed)
        {
            try
            {
                int uid = UidOf(__instance);
                if (!IsWatched(uid)) return;
                Plugin.L.LogInfo($"[CBRA][motion] {NowStr()}s unit={uid} StopMotion(value={value}, speed={timelineSpeed:0.##})");
            }
            catch { }
        }

        [HarmonyPatch(typeof(SD.CharacterAppearance), "OnRecoverBreak")]
        [HarmonyPrefix]
        public static void Pre_OnRecoverBreak(SD.CharacterAppearance __instance, bool isForceMotion)
        {
            try
            {
                int uid = UidOf(__instance);
                if (!IsWatched(uid)) return;
                Plugin.L.LogInfo($"[CBRA][motion] {NowStr()}s unit={uid} OnRecoverBreak(isForceMotion={isForceMotion})");
            }
            catch { }
        }

        [HarmonyPatch(typeof(SD.CharacterAppearance), "OnBreak")]
        [HarmonyPrefix]
        public static void Pre_OnBreak(SD.CharacterAppearance __instance, bool isRefreshState)
        {
            try
            {
                int uid = UidOf(__instance);
                if (!IsWatched(uid)) return;
                Plugin.L.LogInfo($"[CBRA][motion] {NowStr()}s unit={uid} OnBreak(isRefreshState={isRefreshState})");
            }
            catch { }
        }
    }
}
