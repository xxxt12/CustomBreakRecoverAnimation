# CustomBreakRecoverAnimation v4.6

| | |
|---|---|
| Author / 作者 | **希希希土（Xt）** |
| Co-developed / 共同开发 | **DeepSeek（AI 助手）** |
| Version / 版本 | **4.6** |
| Requires / 依赖 | BepInEx 6 (LimiNex), ModularSkillScripts |
| Dev notes / 开发记录 | 知识库_自定义混乱解除动画.md (engine internals, failed attempts, methodology) |

> **Standalone plugin.** Only requires BepInEx 6 (LimiNex) and ModularSkillScripts — nothing else.
>
> **独立插件**：只依赖 BepInEx 6（LimiNex）与 ModularSkillScripts，不依赖任何其它 mod。
> 提供 `breakrecoveranim` + `animmode` 两个函数，以及一个自建时点 `OnForceBreakRecover`。

---

# English Documentation

## 1. What this plugin does

Plays **a clipped segment of a unit's own skill motion** when that unit's **Break** (confusion) state is removed — for any unit, including custom identities. Configured **entirely in JSON**, written into the unit's passive.

> Designed and led by **希希希土（Xt）**; co-developed with **DeepSeek** (engine investigation, failed-approach verification, documentation).

| # | Feature | Notes |
|---|---|---|
| 1 | **Clipped skill-motion playback** | `breakrecoveranim(S3#0, start, end)` — any seconds of any segment |
| 2 | **Two-sided trimming** | Trim head / trim tail / both (they behave differently — see §3) |
| 3 | **Really clears Break at animation start** | The unit's state leaves Break, not just visually |
| 4 | **Position lock, 4 modes** | Both axes / horizontal only / vertical only / none |
| 5 | **Separate voice & SFX muting** | Mute voice but keep impact sounds, or mute both |
| 6 | **Camera close-up** | Pushes in on the unit while playing; auto-releases afterwards |
| 7 | **Concurrent units** | Each unit plays its own clip without interfering |
| 8 | **Native camera priority** | Only takes over during the animation window |

> Difference from existing community "custom motion playback" mods:
> this plugin supports **segment clipping** (arbitrary second ranges), not only full segments —
> so unsuitable parts (lunges, follow-through) can be cut away.

---

## 2. Timing and compatible Modular interfaces

### Timing: `OnForceBreakRecover`

