using System;
using HarmonyLib;
using ModularSkillScripts;
using ModularSkillScripts.Patches;

namespace CustomBreakRecoverAnimation
{
    /// <summary>
    /// 混乱（Break）恢复钩子。
    ///
    /// 【派发】强制解除路径（触发我们自建的 OnForceBreakRecover 时点）
    ///   · RecoverAllBreak                        ← Modular 的 breakrecover() 也走这里
    ///   · RecoverBreakExceptForced
    ///   · RecoverBreakExceptForcedIncludeNextTurn
    ///
    /// 【屏蔽】自然解除：RecoverBreakOnRoundStartExceptForced 期间不派发。
    /// 【防递归】我们自己的 breakrecoveranim 内部也会调用 RecoverAllBreak，
    ///           用 _inRecover 标记避免再次派发。
    /// </summary>
    [HarmonyPatch]
    public static class BreakPatches
    {
        private static int _naturalDepth;
        internal static bool _inRecover;   // our own recovery in progress -> skip reentrant dispatch

        // ---------------- 自然解除：屏蔽 ----------------
        [HarmonyPatch(typeof(BattleUnitModel), "RecoverBreakOnRoundStartExceptForced")]
        [HarmonyPrefix]
        public static void Pre_Natural(BattleUnitModel __instance)
        {
            _naturalDepth++;
            Probe(__instance, "natural-start");
        }

        [HarmonyPatch(typeof(BattleUnitModel), "RecoverBreakOnRoundStartExceptForced")]
        [HarmonyPostfix]
        public static void Post_Natural(BattleUnitModel __instance)
        {
            _naturalDepth = Math.Max(0, _naturalDepth - 1);
        }

        // ---------------- 强制解除：派发 ----------------
        [HarmonyPatch(typeof(BattleUnitModel), "RecoverAllBreak")]
        [HarmonyPostfix]
        public static void Post_RecoverAllBreak(BattleUnitModel __instance)
        {
            Dispatch(__instance, "RecoverAllBreak");
        }

        [HarmonyPatch(typeof(BattleUnitModel), "RecoverBreakExceptForced")]
        [HarmonyPostfix]
        public static void Post_ExceptForced(BattleUnitModel __instance)
        {
            Dispatch(__instance, "RecoverBreakExceptForced");
        }

        [HarmonyPatch(typeof(BattleUnitModel), "RecoverBreakExceptForcedIncludeNextTurn")]
        [HarmonyPostfix]
        public static void Post_ExceptForcedNextTurn(BattleUnitModel __instance)
        {
            Dispatch(__instance, "RecoverBreakExceptForcedIncludeNextTurn");
        }

        // ---------------- 探针 ----------------
        [HarmonyPatch(typeof(BattleUnitModel), "RecoverBreak")]
        [HarmonyPostfix]
        public static void Post_RecoverBreak(BattleUnitModel __instance)
        {
            Probe(__instance, "RecoverBreak");
        }

        private static int Uid(BattleUnitModel m)
        {
            try { return m == null ? -1 : m.GetUnitID(); } catch { return -1; }
        }

        private static void Probe(BattleUnitModel unit, string name)
        {
            try
            {
                bool brk = false; try { brk = unit.IsBreak(); } catch { }
                bool forced = false; try { forced = unit.IsForcelyBreak(); } catch { }
                Plugin.L.LogInfo($"[CBRA] probe {name} unit={Uid(unit)} isBreak={brk} isForced={forced}");
            }
            catch { }
        }

        private static void Dispatch(BattleUnitModel unit, string from)
        {
            try
            {
                if (unit == null) return;
                if (_inRecover) return;
                if (_naturalDepth > 0) return;

                int uid = Uid(unit);
                bool brk = false; try { brk = unit.IsBreak(); } catch { }
                Plugin.L.LogInfo($"[CBRA] {from} unit={uid} isBreak={brk} -> dispatch {Plugin.TimingName}");

                int fired = 0;
                try
                {
                    var list = unit._passiveDetail?._passivelist;
                    if (list != null)
                        for (int i = 0; i < list.Count; i++) fired += Run(list[i], unit);
                }
                catch (Exception e) { Plugin.L.LogWarning("[CBRA] passive list: " + e.Message); }

                try
                {
                    var ego = unit._passiveDetail?._egoPassiveList;
                    if (ego != null)
                        for (int i = 0; i < ego.Count; i++) fired += RunEgo(ego[i], unit);
                }
                catch (Exception e) { Plugin.L.LogWarning("[CBRA] ego list: " + e.Message); }

                if (fired == 0)
                    Plugin.L.LogInfo("[CBRA] no script bound to this timing");
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] dispatch failed: " + e.Message); }
        }

        private static int Run(PassiveModel pm, BattleUnitModel unit)
        {
            int n = 0;
            try
            {
                foreach (var sa in SkillScriptInitPatch.GetAllModpaFromPasmodel(pm))
                {
                    if (sa == null || sa.activationTiming != Plugin.TimingId) continue;
                    sa.modsa_passiveModel = pm;
                    sa.Enact(unit, null, null, null, Plugin.TimingId, (BATTLE_EVENT_TIMING)38);
                    n++;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] Enact(passive): " + e.Message); }
            return n;
        }

        private static int RunEgo(EgoPassiveModel pm, BattleUnitModel unit)
        {
            int n = 0;
            try
            {
                foreach (var sa in SkillScriptInitPatch.GetAllModpaFromPasmodel((PassiveModel)(object)pm, checkActive: false))
                {
                    if (sa == null || sa.activationTiming != Plugin.TimingId) continue;
                    sa.Enact(unit, null, null, null, Plugin.TimingId, (BATTLE_EVENT_TIMING)38);
                    n++;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[CBRA] Enact(ego): " + e.Message); }
            return n;
        }
    }
}
