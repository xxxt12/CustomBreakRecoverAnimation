using System;
using System.Collections.Generic;
using ModularSkillScripts;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// animmode(锁定, 语音, 音效, 特效, 解混, 姿态重置, 倍率)
    ///
    ///   var1 位置锁定：
    ///     0 = 水平 + 垂直都锁（默认）
    ///     1 = 只锁水平（X/Z 平面）
    ///     2 = 只锁垂直（Y）
    ///     3 = 都不锁
    ///   var2 人物语音：
    ///     0 = 保留（默认）
    ///     1 = 关闭（用于改用自定义语音）
    ///   var3 动作音效：
    ///     0 = 保留（默认）
    ///     1 = 关闭
    ///   var4 特效：
    ///     0 = 保留（默认）；1 = 关闭技能动作携带的所有 FX 特效
    ///   var5 解除混乱：
    ///     0 = 照常解除（默认）；1 = 取消解除（只播动画，单位仍保持混乱）
    ///   var6 姿态重置：
    ///     0 = 保持动画最后一帧（默认，挂起引擎的姿态刷新回调）
    ///     1 = 动画结束后自然重置姿态（交给引擎刷新）
    ///   var7 镜头聚焦：
    ///     0 = 启用聚焦（默认）；1 = 关闭聚焦
    ///   var8 水平反转（可不填）：
    ///     0 = 不反转（默认）；1 = 播放前把单位水平反转 180°
    ///     用于抵消某些技能动画自带的转身
    ///     例：animmode(0,1,0,0,0,0,2.0,1)
    ///
    /// 与 breakrecoveranim 并行使用，且**必须写在它前面**（同一单位）。
    /// 例：
    ///   Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,1):breakrecoveranim(S3#0,0,1.5)
    /// </summary>
    public class Cq_AnimMode : IModularConsequence
    {
        internal sealed class Mode
        {
            public int Lock;        // 0..3
            public bool MuteVoice;  // var2=1 关闭人物语音
            public bool MuteSfx;    // var3=1 关闭动作音效
            public bool Focus = true;  // var7=0 启用聚焦（默认）/ 1 关闭
            public bool MuteFx;     // var5=1 关闭技能动作携带的所有 FX 特效
            public bool SkipRecover; // var5=1 取消解除混乱（只播动画）
            public bool ResetPose;   // var6=1 动画结束后自然重置姿态（0 = 保持末帧）
            public bool FlipH;       // var8=1 播放前水平反转（抵消动画自带的转身）
        }

        internal static readonly Dictionary<int, Mode> Modes = new Dictionary<int, Mode>();

        internal static Mode Get(int unitId)
        {
            if (Modes.TryGetValue(unitId, out var m)) return m;
            return null;
        }

        internal static void Clear(int unitId)
        {
            try { Modes.Remove(unitId); } catch { }
        }

        public void ExecuteConsequence(ModularSA modular, string section, string circledSection, string[] circles)
        {
            try
            {
                var unit = modular?.modsa_unitModel;
                if (unit == null) { Plugin.L.LogWarning("[CBRA] animmode: no unit / 取不到单位"); return; }
                int uid = -1; try { uid = unit.GetUnitID(); } catch { }

                int lockMode = IntArg(circles, 0, 0);
                if (lockMode < 0 || lockMode > 3) lockMode = 0;
                // 参数顺序（v2.0 起）：
                //   1 位置锁定 / 2 语音屏蔽 / 3 音效屏蔽 / 4 FX 屏蔽 / 5 解混关闭 / 6 镜头倍率
                bool muteVoice = IntArg(circles, 1, 0) == 1;
                bool muteSfx = IntArg(circles, 2, 0) == 1;
                bool muteFx = IntArg(circles, 3, 0) == 1;
                bool skipRecover = IntArg(circles, 4, 0) == 1;
                bool resetPose = IntArg(circles, 5, 0) == 1;
                bool focus = IntArg(circles, 6, 0) == 0;   // 0 = 启用（默认），1 = 关闭
                bool flipH = IntArg(circles, 7, 0) == 1;   // var8，可不填

                Modes[uid] = new Mode
                {
                    Lock = lockMode,
                    MuteVoice = muteVoice,
                    MuteSfx = muteSfx,
                    Focus = focus,
                    MuteFx = muteFx,
                    SkipRecover = skipRecover,
                    ResetPose = resetPose,
                    FlipH = flipH
                };

                string lockText = lockMode switch
                {
                    0 => "both/水平+垂直",
                    1 => "horizontal/仅水平",
                    2 => "vertical/仅垂直",
                    _ => "none/不锁"
                };
                Plugin.L.LogInfo($"[CBRA] animmode unit={uid} lock={lockMode}({lockText}) muteVoice={muteVoice} muteSfx={muteSfx} muteFx={muteFx} skipRecover={skipRecover} resetPose={resetPose} focus={focus} flipH={flipH} / 播放模式已设置");
            }
            catch (Exception e) { Plugin.L.LogError("[CBRA] animmode exception / 异常: " + e); }
        }

        private static float FloatArg(string[] circles, int i, float def)
        {
            try
            {
                if (circles == null || i >= circles.Length) return def;
                var s = (circles[i] ?? "").Trim();
                if (s.Length == 0) return def;
                if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            }
            catch { }
            return def;
        }

        private static int IntArg(string[] circles, int i, int def)
        {
            try
            {
                if (circles == null || i >= circles.Length) return def;
                var s = (circles[i] ?? "").Trim();
                if (s.Length == 0) return def;
                if (int.TryParse(s, out int v)) return v;
            }
            catch { }
            return def;
        }
    }
}
