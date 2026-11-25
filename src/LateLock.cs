using System;
using HarmonyLib;
using UnityEngine;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 渲染前最终锁定（兜底）。
    ///
    /// 说明：位移是烘焙在动画片段里的（root 曲线），抑制 Track Offsets 治不了；
    /// 而 PlayableDirector 在 PreLateUpdate 写 transform，晚于 BattleCamManager.LateUpdate。
    /// 因此除每帧纠正外，还在回合结束时**强制回位**（见 ForceCleanupAll），
    /// 保证回合交接时单位一定在锚点上。
    ///
    /// 注：Camera.onPreCull 在本 interop 里不是可 += 的委托（是 Il2Cpp 结构体），
    /// 订阅无法编译；Camera.FireOnPreCull 又只在有订阅者时才被调用，故此法不可用。
    /// </summary>
    [HarmonyPatch]
    public static class LateLock
    {
        internal static int Calls;

        [HarmonyPatch(typeof(Camera), "FireOnPreCull")]
        [HarmonyPrefix]
        public static void Pre_FireOnPreCull()
        {
            try
            {
                Calls++;
                if (Calls <= 3) Plugin.L.LogInfo($"[CBRA][latelock] FireOnPreCull #{Calls}");
                BreakClipTick.LateLockAll();
            }
            catch { }
        }
    }
}
