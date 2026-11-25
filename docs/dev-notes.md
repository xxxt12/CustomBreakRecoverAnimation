# 自定义混乱解除动画 —— 开发知识库

> **作者**：希希希土（Xt）　|　**版本**：1.0
> **用途**：把「自定义混乱（Break）解除动画」从需求做到成品的完整技术记录。
> 包含：引擎机制、Modular 集成方式、以及**一长串失败尝试与它们的确切原因**。
> 写给后来者——尤其是想给 Limbus Company 做类似「借用引擎演出通道」插件的开发者。

---

## 目录

1. [需求与最终形态](#1-需求与最终形态)
2. [引擎机制：混乱与解除](#2-引擎机制混乱与解除)
3. [Modular 集成：自建时点与函数](#3-modular-集成自建时点与函数)
4. [播放方案：为什么必须让引擎播](#4-播放方案为什么必须让引擎播)
5. [裁剪：节选与"自然结束"的冲突](#5-裁剪节选与自然结束的冲突)
6. [混乱姿态回退：根因与解法](#6-混乱姿态回退根因与解法)
7. [音频静音：IL2CPP 的两个陷阱](#7-音频静音il2cpp-的两个陷阱)
8. [镜头聚焦：SelectFocus 与 FOV](#8-镜头聚焦selectfocus-与-fov)
9. [失败清单（重点）](#9-失败清单重点)
10. [无法解决的问题：特效残留](#10-无法解决的问题特效残留)
11. [调试方法论](#11-调试方法论)
12. [给后来者的建议](#12-给后来者的建议)

---

## 1. 需求与最终形态

### 需求

让任意单位（含自制人格）在**强制解除混乱**时，播放**该单位自己的技能动作片段**（可裁剪起止），并有三项可调：位置锁定、音频静音、镜头聚焦。

**只用 JSON**，写在单位的被动里，不写代码。

### 最终用法

```json
Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,1,0,2.0):breakrecoveranim(S3#0,0,1.5)/
```

读法：

```
EndBattle                      回合结束时
VALUE_0:getunitstate(Self)     读取自身状态（2 = 陷入混乱）
IF(VALUE_0=2)                  只有混乱才执行
animmode(0,1,0,2.0)            锁定全轴 / 关语音 / 保留音效 / 镜头 2 倍
breakrecoveranim(S3#0,0,1.5)   播放 S3#0 的 0~1.5 秒，并解除混乱
```

### 最终能力表

| 能力 | 状态 |
|---|---|
| 双端裁剪（裁开头 / 裁结尾） | ✅ |
| 裁开头时末姿势自然保持 | ✅ |
| 动画开始即真正解除混乱 | ✅ |
| 位置锁定四档 | ✅ |
| 语音 / 音效分别静音 | ✅ |
| 镜头特写（可调倍率） | ✅ |
| 特效残留自动清理 | ❌（引擎限制，见 §10）|

---

## 2. 引擎机制：混乱与解除

### 状态查询

```csharp
BattleUnitModel.IsBreak()          // 是否处于混乱
BattleUnitModel.IsForcelyBreak()   // 是否是"强制混乱"（外力施加，普通手段解除不掉）
```

Modular 已封装：

```
getunitstate(Self) → 2 = 混乱 / 1 = 正常 / 0 = 死亡
```

### 解除路径（关键区分）

| 引擎方法 | 语义 | 我们是否接管 |
|---|---|---|
| `RecoverAllBreak(timing)` | **强制解除**（Modular 的 `breakrecover()` 也走这里）| ✅ 派发 |
| `RecoverBreakExceptForced()` | 被动发动的强制解除 | ✅ 派发 |
| `RecoverBreakExceptForcedIncludeNextTurn()` | 同上（含"下回合混乱"标记）| ✅ 派发 |
| `RecoverBreakOnRoundStartExceptForced()` | **自然解除**（等一整个回合后）| ❌ 屏蔽 |
| `RecoverBreak(timing)` | 通用恢复 | 仅记录探针 |

> **认知纠正**：中文社区的"混乱"对应引擎的 **Break**，不是 **Panic**。
> Panic 是另一套（精神崩溃），`RecoverPanicState` 与本功能无关。我最初走错这条线，浪费了一轮。

### 自然解除 vs 强制解除

- **自然解除**：第 1 回合被打到混乱 → 第 2 回合保持混乱 → **第 3 回合开始**才恢复
- **强制解除**：拥有相关被动的单位，在**回合结束时**立即恢复 + 播放动画

原版拥有此类被动的人格（实测）：`1011504`（10115）、`1111513`（11115）、`1061405`（10614）、`1091602`（10916）。

---

## 3. Modular 集成：自建时点与函数

### 时点是怎么工作的

Modular 的 `MainClass.timingDict` 是 `Dictionary<string,int>`，脚本字符串 `Modular/TIMING:<名字>/...` 会被解析成 `activationTiming` 整数。数值对应游戏 `TIMING` 枚举（0~92）。

**原版没有"解除混乱"时点**，所以自建：

```csharp
MainClass.timingDict["OnForceBreakRecover"] = 5002;   // 避开 0..92
```

### 派发方式（照抄 Modular 自己的实现）

```csharp
foreach (var sa in SkillScriptInitPatch.GetAllModpaFromPasmodel(passive))
    if (sa.activationTiming == 5002)
        sa.Enact(unit, null, null, null, 5002, (BATTLE_EVENT_TIMING)38);
```

要点：

- `GetAllModpaFromPasmodel` 与 `ModularSA.Enact` 都是 **public**
- `BATTLE_EVENT_TIMING` 传 38（与 Modular 内部 `IgnorePanic` 派发一致）
- 需要 `using ModularSkillScripts.Patches;`（`SkillScriptInitPatch` 在 `.Patches` 命名空间）

### 递归保护（必须）

我们的函数内部会调用 `RecoverAllBreak()`，而它的后置补丁又会派发时点 → 递归。

```csharp
internal static bool _inRecover;   // 派发前检查，调用前置位
```

### 函数注册

```csharp
MainClass.consequenceDict["breakrecoveranim"] = new Cq_BreakRecoverAnim();
MainClass.consequenceDict["animmode"] = new Cq_AnimMode();
```

补丁注册要**每个类单独 try/catch**：

```csharp
PatchOne(h, typeof(BreakPatches), "BreakPatches");
```

> 因为一个类里若有一个方法的 Harmony 匹配有歧义（例如有重载），**整个类都会挂掉**。
> 我踩过：`PlayTurnTransition` 有两个重载 → `Ambiguous match` → 同类的 `ResolveEffectiveTurnFadeOutDuration` 补丁也没挂上，整版功能静默失效。

---

## 4. 播放方案：为什么必须让引擎播

### 唯一正确做法

```csharp
view.ReserveRoundEndAction(motion);   // 只登记，不主动播放
```

引擎随后在回合结束调用 `PlayReservedRoundEndAction()` → **只播一次**，并且它 "播放中" 的状态能让引擎自己做收尾。

我们只接管 `PlayableDirector`：定位起始秒、计时、裁剪。

### 为什么不能自己播

自己播一次 + 引擎再播一次 = **同一份 timeline 播两遍**，表现为**特效重复**。

这是本项目最顽固的 bug，前后复现过 4 次，每次都以为是别的原因。真正原因有两层：

1. 同时调用 `ReserveRoundEndAction` + `OnRoundEndAction` + `ChangeMotion` → 三次启动
2. 只调用 `OnRoundEndAction` 但引擎稍后又执行 `PlayReservedRoundEndAction` → 两次启动

> **只写 `_reservedRoundEndActionMotion` 字段是无效的**：它只是数据，
> 登记动作在 `ReserveRoundEndAction()` 方法体内（会把这个 view 加入待播放列表）。
> 只写字段 → 引擎遍历列表时找不到你 → 完全没动画。

---

## 5. 裁剪：节选与"自然结束"的冲突

### 核心事实

**timeline 只有"自然播完"时才会自己收尾**（回收特效、保留末姿势）。

一旦中途 `Stop()`：

- 特效不回收 → 残留
- 外观回退到**上一份动作**（若当时是混乱状态，就是 `Damaged` 混乱姿势）

### 因此双端裁剪的效果不对称

| 写法 | 结束方式 | 结束姿势 | 残留 |
|---|---|---|---|
| `(S3#0)` 整段 | 自然播完 | 末帧保持 | 无 |
| `(S3#0,1.2)` 裁开头 | 自然播完 | 末帧保持 | 无 |
| `(S3#0,0,1.5)` 裁结尾 | `Stop` | 闲置 | 可能残留 |
| `(S3#0,1.2,2.4)` | `Stop` | 闲置 | 可能残留 |

### 结论

- **裁开头是"免费"的**：既节选又不破坏收尾
- **裁结尾有代价**：能得到正确时长，但姿势与特效都不完美

> 实践建议：需要节选时，优先用「裁开头」。
> 如果多余部分在尾部，用动作槽扫描工具找一个**尾段本来就短**的段位，比裁结尾更干净。

---

## 6. 混乱姿态回退：根因与解法

### 现象

裁剪播放 `Stop()` 后，单位变成**混乱姿势**（蜷缩/倒地），而不是闲置。

### 根因

**外观层此刻仍认为单位处于混乱状态**。`Stop()` 会让它回退到"进入混乱时的那份动作"（`Damaged`）。

### 解法：动画开始即真正解除混乱

```csharp
if (unit.IsBreak()) unit.RecoverAllBreak(...);   // 引擎状态
ca.OnRecoverBreak(true);                          // 外观层（关键）
```

`OnRecoverBreak` 是探针发现的：原版被动在回合结束解除混乱时，都会调用它。

### 为什么提前解除是安全的

播放动画到回合结束的这段时间单位不会受到伤害，提前解除不影响实际战斗结果。

### 附带收益

- 不再需要隐藏混乱 UI（状态已解除，UI 自然不显示）
- 裁剪收尾后回退的是**闲置**而不是混乱姿势

---

## 7. 音频静音：IL2CPP 的两个陷阱

### 需求

只静音**人物语音**（`FMOD Custom Track_Voice`），保留**动作音效**（`FMOD Custom Track`）。

### 陷阱一：类型名不可用

```csharp
tr.GetType().Name   // → 对所有 timeline 轨道都返回 "TrackAsset"
```

IL2CPP 下拿不到真实子类名。**只能按轨道名字识别**：

```csharp
bool isVoice = name.Contains("Voice");
bool isAudio = name.Contains("FMOD") || name.Contains("Audio");
```

### 陷阱二：静音时机必须早于播放

FMOD 事件是在 timeline **播放到那个点时**触发的。播放开始后再静音，事件已经发出去了。

**正确做法**：在登记播放之前，从动作槽拿到同一份资产并提前静音：

```csharp
var assets = ca.GetMotion(motion, false).timelineAssets;
TimelineAsset segAsset = assets[index];          // 引擎将要播的就是这份
segAsset.flattenedTracks[i].muted = true;        // 资产是共享对象，引擎播放时读到静音状态
```

### 关键陷阱：`playableAsset` 在播放前是 null

```csharp
d.playableAsset as TimelineAsset   // 登记时 → null！引擎还没挂上
```

必须用 `GetMotion().timelineAssets` 取资产，不能用 director。

### 陷阱三：`GetClips()` 不能直接 foreach

```csharp
var clips = track.GetClips();                // IEnumerable<TimelineClip>
foreach (var c in clips)                     // ❌ 编译错误：IL2CPP 枚举器不兼容
var it = (System.Collections.IEnumerator)clips;   // ✅
while (it.MoveNext()) { ... }
```

---

## 8. 镜头聚焦：SelectFocus 与 FOV

### 错误直觉：AddFocusingTarget 不是"推近"

```csharp
cam.AddFocusingTarget(info);   // 这是"把这些单位都框进来"（取景范围）
```

所以填任何"倍率"都不会让镜头靠近。

### 正确通道：SelectFocus

```csharp
cam.SelectFocus(bool active, ViewFocusInfo info, BattleUnitView view);
```

配套字段（都在 `BattleCamManager` 里）：

```
_selectFocusMinFov / _selectFocusMaxFov / _selectFocusZoomSmoothTime
_selectFocusTargetHeightViewportRatio / _selectFocusInSmoothTime
```

**FOV 才是"推近"的实现**。

### 用法

```csharp
// 动画开始
cam.SelectFocus(true, info, view);
// 动画结束
cam.SelectFocus(false, info, view);
```

### 优先级设计

```csharp
bool nativeBusy = !cam.IsDefaultViewSelectFocus();
```

**注意**：如果写成"原生占用就跳过"，在回合结束场景下会**永远跳过**（回合结束本身一直在用原生镜头），等于从未生效。

正确做法是**播放期间主动接管、结束立刻交还**。

### 反复开关的坑

释放聚焦后必须清空 `_focusViews`，否则下一帧又满足提交条件 → 反复 ON/OFF。

---

## 9. 失败清单（重点）

这些全部实测过，写下来是为了避免重犯。

| 尝试 | 结果 | 原因 |
|---|---|---|
| `Pause()` 定格末帧 | 回合结束卡 **10 秒** | 引擎认为动作未播完，等超时 |
| `Evaluate()` 定格 | 特效**播两遍** | Evaluate 会重新计算 timeline，效果轨再次触发 |
| 检测漂移后 `ChangeMotion` 修复 | 每次修复特效+1 | ChangeMotion 等于重新启动动作 |
| 用 `ChangeMotion(muteSound:true)` 静音 | 无效 | 引擎自己播放时不走我们的调用；且 muteSound 主要管语音 |
| 播放后静音音频轨 | `count=0` → 无效 | IL2CPP 类型名全为 `TrackAsset`，匹配不到 |
| 播放**开始后**才静音 | 语音照样响 | FMOD 事件已发出 |
| 直接写 `_reservedRoundEndActionMotion` | 完全没动画 | 没有登记进待播放列表 |
| 写 `_roundEndDelay` 加回合结束延时 | 完全无效 | 该字段不参与转场等待计算 |
| `ResolveEffectiveTurnFadeOutDuration` 加时长 | 从未被调用 | 该战斗流程不走 `ProduceAddOn_TurnTransition` |
| `DestroyTrail()` / `SetDisableTrail()` 清残留 | **单位贴图消失 + 游戏软锁** | 销毁了共享渲染对象 |
| 停用 `attackEffects` 列表 | 残留仍在 | 残留不在这里 |
| `Stop(StopEmittingAndClear)` 清粒子 | 残留仍在 | 残留不是粒子（层级扫描只见 1 个无关的 UI 粒子）|
| 收尾强制回闲置 | 残留仍在 | 与"动作结束判定"无关 |

---

## 10. 无法解决的问题：特效残留

### 现象

裁剪播放结束后，技能特效的部分视觉元素留在单位身上。

### 排查过程（全部落空）

| 手段 | 结果 |
|---|---|
| 停用 `GetAttackEffectList()` 中激活对象 | 无效（`attackEffects=49, active=3, disabled=3` 后仍在）|
| `Stop(StopEmittingAndClear)` 停粒子 | 无效（`visible=1` 的名字是 `FX_UI_Battle_MindPowerSphere`，属 UI）|
| 单位层级快照对比（`newlyActive`）| 只发现无关的 SP 条 UI 粒子 |
| 场景根对象对比（`newRootObjects`）| 未发现新增 |
| 强制回闲置 | 无效 |

### 结论

残留是**引擎在 timeline 层面持有的渲染状态**，外部无法在不破坏渲染器的前提下清理。`DestroyTrail()` 那次证明强行清理的代价远大于收益（贴图消失 + 软锁）。

### 最终决定（v6.0 起）

**不做任何自动清理**，把取舍交给使用者：

- 想避免 → 用「保留自然结尾」的写法
- 必须裁结尾 → 接受残留，或用扫描工具避开带长尾特效的段

---

## 11. 调试方法论

### 探针优先，不猜

本项目所有突破都来自探针，而不是推理：

| 探针 | 发现 |
|---|---|
| `SD.CharacterAppearance.ChangeMotion` | 原版用 `Special1` / `S11` 做解混动画；`OnBreak` → `Damaged` |
| `OnRecoverBreak` / `OnBreak` | 外观层的混乱状态入口 |
| `BattleUnitView.OnRoundEndAction` / `ReserveRoundEndAction` | 原版回合结束动作通道 |
| `BattleObjectManager.OnRoundEnd*`（带时间戳）| 原版流程的**阶段间隔**（发现引擎只在等动画长度）|
| 层级快照对比 | 排除"对象是单位子物体"的假设 |
| 场景根对比 | 排除"对象挂在场景根"的假设 |

### 时间戳日志

```csharp
Plugin.L.LogInfo($"[probe] {elapsed,8:0.000}s {where}");
```

有了相对时间戳才能看出"谁在等谁"，这是定位 10 秒超时的唯一手段。

### Unity 时间与引擎状态

检查 `PlayableDirector.state`（`Playing` / `Paused` / `Stopped`）比看画面更可靠。

### 别用运行时快照当永久基准（与连携系统共通的教训）

连携系统曾因"拿当前位置当原位"形成死循环归位失败。

---

## 12. 给后来者的建议

1. **先找原版怎么做的**，再设计实现。本项目 80% 的时间花在试错，而这些试错本可以通过探针一次性避免。
2. **不要与引擎抢控制权**：借用它的通道（`ReserveRoundEndAction` / `SelectFocus`），而不是自己造一套。
3. **区分"数据"与"登记"**：写字段 ≠ 调方法。引擎的列表驱动机制只认方法调用。
4. **IL2CPP 下类型名不可信**：`GetType().Name` 常返回泛型基类名，识别要靠 `name` 或字段。
5. **破坏性接口一律禁用**：`Destroy*` 系列在共享资源上等同于自杀。宁可功能不完美。
6. **每次修改只动一处**，并保留可回滚的版本号。本项目因一次改多处导致过两次回归（整段播放被 Stop、文件被写坏）。

---

*本文档随插件发布，可自由引用；如发现问题欢迎指正。*
*作者：希希希土（Xt）*
