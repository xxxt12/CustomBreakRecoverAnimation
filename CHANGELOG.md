# CHANGELOG

版本号规则：`主.次` —— 主版本在**功能面变化**时递增，次版本在**修复**时递增。

---

## v4.6

**变更**
- `animmode` 的 var7 从「聚焦倍率」改为「聚焦开关」：`0` 启用（默认）/ `1` 关闭

**原因**
- 实测聚焦倍率**完全无效**：引擎的 FOV 计算并不使用该参数，填 `1.5` / `2.0` / `6.0` 表现完全相同。
  与其保留一个"看起来能调、实际无效"的旋钮，不如改成诚实的开关。

**移除**
- `focusLevel` 传参、`_selectFocusMinFov/MaxFov` 缩放逻辑、相关字段

---

## v4.4

**变更**
- 回退 `playskillanim`（任意时点立即播放）的全部实现

**原因**
- 该功能在 `EncounterStart`（进入战斗）阶段无法播动：`ChangeMotion` 只登记动作，引擎不驱动 director；
  `director.Play()` 与 `view.OnRoundEndAction()` 两条备用路径同样无效。
  回合结束通道之外没有可靠的播放入口，该功能交付给社区更有能力的人。

**保留**
- `NoPlaybackTimeout`（引擎不播时自动放弃，防镜头悬空）
- 镜头在"确认开始播放"时纳入集合
- 锚点在"开始播放"时重采样

---

## v3.7

**新增**
- `breakrecoveranim` 第 4 参数：**播放倍速**（0.5 ~ 2.0）
- `animmode` 第 8 参数：**水平反转**（播放前转 180°，抵消动画自带转身）

---

## v3.4

**修复**
- 回合交接时**强制回位**（`snapped back` 日志），避免裁结尾后残留位移

**尝试并放弃**
- 订阅 `Camera.onPreCull` 获取渲染前时机 —— 该 interop 里 `onPreCull` 是 Il2Cpp 结构体，不可 `+=`；
  而 `Camera.FireOnPreCull` 只在存在订阅者时才被调用

---

## v3.2

**修复**
- `AnimationTrack` 转型：IL2CPP 下 `as` 会失败 → 改用 `TryCast<T>()`
- 增加轨道名诊断日志（`[tracknames]`），用于确认动画轨是否被正确识别

---

## v3.0

**修复**
- 挂起刷新回调前**先主动刷新一次**，否则混乱 UI 不会消失
  （清除混乱后的界面刷新本来就走这个回调）

---

## v2.9

**修复**
- 段长改为按**动画轨**内容结束时刻计算，不再被特效轨的长尾撑大
  （多单位同时使用时，回合等待时间取决于最长者，虚高的段长会白等一大截）

---

## v2.8

**变更**
- 镜头保持到**回合结束**才释放（原来动画播完就释放，观感割裂）
- 增加 `MaxFocusSeconds = 12s` 超时兜底

---

## v2.7

**修复**
- 段长改用「内容结束时刻」而非 `TimelineAsset.duration`
  （`duration` 是硬币时间窗，常被拉长 —— 显示 7.6 秒的片段实际动作只有 2~3 秒）

---

## v2.2

**新增**
- `animmode` 第 6 参数：**姿态重置**（`0` 保持末帧 / `1` 播完回闲置）

**机制发现**
- 姿态保持的实现是挂起 `BattleUnitView.refreshRoundEndActions`
- 该字段的发现来自一个对称现象：连携系统里同伴的姿势**无法重置**（引擎从不刷新），
  解混动画里单位的姿势**无法保持**（引擎会刷新）—— 两者是同一机制的两面

---

## v1.9

**修复**
- 未指定段索引（`S11` 而非 `S11#0`）归一化为 `0`
  否则资产查找失败 → 位移抑制、音频静音全部静默失效

---

## v1.8

**修复**
- 资产来源改为从上层传入（`consequence` 已解析的 `segAsset`）
  `director.playableAsset` 在播放开始前是 `null`

---

## v1.6

**修复**
- 位移抑制改为归零 `AnimationTrack` 的 offsets（`position` / `rotation` / `applyOffsets`）
- 确认 `Animator.applyRootMotion` **不是**位移来源（实测本来就是 `false`）

---

## v1.2

**修复**
- 播放开始时**无条件**定位到起始秒
  `PlayableDirector.time` 会保留上次播放位置，不重置会导致「一次对齐、一次不对」
- 增加 `Camera.FireOnPreCull` 兜底锁定

---

## v1.0

**首个可用版本**
- 自建时点 `OnForceBreakRecover`（id 5002）
- `breakrecoveranim` / `animmode` 两个函数
- 动画开始即真正解除混乱
- 播放前静音 FMOD 轨（语音 / 音效可分别关闭）
- 镜头特写（`SelectFocus`）
