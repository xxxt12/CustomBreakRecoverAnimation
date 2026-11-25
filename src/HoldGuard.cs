using System;
using HarmonyLib;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 保持期间阻止引擎把姿势刷回"混乱姿态"。
    ///
    /// 探针显示：回合结束刷新时引擎会调用
    ///   CharacterAppearance.OnBreak(isRefreshState=True) → ChangeMotion(Damaged)
    /// 这段代码会把我们定格好的末姿势冲掉。
    ///
    /// 所以在此拦截：只对"正在保持解混动画"的单位、且是刷新调用时跳过。
    /// 不重播任何动作，因此不会重复触发特效。
    /// </summary>
    [HarmonyPatch]
    public static class HoldGuard
    {
        [HarmonyPatch(typeof(SD.CharacterAppearance), "OnBreak")]
        [HarmonyPrefix]
        public static bool Pre_OnBreak(SD.CharacterAppearance __instance, bool isRefreshState)
        {
            try
            {
                if (!isRefreshState) return true;
                if (!BreakClipTick.IsHolding(__instance)) return true;

                int uid = -1;
                try { uid = __instance.GetView()?._unitModel?.GetUnitID() ?? -1; } catch { }
                Plugin.L.LogInfo($"[CBRA] OnBreak refresh suppressed while holding: unit={uid} / 保持中，已阻止混乱姿势刷新");
                return false;
            }
            catch { return true; }
        }
    }
}
