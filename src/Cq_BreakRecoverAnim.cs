using System;
using System.Text.RegularExpressions;
using UnityEngine.Timeline;
using ModularSkillScripts;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// breakrecoveranim(动作槽, 起始秒, 终止秒)
    ///
    ///   强制解除混乱时播放该单位自己的技能动作片段 + 镜头聚焦。
    ///   var1 动作槽（S1#0 / S3#2 / Special1），var2 起始秒，var3 终止秒。
    ///
    /// 关键顺序：
    ///   ① 解析出该段的 TimelineAsset（用于提前静音音频轨）
    ///   ② 若 animmode 要求静音 → 在引擎播放**之前**静音音频轨
    ///   ③ 真正解除混乱（引擎状态 + 外观层）
    ///   ④ 登记为引擎的回合结束动作，由引擎播放（只播一次）
    ///   ⑤ 登记给 tick：裁剪、位置锁定、收尾
    /// </summary>
    public class Cq_BreakRecoverAnim : IModularConsequence
    {
        public void ExecuteConsequence(ModularSA modular, string section, string circledSection, string[] circles)
        {
            try
            {
                var unit = modular?.modsa_unitModel;
                if (unit == null) { Plugin.L.LogWarning("[CBRA] breakrecoveranim: no unit / 取不到单位"); return; }
                int uid = -1; try { uid = unit.GetUnitID(); } catch { }

                string spec = (circles != null && circles.Length > 0) ? (circles[0] ?? "").Trim() : "S1";
                float startSec = FloatArg(circles, 1, 0f);
                float endSec = FloatArg(circles, 2, -1f);

                if (!TryParseMotion(spec, out MOTION_DETAIL motion, out int index))
                {
                    Plugin.L.LogWarning($"[CBRA] bad motion spec '{spec}' / 动作槽写法错误");
                    return;
                }

                // 未指定段索引（如 S11 而不是 S11#0）时归一化为 0：
                // 引擎的 OnRoundEndAction 也默认播第 0 段，两边必须一致，否则拿不到资产。
                if (index < 0) index = 0;

                var mgr = SingletonBehavior<BattleObjectManager>.Instance;
                var view = mgr?.GetView(unit);
                var ca = view?.Appearance;
                if (ca == null) { Plugin.L.LogWarning($"[CBRA] unit={uid} has no appearance"); return; }

                // ---- 解析该段资产与长度 ----
                float segLen = 0f;
                TimelineAsset segAsset = null;
                try
                {
                    var assets = ca.GetMotion(motion, false)?.timelineAssets;
                    if (assets != null && index >= 0 && index < assets.Count && assets[index] != null)
                    {
                        segAsset = assets[index];
                        // 用"内容结束时刻"而不是 duration：
                        // duration 是硬币时间窗，常被拉长（例：显示 7.6 秒，实际动作只有 2~3 秒）。
                        // 用 duration 会导致动画播到 padding 区、回合结束白等一大截。
                        segLen = (float)ContentEnd(segAsset);
                        if (segLen <= 0.01f) segLen = (float)segAsset.duration;
                    }
                }
                catch { }

                if (segAsset == null)
                    Plugin.L.LogWarning($"[CBRA] segAsset is null (motion={motion}#{index}) / 拿不到该段资产，位移抑制会失败");

                if (endSec < 0f) endSec = segLen > 0.01f ? segLen : 1f;

                // var4 播放倍速（0.5 = 慢一倍，2.0 = 快一倍）。默认 1.0。
                // 注意：位置参数，必须同时填写 var2 与 var3 才能用到 var4。
                float speed = FloatArg(circles, 3, 1.0f);
                if (speed < 0.1f) speed = 0.1f;
                if (speed > 4.0f) speed = 4.0f;
                if (startSec < 0f) startSec = 0f;
                if (endSec <= startSec + 0.01f) endSec = startSec + 0.05f;
                float clipLen = endSec - startSec;

                // ---- 播放前静音（语音事件在播放到该点时就会发出，晚了没用）----
                bool muteVoice = false, muteSfx = false, muteFx = false, skipRecover = false;
                try
                {
                    var m = Cq_AnimMode.Get(uid);
                    if (m != null)
                    {
                        muteVoice = m.MuteVoice;
                        muteSfx = m.MuteSfx;
                        muteFx = m.MuteFx;
                        skipRecover = m.SkipRecover;
                    }
                }
                catch { }
                if ((muteVoice || muteSfx || muteFx) && segAsset != null)
                {
                    try { BreakClipTick.PreMuteOnAsset(segAsset, uid, muteVoice, muteSfx, muteFx); } catch { }
                }

                // ---- 真正解除混乱（var6=1 时跳过）----
                bool recovered = false;
                if (skipRecover)
                {
                    Plugin.L.LogInfo($"[CBRA] break recovery skipped by animmode(var6=1): unit={uid} / 按配置取消解除混乱");
                }
                else
                try
                {
                    bool wasBreak = false;
                    try { wasBreak = unit.IsBreak(); } catch { }
                    if (wasBreak)
                    {
                        try
                        {
                            BreakPatches._inRecover = true;
                            unit.RecoverAllBreak((BATTLE_EVENT_TIMING)0);
                        }
                        catch { }
                        finally { BreakPatches._inRecover = false; }
                    }
                    ca.OnRecoverBreak(true);
                    recovered = true;
                    Plugin.L.LogInfo($"[CBRA] break cleared at animation start: unit={uid} wasBreak={wasBreak} / 动画开始即解除混乱");
                }
                catch (Exception e0) { Plugin.L.LogWarning("[CBRA] clear break state failed: " + e0.Message); }

                // ---- 登记为引擎的回合结束动作 ----
                bool registered = false;
                try
                {
                    view.ReserveRoundEndAction(motion);
                    view._skipNextRefreshMotionForReservedRoundEndAction = true;
                    registered = true;
                }
                catch (Exception e1) { Plugin.L.LogWarning("[CBRA] ReserveRoundEndAction failed: " + e1.Message); }

                // 段索引非 0 时通道无法指定，补一次切换（会重播一次，已知代价）
                bool forcedSwitch = false;
                if (registered && index > 0)
                {
                    try { ca.ChangeMotion(motion, true, index, false, null, false); forcedSwitch = true; }
                    catch (Exception e2) { Plugin.L.LogWarning("[CBRA] ChangeMotion failed: " + e2.Message); }
                }

                // ---- 交给 tick 接管 ----
                bool trimmed = (endSec < segLen - 0.02f);
                if (startSec > 0.02f && !trimmed)
                    Plugin.L.LogInfo("[CBRA] start trimmed, natural end: pose will be held naturally / 裁开头保留自然结尾");

                BreakClipTick.Track(ca, view, startSec, clipLen, uid, trimmed, segAsset, speed);

                // ---- 镜头 ----
                bool focused = false;
                try
                {
                    if (mgr != null && view != null)
                    {
                        var list = new Il2CppSystem.Collections.Generic.List<BattleUnitView>();
                        list.Add(view);
                        mgr.AddFocusingInRoundEnd(new ViewFocusInfo(list));
                        focused = true;
                    }
                }
                catch (Exception e) { Plugin.L.LogWarning("[CBRA] focusing failed: " + e.Message); }

                Plugin.L.LogInfo($"[CBRA] breakrecoveranim unit={uid} motion={motion}#{index} segLen={segLen:0.###} clip=[{startSec:0.###},{endSec:0.###}] len={clipLen:0.###} recovered={recovered} registered={registered} switched={forcedSwitch} trimmed={trimmed} speed={speed:0.##} muteVoice={muteVoice} muteSfx={muteSfx} muteFx={muteFx} skipRecover={skipRecover} focused={focused} / 混乱解除动画");
            }
            catch (Exception e) { Plugin.L.LogError("[CBRA] breakrecoveranim exception / 异常: " + e); }
        }

        /// <summary>
        /// 计算 timeline 的"真实内容结束时刻"：所有 clip 的最大结束时间，
        /// 但忽略镜头/语音/音效类轨道（它们的长度远大于动作本身）。
        /// </summary>
        /// <summary>
        /// 计算 timeline 的"真实动作长度"。
        ///
        /// 分两轮：
        ///   ① 只统计**动画轨**（AnimationTrack / 名字含 Animation）——这才是动作本身的长度；
        ///   ② 若没有动画轨，退回统计所有非 镜头/语音/音效 轨。
        ///
        /// 为什么要分轮：特效轨常比动作长（拖尾、余波），若把它算进去，
        /// 会让段长虚高（实测出现过 8.8 秒，实际动作只有 3 秒），
        /// 结果是动画播到空处、回合结束白等一大截。多单位同时使用时尤其明显
        /// （引擎取所有单位里最长的那个作为等待时间）。
        /// </summary>
        private static double ContentEnd(TimelineAsset ta)
        {
            double anim = 0, any = 0;
            try
            {
                var tracks = ta?.flattenedTracks;
                if (tracks == null) return 0;

                for (int i = 0; i < tracks.Length; i++)
                {
                    var tr = tracks[i];
                    if (tr == null) continue;

                    string nm = "";
                    try { nm = tr.name ?? ""; } catch { }

                    bool isCam = nm.IndexOf("CAM", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isVoice = nm.IndexOf("VOICE", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isAudio = nm.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isCam || isVoice || isAudio) continue;

                    bool isAnimation = tr is AnimationTrack
                                    || nm.IndexOf("ANIMATION", StringComparison.OrdinalIgnoreCase) >= 0;

                    double maxEnd = 0;
                    try
                    {
                        var clips = tr.GetClips();
                        if (clips != null)
                        {
                            var it = (System.Collections.IEnumerator)clips;
                            while (it.MoveNext())
                            {
                                var clip = it.Current as TimelineClip;
                                if (clip == null) continue;
                                double e = clip.end;
                                if (e > maxEnd) maxEnd = e;
                            }
                        }
                    }
                    catch { }

                    if (maxEnd > any) any = maxEnd;
                    if (isAnimation && maxEnd > anim) anim = maxEnd;
                }
            }
            catch { }

            Plugin.L.LogInfo($"[CBRA] content end: anim={anim:0.###} any={any:0.###} -> use={(anim > 0.01 ? anim : any):0.###}");
            return anim > 0.01 ? anim : any;
        }

        private static float FloatArg(string[] circles, int i, float def)
        {
            try
            {
                if (circles == null || i >= circles.Length) return def;
                var s = (circles[i] ?? "").Trim();
                if (s.Length == 0) return def;
                if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            }
            catch { }
            return def;
        }

        private static bool TryParseMotion(string spec, out MOTION_DETAIL motion, out int index)
        {
            motion = MOTION_DETAIL.S1;
            index = -1;
            if (string.IsNullOrEmpty(spec)) return false;

            var m = Regex.Match(spec.Trim(), @"^([A-Za-z]+)(?:[_]?)(\d+)?(?:#(\d*))?$");
            if (!m.Success) return false;

            string name = m.Groups[1].Value + (m.Groups[2].Success ? m.Groups[2].Value : "");
            try { motion = (MOTION_DETAIL)Enum.Parse(typeof(MOTION_DETAIL), name, true); }
            catch { return false; }

            if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int si)) index = si;
            return true;
        }
    }
}
