using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 解混动画的播放控制（v1.1）。
    ///
    /// 相较 v1.0 的三处变化：
    ///   ① 结束判定改用 **director 的实际播放时间**（`d.time >= endSec`），
    ///      不再用墙钟计时 —— 引擎重置播放位置或改变时间缩放时，墙钟会失准
    ///      （表现为"设 0~1 秒却播了 2 秒"）。
    ///   ② 位置锁定从 **Track 时刻**就开始（原来要等引擎真正开始播放），
    ///      避免"等待播放"的那段被 root motion 带走。
    ///   ③ 检测到播放位置被重置（time 回到起点之前）会重新定位，并记录日志。
    /// </summary>
    public static class BreakClipTick
    {
        private sealed class Clip
        {
            public SD.CharacterAppearance CA;
            public BattleUnitView View;
            public float Start;
            public float EndSec;      // 画布时间轴上的绝对结束秒
            public int Uid;
            public Vector3 Pos;
            public Quaternion Rot;
            public bool Trimmed;
            public int LockMode;
            public bool MuteVoice;
            public bool MuteSfx;
            public bool Focus = true;
            public bool ResetPose;
            public float Speed = 1f;
            public bool FlipH;
            public double TrackedAt;   // Track 时刻，用于超时检测

            public bool Started;
            public double StartedAt;   // 墙钟，仅用于超时保护
            public int ReseekCount;
            public int DiagCount;
            public int LockFixes;   // 实际执行过多少次坐标纠正（>0 说明引擎真的移动过）
            public bool RootMotionDisabled;
            public TimelineAsset Asset;
            public Il2CppSystem.Action SavedRefresh;   // 原 refreshRoundEndActions 回调
            public bool RefreshSuspended;   // 由 consequence 传入（director.playableAsset 此刻可能还是 null）
            public readonly List<TrackAsset> OffsetSuppressed = new List<TrackAsset>();
            public readonly List<Vector3> SavedPos = new List<Vector3>();
            public readonly List<Quaternion> SavedRot = new List<Quaternion>();
            public readonly List<bool> SavedApply = new List<bool>();
            public readonly List<TrackOffset> SavedTrackOffset = new List<TrackOffset>();

            public readonly List<TrackAsset> MutedTracks = new List<TrackAsset>();
        }

        /// <summary>单个片段的最长时长上限（秒）。超过则截断，防止长时间占用原生镜头与回合流程。</summary>
        internal const float MaxClipSeconds = 30.0f;   // 仅作异常兜底，正常由回合推进信号提前收尾
        internal const float MaxFocusSeconds = 12.0f;   // 聚焦最长保持时间（超时兜底，正常由回合结束释放）
        internal const float NoPlaybackTimeout = 2.0f;  // 引擎迟迟未开始播放则放弃（防镜头悬空）

        private static readonly HashSet<int> _loggedTrackNames = new HashSet<int>();
        private static readonly List<Clip> _clips = new List<Clip>();
        private static readonly List<Clip> _holding = new List<Clip>();
        private static readonly List<BattleUnitView> _focusViews = new List<BattleUnitView>();
        private static readonly HashSet<int> _loggedTracks = new HashSet<int>();

        private static ViewFocusInfo _focusInfo;
        private static ViewFocusInfo _selectInfo;
        private static BattleUnitView _selectView;
        private static bool _selectActive;
        private static double _focusAddDue;
        private static double _focusStartAt;
        private static int _lastTickFrame = -1;   // 聚焦开始时刻（用于超时兜底）
        private static int _lastRound = -1;

        // ---------------- 注册播放 ----------------
        public static void Track(SD.CharacterAppearance ca, BattleUnitView view,
                                 float start, float len, int uid, bool trimmed = false,
                                 TimelineAsset asset = null, float speed = 1.0f)
        {
            try
            {
                if (ca == null) return;
                for (int i = _clips.Count - 1; i >= 0; i--)
                    if (_clips[i].CA == ca) _clips.RemoveAt(i);

                int lockMode = 0;
                bool muteVoice = false, muteSfx = false;
                bool focusNow = true;
                bool resetPose = false;
                bool flipH = false;
                var mode = Cq_AnimMode.Get(uid);
                if (mode != null)
                {
                    lockMode = mode.Lock;
                    muteVoice = mode.MuteVoice;
                    muteSfx = mode.MuteSfx;
                    focusNow = mode.Focus;
                    resetPose = mode.ResetPose;
                    flipH = mode.FlipH;
                }

                var pos = Vector3.zero;
                var rot = Quaternion.identity;
                bool flipRequested = flipH;
                try
                {
                    if (view != null)
                    {
                        pos = view.transform.position;
                        rot = view.transform.rotation;
                    }
                }
                catch { }

                // 水平反转：播放前先把单位转 180°，抵消动画自带的转身。
                // 锁定目标也用反转后的朝向，保证整段播放期间方向不变。
                if (flipRequested && view != null)
                {
                    try
                    {
                        rot = rot * Quaternion.Euler(0f, 180f, 0f);
                        view.transform.rotation = rot;
                        Plugin.L.LogInfo($"[CBRA] flipped 180 (horizontal): unit={uid} / 播放前水平反转");
                    }
                    catch (Exception ef) { Plugin.L.LogWarning("[CBRA] flip failed: " + ef.Message); }
                }

                var c = new Clip
                {
                    CA = ca,
                    View = view,
                    Start = start,
                    EndSec = start + Math.Min(len, MaxClipSeconds),
                    Uid = uid,
                    Pos = pos,
                    Rot = rot,
                    Trimmed = trimmed,
                    Asset = asset,
                    LockMode = lockMode,
                    MuteVoice = muteVoice,
                    MuteSfx = muteSfx,
                    Focus = focusNow,
                    ResetPose = resetPose,
                    Speed = speed,
                    FlipH = flipH,
                    TrackedAt = Now()
                };
                _clips.Add(c);

                // 镜头集合在"真正开始播放"时才加入（见下方），这里不再提前加入，
                // 避免引擎不播时镜头一直在两个单位之间来回切（画面抽搐）。

                if (_focusAddDue <= 0) _focusAddDue = Now() + 0.1;

                Plugin.L.LogInfo($"[CBRA] clip tracked: unit={uid} start={start:0.###} end={c.EndSec:0.###} trimmed={trimmed} lock={lockMode} focus={focusNow}");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] Track failed: " + e.Message); }
        }

        internal static bool IsHolding(SD.CharacterAppearance ca)
        {
            if (ca == null) return false;
            for (int i = 0; i < _holding.Count; i++) if (_holding[i].CA == ca) return true;
            for (int i = 0; i < _clips.Count; i++) if (_clips[i].CA == ca) return true;
            return false;
        }

        /// <summary>
        /// 最终阶段的坐标锁定：由 Camera.FireOnPreCull 调用。
        ///
        /// 为什么要这么晚：Unity 的 PlayableDirector 在 PreLateUpdate 阶段才更新，
        /// 比 BattleCamManager.LateUpdate 更晚 —— 在 LateUpdate 里锁的位置会被 timeline 覆盖，
        /// 所以必须在渲染前的最后一个时机再压一次。
        /// </summary>
        internal static void LateLockAll()
        {
            for (int i = 0; i < _clips.Count; i++)
            {
                try { LockPosition(_clips[i]); } catch { }
            }
            // 保持阶段（动画已播完、姿势保持中）也要继续锁定：
            // 引擎在该阶段仍会应用残余的位移/转身偏移，若不锁就会"播完还往前走"。
            for (int i = 0; i < _holding.Count; i++)
            {
                try { LockPosition(_holding[i]); } catch { }
            }
        }

        /// <summary>
        /// 强制清理所有状态（回合切换 / 异常时的保险）。
        ///
        /// 为什么需要：若我们的动画比其他人更长，回合会先切换；此时若镜头仍处于
        /// SelectFocus 状态，就会锁死原生镜头，并连带让原版的站位重置失效。
        /// 所以回合切换时必须无条件：停片段、还原回调/音频/位移抑制、释放镜头。
        /// </summary>
        /// <summary>是否仍有片段在播放或镜头被我们占用。</summary>
        internal static bool IsActive => (_clips.Count > 0 || _holding.Count > 0 || _selectActive || _focusInfo != null);

        internal static void ForceCleanupAll(string reason)
        {
            try
            {
                for (int i = 0; i < _clips.Count; i++) TeardownOne(_clips[i]);
                for (int i = 0; i < _holding.Count; i++) TeardownOne(_holding[i]);
                _clips.Clear();
                _holding.Clear();
                ReleaseFocusForce();
                Plugin.L.LogInfo($"[CBRA] force cleanup ({reason}) / 已强制清理（{reason}）");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] ForceCleanupAll: " + e.Message); }
        }

        private static void TeardownOne(Clip c)
        {
            // 最后一步矫正：把单位放回锚点。
            // 动画的位移烘焙在片段里，逐帧纠正只能"减少"视觉偏移；
            // 回合交接时强制回位，保证不会留在错误坐标上（用户看到的就是"播完位移了"）。
            try
            {
                if (c.View != null && c.LockMode != 3)
                {
                    var cur = c.View.transform.position;
                    bool lockH = (c.LockMode == 0 || c.LockMode == 1);
                    bool lockV = (c.LockMode == 0 || c.LockMode == 2);
                    var target = new Vector3(
                        lockH ? c.Pos.x : cur.x,
                        lockV ? c.Pos.y : cur.y,
                        lockH ? c.Pos.z : cur.z);
                    if ((cur - target).sqrMagnitude > 0.0001f)
                    {
                        c.View.transform.position = target;
                        Plugin.L.LogInfo($"[CBRA] snapped back: unit={c.Uid} ({cur.x:0.##},{cur.z:0.##}) -> ({target.x:0.##},{target.z:0.##}) / 已回位");
                    }
                    try { c.View.transform.rotation = c.Rot; } catch { }
                }
            }
            catch { }

            try { RestoreTrackOffsets(c); } catch { }
            try { RestoreAudioTracks(c); } catch { }
            try { if (c.RefreshSuspended) { c.View.refreshRoundEndActions = c.SavedRefresh; c.RefreshSuspended = false; } } catch { }
            try { if (c.RootMotionDisabled) { c.CA._anim.applyRootMotion = true; c.RootMotionDisabled = false; } } catch { }
            try { RestorePreMuted(c.Uid); } catch { }
            try
            {
                var dd = c.CA?._playableDirector;
                if (dd != null && Math.Abs(c.Speed - 1f) > 0.01f)
                {
                    var g2 = dd.playableGraph;
                    if (g2.IsValid()) g2.GetRootPlayable(0).SetSpeed(1.0);
                }
            }
            catch { }
            try { c.CA._playableDirector?.Stop(); } catch { }
        }

        private static void ReleaseFocusForce()
        {
            var cam = SingletonBehavior<BattleCamManager>.Instance;

            if (_selectActive)
            {
                try { cam?.SelectFocus(false, _selectInfo, _selectView); } catch { }
                _selectActive = false; _selectInfo = null; _selectView = null;
                Plugin.L.LogInfo("[CBRA] SelectFocus forced OFF / 强制释放镜头特写");
            }
            if (_focusInfo != null)
            {
                try { cam?.RemoveFocusingTarget(_focusInfo); } catch { }
                _focusInfo = null;
            }
            _focusViews.Clear();
            _focusAddDue = 0;
            _focusStartAt = 0;
        }

        private static double Now() => (double)Time.unscaledTime;

        private static float SafeTime(Clip c)
        {
            try { return (float)c.CA._playableDirector.time; } catch { return -1f; }
        }

        /// <summary>按锁定模式钉住位置：从 Track 起就一直生效，直到收尾。</summary>
        private static void LockPosition(Clip c)
        {
            if (c.View == null || c.LockMode == 3) return;
            try
            {
                var cur = c.View.transform.position;
                bool lockH = (c.LockMode == 0 || c.LockMode == 1);
                bool lockV = (c.LockMode == 0 || c.LockMode == 2);
                float nx = lockH ? c.Pos.x : cur.x;
                float ny = lockV ? c.Pos.y : cur.y;
                float nz = lockH ? c.Pos.z : cur.z;
                // 朝向也要固定：动作里常带转身，播完若不复位，单位会背对/侧对镜头
                try
                {
                    var curRot = c.View.transform.rotation;
                    if (Quaternion.Angle(curRot, c.Rot) > 0.5f)
                        c.View.transform.rotation = c.Rot;
                }
                catch { }

                if (Math.Abs(cur.x - nx) > 0.001f || Math.Abs(cur.y - ny) > 0.001f || Math.Abs(cur.z - nz) > 0.001f)
                {
                    c.View.transform.position = new Vector3(nx, ny, nz);
                    c.LockFixes++;
                    if (c.LockFixes <= 5)
                        Plugin.L.LogInfo($"[CBRA][lock] unit={c.Uid} corrected ({cur.x:0.###},{cur.y:0.###},{cur.z:0.###}) -> ({nx:0.###},{ny:0.###},{nz:0.###}) count={c.LockFixes}");
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(BattleCamManager), "LateUpdate")]
        [HarmonyPostfix]
        public static void Post_LateUpdate()
        {
            try
            {
                double now = Now();

                // 回合切换：无条件强制清理（防止镜头锁死 / 站位重置失效）
                int roundNow = -1;
                try { roundNow = Singleton<StageController>.Instance.GetCurrentRound(); } catch { }
                if (roundNow >= 0)
                {
                    if (_lastRound < 0) _lastRound = roundNow;
                    else if (roundNow != _lastRound)
                    {
                        _lastRound = roundNow;
                        if (_clips.Count > 0 || _holding.Count > 0 || _selectActive || _focusInfo != null)
                            ForceCleanupAll($"round {_lastRound}");
                    }
                }

                for (int i = _clips.Count - 1; i >= 0; i--)
                {
                    var c = _clips[i];
                    try
                    {
                        var d = c.CA._playableDirector;
                        if (d == null) { _clips.RemoveAt(i); continue; }

                        // ---- 未开始播放：先钉住位置，避免等待期间被 root motion 带走 ----
                        if (!c.Started)
                        {
                            // 引擎一直没开始播（例如在 EncounterStart 这类时点使用本系统）：
                            // 说明当前流程没有"回合结束动作"通道，永远不会播。
                            // 必须在超时后放弃，否则镜头/锁定会一直悬空，表现为画面抽搐。
                            if (now - c.TrackedAt > NoPlaybackTimeout)
                            {
                                Plugin.L.LogWarning($"[CBRA] playback never started (timeout {NoPlaybackTimeout:0.#}s) -> aborted: unit={c.Uid} / 引擎未播放，已放弃");
                                TeardownOne(c);
                                _clips.RemoveAt(i);
                                if (_clips.Count == 0 && _holding.Count == 0) ReleaseFocusForce();
                                continue;
                            }

                            LockPosition(c);
                            if (d.state != PlayState.Playing) continue;

                            // 无条件定位到起始秒：director 的 time 会保留上一次播放的位置，
                            // 若这里不重置，第二次播放会从上次的位置开始（表现为"一次对齐一次不对齐"）。
                            try { d.time = c.Start; } catch { }
                            c.Started = true;
                            c.StartedAt = now;

                            try
                            {
                                c.View._reservedRoundEndActionMotion = MOTION_DETAIL.Empty;
                                c.View._skipNextRefreshMotionForReservedRoundEndAction = true;
                            }
                            catch { }

                            // 关键：挂起引擎的"回合结束动作刷新"回调。
                            // 单位在回合结束流程里会被引擎刷新动作（表现就是姿态被重置）；
                            // 把该 view 的 refreshRoundEndActions 置空，引擎就无从刷新，末帧得以保持。
                            try
                            {
                                if (c.ResetPose)
                                {
                                    Plugin.L.LogInfo($"[CBRA] pose reset requested (animmode var6=1): unit={c.Uid}, keep engine refresh / 交给引擎刷新姿态");
                                    throw new InvalidOperationException("skip");
                                }

                                var act = c.View.refreshRoundEndActions;
                                if (act != null)
                                {
                                    // 关键顺序：先**主动调用一次**刷新，让引擎把混乱 UI / 状态图标更新掉，
                                    // 再挂起它。若直接挂起，UI 会停留在"混乱"状态（实测 10912/10313 中招），
                                    // 因为清除混乱后的界面刷新本来就走这个回调。
                                    try
                                    {
                                        act.Invoke();
                                        Plugin.L.LogInfo($"[CBRA] refresh callback invoked once (UI refresh): unit={c.Uid} / 已主动刷新一次界面状态");
                                    }
                                    catch (Exception ei) { Plugin.L.LogWarning("[CBRA] invoke refresh failed: " + ei.Message); }

                                    c.SavedRefresh = act;
                                    c.View.refreshRoundEndActions = null;
                                    c.RefreshSuspended = true;
                                    Plugin.L.LogInfo($"[CBRA] refresh callback suspended: unit={c.Uid} / 已挂起姿态刷新回调");
                                }
                                else
                                {
                                    Plugin.L.LogInfo($"[CBRA] refresh callback already null: unit={c.Uid}");
                                }
                            }
                            catch (InvalidOperationException) { /* 按配置交给引擎刷新 */ }
                            catch (Exception er) { Plugin.L.LogWarning("[CBRA] suspend refresh failed: " + er.Message); }

                            // 源头抑制（主）：把动画轨的 Track Offsets 归零。
                            // Timeline 的位移不是走 Animator root motion，而是由 AnimationTrack 的
                            // position/rotation/applyOffsets 直接写入 transform —— 必须在轨上关掉。
                            SuppressTrackOffsets(c);

                            // 源头抑制（备）：关闭 Animator 的 root motion
                            try
                            {
                                var anim = c.CA._anim;
                                if (anim != null && anim.applyRootMotion)
                                {
                                    anim.applyRootMotion = false;
                                    c.RootMotionDisabled = true;
                                }
                                Plugin.L.LogInfo($"[CBRA] root motion disabled: unit={c.Uid} (was on={c.RootMotionDisabled}) / 已关闭 root motion");
                            }
                            catch (Exception er) { Plugin.L.LogWarning("[CBRA] disable root motion failed: " + er.Message); }

                            // 倍速：改 root playable 的 timeScale。director.time 仍按时间轴推进，
                            // 所以结束判定（t >= EndSec）不受影响，只是墙钟时间按倍率缩放。
                            if (Math.Abs(c.Speed - 1f) > 0.01f)
                            {
                                try
                                {
                                    var gph = d.playableGraph;
                                    if (gph.IsValid())
                                    {
                                        var root = gph.GetRootPlayable(0);
                                        root.SetSpeed(c.Speed);
                                        Plugin.L.LogInfo($"[CBRA] speed applied: unit={c.Uid} x{c.Speed:0.##} / 已设置倍速");
                                    }
                                }
                                catch (Exception es) { Plugin.L.LogWarning("[CBRA] apply speed failed: " + es.Message); }
                            }

                            // 锚点在"真正开始播放"时重新采样：
                            // 若在入场阶段就记录，会拿到尚未排好的站位，锁定后会把单位拉到错误位置。
                            try
                            {
                                if (c.View != null && !c.FlipH)
                                {
                                    c.Pos = c.View.transform.position;
                                    c.Rot = c.View.transform.rotation;
                                }
                            }
                            catch { }

                            // 此刻才把该单位纳入镜头候选（避免"引擎不播"时镜头来回切）
                            try
                            {
                                if (c.View != null && c.Focus && !_focusViews.Contains(c.View))
                                {
                                    _focusViews.Add(c.View);
                                    if (_focusAddDue <= 0) _focusAddDue = now + 0.1;
                                }
                            }
                            catch { }

                            Plugin.L.LogInfo($"[CBRA] clip started: unit={c.Uid} start={c.Start:0.###} end={c.EndSec:0.###} speed={c.Speed:0.##} / 引擎已开始播放");
                            LogTrackList(c);
                            if (c.MuteVoice || c.MuteSfx) MuteAudioTracks(c);
                            continue;
                        }

                        // ---- 播放中：钉位置与朝向 + 按"播放位置"判定结束 ----
                        LockPosition(c);

                        // 诊断：确认到底是哪个 Transform 在动（每个片段最多 8 次）
                        try
                        {
                            float diagStep = Math.Max(0.05f, (c.EndSec - c.Start) / 12f);
                            float dt = SafeTime(c);
                            if (c.DiagCount < 12 && dt >= c.Start + c.DiagCount * diagStep)
                            {
                                c.DiagCount++;
                                var vp = c.View != null ? c.View.transform.position : Vector3.zero;
                                var ap = c.CA.transform.position;
                                var al = c.CA.transform.localPosition;
                                float childY = 0f, childX = 0f, childZ = 0f;
                                int childN = 0;
                                try
                                {
                                    var ct = c.CA.transform;
                                    if (ct.childCount > 0)
                                    {
                                        var ch = ct.GetChild(0);
                                        var lp = ch.localPosition;
                                        childX = lp.x; childY = lp.y; childZ = lp.z;
                                        childN = ct.childCount;
                                    }
                                }
                                catch { }
                                var wp = Vector3.zero;
                                int grandN = 0;
                                try
                                {
                                    var ct2 = c.CA.transform;
                                    if (ct2.childCount > 0)
                                    {
                                        var ch2 = ct2.GetChild(0);
                                        wp = ch2.position;
                                        grandN = ch2.childCount;
                                    }
                                }
                                catch { }
                                Plugin.L.LogInfo($"[CBRA][pos] unit={c.Uid} t={(float)SafeTime(c):0.###} view=({vp.x:0.###},{vp.y:0.###},{vp.z:0.###}) app=({ap.x:0.###},{ap.y:0.###},{ap.z:0.###}) child0World=({wp.x:0.###},{wp.y:0.###},{wp.z:0.###}) child0Local=({childX:0.###},{childY:0.###},{childZ:0.###}) children={childN}/{grandN}");
                            }
                        }
                        catch { }

                        float t = 0f;
                        try { t = (float)d.time; } catch { }

                        // 位置异常（被重置或残留上次位置）：重新定位到起始秒
                        bool tooEarly = t < c.Start - 0.05f;
                        bool jumpedPast = (t > c.EndSec + 0.05f) && !c.Trimmed;
                        if ((tooEarly || jumpedPast) && c.ReseekCount < 5)
                        {
                            try { d.time = c.Start; } catch { }
                            c.ReseekCount++;
                            Plugin.L.LogInfo($"[CBRA] playback position reset -> re-seek to {c.Start:0.###}: unit={c.Uid} reseek={c.ReseekCount} / 播放位置被重置，已重新定位");
                            continue;
                        }

                        bool finished = (t >= c.EndSec - 0.01f);
                        // 超时保护：墙钟远超预期（例如时间缩放极小）时强制收尾
                        bool timeout = (now - c.StartedAt) > Math.Max(3.0, (c.EndSec - c.Start) * 4.0) + 2.0;
                        if (!finished && !timeout) continue;

                        // ---- 收尾 ----
                        string why = finished ? "reached" : "timeout";
                        if (c.Trimmed)
                        {
                            // 裁结尾：只能截断。
                            // 说明：PlayableDirector.Stop() 会让外观复位，所以裁结尾后姿势必然是闲置，
                            // 无法保持裁切点的姿势（已实测多种补救均无效）。需要保持姿势请用自然结尾。
                            try { d.Stop(); } catch { }
                            RestoreAudioTracks(c);
                            Plugin.L.LogInfo($"[CBRA] clip end (trimmed): unit={c.Uid} at {t:0.###}s ({why}) lockFixes={c.LockFixes} / 裁剪收尾（姿势回闲置）");
                        }
                        else
                        {
                            RestoreAudioTracks(c);
                            Plugin.L.LogInfo($"[CBRA] clip end (natural): unit={c.Uid} at {t:0.###}s ({why}) lockFixes={c.LockFixes} / 自然播完");
                        }

                        RestoreTrackOffsets(c);
                        if (c.RefreshSuspended)
                        {
                            try { c.View.refreshRoundEndActions = c.SavedRefresh; } catch { }
                            c.RefreshSuspended = false;
                        }
                        if (c.RootMotionDisabled)
                        {
                            try { c.CA._anim.applyRootMotion = true; } catch { }
                            c.RootMotionDisabled = false;
                        }

                        _holding.Add(c);
                        _clips.RemoveAt(i);
                    }
                    catch { }
                }

                // ---- 保持状态：回合切换已在上方 ForceCleanupAll 统一处理 ----
                if (false)
                {
                    for (int i = 0; i < _holding.Count; i++)
                    {
                        var c0 = _holding[i];
                        try { RestoreAudioTracks(c0); } catch { }
                        try { c0.CA._playableDirector?.Stop(); } catch { }
                        if (c0.RefreshSuspended)
                        {
                            try { c0.View.refreshRoundEndActions = c0.SavedRefresh; } catch { }
                            c0.RefreshSuspended = false;
                        }
                        Plugin.L.LogInfo($"[CBRA] hold cleared at new round: unit={c0.Uid} / 新回合清理（回调已恢复）");
                    }
                    _holding.Clear();
                    _focusViews.Clear();
                }

                // ---- 镜头 ----
                if (_focusInfo == null && !_selectActive && _focusViews.Count > 0 && now >= _focusAddDue)
                {
                    bool wantFocus = false;
                    for (int i = 0; i < _clips.Count; i++) if (_clips[i].Focus) wantFocus = true;
                    for (int i = 0; i < _holding.Count; i++) if (_holding[i].Focus) wantFocus = true;

                    if (wantFocus)
                    {
                        var list = new Il2CppSystem.Collections.Generic.List<BattleUnitView>();
                        for (int i = 0; i < _focusViews.Count; i++)
                            if (_focusViews[i] != null) list.Add(_focusViews[i]);

                        if (list.Count > 0)
                        {
                            var cam = SingletonBehavior<BattleCamManager>.Instance;
                            if (cam != null)
                            {
                                try
                                {
                                    var info = new ViewFocusInfo(list);

                                    _selectInfo = info;
                                    _selectView = list[0];
                                    cam.SelectFocus(true, info, list[0]);
                                    _selectActive = true;
                                    _focusStartAt = now;
                                    Plugin.L.LogInfo("[CBRA] SelectFocus ON / 镜头特写开始（保持到回合结束）");
                                }
                                catch (Exception e1) { Plugin.L.LogWarning("[CBRA] SelectFocus failed: " + e1.Message); }

                                try
                                {
                                    var info2 = new ViewFocusInfo(list);
                                    _focusInfo = info2;
                                    cam.AddFocusingTarget(info2);
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        _focusAddDue = 0;
                    }
                }

                // 镜头保持到回合结束：不再因为"片段播完"就释放（否则动画结束后镜头会跳回原版，
                // 而单位这时还保持着末帧姿势，观感割裂）。
                // 释放时机：回合结束信号（RoundEndGuard）/ 回合切换 / 下面的超时兜底。
                if (_selectActive && _focusStartAt > 0 && (now - _focusStartAt) > MaxFocusSeconds)
                {
                    Plugin.L.LogWarning($"[CBRA] focus timeout ({MaxFocusSeconds:0.#}s) -> force release / 聚焦超时，强制释放");
                    ReleaseFocusForce();
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] tick: " + e.Message); }
        }

        // ---------------- 音频静音 ----------------
        private static void MuteAudioTracks(Clip c)
        {
            try
            {
                var ta = c.Asset;
                if (ta == null) { try { ta = c.CA?._playableDirector?.playableAsset as TimelineAsset; } catch { } }
                if (ta == null) return;
                var tracks = ta.flattenedTracks;
                if (tracks == null) return;

                int v = 0, s = 0;
                for (int i = 0; i < tracks.Length; i++)
                {
                    var tr = tracks[i];
                    if (tr == null) continue;
                    string tn = "";
                    try { tn = tr.name ?? ""; } catch { }
                    bool namedAudio = tn.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0
                                   || tn.IndexOf("AUDIO", StringComparison.OrdinalIgnoreCase) >= 0
                                   || tn.IndexOf("VOICE", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!namedAudio) continue;
                    bool isVoice = tn.IndexOf("VOICE", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isVoice ? !c.MuteVoice : !c.MuteSfx) continue;
                    try
                    {
                        if (!tr.muted) { tr.muted = true; c.MutedTracks.Add(tr); if (isVoice) v++; else s++; }
                    }
                    catch { }
                }
                Plugin.L.LogInfo($"[CBRA] audio tracks muted (late): voice={v} sfx={s} / 播放后补静音");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] MuteAudioTracks: " + e.Message); }
        }

        private static void RestoreAudioTracks(Clip c)
        {
            for (int i = 0; i < c.MutedTracks.Count; i++)
                try { c.MutedTracks[i].muted = false; } catch { }
            c.MutedTracks.Clear();
        }

        // ---------------- 播放前静音（音频 + 可选特效）----------------
        private static readonly Dictionary<int, List<TrackAsset>> _preMuted = new Dictionary<int, List<TrackAsset>>();

        /// <summary>
        /// 在引擎播放**之前**静音指定类别的轨道。
        /// 为什么必须提前：FMOD/特效事件是在播放到该点时触发的，播放后再静音/关轨已经晚了。
        /// muteFx = true 时同时静音特效轨（轨道名含 Effect/Fx），即"关闭技能动作携带的所有 FX"。
        /// </summary>
        internal static void PreMuteOnAsset(TimelineAsset ta, int uid, bool muteVoice, bool muteSfx, bool muteFx)
        {
            try
            {
                if (ta == null) return;
                var tracks = ta.flattenedTracks;
                if (tracks == null) return;

                var list = new List<TrackAsset>();
                int v = 0, s = 0, f = 0;
                for (int i = 0; i < tracks.Length; i++)
                {
                    var tr = tracks[i];
                    if (tr == null) continue;
                    string tn = "";
                    try { tn = tr.name ?? ""; } catch { }

                    bool isVoice = tn.IndexOf("VOICE", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isAudio = tn.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0
                                || tn.IndexOf("AUDIO", StringComparison.OrdinalIgnoreCase) >= 0
                                || isVoice;
                    bool isFx = tn.IndexOf("EFFECT", StringComparison.OrdinalIgnoreCase) >= 0
                             || tn.IndexOf("FX", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool want = false;
                    if (isAudio) want = isVoice ? muteVoice : muteSfx;
                    else if (isFx) want = muteFx;
                    if (!want) continue;

                    try
                    {
                        if (!tr.muted)
                        {
                            tr.muted = true;
                            list.Add(tr);
                            if (isAudio) { if (isVoice) v++; else s++; } else f++;
                        }
                    }
                    catch { }
                }
                if (list.Count > 0) _preMuted[uid] = list;
                Plugin.L.LogInfo($"[CBRA] pre-muted (before play): unit={uid} voice={v} sfx={s} fx={f} / 播放前静音");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] PreMuteOnAsset: " + e.Message); }
        }

        internal static void RestorePreMuted(int uid)
        {
            try
            {
                if (!_preMuted.TryGetValue(uid, out var list)) return;
                for (int i = 0; i < list.Count; i++)
                    try { list[i].muted = false; } catch { }
                _preMuted.Remove(uid);
            }
            catch { }
        }

        // ---------------- 诊断 ----------------
        private static void LogTrackList(Clip c)
        {
            try
            {
                if (!_loggedTracks.Add(c.Uid)) return;
                var ta = c.Asset;
                if (ta == null) { try { ta = c.CA?._playableDirector?.playableAsset as TimelineAsset; } catch { } }
                if (ta == null) { Plugin.L.LogInfo($"[CBRA][tracks] unit={c.Uid} (no TimelineAsset)"); return; }
                var tracks = ta.flattenedTracks;
                if (tracks == null) return;

                var sb = "";
                for (int i = 0; i < tracks.Length; i++)
                {
                    var tr = tracks[i];
                    if (tr == null) continue;
                    string nm = ""; try { nm = tr.name ?? ""; } catch { }
                    sb += "[" + nm + "] ";
                }
                Plugin.L.LogInfo($"[CBRA][tracks] unit={c.Uid} total={tracks.Length}: {sb}");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] LogTrackList: " + e.Message); }
        }

        /// <summary>
        /// 把动画轨的位移偏移压制为 0（播放期间）。
        /// Timeline 的位移来自 AnimationTrack.position / rotation / applyOffsets，
        /// 直接写入被绑定的 transform，因此改 Animator.applyRootMotion 无效。
        /// </summary>
        private static void SuppressTrackOffsets(Clip c)
        {
            try
            {
                Plugin.L.LogInfo("[CBRA] enter suppress / 进入位移抑制");
                var ta = c.Asset;
                if (ta == null)
                {
                    try { ta = c.CA?._playableDirector?.playableAsset as TimelineAsset; } catch { }
                }
                if (ta == null) { Plugin.L.LogWarning("[CBRA] suppress: no TimelineAsset"); return; }
                var tracks = ta.flattenedTracks;
                if (tracks == null) { Plugin.L.LogWarning("[CBRA] suppress: no tracks"); return; }

                int n = 0;
                for (int i = 0; i < tracks.Length; i++)
                {
                    var at = tracks[i].TryCast<AnimationTrack>();
                    if (at == null) continue;
                    try
                    {
                        c.OffsetSuppressed.Add(at);
                        c.SavedPos.Add(at.position);
                        c.SavedRot.Add(at.rotation);
                        c.SavedApply.Add(at.applyOffsets);
                        c.SavedTrackOffset.Add(at.trackOffset);

                        at.position = Vector3.zero;
                        at.rotation = Quaternion.identity;
                        at.applyOffsets = false;
                        n++;
                    }
                    catch { }
                }
                if (_loggedTrackNames.Add(c.Uid))
                {
                    var sb = "";
                    for (int i = 0; i < tracks.Length; i++)
                    {
                        var tr = tracks[i];
                        if (tr == null) continue;
                        string nm = ""; try { nm = tr.name ?? ""; } catch { }
                        bool isAnim = tr.TryCast<AnimationTrack>() != null;
                        sb += $"[{nm}{(isAnim ? "*" : "")}] ";
                    }
                    Plugin.L.LogInfo($"[CBRA][tracknames] unit={c.Uid} total={tracks.Length}: {sb}");
                }

                Plugin.L.LogInfo($"[CBRA] track offsets suppressed: unit={c.Uid} animationTracks={n} / 已归零动画轨位移");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] SuppressTrackOffsets: " + e.Message); }
        }

        private static void RestoreTrackOffsets(Clip c)
        {
            try
            {
                for (int i = 0; i < c.OffsetSuppressed.Count; i++)
                {
                    var at = c.OffsetSuppressed[i] as AnimationTrack;
                    if (at == null) continue;
                    try { at.position = c.SavedPos[i]; } catch { }
                    try { at.rotation = c.SavedRot[i]; } catch { }
                    try { at.applyOffsets = c.SavedApply[i]; } catch { }
                    try { at.trackOffset = c.SavedTrackOffset[i]; } catch { }
                }
            }
            catch { }
            c.OffsetSuppressed.Clear();
            c.SavedPos.Clear(); c.SavedRot.Clear(); c.SavedApply.Clear(); c.SavedTrackOffset.Clear();
        }

        private static bool RoundChanged()
        {
            int r = -1;
            try { r = Singleton<StageController>.Instance.GetCurrentRound(); } catch { }
            if (r < 0) return false;
            if (_lastRound < 0) { _lastRound = r; return false; }
            if (r != _lastRound) { _lastRound = r; return true; }
            return false;
        }
    }
}
