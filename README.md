# CustomBreakRecoverAnimation

[![Release](https://img.shields.io/badge/release-v4.6-blue)]()
[![Platform](https://img.shields.io/badge/platform-Limbus%20Company%20%7C%20BepInEx%206-green)]()
[![License](https://img.shields.io/badge/license-MIT-yellow)]()

Custom **Break-recovery animation** plugin for *Limbus Company*.
Plays a chosen **segment** of a unit's own skill motion when its Break (confusion) state is removed —
configured entirely in JSON, no code required by the user.

> Designed and led by **希希希土（Xt）** · co-developed with **DeepSeek**

---

## Features

| Feature | Notes |
|---|---|
| Segment clipping | `breakrecoveranim(S3#0, 0, 1.2)` — any second range of any segment |
| Two-sided trim | head trim keeps the final pose; tail trim resets to idle (see Limitations) |
| Real Break clearing | the unit's state actually leaves Break at animation start |
| Position lock | both axes / horizontal only / vertical only / off |
| Voice & SFX muting | mute the shout but keep the impact — separately |
| FX suppression | disable all effect tracks carried by the motion |
| Pose control | keep the final pose, or let the engine reset it |
| Camera focus | engine `SelectFocus` channel, released at round end |
| Horizontal flip | turn the unit 180° before playback to cancel a built-in turn |
| Playback speed | 0.5x – 2x |

---

## Usage

One Modular script line inside a passive:

```json
"Modular/TIMING:EndBattle/VALUE_0:getunitstate(Self)/IF(VALUE_0=2):animmode(0,0,0,0,0,0,0):breakrecoveranim(S3#0,0,1.2,0.8)/"
```

Read as: *at round end → if in Break → set playback mode → play `S3#0` from 0 to 1.2s at 0.8x and clear Break.*

### `breakrecoveranim(motion, startSec, endSec, speed)`

| Param | Meaning |
|---|---|
| motion | `S1`, `S3#0`, `S3#2`, `Special1` … (`#` = segment index, optional) |
| startSec | relative to segment start (omit = 0) |
| endSec | relative to segment end (omit = whole segment) |
| speed | `0.5` slow … `2.0` fast, default `1.0` (positional — needs start/end) |

### `animmode(lock, voice, sfx, fx, skipBreak, pose, focus, flip)`

| Param | Values |
|---|---|
| lock | `0` both / `1` horizontal / `2` vertical / `3` off |
| voice / sfx / fx | `0` keep / `1` mute |
| skipBreak | `0` clear Break / `1` animation only |
| pose | `0` keep final pose / `1` reset to idle |
| focus | `0` camera focus on / `1` off |
| flip | `1` = turn 180° before playback |

Must be written **before** `breakrecoveranim`.

---

## Limitations (honest)

| Limitation | Cause |
|---|---|
| **Tail trim → pose resets to idle** | `PlayableDirector.Stop()` always resets the appearance; every workaround tested failed |
| Movement baked into clips can't be fully removed during playback | we correct every frame and snap the unit back at round end, but the rendered frame still uses the director's transform |
| Multiple units → round-end wait equals the longest clip | engine design |
| Residual VFX after tail trim is **not** cleaned | destructive cleanup (`DestroyTrail`) broke shared renderers and soft-locked the game |

> `Stop` and "frozen pose" are mutually exclusive at the engine level.
> If you need a held pose, use the whole segment or a **head trim**, or pick a motion slot whose tail is already short.

---

## Install

```
BepInEx/plugins/CustomBreakRecoverAnimation.dll
```

Requires **BepInEx 6 (LimiNex)** and **ModularSkillScripts 5.3.5+**.
See [`example/`](example/) for a ready-to-use passive (note: it contains a test-only script).

---

## Build from source

See [`BUILDING.md`](BUILDING.md). Short version:

```bat
setx LIMBUS_BEPINEX  "C:\...\Limbus Company\BepInEx"
setx LIMBUS_INTEROP "%LIMBUS_BEPINEX%\interop"
setx LIMBUS_UNITYLIB "%LIMBUS_BEPINEX%\unity-libs"
dotnet build src\CustomBreakRecoverAnimation.csproj -c Release
```

Game assemblies are **not** included in this repository (copyright + size).

---

## Documentation

| Doc | Contents |
|---|---|
| [`docs/plugin-guide.md`](docs/plugin-guide.md) | Full user manual (Chinese + English) |
| [`docs/dev-notes.md`](docs/dev-notes.md) | Dev notes: engine internals, **failed attempts and why**, debugging method |
| [`docs/technical-report.md`](docs/technical-report.md) | Project report: goals, design decisions, what didn't work |
| [`CHANGELOG.md`](CHANGELOG.md) | Version history |

The dev notes are arguably the most reusable part: they document dead ends such as
`Pause()` freezing the round end for 10 seconds, `Evaluate()` replaying effect tracks,
and `DestroyTrail()` destroying shared renderers.

---

## License

MIT — see [LICENSE](LICENSE).

Using the mod in videos/streams/mods is fine; credit is appreciated but not required.

---

## 中文简介

《边狱公司》插件：单位在**混乱（Break）解除**时，播放**它自己技能动作的节选片段**。全部用 JSON 配置，不需要写代码。

支持双端裁剪、位置锁定、语音与音效分别静音、关闭特效、保持末帧姿势、镜头特写、水平反转、0.5~2 倍速。

**已知限制**：裁结尾会让姿势回闲置（引擎限制）；片段内的位移无法在播放中完全消除；多单位同时触发时回合等待取最长者；裁结尾的特效残留不做清理。

详见 [`docs/plugin-guide.md`](docs/plugin-guide.md)。
