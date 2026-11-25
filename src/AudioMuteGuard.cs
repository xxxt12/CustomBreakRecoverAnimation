using System;
using HarmonyLib;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// animmode(?,1) 时关闭技能动作自带的音频。
    ///
    /// 技能动作的音频是 timeline 里的 FMOD 轨，由外观自己的 ChangeMotion 播放。
    /// 我们无法在引擎播放时传参，所以在 ChangeMotion 上拦截：
    /// 若该单位配置了静音，则把 muteSound 强制置 true。
    /// </summary>
    [HarmonyPatch]
    public static class AudioMuteGuard
    {
        [HarmonyPatch(typeof(SD.CharacterAppearance), "ChangeMotion")]
        [HarmonyPrefix]
        public static void Pre_ChangeMotion(SD.CharacterAppearance __instance, ref bool muteSound)
        {
            try
            {
                if (muteSound) return;
                int uid = -1;
                try { uid = __instance?.GetView()?._unitModel?.GetUnitID() ?? -1; } catch { }
                if (uid <= 0) return;

                var mode = Cq_AnimMode.Get(uid);
                if (mode == null || !(mode.MuteVoice || mode.MuteSfx)) return;

                muteSound = true;
                Plugin.L.LogInfo($"[CBRA] native motion audio muted: unit={uid} / 已关闭动作原生音频（兜底）");
            }
            catch { }
        }
    }
}
