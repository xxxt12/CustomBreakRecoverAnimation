using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ModularSkillScripts;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 自定义「混乱解除动画」插件（混乱 = Break）。
    ///
    /// 目的：让任意单位（含自制人格）通过 **纯 JSON** 获得"被特殊被动强制解除混乱时播放动画 + 镜头聚焦"的效果。
    ///
    /// 用法（写在单位的被动里）：
    ///   Modular/TIMING:OnForceBreakRecover/breakrecoveranim(S1#0,0,0.3)
    ///     var1 : 动作槽（S1#0 这种写法，段索引可省）
    ///     var2 : 动画起始秒（相对该动作段）
    ///     var3 : 动画终止秒
    ///
    /// 生效时点：自建 Modular 时点 `OnForceBreakRecover`，
    ///   挂在引擎的「强制解除混乱」路径上（RecoverBreakExceptForced 等），
    ///   **不会**在自然解除混乱时触发（自然解除走 RecoverBreakOnRoundStart...，单独探针记录）。
    ///
    /// 名词说明：
    ///   · Break         = 混乱（游戏内的崩溃状态）
    ///   · 强制解除      = 由特殊被动在回合结束时立即解除；"强制"指的是发起者,
    ///                     它仍然无法解除"强制混乱(IsForcelyBreak)"状态。
    ///   · 自然解除      = 混乱后要等一整个回合，再下一回合开始才恢复。本插件不接管这条路径。
    /// </summary>
    [BepInPlugin(Guid, "CustomBreakRecoverAnimation / 自定义混乱解除动画", "1.0.0")]
    [BepInDependency("GlitchGames.ModularSkillScripts")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "CustomBreakRecoverAnimation";
        public const string TimingName = "OnForceBreakRecover";
        public const int TimingId = 5002;      // 避开原版 0..92 与其它自定义时点

        public static ManualLogSource L;

        public override void Load()
        {
            L = base.Log;
            L.LogInfo("[CBRA] build v4.6.0 loaded / 版本已加载 (focus-switch)");

            // 1) 注册时点
            try
            {
                if (!MainClass.timingDict.ContainsKey(TimingName))
                    MainClass.timingDict.Add(TimingName, TimingId);
                foreach (var alias in new[] { "OnBreakRecoverForced", "ForceBreakRecover", "OnForcedBreakRecover" })
                    if (!MainClass.timingDict.ContainsKey(alias))
                        MainClass.timingDict.Add(alias, TimingId);
                L.LogInfo($"[CBRA] timing registered: {TimingName}={TimingId} / 时点已注册（强制解除混乱）");
            }
            catch (Exception e) { L.LogError("[CBRA] timing register failed / 时点注册失败: " + e); }

            // 2) 注册函数
            try
            {
                MainClass.consequenceDict["breakrecoveranim"] = new Cq_BreakRecoverAnim();
                MainClass.consequenceDict["animmode"] = new Cq_AnimMode();
                L.LogInfo("[CBRA] functions registered: breakrecoveranim + animmode / 已注册函数");
            }
            catch (Exception e) { L.LogError("[CBRA] function register failed / 函数注册失败: " + e); }

            // 3) 补丁：每个类单独 try，避免一个失败拖垮其它
            var h = new Harmony(Guid);
            PatchOne(h, typeof(BreakPatches), "BreakPatches");
            PatchOne(h, typeof(BreakClipTick), "BreakClipTick");
            PatchOne(h, typeof(RoundEndProbe), "RoundEndProbe");
            PatchOne(h, typeof(VanillaMotionProbe), "VanillaMotionProbe");
            PatchOne(h, typeof(HoldGuard), "HoldGuard");
            PatchOne(h, typeof(LateLock), "LateLock");
            PatchOne(h, typeof(RoundEndGuard), "RoundEndGuard");
            PatchOne(h, typeof(AudioMuteGuard), "AudioMuteGuard");
        }

        private static void PatchOne(Harmony h, Type t, string name)
        {
            try
            {
                h.PatchAll(t);
                L.LogInfo($"[CBRA] patches ok: {name}");
            }
            catch (Exception e)
            {
                L.LogError($"[CBRA] patches FAILED: {name} -> {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
