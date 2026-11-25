using System;
using HarmonyLib;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 回合即将推进时提前收尾并交还原生系统。
    ///
    /// 背景：我们的片段若比其他人长，引擎会先推进回合；此时若镜头仍在 SelectFocus、
    /// 或姿态刷新回调仍被挂起，就会锁死原生镜头并让原版站位重置失效。
    ///
    /// 做法：挂在引擎"回合结束流程的后段"（OnRoundEnd_After_Model）上——
    /// 这是引擎已经等待完所有回合结束动作、即将推进回合的时刻。
    /// 在此处若我们仍在演出，就立刻收尾、还原所有状态、释放镜头，把控制权交回原版。
    /// </summary>
    [HarmonyPatch]
    public static class RoundEndGuard
    {
        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_After_Model")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_After_Model()
        {
            try
            {
                if (!BreakClipTick.IsActive) return;
                Plugin.L.LogInfo("[CBRA] round-end reached while clip active -> early finish / 回合即将推进，提前收尾");
                BreakClipTick.ForceCleanupAll("round-end signal");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] RoundEndGuard: " + e.Message); }
        }

        [HarmonyPatch(typeof(BattleObjectManager), "OnRoundEnd_After_View")]
        [HarmonyPrefix]
        public static void Pre_OnRoundEnd_After_View()
        {
            try
            {
                if (!BreakClipTick.IsActive) return;
                BreakClipTick.ForceCleanupAll("round-end view signal");
            }
            catch { }
        }
    }
}
