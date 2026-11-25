# 示例文件说明 / Example files

## ⚠ 先看这条

本目录的 `passive_breakrecover_test.json` **包含一条测试用脚本**：

```json
"Modular/TIMING:EncounterStart/break(Self,natural)/"
```

它的作用是**进入战斗时让该单位强制进入混乱**，只为了方便测试"混乱解除动画"。
**实际使用请把这整行删掉**，否则你的单位每场战斗开局就会处于混乱状态。

---

## 文件内容

`passive_breakrecover_test.json` —— 给单位 **10713** 的被动示例（被动 ID `1071301`）。

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

| 行 | 作用 | 实战建议 |
|---|---|---|
| 第 1 条 | 回合结束时若自身混乱 → 播放 `S3#0` 的 0~1.2 秒、0.8 倍速，并解除混乱 | ✅ 保留（按需改动作槽与时间）|
| 第 2 条 | 进场强制混乱 | ❌ **删掉**（仅测试用）|

---

## 怎么用在自己的人格上

1. **确认被动 ID**
   打开 `limbus_data/personality/<人格编号>.json`，找 `passiveList`。
   例如人格 10713 的被动之一是 `1071301`。

2. **改 `id`**
   把示例里的 `"id": 1071301` 换成你的目标被动 ID。

3. **改动作槽与时间**
   `breakrecoveranim(S3#0,0,1.2,0.8)` 的四个参数依次是：
   动作槽 / 起始秒 / 终止秒 / 倍速。

   不知道有哪些动作槽？战斗中按 **`0`**（需要同作者的动作槽扫描插件），
   或查 `limbus_data/skill/*.json` 里该人格技能的动作名。

4. **放置位置**

   ```
   BepInEx/plugins/Lethe/mods/<你的mod名>/custom_limbus_data/passive/<文件名>.json
   ```

---

## 动作槽写法速查

| 写法 | 含义 |
|---|---|
| `S3` | S3 的第 0 段 |
| `S3#0` | 同上（显式写段索引）|
| `S3#2` | S3 的第 2 段 |
| `Special1` | 特殊动作槽 1 |

时间参数：

| 写法 | 含义 |
|---|---|
| `breakrecoveranim(S3#0)` | 整段自然播完（**能保持末帧姿势**）|
| `breakrecoveranim(S3#0,0.5)` | 从 0.5 秒播到自然结尾（裁开头，也能保持姿势）|
| `breakrecoveranim(S3#0,0,1.5)` | 只播 0~1.5 秒（裁结尾，**姿势会回闲置**）|

> 想保持末帧姿势，就用整段或只裁开头。裁结尾必然回闲置，这是引擎限制。

---

# English

## ⚠ Read this first

`passive_breakrecover_test.json` contains a **test-only** script:

```json
"Modular/TIMING:EncounterStart/break(Self,natural)/"
```

It forces the unit into Break at battle start so you can test the animation.
**Remove this line for real use**, otherwise your unit starts every battle broken.

## Using it on your own identity

1. Find the passive ID in `limbus_data/personality/<id>.json` → `passiveList`
2. Replace `"id": 1071301` with your passive ID
3. Adjust `breakrecoveranim(S3#0,0,1.2,0.8)` → `(motion, startSec, endSec, speed)`
4. Put the file in `BepInEx/plugins/Lethe/mods/<your mod>/custom_limbus_data/passive/`

| Form | Meaning |
|---|---|
| `breakrecoveranim(S3#0)` | Whole segment — **keeps the final pose** |
| `breakrecoveranim(S3#0,0.5)` | From 0.5s to the natural end (head trim) — also keeps the pose |
| `breakrecoveranim(S3#0,0,1.5)` | Only 0–1.5s (tail trim) — **pose resets to idle** |
