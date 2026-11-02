> **AI TRANSLATION NOTICE: This document is AI-translated. If you find any ambiguity or discrepancy, the Chinese version ([JSON转CNV.md](JSON转CNV.md)) is authoritative.**

# JSON → CNV User Manual

This document explains how to **hand-write JSON to generate Grim Dawn `.cnv` conversation files**.

- Tools:
  - `GDCnvQstEditor.exe`: graphical interface; when launched with arguments it runs the same command-line commands.
  - `GDCnvQstEditor-cli.exe`: pure command line, suitable for scripts and batch processing.
  - Both programs expose identical commands, so the examples below always use `GDCnvQstEditor-cli.exe`.
- Companion examples:
  - [`cnv_full_en.json`](cnv_full_en.json) — **a single file that covers everything**: top-level fields, node fields, every node type, 30 conditions, 27 available actions, the `languages` block, voice-over, flags, and links, with an inline explanation for each field.
- For how to write QST quest files, see [`JSON_to_QST_EN.md`](JSON_to_QST_EN.md); both file types share the same condition/action system.

> Goal: after reading this document you can write valid CNV JSON without relying on the GUI, and generate a `.cnv` that the game and the official ConversationEditor can read.

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [JSON Writing Rules](#2-json-writing-rules)
3. [Top-Level Fields](#3-top-level-fields)
4. [`rows`: Conversation Nodes and the Tree Structure](#4-rows-conversation-nodes-and-the-tree-structure)
5. [Four Ways to Write Conditions and Actions](#5-four-ways-to-write-conditions-and-actions)
6. [The `cmp` Comparator](#6-the-cmp-comparator)
7. [Full Condition Table (30 Types)](#7-full-condition-table-30-types)
8. [Full Action Table (27 Available)](#8-full-action-table-27-available)
9. [P Form (Path Lists)](#9-p-form-path-lists)
10. [L Form (Lua)](#10-l-form-lua)
11. [Reference Values, Paths, and Strings](#11-reference-values-paths-and-strings)
12. [Complete Working Example](#12-complete-working-example)
13. [CLI Command Reference](#13-cli-command-reference)
14. [Common Errors](#14-common-errors)
15. [Limitations and Common Pitfalls](#15-limitations-and-common-pitfalls)
- [Appendix: Quick Checklist](#appendix-quick-checklist)

---

## 1. Quick Start

### 1.1 Minimal Working JSON

Create `hello.json`:

```json
{
  // "cnv": generates a conversation file. "qst": generates a quest file.
  "format": "cnv",
  // 6: recommended version (supports vo voice-over paths).
  "version": 6,
  // Root node text = speaker name, occupying localization slot 0.
  "speaker": "Alex",
  "rows": [
    // level 1 = a direct child of the root
    { "level": 1, "kind": "speech", "text": "Hello, traveler." },
    { "level": 2, "kind": "end", "text": "Goodbye." }
  ]
}
```

### 1.2 Generate

```bash
GDCnvQstEditor-cli.exe convert hello.json -o hello.cnv
```

Output:

```text
Generated hello.cnv (... bytes, format=cnv)
Three checks passed: fill / locOffset / round-trip
```

If `-o` is omitted, the output path is a `.cnv` file with the same name in the same directory as the JSON file.

### 1.3 Inspect the Result

```bash
GDCnvQstEditor-cli.exe view hello.cnv
GDCnvQstEditor-cli.exe verify hello.cnv
```

You can also open `hello.cnv` directly with `GDCnvQstEditor.exe`, or check it with the official ConversationEditor.

---

## 2. JSON Writing Rules

Always use the `.json` extension; the parser accepts comments and trailing commas to make hand-writing easier:

| Feature | Supported | Notes |
|---|---|---|
| `//` line comments | Yes | Used by every example in this document |
| `/* … */` block comments | Yes | |
| Trailing commas | Yes | Allowed after the last element of an array or object |
| UTF-8 BOM | Yes | |
| Single-quoted strings | **No** | Double quotes are required |
| Unquoted keys | **No** | Double quotes are required |
| Multi-line strings | **No** | Use the `\n` escape |
| `NaN` / `Infinity` | **No** | Only ordinary decimal numbers are allowed |

### 2.1 Key Name Casing

- **The JSON key names listed in this document must be written exactly as shown** (`format`, `version`, `speaker`, `level`, `kind`, `cond`, `act`, and so on); casing is significant.
- **The official type names of conditions/actions** (`HasFaction`, `GiveMoney`…) are case-insensitive.
- **Field names inside named argument objects** (`FactionName`, `Value`…) are case-insensitive.
- **A Lua object's `call` / `args` / `tail`** must be exact lowercase; `t0..t3` and `text` inside `tail` are also exact lowercase.

Recommendation: always use the spelling shown in this document instead of relying on case tolerance.

### 2.2 Strings and Numbers

- Strings are written to the file as **UTF-8**; non-ASCII text (Chinese, for example) can be written directly.
- `u32` fields must be **integers in the range 0…4294967295**; do not write decimals or negative numbers.
- `u8` fields must be **integers in the range 0…255**; boolean fields accept `true` / `false` as well as `1` / `0`.

---

## 3. Top-Level Fields

```json
{
  "format": "cnv",
  "version": 6,
  "id": 0,
  "speaker": "Alex",
  "flags": 0,
  "vo": "",
  "logic": "AND",
  "cond": [],
  "act": [],
  "languages": [
    { "tag": "enUS", "texts": ["Alex", "Hello.", "Goodbye."] }
  ],
  "rows": [ /* … */ ]
}
```

| Key | Type | Required | Default | Description |
|---|---|---|---|---|
| `format` | string | **Yes** | — | Must be `"cnv"` (case-insensitive). An error is reported if it is missing. |
| `version` | u32 | No | `6` | Must be ≥5. `5`: no `vo` field; `6`: includes `vo`. `6` is recommended. |
| `id` | u32 | No | automatic | Conversation file ID. **When omitted**, it is generated automatically from `speaker`; writing `0` explicitly really means 0. |
| `speaker` | string | No | `""` | Root node text (usually the speaker name), occupying localization slot 0. |
| `flags` | u32 | No | `0` | Root node switches; see §4.3 for the meaning of the low 3 bits. |
| `vo` | string | No | `""` | Root node voice-over path; only written when `version>=6`. |
| `logic` | string / u32 | No | `"AND"` | Relationship of the root condition list: `"AND"`=0, `"OR"`=1. |
| `cond` | array | No | `[]` | Root node condition list. |
| `act` | array | No | `[]` | Root node action list. |
| `languages` | array | No | — | Explicit language block, see §3.2. Once present it takes precedence, and `speaker`/`text` are used only as a fallback. |
| `lang` | string | No | `"enUS"` | Single language block tag (legacy style). Only effective when there is **no** `languages`; `language` is also accepted. |
| `rows` | array | **Yes** | — | Conversation nodes below the root; a missing value is an immediate error. |

### 3.1 `id`: the File's Own ID

- When `id` is omitted, the tool generates one automatically from `speaker`; speakers with the same name get the same ID, so specify it manually when you need to distinguish them.
- When `id` is written explicitly (including `0`), it is used as-is.
- The `id` of this file identifies only this conversation file; other file paths and quest/objective IDs referenced in conditions/actions are **reference IDs** — the tool neither generates nor rewrites them, and they must be copied verbatim from the target file.

### 3.2 `languages` and Localization

Using `languages` to describe language blocks explicitly is recommended, since it lets you put multiple languages in one JSON:

```json
"languages": [
  {
    "tag": "enUS",
    "texts": ["Alex", "You finally made it.{^n}Was the journey smooth?", "Continue", "", "Goodbye."]
  }
]
```

| Key | Type | Description |
|---|---|---|
| `tag` | string | Language tag. The 6 official ones: `enUS` / `enGB` / `es` / `fr` / `de` / `it`; other tags produce a warning and the game may ignore that block. |
| `texts` | array | Array of text lines; **the number of lines must match the number of nodes exactly**, see the rules below. |

Alignment rules (CNV localization slots are keyed to nodes):

- The number of lines must equal `1 + rows.Length`: slot 0 = the root node (that is, `speaker`), slot 1 = `rows[0]`, slot 2 = `rows[1]`…
- `link` nodes also occupy a line, usually an empty string `""`; an empty string is valid text.
- Every language block must have the same number of `texts` lines.
- When the line count does not match, `convert` reports an error and refuses to generate, for example:

```text
16 nodes, languages[enUS] has 14 lines, 2 missing; the number of text lines must match the number of nodes.
Node rows that accept text (1-based): 1-16. Missing rows that accept text: 15-16.
```

- **In-game line breaks are written as `{^n}`** (four literal characters), for example `"You finally made it.{^n}Was the journey smooth?"`.
  Do not write the JSON escape `\n`: that writes a real newline byte, which does not follow the official text convention.
- Even after `languages` is present, `speaker` / `text` must still be written (they define the structure), but the output follows `languages`.
- When there is only one language block, you can keep using the legacy style `"lang": "enUS"`; in that case text is collected automatically from `speaker`/`text`.

```json
// Legacy style: single language, tag defaults to enUS
"lang": "enUS"
```

- A tag is just a string; anything other than the 6 official tags produces a warning but does not block generation.

### 3.3 Root Node Conditions/Actions

The root node also has `flags`, `vo`, `cond`, and `act`, so you can set entry requirements or run actions when the conversation is first entered:

```json
{
  "format": "cnv",
  "speaker": "Alex",
  "logic": "OR",
  "cond": [
    { "HasToken": ["MET_ALEX", true] },
    { "IsLevel": [10], "cmp": ">=" }
  ],
  "act": [
    { "GiveToken": ["MET_ALEX"] }
  ],
  "rows": [ /* … */ ]
}
```

The root node has no `text` key: its text is always taken from `speaker`.

---

## 4. `rows`: Conversation Nodes and the Tree Structure

Each array element is one conversation node:

```json
{
  "level": 1,
  "kind": "speech",
  "text": "You finally made it.",
  "flags": 0,
  "vo": "conversations/alex_01.ogg",
  "link": 5,
  "logic": "AND",
  "cond": [],
  "act": []
}
```

| Key | Type | Required | Default | Description |
|---|---|---|---|---|
| `level` | int | **Yes** | — | Indentation level, ≥1. The parent node = the nearest preceding row with `level-1`. |
| `kind` | string / u32 | **Yes** | — | Node type, see §4.2. |
| `text` | string | No | `""` | Node text, occupying one localization slot. |
| `flags` | u32 | No | `0` | Node switches; the low 3 bits are editor switches, see §4.3. |
| `vo` | string | No | `""` | Voice-over file path; only written when `version>=6`. |
| `link` | int | No | itself | Meaningful only for `kind=link`: the target node index, see §4.4. |
| `logic` | string / u32 | No | `"AND"` | Relationship of this node's condition list. |
| `cond` | array | No | `[]` | This node's condition list. |
| `act` | array | No | `[]` | This node's action list. |

### 4.1 `level` and Tree Construction

Rules:

1. The first row must be `level=1` (a direct child of the root); `level<1` is an error.
2. Each row's parent is **the nearest preceding** row with `level-1`.
3. Levels cannot be skipped: a `level=1` row must already have appeared before you write `level=2`, otherwise it is an error.
4. When you return to a shallower level, the recorded parents of deeper levels are cleared; that old subtree can no longer be used as a parent afterwards.

With these rules, the order of `rows` is naturally **depth-first pre-order (DFS pre-order)**:

```json
"rows": [
  { "level": 1, "kind": "speech",  "text": "A" },  // slot 1
  { "level": 2, "kind": "speech",  "text": "B" },  // slot 2, child of A
  { "level": 3, "kind": "end",     "text": "C" },  // slot 3, child of B
  { "level": 2, "kind": "end",     "text": "D" },  // slot 4, another child of A
  { "level": 1, "kind": "speech",  "text": "E" },  // slot 5, another child of the root
]
```

In the generated file:

- the flattened node order = the row order above;
- text slot 0 = `speaker`;
- text slot i = `rows[i-1].text`;
- `link` uses this flattened index directly (see §4.4).

**So do not shuffle the pre-order row order**: text is written in row order while nodes are flattened by tree, and the two correspond one-to-one only when the rows are written in pre-order.

### 4.2 `kind`: Node Types

| JSON `kind` | Number | Official enum | Meaning |
|---|---|---|---|
| `"root"` | 0 | Root | Root node; writing 0 in `rows` creates a second root-style node, which is not recommended. |
| `"speech"` | 1 | Speech | Ordinary speech line node. |
| `"continue"` | 2 | Continue | The player chooses "Continue". |
| `"accept"` | 3 | Accept | The player chooses "Accept". |
| `"decline"` | 4 | Decline | The player chooses "Decline". |
| `"generic"` | 5 | Generic | Generic node, unnamed in the official editor. |
| `"end"` | 6 | End | Ends the conversation. |
| `"link"` | 7 | Link | Jumps to another node in this file. |
| `"random"` | 8 | Random | Random/selection node. |
| `"selector"` | 8 | Random | Alias of `random`. |
| number `0`–`8` | — | — | A number can be written directly; the meaning is the same as the table above. |

`kind` strings are case-insensitive and are also `Trim()`ed. An unrecognized name is an error.

### 4.3 `flags`: Node Switches

The official editor uses only the low 3 bits:

| Bit | Value | Name | Meaning |
|---|---|---|---|
| bit 0 | 1 | OnceOnly | Shown only once. |
| bit 1 | 2 | AlwaysShow | Always shown. |
| bit 2 | 4 | Unimportant | Unimportant (greyed out / secondary). |
| bit 3+ | 8/16/32… | undefined | Keep 0 in new files. |

Bits can be added together, for example `3` = OnceOnly + AlwaysShow, `7` = all three switches on.

### 4.4 `link`: Jump Target

Only effective when `kind` is `link` (type 7).

- The value of `link` is a **flattened node index**:
  - `0` = the root node;
  - `1..rows.Length` = the corresponding `rows` row number (1-based);
  - omitted = self-reference (this row).
- Because the row number equals the flattened index when `rows` is written in pre-order, you can usually just write "row number N".
- Out-of-range values are clamped into the valid range; still confirm the target yourself.
- When `kind` is not `link`, the `link` key is ignored.
- A link node usually has no children; the tool allows them but does not recommend it.

Example:

```json
{ "level": 1, "kind": "link", "text": "", "link": 5 }  // jump to row 5 of rows
```

### 4.5 `text` and `vo`

- `text` occupies one localization slot. An empty string still occupies a slot, so a slot cannot be "deleted" by omitting the key.
- With `version=5`, `vo` is not written to the file; with `version>=6`, every node (including the root) writes a path string, and an empty string means no voice-over.
- `vo` is usually a path relative to `resources/`, for example `conversations/alex_01.ogg`. The tool does not check whether the file exists.

### 4.6 Empty `rows`

`"rows": []` is valid and generates a conversation file containing only the root node. The official editor can open such a file, but it usually has no actual conversation content.

---

## 5. Four Ways to Write Conditions and Actions

Each element in the `cond` / `act` arrays is an object. The following four styles are supported and can be mixed.

### 5.1 Style A: Named Type + Positional Arguments

```json
{ "HasFaction": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `"HasFaction"` is the official type name (case-insensitive).
- The value can be an array, giving values in the field order of the condition/action table.
- Conditions may add `cmp`; actions must not.

> **The value must be an array or an object.** A bare scalar is not read, for example `{"GiveMoney": 500}` yields 0,
> and `{"IsHardcore": true}` yields `false`. Write `{"GiveMoney": [500]}` / `{"IsHardcore": [true]}` instead.
> The only exception is `LuaScript`: it accepts a function-name string directly (see §10.2).

**The type name must be the first "non-metadata" property in the object.** Metadata keys are:

```text
subType, cmp, comparator, comparison, uid, args
```

So do not put keys such as `logic` or `note` before the type name. It is recommended to put the type name on the first line.

### 5.2 Style B: Named Type + Named Arguments

```json
{ "HasFaction": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

- Keys in the value object are matched by official field name and are **case-insensitive**.
- Missing fields are filled with zero values (string `""`, number `0`, boolean `false`).
- Extra fields are ignored.
- Special rules apply to the `P` / `L` forms; see §9 and §10.

### 5.3 Style C: Raw Type Number

```json
{ "type": 6, "args": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `type` must be a number and uses the type numbers from the condition/action table (conditions 0–29, actions 1–28, with 0/16 unavailable).
- `args` can be omitted; it may be a positional array or a named dictionary.
- Conditions may add `cmp`.
- Normally the official type name is enough; raw type numbers suit batch generation or cases that need precise control.

### 5.4 Style D: Raw Type Number + Named Arguments

```json
{ "type": 6, "args": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

### 5.5 Precedence and Conflicts

- When both `"type"` and a named type are written, **`type` wins** and the named property is ignored.
- When both a named type and `"args"` are written, **the named type's value wins**.
- In a condition, `"uid"` is a legacy field name and is **not** treated as a comparator; only `cmp` / `comparator` / `comparison` are recognized as comparators.
- `cmp` inside an action is ignored.
- `subType` is an advanced compatibility field whose default value is already correct; do not write it unless you know for certain that you need it.

### 5.6 Empty Arguments

A type with no fields (such as `WaitForCompletion`) has three equivalent spellings:

```json
{ "WaitForCompletion": {} }
{ "WaitForCompletion": [] }
{ "type": 17 }
```

---

## 6. The `cmp` Comparator

`cmp` is the `Comparator` dropdown in the official editor. Actions do not have this field.

### 6.1 Value Table

| Number | String (case-insensitive) | Symbol | Official enum |
|---|---|---|---|
| 0 | `lt` / `less` / `<` | `<` | Less |
| 1 | `le` / `lessequal` / `<=` | `<=` | LessEqual |
| 2 | `eq` / `equal` / `=` / `==` | `=` | Equal (default) |
| 3 | `ge` / `greaterequal` / `>=` | `>=` | GreaterEqual |
| 4 | `gt` / `greater` / `>` | `>` | Greater |
| 5 | `ne` / `notequal` / `!=` / `<>` | `!=` | NotEqual |

### 6.2 Spelling

```json
{ "HasMoney": [1000], "cmp": ">=" }
{ "HasMoney": [1000], "comparator": 3 }
{ "HasMoney": [1000], "comparison": "GreaterEqual" }
```

The three key names are equivalent and are checked in the order `cmp` → `comparator` → `comparison`. When none is written, the default is `2` (`=`).

### 6.3 Which Conditions Use `cmp`

- **Conditions with numeric comparison semantics**: HasExperience, IsLevel, IsDifficulty, HasFaction, HasMoney, HasItem, HasKilled, UsedQuestItem, HasKilledProxy, CheckRandomValue, OwnsDLC, HasTribute, EarnedDevotion, IsChallenge, HasKilledMonsterClass, HasKilledFaction. `cmp` applies to the numeric part (KillCount / Value / count, and so on).
- **Boolean conditions** (HasToken, IsHardcore, KnowsPlayer, QuestBlocked…): the official dialog does not show a Comparator; keep the default `=`.
- **Quest step conditions** (QuestTaskComplete, OnQuestTask, HasQuestObjective): the official dialog likewise shows no Comparator; keep the default `=`.
- **Lua conditions**: the comparison semantics live in `tail`, so `cmp` can stay at its default.
- **Actions**: there is no `cmp`.

---

## 7. Full Condition Table (30 Types)

The condition type numbers range over **0–29**, 30 types in total. The "JSON fields" column lists fields in positional order; for the special `P`/`L` forms see §9 and §10.

| type | Official name | JSON fields (positional order) | Description / enum | Example |
|---:|---|---|---|---|
| 0 | OnQuest | `QuestFile`, `OnQuest` | Quest file string + boolean | `{"OnQuest":["quests/a.qst",true]}` |
| 1 | QuestTaskComplete | `QuestFile`, `Task`, `TaskComplete` | `Task` is a task uid | `{"QuestTaskComplete":["quests/a.qst",305419896,true]}` |
| 2 | HasExperience | `Experience` | Experience amount, used with `cmp` | `{"HasExperience":[10000],"cmp":">="}` |
| 3 | IsLevel | `IsLevel` | Level, used with `cmp` | `{"IsLevel":[10],"cmp":">="}` |
| 4 | IsDifficulty | `IsDifficulty` | 0 Normal / 1 Epic / 2 Legendary | `{"IsDifficulty":[2],"cmp":"="}` |
| 5 | IsHardcore | `IsHardcore` | Boolean | `{"IsHardcore":[true]}` |
| 6 | HasFaction | `FactionName`, `Value` | Faction name + value, used with `cmp` | `{"HasFaction":["SURVIVORS",5000],"cmp":">="}` |
| 7 | HasToken | `TokenName`, `HasToken` | Token name + boolean | `{"HasToken":["TOKEN_A",true]}` |
| 8 | HasMoney | `MoneyAmount` | Money, used with `cmp` | `{"HasMoney":[1000],"cmp":">="}` |
| 9 | HasItem | `ItemRecord`, `ItemCount`, `CompleteOnly` | `CompleteOnly` may be omitted | `{"HasItem":["records/items/a.dbr",1,false],"cmp":">="}` |
| 10 | HasKilled | `MonsterRecords` (path list), `KillCount` (trailing value) | Used with `cmp` | `{"HasKilled":{"MonsterRecords":["records/monsters/a.dbr"],"KillCount":1}}` |
| 11 | HasQuestObjective | `Quest`, `Task`, `Objective`, `Satisfied` | Task/Objective are persistent uids | see §7.1 |
| 12 | LuaScript (condition) | `call`, `args`, `tail` | see §10 | `{"LuaScript":{"call":"gd.x.y()","args":[]}}` |
| 13 | KnowsPlayer | `KnowsPlayer` | Boolean | `{"KnowsPlayer":[true]}` |
| 14 | OnQuestTask | `QuestFile`, `Task`, `OnTask` | `Task` is a task uid | `{"OnQuestTask":["quests/a.qst",305419896,true]}` |
| 15 | UsedQuestItem | `ItemRecord`, `NumUsed` | Used with `cmp` | `{"UsedQuestItem":["records/items/a.dbr",1],"cmp":">="}` |
| 16 | HasKilledProxy | `ProxyRecords` (path list), `KillCount` (trailing value) | Used with `cmp` | `{"HasKilledProxy":{"ProxyRecords":["records/proxies/a.dbr"],"KillCount":1}}` |
| 17 | WaitForCompletion | (none) | No parameters | `{"WaitForCompletion":{}}` |
| 18 | ServerHasToken | `TokenName`, `HasToken` | Boolean | `{"ServerHasToken":["TOKEN_B",true]}` |
| 19 | QuestBlocked | `QuestFile`, `QuestBlocked` | Both fields in the official dialog share the same name; positional style is recommended | `{"QuestBlocked":["quests/a.qst",false]}` |
| 20 | AnyoneHasToken | `TokenName`, `HasToken` | Boolean | `{"AnyoneHasToken":["TOKEN_C",false]}` |
| 21 | CheckRandomValue | `Name`, `Value` | Used with `cmp` | `{"CheckRandomValue":["roll",50],"cmp":"<="}` |
| 22 | OwnsDLC | `DLC_ID` | DLC numeric id, used with `cmp` | `{"OwnsDLC":[1],"cmp":"="}` |
| 23 | HasTribute | `TributeAmount` | Used with `cmp` | `{"HasTribute":[10],"cmp":">="}` |
| 24 | EarnedDevotion | `DevotionAmount` | Used with `cmp` | `{"EarnedDevotion":[20],"cmp":">="}` |
| 25 | HasMastery | `MasteryName`, `HasMastery` | `MasteryName` index 0–29 (SkillClass01–30) + boolean | `{"HasMastery":[0,true]}` |
| 26 | IsChallenge | `IsChallenge` | 0 Normal Veteran / 1 Epic Veteran / 2 Legendary Veteran | `{"IsChallenge":[2],"cmp":">="}` |
| 27 | HasBuff | `BuffName`, `HasBuff` | Buff file string + boolean | `{"HasBuff":["records/buffs/a.dbr",true]}` |
| 28 | HasKilledMonsterClass | `MonsterClass1`, `MonsterClass2`, `KillCount` | First two values: 0 Common / 1 Champion / 2 Hero / 3 Boss / 4 Quest / 5 SuperBoss | `{"HasKilledMonsterClass":[0,5,1],"cmp":">="}` |
| 29 | HasKilledFaction | `Faction1`, `Faction2`, `KillCount` | 0 None / 1 Alone / 2 GenericMonster / 3 Player / 4 Survivors / 5 Aetherials / 6 Cthonians / 7 Outlaws / 8 Beasts / 9 User0… | `{"HasKilledFaction":[4,5,100],"cmp":">="}` |

### 7.1 HasQuestObjective in Detail

```json
{
  "HasQuestObjective": {
    "Quest": "quests/example.qst",   // relative path of the .qst file
    "Task": 305419896,               // persistent uid of the target task
    "Objective": 2271560481,         // persistent uid of the target objective
    "Satisfied": true                // boolean
  },
  "cmp": "="
}
```

Positional style (all four values must be given):

```json
{ "HasQuestObjective": ["quests/example.qst", 305419896, 2271560481, true] }
```

> The `Task` / `Objective` values must **match exactly** the `tasks[].uid` / `tasks[].objectives[].uid` in the QST file.
> See [`JSON_to_QST_EN.md`](JSON_to_QST_EN.md) §4 and §7 for how these uids are generated.
> The official editor only offers dropdowns for these two parameters and does not allow typing; in JSON they are ordinary u32 values.

### 7.2 Value Ranges of Enum Fields

The following fields are dropdowns in the official editor, and out-of-range values make the official editor report an error:

- `IsDifficulty`: 0 / 1 / 2.
- `IsChallenge`: 0 / 1 / 2.
- `HasMastery.MasteryName`: 0–29 (SkillClass01–30).
- `HasKilledMonsterClass.MonsterClass1/2`: 0–5.
- `HasKilledFaction.Faction1/2`: at least 0–9; the official dropdown can scroll further, but do not invent values.
- `PlayAnimation.AnimationType`: always 9 (Chat) in the current game; confirm other values with the official editor first.

### 7.3 Condition Type Out of Range

`type` must be 0–29; when it is out of range, `convert` reports `condition type N out of range (valid range 0–29)`.

---

## 8. Full Action Table (27 Available)

Action type numbers range over **0–28**, but **0** and **16** are unavailable, leaving **27** usable. Actions have no `cmp` and no AND/OR; the `act` array executes in the order written.

| type | Official name | JSON fields (positional order) | Description | Example |
|---:|---|---|---|---|
| 1 | BeginQuestTask | `QuestFile`, `Task` | Start a task; Task is a uid | `{"BeginQuestTask":["quests/a.qst",305419896]}` |
| 2 | CompleteQuest | `QuestFile` | Complete a quest file | `{"CompleteQuest":["quests/a.qst"]}` |
| 3 | CompleteQuestTask | `QuestFile`, `Task` | Complete the specified task | `{"CompleteQuestTask":["quests/a.qst",305419896]}` |
| 4 | GiveMoney | `MoneyAmount` | Give money | `{"GiveMoney":[500]}` |
| 5 | GiveFaction | `FactionType`, `Amount` | Give faction reputation | `{"GiveFaction":["SURVIVORS",100]}` |
| 6 | GiveItem | `ItemRecord`, `NumItems` | Give items | `{"GiveItem":["records/items/a.dbr",1]}` |
| 7 | GiveLevel | `NumLevels` | Give levels | `{"GiveLevel":[1]}` |
| 8 | GiveExperience | `Experience` | Give experience | `{"GiveExperience":[1000]}` |
| 9 | GiveToken | `TokenName` | Give a token | `{"GiveToken":["TOKEN_A"]}` |
| 10 | RemoveToken | `TokenName` | Remove a token | `{"RemoveToken":["TOKEN_A"]}` |
| 11 | LuaScript (action) | `call`, `args` | No `tail` | see §10 |
| 12 | DebugPrint | `Text` | Debug output | `{"DebugPrint":["hello"]}` |
| 13 | PlaySound | `SoundRecord` | Play a sound record | `{"PlaySound":["records/sounds/a.dbr"]}` |
| 14 | PlayAnimation | `AnimationType` | Animation enum, mostly 9 in the current game | `{"PlayAnimation":[9]}` |
| 15 | GiveSkillPoint | `NumSkillpoints` | Give skill points | `{"GiveSkillPoint":[1]}` |
| 16 | (unavailable) | — | **Do not use** | — |
| 17 | Notification | `Tag` | Localized notification tag | `{"Notification":["tagAccepted"]}` |
| 18 | GiveAttribPoint | `NumAttributePoints` | Give attribute points | `{"GiveAttribPoint":[1]}` |
| 19 | ScriptEvent | `ScriptEvent` | Script event name | `{"ScriptEvent":["EV_EXAMPLE"]}` |
| 20 | GiveRandomItem | `ItemRecord`, `NumItems` | Give random items | `{"GiveRandomItem":["records/items/a.dbr",2]}` |
| 21 | GenerateRandomValue | `Name`, `Min`, `Max` | Generate a random value | `{"GenerateRandomValue":["roll",1,100]}` |
| 22 | CastSkill | `SkillRecord` | Cast a skill | `{"CastSkill":["records/skills/a.dbr"]}` |
| 23 | UnlockFaction | `FactionType` | Unlock a faction | `{"UnlockFaction":["SURVIVORS"]}` |
| 24 | SetFaction | `FactionType`, `Value` | Set a faction value | `{"SetFaction":["SURVIVORS",1000]}` |
| 25 | GiveDevotion | `NumDevotionPoints` | Give devotion points | `{"GiveDevotion":[1]}` |
| 26 | GiveTribute | `TributeAmount` | Give tribute | `{"GiveTribute":[10]}` |
| 27 | UnlockTutorial | `TutorialPage` | Unlock a tutorial page | `{"UnlockTutorial":[1]}` |
| 28 | PlayVideo | `Video path`, `AllowSkip` | Play a video + whether skipping is allowed | `{"PlayVideo":["video/intro.ivf",false]}` |

### 8.1 Actions 0 / 16

- Both types are unavailable; `convert` reports `action type N is not implemented (factory returned null)`.
- Do not try to write them; the official editor may also report an error when opening such a file.

### 8.2 Action 19 ScriptEvent

The field name is the same as the type name. The string is usually a script event name with the `EV_` prefix:

```json
{ "ScriptEvent": ["EV_PLAYER_ENTERED_REGION"] }
```

### 8.3 Field Name of Action 28 PlayVideo

The official field name is **`Video path`** (with a space in the middle and a lowercase `path`). The positional style is the simplest:

```json
{ "PlayVideo": ["video/intro.ivf", false] }
```

Named style:

```json
{ "PlayVideo": { "Video path": "video/intro.ivf", "AllowSkip": false } }
```

---

## 9. P Form (Path Lists)

`P` is used by HasKilled (type 10) and HasKilledProxy (type 16).

### 9.1 Recommended: Named Object

```json
{ "HasKilled": { "MonsterRecords": ["records/monsters/a.dbr", "records/monsters/b.dbr"], "KillCount": 3 }, "cmp": ">=" }
{ "HasKilledProxy": { "ProxyRecords": ["records/proxies/a.dbr"], "KillCount": 1 } }
```

- The path array key can be the official field name (`MonsterRecords` / `ProxyRecords`) or the generic `paths`.
- The trailing value key can be `KillCount` / `trailing` / `count`, case-insensitive.
- When the path array is empty, only the trailing value is written (KillCount=0).

### 9.2 Raw Type Style

```json
{ "type": 10, "args": { "paths": ["records/monsters/a.dbr"], "trailing": 3 }, "cmp": ">=" }
```

### 9.3 An Easy Pitfall: Positional Arrays Cannot Set KillCount

The following spelling **only sets the paths, and KillCount stays 0 forever**:

```json
{ "HasKilled": [["records/monsters/a.dbr"]] }   // KillCount = 0
```

This happens because item 0 of the positional arguments is taken as the path array, and the trailing value has no second entry point. To set the trailing value, use the named object above, or wrap it in an object:

```json
{ "HasKilled": [{ "paths": ["records/monsters/a.dbr"], "trailing": 3 }] }
```

### 9.4 Path Contents

- Paths are saved into the file verbatim; the tool does not check existence and does not complete extensions.
- Official files usually use relative paths such as `records/monsters/xxx.dbr`.
- The order of the path array is written as-is; whether order carries meaning is up to the game.

---

## 10. L Form (Lua)

`L` is used by condition 12 and action 11.

### 10.1 Recommended: Dedicated Object

Condition:

```json
{
  "LuaScript": {
    "call": "gd.example.check()",
    "args": ["arg1", "arg2"],
    "tail": { "t0": 0, "t1": 0, "t2": 0, "t3": 0, "text": "TRUE" }
  }
}
```

Action:

```json
{ "LuaScript": { "call": "gd.example.run()", "args": ["arg1"] } }
```

Requirements:

- The outer key is the official type name `LuaScript`.
- `call` / `function`: the function name or call string; **writing lowercase `call` is recommended**.
- `args`: an array of strings; each string is written as one argument, and the remaining numeric fields are written as 0.
- `tail`: valid for conditions only; it can be omitted entirely (the tool writes all 0 + an empty string).
- The keys inside `tail` are exact lowercase `t0`, `t1`, `t2`, `t3`, `text`.

### 10.2 Simplified Style: Function Name Only

```json
{ "LuaScript": "gd.example.check()" }
{ "type": 12, "args": "gd.example.check()" }
```

Only the function name is given: Args is empty and the condition tail is all 0/empty.

### 10.3 Raw Form

```json
{
  "type": 12,
  "args": {
    "call": "gd.example.check()",
    "args": ["arg1"],
    "tail": { "t0": 0, "t1": 0, "t2": 0, "t3": 0, "text": "TRUE" }
  }
}
```

### 10.4 Tail Fields (Evaluate Area)

The official editor shows the condition tail as the **Evaluate** area: a return-type dropdown, a comparator dropdown, and an expected value. The bit-by-bit correspondence between `t0..t3` and these three is not yet fully confirmed, therefore:

- when creating a new ordinary Lua condition, `tail` can be omitted and the tool writes all 0/empty string;
- if you export JSON from an existing file and then modify it, keep the original `tail` values;
- do not fill in `t0..t3` by guessing.

### 10.5 Common Mistakes

- `{"LuaScript": {"Function": "gd.x()", "args": ["a"]}}`: `Function` is treated as a single string and `args` is lost. Use `call` instead.
- Writing `tail` in an action L form: it is ignored (actions have no tail).
- Putting numbers in `args`: non-string items become empty strings.

---

## 11. Reference Values, Paths, and Strings

### 11.1 Quest Reference Values

Fields referring to `Task` / `Objective` in conditions/actions take the **persisted uids saved in the QST file**:

- `Task` = the QST's `tasks[].uid`.
- `Objective` = the QST's `tasks[].objectives[].uid`.

These values are neither array indices nor hashes of quest names (although the official editor displays them as `?` or hexadecimal). When generating a QST, write the `uid` explicitly and then reference the same number in the CNV:

QST:

```json
{ "uid": 305419896, "title": "…", "objectives": [ { "uid": 2271560481, "text": "…" } ] }
```

CNV:

```json
{ "BeginQuestTask": ["quests/example.qst", 305419896] }
{ "HasQuestObjective": { "Quest": "quests/example.qst", "Task": 305419896, "Objective": 2271560481, "Satisfied": true } }
```

> If a QST `uid` is omitted, the tool generates a random non-zero value; the output is not reproducible and cannot be referenced in a CNV in advance. **Always write uids explicitly when cross-file references are needed.**

### 11.2 Path Strings

| Field | Base directory (official behavior) | Example |
|---|---|---|
| `QuestFile` / `Quest` | `resources/` | `quests/example.qst` |
| `vo` (CNV voice-over) | `resources/` | `conversations/alex_01.ogg` |
| `ItemRecord`, `SoundRecord`, `SkillRecord`, `BuffName` | game database (`database/`) | `records/items/gear/example.dbr` |
| `MonsterRecords`, `ProxyRecords` | game database (`database/`) | `records/monsters/example.dbr` |
| `Video path` | `resources/` or the video directory | `video/intro.ivf` |

The tool **writes strings into the file verbatim**; it does not validate existence and does not normalize paths. Use forward slashes `/` and match the actual in-game paths.

### 11.3 String Encoding and Special Characters

- Strings inside the file are UTF-8; the JSON source file should also be UTF-8.
- Chinese, Japanese, and accented characters can all be written directly.
- The JSON escape `\n` writes a real newline byte; the in-game line break convention is the literal `{^n}`, so write `{^n}` in both `text` and `languages.texts`.
- Backslashes in paths must be JSON-escaped (`\\`); using `/` directly is recommended.

---

## 12. Complete Working Example

The JSON below covers most features of this document and can be run as-is:

```json
{
  "format": "cnv",

  // 6: supports vo; 5: does not support vo.
  "version": 6,

  // Omitting id generates one from speaker automatically; here 0 is given explicitly.
  "id": 0,

  // Root node text (slot 0).
  "speaker": "Alex",

  // Root flags: 0 none; 1 once only; 2 always show; 4 unimportant.
  "flags": 0,

  // Root voice-over path (version>=6).
  "vo": "",

  // Root condition relationship and list.
  "logic": "AND",
  "cond": [
    { "HasToken": ["MET_ALEX", true] }
  ],
  "act": [
    { "GiveToken": ["MET_ALEX"] }
  ],

  "rows": [
    {
      "level": 1,
      "kind": "speech",
      "text": "You finally made it.",
      "flags": 0,
      "vo": "conversations/alex_01.ogg",
      "logic": "AND",
      "cond": [
        { "HasFaction": ["SURVIVORS", 5000], "cmp": ">=" }
      ],
      "act": [
        { "GiveMoney": [500] },
        { "GiveExperience": [1200] }
      ]
    },

    { "level": 2, "kind": "continue", "text": "Continue" },
    {
      "level": 3,
      "kind": "accept",
      "text": "I accept.",
      "act": [
        { "BeginQuestTask": ["quests/example.qst", 305419896] },
        { "GiveItem": ["records/items/gear/example.dbr", 1] }
      ]
    },
    { "level": 3, "kind": "decline", "text": "Some other time." },
    { "level": 3, "kind": "end", "text": "End" },

    // Jump to row 5 of rows (the end node above).
    { "level": 1, "kind": "link", "text": "", "link": 5 },

    // Random branch.
    { "level": 1, "kind": "random", "text": "Random greeting" },
    {
      "level": 2,
      "kind": "speech",
      "text": "Random result A",
      "logic": "OR",
      "cond": [
        { "IsDifficulty": [2], "cmp": "=" },
        { "HasMoney": [1000], "cmp": ">" }
      ]
    },
    { "level": 2, "kind": "speech", "text": "Random result B" },
    { "level": 2, "kind": "end", "text": "Random end" }
  ]
}
```

For a complete example (covering every field, every node type, the 30 conditions, the 27 actions, and `languages`), see [`cnv_full_en.json`](cnv_full_en.json), which can be run as-is:

```bash
GDCnvQstEditor-cli.exe convert cnv_full_en.json -o demo.cnv
```

That file starts with an overview of the fields, every field in the body carries an inline comment, and each line of `languages` is annotated with the node it belongs to.

---

## 13. CLI Command Reference

`GDCnvQstEditor.exe` and `GDCnvQstEditor-cli.exe` have identical commands; the examples below use `GDCnvQstEditor-cli.exe`. To view the full usage:

```bash
GDCnvQstEditor-cli.exe help
```

### 13.1 Generate

```bash
GDCnvQstEditor-cli.exe convert <spec.json> [-o out.cnv]
```

- `make` is a compatible alias of `convert`, so old scripts still work.
- The input extension must be `.json` (the content may contain `//`, `/* */` comments and trailing commas).
- `-o` also accepts `--out`.
- Omitting `-o`: the output goes to a `.cnv` with the same name in the same directory as the JSON (`hello.cnv.json` → `hello.cnv`).
- After generation the file is read back and verified automatically; seeing `Three checks passed` means the file structure is complete.

### 13.2 Templates

```bash
GDCnvQstEditor-cli.exe sample cnv                    # extract cnv_full_en.json (English CLI) or cnv_full.json (Chinese CLI) into the current directory
GDCnvQstEditor-cli.exe sample cnv -o D:\mydir        # extract into the given directory
```

`sample` extracts the built-in full example in the current language: `cnv_full_en.json` / `qst_full_en.json` (English comments) for English, `cnv_full.json` / `qst_full.json` otherwise. Each includes per-field descriptions and can be used directly as `convert` input or as an editing starting point.

### 13.3 View and Verify

```bash
GDCnvQstEditor-cli.exe view   <file.cnv>       # editor-style tree text
GDCnvQstEditor-cli.exe verify <file or directory>      # check the file structure
```

### 13.4 Same Commands in the GUI

```bash
GDCnvQstEditor.exe convert hello.json -o hello.cnv
```

Launching without arguments opens the graphical interface.

---

## 14. Common Errors

| Message | Cause |
|---|---|
| `spec is missing "format" (cnv or qst)` | The top level has no `format`. |
| `cnv spec is missing the "rows" array` | The top level lacks `rows`, or `rows` is not an array. |
| `row N is missing level` | A `rows` element has no `level`. |
| `row N: level must be ≥1` | `0` or a negative number was written. |
| `row N: level=X cannot find a parent row with level=X-1` | A level was skipped, or the first row is not 1. |
| `row N is missing kind` | There is no `kind`. |
| `row N: unknown kind: xxx` | The `kind` string is not in the table. |
| `row N: cond must be an array` | `cond` was written as an object/string. |
| `row N: every item in cond must be an object` | An array element is not an object. |
| `unknown type name "xxx"` | A named type is misspelled; the error message lists the available names. |
| `condition type N out of range (valid range 0–29)` | `type` is outside 0–29. |
| `action type N is not implemented (factory returned null)` | The action type uses 0/16, or is out of range. |
| `comparator N out of range (0..5)` | The `cmp` number is out of range. |
| `unknown comparator "xxx"` | The `cmp` string is not recognized. |
| `logic accepts only AND/OR` | The `logic` string is wrong. |
| `CNV version must be ≥5` | `version` is too small. |

---

## 15. Limitations and Common Pitfalls

### 15.1 Structural Limitations

1. **Only one tree**: all `rows` hang below the root node; multiple roots cannot be created.
2. **Row order must be pre-order**: text slots are written in row order while nodes are flattened by tree, so manually reordering rows breaks the text correspondence.
3. **Nesting depth**: the limit is 512; real conversations should stay within a few dozen levels.
4. **Language block line counts must align with nodes**: each explicit `languages` block has `1 + rows.Length` lines, and all blocks must have the same count in a multi-language file.
5. **link targets**: the value is a flattened index (1-based row number, 0 for the root); confirm that the target exists.

### 15.2 Condition/Action Limitations

1. **30 conditions and 27 actions are available**; actions 0/16 cannot be written.
2. **Do not hand-write `subType`**: the default value is already correct, and a wrong change makes the official editor read the wrong fields.
3. **Do not let enum fields go out of range**: the official editor uses them as table indices, and out-of-range values cause errors.
4. **Boolean/quest step conditions do not need `cmp`**: the official editor fixes them at `=`.
5. **Actions have no `cmp` and no AND/OR**: they execute in array order.
6. **Positional arrays in the P form do not set KillCount**: see §9.3.
7. **Lua condition tails are not fully confirmed**: when unsure, omit `tail` or keep the exported values as-is.
8. **Position of the named type**: it must be the first non-metadata key.

### 15.3 Paths and References

1. The tool does **not check** whether `.qst`, `.dbr`, or audio files exist.
2. `Task` / `Objective` references must match the persistent uids in the QST file exactly; random uids cannot be referenced in advance.
3. Paths are written verbatim, so casing, slash direction, and extensions are your responsibility.
4. To **modify an existing file**, open it directly with `GDCnvQstEditor.exe`; hand-written JSON suits creating files from scratch.

---

## Appendix: Quick Checklist

After writing, confirm each item:

- [ ] `format` is `"cnv"`.
- [ ] `version` is ≥5; 6 is recommended.
- [ ] Every row has `level` and `kind`; the first row has `level=1`, and no level is skipped.
- [ ] `rows` is in pre-order.
- [ ] The target of every `link` is within `0..rows.Length`.
- [ ] Condition/action type names are spelled correctly, and the type name is the first non-metadata key in the object.
- [ ] When `languages` is present, each block's `texts` has `1 + rows.Length` lines and the `link` line is left empty; line breaks use `{^n}`.
- [ ] Conditions that need a comparison have `cmp`; boolean/quest step conditions keep the default.
- [ ] `subType` is not hand-written; `CompleteOnly` on `HasItem` and `Objective` on `HasQuestObjective` follow this document.
- [ ] The P form writes `KillCount` with an object, not a positional array.
- [ ] Cross-file `Task` / `Objective` references match the QST `uid` values.
- [ ] After running `convert`, the three checks pass, then spot-check with `view` / the official editor.