| Field | Value |
|---|---|
| Name | `OnForceBreakRecover` |
| Internal id | `5002` (outside the game's `TIMING` range 0–92) |
| Aliases | `OnBreakRecoverForced` / `ForceBreakRecover` / `OnForcedBreakRecover` |

### Engine hooks (Harmony)

| Engine method | Meaning | Dispatches our timing |
|---|---|---|
| `BattleUnitModel.RecoverAllBreak` | forced recovery (Modular's `breakrecover()` uses this) | ✅ |
| `BattleUnitModel.RecoverBreakExceptForced` | passive-driven forced recovery | ✅ |
| `BattleUnitModel.RecoverBreakExceptForcedIncludeNextTurn` | same, with "next-turn break" flag | ✅ |
| `BattleUnitModel.RecoverBreakOnRoundStartExceptForced` | **natural recovery** | ❌ blocked on purpose |

### Compatible Modular functions

The timing is dispatched through Modular's own passive mechanism
(`GetAllModpaFromPasmodel` + `ModularSA.Enact`), so **any Modular function that works inside a passive works here**:

```text
CLEARVALUES / VALUE_x           variables
IF(...)                         conditions
getunitstate(Self)              unit state (2 = Break)
breakrecover(Self,force)        clear Break
buff / healsp / heal
playsound / voice
```

⚠️ Functions that need a **combat-action context** (`SkillModel` / `CoinModel` / `BattleActionModel`) have no context here and may return nothing.

### Recommended usage

```json
Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,1,0,2.0):breakrecoveranim(S3#0,0,1.5)/
```

Read: at round end, read own state; if 2 (Break) → set playback mode → play the clip and clear Break.

### Functions

#### `breakrecoveranim(motion, startSec, endSec)`

| Param | Meaning |
|---|---|
| motion | `S1`, `S3#0`, `S3#2`, `Special1` … (`#` = segment index, optional) |
| startSec | relative to segment start (omit = 0) |
| endSec | relative to segment end (omit = whole segment) |
| **speed** | **playback rate: `0.5` slow, `2.0` fast, default `1.0`** (positional — requires start/end to be filled) |

```text
breakrecoveranim(S3#0)            whole segment
breakrecoveranim(S3#0,1.2)        from 1.2s to the natural end (head trimmed)
breakrecoveranim(S3#0,0,1.5)      only 0–1.5s (tail trimmed)
breakrecoveranim(S3#0,1.2,2.4)    both trimmed
```

#### `animmode(lock, muteVoice, muteSfx, muteFx, skipRecover, resetPose, focus, flipHorizontal)`

| lock | |
|---|---|
| `0` | both axes locked (default) |
| `1` | horizontal only (X/Z) |
| `2` | vertical only (Y) |
| `3` | none |

| muteVoice | | muteSfx | | muteFx | |
|---|---|---|---|---|
| `0` | keep (default) | `0` | keep (default) | `0` | keep skill FX (default) |
| `1` | mute | `1` | mute | `1` | **disable all FX of the motion** |

| skipRecover | | resetPose | |
|---|---|---|---|
| `0` | clear Break as usual (default) | `0` | **keep the final pose** (default) |
| `1` | **do not clear Break** (animation only) | `1` | **reset pose naturally** after the animation |

| focus | |
|---|---|
| `0` | **enable focus** (default) |
| `1` | disable focus |

| flipHorizontal (optional) | |
|---|---|
| `0` | no flip (default) |
| `1` | **turn the unit 180° horizontally before playback**, cancelling a built-in turn |

```text
animmode(0,1,0,0,0,0,0)     mute voice, keep FX, keep final pose, focus on
animmode(0,0,0,1,0,0,1)     disable all FX, keep final pose, no camera focus
animmode(0,0,0,0,1,1,0)     animation only, unit stays in Break, pose resets
```

Must be written **before** `breakrecoveranim` (same unit, same script chain).

---

## 3. Limitations

| # | Limitation | Cause / workaround |
|---|---|---|
| 1 | **Tail trimming leaves residual VFX; not cleaned** | mid-timeline `Stop` prevents engine cleanup; every cleanup API tested was either ineffective or broke renderers. **Workaround: prefer head trimming** |
| 2 | **Tail trimming ends in idle pose** (final pose not kept) | `Stop` makes the appearance fall back |
| 3 | **Natural recovery never triggers** | by design — only forced (passive-driven) recovery is hooked |
| 4 | **Non-zero segment index replays once** | `OnRoundEndAction` cannot specify a segment index |
| 5 | **Motion slot must exist** | otherwise nothing happens (log only) |
| 6 | **`animmode` must precede `breakrecoveranim`** | playback mode is read when playback is registered |
| 7 | **Zoom is a "closeness" value, not exact FOV** | passed as `SelectFocus` focus level |
| 8 | **Requires ModularSkillScripts** | timing and functions live in its dictionaries |

> The root conflict behind #1 and #2: **a timeline only cleans up and keeps its final pose when it finishes naturally.**
> Stopping it mid-way necessarily loses both. **Head trimming avoids both problems.**

---

## 4. Install

| File | Location |
|---|---|
| `CustomBreakRecoverAnimation.dll` | `BepInEx/plugins/` |

No config files are created.

---

## 5. Clip semantics

| Form | End behaviour | Final pose | Residual VFX | Round-end wait |
|---|---|---|---|---|
| `(S3#0)` full | natural finish | **final pose kept** | none | ≈ clip length |
| `(S3#0,1.2)` head trimmed | natural finish | **final pose kept** | none | ≈ `len-1.2` |
| `(S3#0,0,1.5)` tail trimmed | `Stop` (pose resets to idle — unavoidable) | idle | ⚠️ possible | ≈1.5s |
| `(S3#0,1.2,2.4)` both | `Stop` | idle | ⚠️ possible | ≈1.2s |

> Why tail trimming ends in idle: `PlayableDirector.Stop()` makes the appearance re-apply its default frame.
> We tested several workarounds (Evaluate after Stop, re-applying the pose with FX muted, Pause + forced cleanup) —
> **none of them keep the pose**, because Stop and "frozen pose" are mutually exclusive at the engine level.
> **If you need the final pose held, use a natural end** (whole segment or head-trimmed only).

---

## 6. Implementation notes (for maintainers)

1. **Let the engine play it**: only call `view.ReserveRoundEndAction(motion)`; the engine then calls
   `PlayReservedRoundEndAction` (single playback). We control the `PlayableDirector` for seeking/clipping.
   Do **not** also call `OnRoundEndAction`/`ChangeMotion` — that plays the same timeline twice (duplicate VFX).
   Writing the `_reservedRoundEndActionMotion` field alone does nothing (registration happens inside the method).
2. **Clear Break at animation start**: `unit.RecoverAllBreak(...)` + `ca.OnRecoverBreak(true)`.
   A reentrancy guard (`_inRecover`) prevents timing recursion.
3. **Audio muting must happen before playback** — FMOD events fire when the track is reached.
   Get the exact asset via `ca.GetMotion(motion).timelineAssets[index]` and mute its tracks.
   IL2CPP returns `"TrackAsset"` for `GetType().Name` → identify tracks **by name** (`Voice` = voice, other `FMOD` = SFX).
4. **Camera** uses `BattleCamManager.SelectFocus(on/off, info, view)` — `AddFocusingTarget` only frames, it does not push in.
5. **Never** use `DestroyTrail()` / `SetDisableTrail()` → destroys shared renderers (invisible unit, soft-lock).
   `Pause()` freezes the round end for 10s (engine waits for the motion to finish). `Evaluate()` re-triggers effect tracks.


### 6.1 Fixes in v1.1

| Issue | Cause | Fix |
|---|---|---|
| Clipped duration did not match (asked 0–1s, played 2s) | The end was decided by **wall-clock** time; the engine can reset the playback position (refresh) or change time scale | End is now decided by **`PlayableDirector.time >= endSec`** (absolute timeline time); position resets are detected and re-seeked (logged as `playback position reset`) |
| Position lock sometimes failed | Locking only started after the engine began playing — root motion moved the unit during the wait | Locking now starts at **Track time** and continues until the clip ends |


### 6.2 Fixes in v1.2

| Issue | Cause | Fix |
|---|---|---|
| Clipped duration alternates between correct and wrong | `PlayableDirector.time` **retains the previous playback position**; we only seeked when `start > 0`, so every other playback began from the leftover position | Seek to `start` **unconditionally** when playback begins; also detect out-of-range times (before `start` or past `end`) and re-seek (max 5 times, logged) |
| Position lock still failed with timelines | Unity's **PlayableDirector updates in `PreLateUpdate`, after `LateUpdate`** — a lock applied in `BattleCamManager.LateUpdate` is overwritten by root motion | Added a second lock at the last possible moment: `Camera.FireOnPreCull` (after all logic, right before rendering) |

---

## 7. Logs

| Log | Meaning |
|---|---|
| `timing registered: OnForceBreakRecover=5002` | timing registered |
| `functions registered: breakrecoveranim + animmode` | functions registered |
| `animmode unit=… lock=… muteVoice=… muteSfx=… zoom=…` | playback mode set |
| `break cleared at animation start: unit=… wasBreak=…` | Break cleared at animation start |
| `pre-muted (before play): unit=… voice=N sfx=M` | pre-muted tracks |
| `clip started: unit=… start=… len=… trimmed=…` | engine started playback (once) |
| `clip end (trimmed->stop)` / `clip end (natural)` | end path (includes the director time) |
| `playback position reset -> re-seek` | playback position was reset by the engine |
| `SelectFocus ON/OFF` | camera close-up on/off |
| `hold cleared at new round` | state cleared at round change |

---

## 8. Example

`example/passive_breakrecover_test.json` (sample for unit **10713**; change the `id` for other identities).
**Read `example/README.md` first** — the sample contains a test-only script that must be removed for real use.


```json
{
  "list": [
    {
      "id": 1071301,
      "attributeStockCondition": [],
      "requireIDList": [
        "Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,0,0,0,0,0,0):breakrecoveranim(S3#0,0,1.2,0.8)/",
        "Modular/TIMING:EncounterStart/break(Self,natural)/"
      ]
    }
  ]
}
```

| Script | What it does |
|---|---|
| `TIMING:EndBattle/...:breakrecoveranim(S3#0,0,1.2,0.8)` | At round end: if in Break → play `S3#0` from 0 to 1.2s at 0.8x, and clear Break |
| `TIMING:EncounterStart/break(Self,natural)` | Force self into Break at battle start (**for testing only — remove in real use**) |

> The passive `id` must match the target identity's passive ID (10713 uses `1071301`).
> For other identities, look up `passiveList` in `limbus_data/personality/*.json`.

---

# 中文文档

## 一、本插件实现了什么

让任意单位（含自制人格）在**混乱（Break）解除**时，播放**该单位自己的技能动作片段**，并且全部可调。**只用 JSON，写在单位的被动里，不需要写代码。**

| # | 实现了什么 | 说明 |
|---|---|---|
| 1 | **节选播放技能动作** | `breakrecoveranim(S3#0, 起, 止)`，可从任意段的任意秒数播放 |
| 2 | **双端裁剪** | 裁开头 / 裁结尾 / 两端都裁（两种裁剪的收尾效果不同，见 §5）|
| 3 | **动画开始即真正解除混乱** | 不再是"仅视觉解除"，单位状态确实脱离 Break |
| 4 | **位置锁定四档** | 全锁 / 仅水平 / 仅垂直 / 不锁，防止技能动作把单位带跑 |
| 5 | **语音与音效分别静音** | 关人物语音但保留打击音效，或两者都关 |
| 6 | **镜头特写** | 播放期间镜头推近该单位，倍率可调（1.0~4.0），结束自动交还原生镜头 |
| 7 | **多单位同时触发** | 各播各的、各自遮自己的状态，互不干扰 |
| 8 | **原版相机优先** | 只在动画窗口内接管镜头，其余时间完全不干预 |

> 与社区已有"自定义播放动画"功能的区别：**本插件支持节选片段**（任意秒数区间），
> 而不是只能整段播放——这样可以把技能动作里不适合的部分（如前冲、收招）裁掉。

---

## 二、新增时点与兼容的 Modular 接口

### 新增时点：`OnForceBreakRecover`

| 项 | 值 |
|---|---|
| 名称 | `OnForceBreakRecover` |
| 内部 ID | `5002`（避开原版 `TIMING` 枚举的 0~92）|
| 别名 | `OnBreakRecoverForced` / `ForceBreakRecover` / `OnForcedBreakRecover` |

**适配的引擎接口**（均通过 Harmony 挂载）：

| 引擎方法 | 语义 | 是否派发本时点 |
|---|---|---|
| `BattleUnitModel.RecoverAllBreak` | 强制解除（Modular 的 `breakrecover()` 也走这里）| ✅ |
| `BattleUnitModel.RecoverBreakExceptForced` | 被动发动的强制解除 | ✅ |
| `BattleUnitModel.RecoverBreakExceptForcedIncludeNextTurn` | 同上（含"下回合混乱"标记）| ✅ |
| `BattleUnitModel.RecoverBreakOnRoundStartExceptForced` | **自然解除** | ❌ 主动屏蔽 |

**兼容的 Modular 函数**：本时点走 Modular 官方被动派发机制（`GetAllModpaFromPasmodel` + `ModularSA.Enact`），因此**凡是在被动里能正常工作的 Modular 函数都可以使用**，例如：

```text
CLEARVALUES / VALUE_x           变量与取值
IF(...)                         条件判断
getunitstate(Self)              单位状态（2 = 混乱）
breakrecover(Self,force)        强制解除混乱
buff / healsp / heal            增益与回复
playsound / voice               播放自定义音频
```

⚠️ 依赖**战斗动作上下文**（`SkillModel` / `CoinModel` / `BattleActionModel`）的函数在此处没有上下文，可能取不到数据。

### 推荐写法（整合式）

```json
Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,1,0,2.0):breakrecoveranim(S3#0,0,1.5)/
```

读法：回合结束时读取自身状态，若为 2（混乱）→ 设置播放模式 → 播放动作片段并解除混乱。

### 新增函数

#### `breakrecoveranim(动作槽, 起始秒, 终止秒)`

| 参数 | 含义 |
|---|---|
| 动作槽 | `S1`、`S3#0`、`S3#2`、`Special1` …（`#` 后为段索引，可省略）|
| 起始秒 | 相对该段起点（省略 = 0）|
| 终止秒 | 相对该段终点（省略 = 整段）|
| **倍速** | **播放速度倍率：`0.5` 慢放、`2.0` 快放，默认 `1.0`**<br>（位置参数，必须填了起始/终止秒才能用到）|

```text
breakrecoveranim(S3#0)            整段
breakrecoveranim(S3#0,1.2)        从 1.2s 播到自然结尾（裁开头）
breakrecoveranim(S3#0,0,1.5)      只播 0~1.5s（裁结尾）
breakrecoveranim(S3#0,1.2,2.4)    两端都裁
breakrecoveranim(S3#0,0,2,0.5)    0~2 秒半速（实际 4 秒）
breakrecoveranim(S3#0,0,2,2.0)    0~2 秒双速（实际 1 秒）
```

#### `animmode(锁定, 语音, 音效, 特效, 解混, 姿态重置, 聚焦开关, 水平反转)`

| var1 锁定 | |
|---|---|
| `0` | 水平 + 垂直都锁（默认）|
| `1` | 只锁水平（X/Z）|
| `2` | 只锁垂直（Y）|
| `3` | 都不锁 |

| var2 语音 | | var3 音效 | | var4 特效 | |
|---|---|---|---|---|
| `0` | 保留（默认）| `0` | 保留（默认）| `0` | 保留技能 FX（默认）|
| `1` | 关闭 | `1` | 关闭 | `1` | **关闭技能动作携带的所有 FX** |

| var5 解除混乱 | | var6 姿态重置 | |
|---|---|---|---|
| `0` | 照常解除混乱（默认）| `0` | **保持动画最后一帧**（默认）|
| `1` | **不解除混乱**（只播动画）| `1` | **动画结束后自然重置姿态** |

| var7 镜头聚焦 | |
|---|---|
| `0` | **启用聚焦**（默认）|
| `1` | 关闭聚焦 |

| var8 水平反转（可不填）| |
|---|---|
| `0` | 不反转（默认）|
| `1` | **播放前把单位水平转 180°**，抵消动画自带的转身 |

```text
animmode(0,1,0,0,0,0,0)     关语音 + 保留特效 + 保持末帧 + 启用聚焦
animmode(0,0,0,1,0,0,1)     关闭所有特效 + 保持末帧 + 不聚焦
animmode(0,0,0,0,1,1,0)     只播动画 + 保持混乱 + 姿态自然重置
animmode(0,0,0,0,0,0,0,1)   启用聚焦 + 水平反转
```

必须写在 `breakrecoveranim` **之前**（同一单位、同一条脚本链）。

---

## 三、当前局限性

| # | 限制 | 原因 / 应对 |
|---|---|---|
| 1 | **裁结尾会留下特效残留，插件不清理** | timeline 中途 `Stop` 后引擎不再回收特效；所有清理手段实测要么无效要么会破坏渲染器（见 §7）。**应对：优先用「裁开头」的写法** |
| 2 | **裁结尾后姿势变为闲置**（不保留末帧） | `Stop` 会让外观回退，无法既截断又保留末姿势 |
| 3 | **自然解除不会触发** | 设计如此：只接管"强制解除"（被动发动），避免影响原版机制 |
| 4 | **段索引非 0 时会多播一次** | 引擎通道 `OnRoundEndAction` 无法指定段索引，只能补一次 `ChangeMotion` |
| 5 | **动作槽必须存在** | 该人格没有此槽位时不生效，仅打日志 |
| 6 | **`animmode` 必须先于 `breakrecoveranim`** | 播放模式在登记播放时读取 |
| 7 | **镜头倍率是"接近程度"而非精确 FOV** | 走引擎 `SelectFocus` 的 FOV 逻辑，倍率只作为聚焦等级传入 |
| 8 | **依赖 ModularSkillScripts** | 时点与函数都注册在它的字典里，缺它则无法使用 |

> 关于第 1、2 条的取舍，来自一个根本矛盾：
> **timeline 只有"自然播完"时才会自己收尾**（回收特效 + 保留末姿势）。
> 一旦中途停止，就必然丢掉这两样。**裁开头能同时避开这两个问题。**

---

## 四、安装

| 文件 | 位置 |
|---|---|
| `CustomBreakRecoverAnimation.dll` | `BepInEx/plugins/` |

不产生任何配置文件。

---

## 五、裁剪语义与效果（重要）

| 写法 | 结束方式 | 结束姿势 | 残留特效 | 回合结束等待 |
|---|---|---|---|---|
| `(S3#0)` 整段 | 自然播完 | **末帧自然保持** | 无 | ≈动画长度 |
| `(S3#0,1.2)` 裁开头 | 自然播完 | **末帧自然保持** | 无 | ≈`段长-1.2` |
| `(S3#0,0,1.5)` 裁结尾 | `Stop` 截断（姿势必然回闲置）| 回到**闲置** | ⚠️ 可能残留 | ≈1.5s |
| `(S3#0,1.2,2.4)` 两端都裁 | `Stop` 截断 | 回到**闲置** | ⚠️ 可能残留 | ≈1.2s |

> 为什么裁结尾必然是闲置：`PlayableDirector.Stop()` 会让外观重新套用默认帧。
> 我们实测过多种补救（Stop 后再 Evaluate、静音特效后重新定格、Pause + 强制清理）——**都无法保持姿势**，
> 因为 Stop 与"定格姿势"在引擎层面互斥。
> **需要保持末帧就必须用自然结尾**（整段，或只裁开头）。

---

## 六、实现要点（维护者）

### 1. 播放权交给引擎

```csharp
view.ReserveRoundEndAction(motion);   // 只登记，不主动播放
```

引擎随后调用 `PlayReservedRoundEndAction` → **只播一次**；我们只接管 `PlayableDirector` 做定位与收尾。

> 早期版本同时调用 `OnRoundEndAction` + `ChangeMotion`，同一份 timeline 被播两次（特效重复）。**不要加回来。**
> 只写 `_reservedRoundEndActionMotion` 字段是无效的——登记动作在方法体内。

### 2. 动画开始即解除混乱

```csharp
if (unit.IsBreak()) unit.RecoverAllBreak(...);
ca.OnRecoverBreak(true);      // 外观层（关键）
```

递归保护：`BreakPatches._inRecover` 在内部解除时置位，避免再次派发时点。

副作用：**不再需要隐藏混乱 UI**。

### 3. 位置锁定

按 `animmode` 的 var1 决定锁定轴；播放期间每帧钉住，播完解除。

### 4. 音频静音

**必须在播放之前**：语音/音效是 FMOD 轨播放到该点才触发的事件。

```csharp
var segAsset = ca.GetMotion(motion, false).timelineAssets[index];   // 引擎将要播的同一份资产
segAsset.flattenedTracks[i].muted = true;                           // 共享资产，提前静音
```

- IL2CPP 下 `GetType().Name` 对所有轨都返回 `"TrackAsset"` → **按轨道名字识别**（含 `Voice` 算语音，其余 `FMOD` 算音效）
- `track.GetClips()` 不能 `foreach`，需转 `System.Collections.IEnumerator`

### 5. 镜头

```csharp
cam.SelectFocus(true, info, view);    // 动画开始
cam.SelectFocus(false, info, view);   // 动画结束，交还原生
```

`AddFocusingTarget` 是"取景范围"不是"推近"；`SelectFocus` 才带 FOV 推近。

### 6. 已废弃/禁止的做法（踩坑记录）

| 做法 | 后果 |
|---|---|
| `Pause()` 定格 | 引擎认为动作未播完 → 回合结束卡 **10 秒** |
| `Evaluate()` 定格 | 重新计算 timeline → **特效播两遍** |
| 漂移后 `ChangeMotion` 修复 | 每次修复再重播一次动作 |
| `DestroyTrail()` / `SetDisableTrail()` | **销毁共享渲染对象 → 贴图消失、游戏软锁** |
| 写 `_roundEndDelay` 加延时 | 该字段不参与转场等待，完全无效 |
| 播放开始后再静音 | FMOD 事件已发出，无效 |


### 六.1 v1.1 修复

| 问题 | 原因 | 修复 |
|---|---|---|
| 节选时长不匹配（设 0~1 秒却播了 2 秒）| 结束判定用的是**墙钟时间**；引擎可能重置播放位置（刷新）或改变时间缩放 | 改为按 **`PlayableDirector.time >= endSec`**（时间轴绝对时间）判定；检测到播放位置被重置会自动重新定位并打日志 `playback position reset` |
| 位置锁定有时失效 | 原来要等引擎真正开始播放才锁，等待期间已被 root motion 带走 | 改为**从 Track 时刻起**就开始钉住，直到片段结束 |


### 六.2 v1.2 修复

| 问题 | 原因 | 修复 |
|---|---|---|
| 节选时长"一次对、一次不对" | `PlayableDirector.time` **会保留上一次播放的位置**；我们只在 `start > 0` 时定位，导致每隔一次从残留位置开始 | 开始播放时**无条件**定位到 `start`；并检测时间越界（小于 start 或超过 end）自动重新定位（最多 5 次，有日志）|
| 位置锁定在 timeline 下仍失效 | Unity 的 **PlayableDirector 在 `PreLateUpdate` 更新，晚于 `LateUpdate`**；在 `BattleCamManager.LateUpdate` 里锁会被 root motion 覆盖 | 在**最晚时机**再锁一次：`Camera.FireOnPreCull`（所有逻辑之后、渲染之前）|


### 6.3 v2.0：`animmode` 参数顺序调整

为了可读性把「镜头倍率」移到最后：

```
animmode(var1, var2, var3, var4, var5, var6)
        锁定  语音  音效  特效  解混  倍率
```

v1.1~v1.9 的旧顺序是 `锁定 / 语音 / 音效 / 倍率 / 特效 / 解混`。
**升级后请把倍率参数移到第 6 位**，否则参数会错位。


### 6.4 v2.2：新增「姿态重置」开关（var6）

```
animmode(var1, var2, var3, var4, var5, var6, var7)
        锁定  语音  音效  特效  解混  姿态  倍率
```

| var6 | 行为 | 实现 |
|---|---|---|
| `0`（默认）| 保持动画最后一帧 | 挂起该单位的 `refreshRoundEndActions` 回调，引擎无从刷新 |
| `1` | 动画结束后自然重置 | 不挂起，交给引擎自己的刷新逻辑 |

> 这个开关的发现来自一个对称现象：
> 连携系统里同伴的姿势**无法重置**（同伴不在技能流程里，引擎从不刷新他们）；
> 解混动画里单位的姿势**无法保持**（单位在回合结束流程里，引擎会刷新它）。
> 两者是同一机制的两面——**引擎是否刷新这个单位**。
> 因此解混这边只要"挂起刷新回调"就能保持，而连携那边要做到重置，就需要"主动触发刷新"。


### 6.5 v2.6：明确「裁结尾不保持姿势」

经过多轮实测，确认一条硬约束：

| 写法 | 姿势 |
|---|---|
| 整段 / 只裁开头（自然播完）| **保持末帧**（`var6=0`）或交给引擎刷新（`var6=1`）|
| **裁结尾** | **必定回闲置**，`var6` 不起作用 |

原因：`PlayableDirector.Stop()` 会触发外观复位；而"定格"必须靠 `Evaluate()`，
两者无法共存，且引擎刷新会覆盖任何事后补救。

因此若某个动作的**尾部**不合适，推荐做法是：**换一个尾段本来就短的动作槽**，
而不是裁结尾（可用动作槽扫描工具查看每段的 `fxEnd` 与时长）。


### 6.6 v3.7：倍速与水平反转

| 新能力 | 位置 | 取值 |
|---|---|---|
| 播放倍速 | `breakrecoveranim` 的 var4 | `0.5` 慢 / `1.0` 原速 / `2.0` 快（须先填起始、终止秒）|
| 水平反转 | `animmode` 的 var8（可不填）| `1` = 播放前转 180°，抵消动画自带转身 |


### 6.7 v4.6：聚焦改为开关

原来的「聚焦倍率」实测**没有任何效果**（引擎的 FOV 计算不使用该参数，填 1.5 / 2.0 / 6.0 表现完全相同），
因此改为更诚实的开关形式：

| var7 | 行为 |
|---|---|
| `0` | 启用聚焦（默认）|
| `1` | 关闭聚焦 |

> 聚焦用的是引擎自带的「选中单位特写」通道，距离由引擎决定，**不可调**。

---

## 七、日志对照

| 日志 | 含义 |
|---|---|
| `timing registered: OnForceBreakRecover=5002` | 时点注册成功 |
| `functions registered: breakrecoveranim + animmode` | 函数注册成功 |
| `animmode unit=… lock=… muteVoice=… muteSfx=… zoom=…` | 播放模式已设置 |
| `break cleared at animation start: unit=… wasBreak=…` | 动画开始即解除混乱 |
| `pre-muted (before play): unit=… voice=N sfx=M` | 播放前已静音（语音/音效分别计数）|
| `clip started: unit=… start=… len=… trimmed=…` | 引擎开始播放（唯一一次）|
| `clip end (trimmed->stop)` / `clip end (natural)` | 收尾方式（含 director 实际时间）|
| `playback position reset -> re-seek` | 播放位置被引擎重置，已重新定位 |
| `SelectFocus ON/OFF` | 镜头特写开/关 |
| `hold cleared at new round` | 回合切换清理保持状态 |

---

## 八、示例

`example/passive_breakrecover_test.json`（给单位 **10713** 的可用样例，改 `id` 即可用于其它人格）。
**先读 `example/README.md`** —— 示例里含一条测试用脚本，实战需删除。


```json
{
  "list": [
    {
      "id": 1071301,
      "attributeStockCondition": [],
      "requireIDList": [
        "Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,0,0,0,0,0,0):breakrecoveranim(S3#0,0,1.2,0.8)/",
        "Modular/TIMING:EncounterStart/break(Self,natural)/"
      ]
    }
  ]
}
```

两条脚本各做什么：

| 脚本 | 作用 |
|---|---|
| `TIMING:EndBattle/...:breakrecoveranim(S3#0,0,1.2,0.8)` | 回合结束时：若自身混乱 → 播放 `S3#0` 的 0~1.2 秒、0.8 倍速，并解除混乱 |
| `TIMING:EncounterStart/break(Self,natural)` | 进入战斗时让自身进入自然混乱（测试用，实战应删掉）|

> **注意**：`passive` 的 `id` 必须与目标人格的被动 ID 对应（10713 用 `1071301`）。
> 换成别的人格时，请查 `limbus_data/personality/*.json` 里该人格的 `passiveList`。

---
