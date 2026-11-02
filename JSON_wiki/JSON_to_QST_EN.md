> **AI TRANSLATION NOTICE: This document is AI-translated. If you find any ambiguity or discrepancy, the Chinese version ([JSON转QST.md](JSON转QST.md)) is authoritative.**

# JSON → QST User Manual

This document explains how to **hand-write JSON to generate Grim Dawn `.qst` quest files**.

- Tools:
  - `GDCnvQstEditor.exe`: graphical interface; when launched with arguments it runs the same CLI commands.
  - `GDCnvQstEditor-cli.exe`: pure command line, suitable for scripts and batch processing.
  - Both programs accept exactly the same commands; the examples below always use `GDCnvQstEditor-cli.exe`.
- Companion example:
  - [`qst_full_en.json`](qst_full_en.json) — **one file covering everything**: top-level fields, every field of task / incentive / objective / event, all 30 conditions, 27 usable actions, the `languages` block, persistent uids, with an inline note for every field.
- For how to write conversation files, see [`JSON_to_CNV_EN.md`](JSON_to_CNV_EN.md); both file kinds share the same condition/action system.

> Goal: after reading this document you can write valid QST JSON without the GUI and generate a `.qst` that the game and the official QuestEditor can read.

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [JSON Authoring Rules](#2-json-authoring-rules)
3. [Top-Level Fields](#3-top-level-fields)
4. [tasks: Tasks](#4-tasks-tasks)
5. [incentives / objectives / events](#5-incentives--objectives--events)
6. [Localization Text Slots](#6-localization-text-slots-the-most-critical-part)
7. [uid and Cross-File References](#7-uid-and-cross-file-references)
8. [Four Ways to Write Conditions/Actions](#8-four-ways-to-write-conditionsactions)
9. [The Comparison Operator cmp](#9-the-comparison-operator-cmp)
10. [Condition Reference (30 Types)](#10-condition-reference-30-types)
11. [Action Reference (27 Available)](#11-action-reference-27-available)
12. [P / L Forms](#12-p--l-forms)
13. [Path and String Conventions](#13-path-and-string-conventions)
14. [Complete Runnable Example](#14-complete-runnable-example)
15. [CLI Command Reference](#15-cli-command-reference)
16. [Common Errors](#16-common-errors)
17. [Limitations and Common Pitfalls](#17-limitations-and-common-pitfalls)
- [Appendix: Quick Checklist](#appendix-quick-checklist)

---

## 1. Quick Start

### 1.1 Minimal Runnable JSON

Create `hello.qst.json`:

```json
{
  // "qst": generate a quest file. "cnv": generate a conversation file.
  "format": "qst",
  // 9: recommended version.
  "version": 9,
  "group": "MY_GROUP",
  "name": "MY_QUEST",
  "tasks": [
    {
      "title": "First Task",
      "desc": "Quest description.",
      "objectives": [
        {
          "text": "Reach level 10",
          "cond": [ { "IsLevel": [10], "cmp": ">=" } ],
          "act": [ { "Notification": ["tagObjectiveDone"] } ]
        }
      ]
    }
  ]
}
```

### 1.2 Generating

```bash
GDCnvQstEditor-cli.exe convert hello.qst.json -o hello.qst
```

Output:

```text
Generated hello.qst (... bytes, format=qst)
Three checks passed: tiled / locOffset / round-trip
```

If `-o` is omitted, the output path is the same directory and the same base name as the JSON file, with a `.qst` extension (`hello.qst.json` → `hello.qst`).

### 1.3 Checking the Output

```bash
GDCnvQstEditor-cli.exe view hello.qst
GDCnvQstEditor-cli.exe verify hello.qst
```

You can also open the file directly with `GDCnvQstEditor.exe`, or cross-check it in the official QuestEditor.

---

## 2. JSON Authoring Rules

The file extension is always `.json`; the parser accepts comments and trailing commas, which makes hand-writing easier:

| Feature | Supported | Notes |
|---|---|---|
| `//` line comments | Yes | |
| `/* … */` block comments | Yes | |
| Trailing commas | Yes | |
| UTF-8 BOM | Yes | |
| Single-quoted strings | **No** | double quotes required |
| Unquoted keys | **No** | |
| Multi-line strings | **No** | use `\n` |
| `NaN` / `Infinity` | **No** | |

### 2.1 Key Name Case

- The JSON key names listed in this document must be written exactly as shown (`format`, `tasks`, `objectives`, `title`, etc.); they are case-sensitive.
- The official type names of conditions/actions are case-insensitive.
- Named-argument field names are case-insensitive.
- Lua's `call` / `args` / `tail` and their inner keys use exact lowercase.

### 2.2 Strings and Numbers

- Strings are written to the file as UTF-8, so non-ASCII text (Chinese, for example) can be typed directly.
- `u32` fields must be **integers in 0…4294967295**; do not write decimals or negative numbers.
- `u8` fields must be **integers in 0…255**; boolean fields accept `true`/`false` or `1`/`0`.

---

## 3. Top-Level Fields

```json
{
  "format": "qst",
  "version": 9,
  "flags": 0,
  "id": 0,
  "group": "MY_GROUP",
  "name": "MY_QUEST",
  "languages": [
    { "tag": "enUS", "texts": ["MY_GROUP", "MY_QUEST", "Title", "Description", "Objective text"] }
  ],
  "tasks": [ /* … */ ]
}
```

| Key | Type | Required | Default | Notes |
|---|---|---|---|---|
| `format` | string | **Yes** | — | Must be `"qst"` (case-insensitive). |
| `version` | u32 | No | `9` | Must be ≥7. `9` is recommended. |
| `flags` | u32 | No | `0` | Quest type flag bitmask, see §3.1. |
| `id` | u32 | No | random non-zero when omitted | **This file's own ID**. If written explicitly it is used as-is (including `0`); only complete omission generates a random value. Unrelated to task uids. |
| `group` | string | No | `""` | Quest group name, localization slot 0. |
| `name` | string | No | `""` | Quest name, localization slot 1. |
| `languages` | array | No | — | Explicit language blocks, see §3.3. When present they take precedence; `group`/`name`/`title` etc. are only fallbacks. |
| `lang` | string | No | `"enUS"` | Single-language block tag (legacy form). Only effective when `languages` is **absent**; `language` is also accepted. |
| `tasks` | array | **Yes** | — | Quest array; missing means an error. |

### 3.1 Header `flags` (Quest Type)

Official `Common.Quest+Flag`:

| Value | Name | Meaning |
|---:|---|---|
| 0 | None | No flag. |
| 1 | Primary | Main quest. |
| 2 | Secondary | Side quest. |
| 4 | Repeatable | Repeatable. |
| 3 / 5 / 6 / 7 | Combination | Bitwise OR; the official editor normally lets you pick only one value, but writing a combination is legal. |

### 3.2 `id` vs `group` / `name`

- `id` is **this file's own ID**; quest references **do not** use it.
  - **Written in the JSON: written as-is**, including an explicit `0`;
  - **Omitted entirely: a random non-zero value is generated**.
- When modifying an existing quest, always copy the original QST's `id` explicitly into the JSON; otherwise regenerating the file replaces the file ID.
- This rule applies only to this file's `id`. **Other file paths and IDs** referenced from conditions/actions are always written exactly as you specify; the tool never generates or rewrites them.
- `group` / `name` are not required, but even when empty each still occupies one localization slot (slot 0 and slot 1).
- The official editor groups its task list by group name and displays quest names; fill both in for new quests.

### 3.3 `languages` and Localization

It is recommended to describe language blocks explicitly with `languages`, which lets you keep several languages in one JSON:

```json
"languages": [
  { "tag": "enUS", "texts": ["Group name", "Quest name", "Title", "Description", "Objective text"] }
]
```

| Key | Type | Notes |
|---|---|---|
| `tag` | string | Language tag. 6 official ones: `enUS` / `enGB` / `es` / `fr` / `de` / `it`; other tags produce a warning, and the game may ignore that block. |
| `texts` | array | Array of text rows; **the row count must exactly match the number of localization slots**. |

The QST slot formula and ordering are described in [§6](#6-localization-text-slots-the-most-critical-part):

```text
total = 2 (group name, quest name)
      + Σ per task [ 2 (title, description) + incentives + objectives + events ]
```

- Every language block must have the same number of `texts` rows, equal to the formula result above.
- The official slots for incentive / event are empty strings, but the empty string must still occupy a row.
- If the row count does not match, `convert` fails immediately and refuses to generate, for example:

```text
4 tasks (titles/descriptions/events/objectives combined), text slots should be 24 lines, languages[enUS] has 23 lines, missing 1 line.
Missing slots: 24=…
Slot order: 1=group quest group name, 2=name quest name, 3=T1 title, 4=T1 description…
```

- **In-game line breaks are written as `{^n}`** (four literal characters), for example `"First line{^n}Second line"`; do not use the JSON escape `\n`.
- Once `languages` is present, `group` / `name` / `title` / `desc` / `text` must still be written (they define the structure), but the output follows `languages`.
- With only one language block you can keep using the legacy form `"lang": "enUS"`; the text is then collected automatically from the individual fields.

```json
// Legacy form: single language, tag defaults to enUS
"lang": "enUS"
```

- The tag is just a string; tags other than the 6 official ones produce a warning but do not block generation.

---

## 4. `tasks`: Tasks

```json
{
  "uid": 305419896,
  "title": "Quest title",
  "desc": "Quest description.",
  "incentives": [ /* OnAccept events */ ],
  "objectives": [ /* objectives */ ],
  "events": [ /* OnComplete events */ ]
}
```

| Key | Type | Required | Default | Notes |
|---|---|---|---|---|
| `uid` | u32 | No | random non-zero | **Persistent task uid**. Used by cross-file references, see §7. `0`/omitted generates a random value. |
| `title` | string | No | `""` | Quest title, occupies one localization slot. |
| `desc` | string | No | `""` | Quest description, occupies one localization slot. |
| `incentives` | array | No | `[]` | Triggered when the quest is accepted (the editor's OnAccept). Each element occupies one empty text slot. |
| `objectives` | array | No | `[]` | Quest objectives. Each element occupies one text slot. |
| `events` | array | No | `[]` | Triggered when the quest is completed (the editor's OnComplete). Each element occupies one empty text slot. |

> `unknownAC`, `flag8` and `flag9` are compatibility fields in the format whose semantics are not yet confirmed. Just omit them (default `0`/`false`); do not fill them in at random just to “complete” the structure.

### 4.1 Task Count Limit

A single file holds at most **4096 tasks**. Beyond that:

```text
task count N exceeds the limit 4096
```

objectives / incentives / events have no separate hard limit, but do not build enormous arrays in practice.

### 4.2 Random Generation of task uids

- When `uid` is omitted or `0`, the tool generates a random non-zero value.
- The random value differs on every run, so the output is not reproducible.
- As soon as you need cross-file references (CNV's `BeginQuestTask`, `HasQuestObjective`, etc.), you **must write uid explicitly**.

---

## 5. `incentives` / `objectives` / `events`

### 5.1 incentives (OnAccept)

A group of conditions/actions triggered when the player accepts the quest:

```json
"incentives": [
  {
    "logic": "AND",
    "cond": [],
    "act": [
      { "Notification": ["tagQuestAccepted"] }
    ]
  }
]
```

| Key | Type | Default | Notes |
|---|---|---|---|
| `logic` | string / u32 | `"AND"` | Relationship of the condition list: `"AND"`=0, `"OR"`=1. |
| `cond` | array | `[]` | Condition list; an empty array means it triggers unconditionally. |
| `act` | array | `[]` | Action list, executed in order. |

- Each incentive occupies one localization slot (the slot is empty; the tool fills in an empty string automatically).
- incentives have no persistent uid; do not try to write `uid` in the JSON (it is ignored).

### 5.2 objectives (Objectives)

```json
"objectives": [
  {
    "uid": 2271560481,
    "text": "Collect 5 items",
    "logic": "AND",
    "cond": [
      { "HasItem": ["records/items/quest/example.dbr", 5, false], "cmp": ">=" }
    ],
    "act": [
      { "Notification": ["tagObjectiveComplete"] }
    ]
  }
]
```

| Key | Type | Default | Notes |
|---|---|---|---|
| `uid` | u32 | random non-zero | **Persistent objective uid**. Referenced by `HasQuestObjective` and similar. |
| `text` | string | `""` | Objective text, occupies one localization slot. |
| `logic` | string / u32 | `"AND"` | Relationship of the condition list. |
| `cond` | array | `[]` | Conditions that decide whether the objective is complete/interactable. |
| `act` | array | `[]` | Actions executed when the objective's conditions are met. |

- An objective `uid` that is omitted/0 is generated randomly; when other files reference it, it must be written explicitly.
- An objective's `logic` only affects `cond`; it is not a quest property.
- An objective has no `title` / `desc`, only a single `text`.
- `unknown1` is a compatibility field; just omit it.

### 5.3 events (OnComplete)

A group of conditions/actions triggered when the player completes the quest; the structure is the same as incentive:

```json
"events": [
  {
    "logic": "AND",
    "cond": [
      {
        "HasQuestObjective": {
          "Quest": "quests/example.qst",
          "Task": 305419896,
          "Objective": 2271560481,
          "Satisfied": true
        }
      }
    ],
    "act": [
      { "CompleteQuestTask": ["quests/example.qst", 305419896] },
      { "GiveExperience": [2500] },
      { "GiveMoney": [1000] }
    ]
  }
]
```

- Each event occupies one localization slot (the tool fills in an empty string).
- events themselves have no persistent uid; `unknown` is a compatibility field, just omit it.

### 5.4 Consequences of an Incorrectly Typed Array Key

If `incentives` / `objectives` / `events` are written as something other than an array (an object or a string, for example), their contents are **silently ignored** with no error:

```json
"objectives": { "text": "x" }   // treated as having no objectives; no error is reported
```

`cond` / `act` behave differently: a non-array value is an immediate `must be an array` error. After writing a QST, check with `view` that the objective/event counts are what you expect.

---

## 6. Localization Text Slots (The Most Critical Part)

A QST localization table is not “as many entries as there is text”; slots are allocated by a fixed formula. The official QuestEditor indexes by that formula, and a wrong slot count is an immediate error.

### 6.1 Formula

```text
S = 2 + 2T + O + I + E

T = number of tasks
O = total objectives
I = total incentives
E = total events
2 = group name + quest name
```

### 6.2 Slot Order

```text
slot 0 : group
slot 1 : name

then, in task order, for each task:
  title
  description
  incentive slots of this task (1 per incentive, empty)
  objective texts of this task (1 per objective)
  event slots of this task (1 per event, empty)
```

### 6.3 A Complete Example

Assume:

- Task 1: 1 incentive, 2 objectives, 1 event
- Task 2: 0 incentives, 1 objective, 0 events

Calculation:

```text
T = 2
O = 2 + 1 = 3
I = 1 + 0 = 1
E = 1 + 0 = 1
S = 2 + 2*2 + 3 + 1 + 1 = 11
```

Slots:

| Slot | Content |
|---:|---|
| 0 | group |
| 1 | name |
| 2 | Task 1 title |
| 3 | Task 1 description |
| 4 | Task 1 incentive #1 (empty) |
| 5 | Task 1 objective #1 text |
| 6 | Task 1 objective #2 text |
| 7 | Task 1 event #1 (empty) |
| 8 | Task 2 title |
| 9 | Task 2 description |
| 10 | Task 2 objective #1 text |

### 6.4 Generated Automatically: Slot Numbers Are Neither Needed Nor Accepted

The JSON only contains:

```json
"incentives": [ ... ],
"objectives": [ { "text": "..." } ],
"events": [ ... ]
```

The tool fills the empty slots automatically according to the formula and ordering. You **cannot** say “put this text in slot 7” in the JSON; extra keys are ignored as well.

### 6.5 Why incentive/event Text Is Empty

In official files the OnAccept / OnComplete events have no visible text, yet each event still occupies a slot. The tool always writes an empty string. Do not try to stuff text into events — the event object has no `text` key, and anything you write is not saved.

### 6.6 The Number of Text Rows Is Determined by the Formula

- Without `languages`: the tool fills empty slots automatically according to the formula; you **cannot** specify slot numbers, and extra keys are ignored.
- With `languages`: each block's `texts` must be **exactly** the number of rows computed by the formula; too few or too many rows is an error and generation is refused. The tool will not pad or truncate for you.
- To change text, edit `languages.texts` (or the legacy `group` / `name` / `title` / `desc` / `text`); do not add or remove rows in the generated binary text table by hand.

---

## 7. uid and Cross-File References

> **The most important rule when editing an existing quest: first distinguish “this file's own IDs” from “reference IDs”.**
>
> - **This file's own IDs**: the root-level `id`, plus the `uid` of each task / objective. They are defined in the current file, and the values you write are preserved (exception: `0` on a task / objective counts as unset).
> - **Root-level `id`**: if written in the JSON it is used as-is (including an explicit `0`); only complete omission generates a random non-zero value.
> - **task / objective `uid`**: an explicitly written non-zero value is preserved as-is; omitted or `0` counts as unset and a random value is generated.
> - **Reference IDs**: `QuestFile` / `Task` / `Objective` etc. in condition/action arguments are IDs already stored in the **target file**.
>   The tool does not generate, guess or rewrite them — copy them exactly; a wrong value prevents the quest from progressing.
>
> So when editing an existing quest, always note the original file's root-level `id` and the task / objective `uid`s you use,
> then write them back into the JSON explicitly instead of relying on automatic generation.

### 7.1 Which IDs Are Written to Disk

| Object | JSON key | Purpose |
|---|---|---|
| task | `tasks[].uid` | Referenced by BeginQuestTask / CompleteQuestTask / QuestTaskComplete / OnQuestTask etc. |
| objective | `tasks[].objectives[].uid` | Referenced by HasQuestObjective |
| file | root-level `id` | **This file's own ID**; quest references do not use it. Written when present (including 0), random only when omitted |

- incentives / events have no persistent uid; do not write one and do not reference one.
- Within one QST file, task uids should all differ, and objective uids should all differ too. The tool does not check for duplicates.

### 7.2 Reference Example

QST file A (`quests/example.qst`):

```json
{
  "format": "qst",
  "group": "EXAMPLE_GROUP",
  "name": "EXAMPLE_QUEST",
  "tasks": [
    {
      "uid": 305419896,
      "title": "Task One",
      "objectives": [
        { "uid": 2271560481, "text": "Collect 5 items" }
      ]
    }
  ]
}
```

A CNV referencing it:

```json
"act": [
  { "BeginQuestTask": ["quests/example.qst", 305419896] }
],
"cond": [
  {
    "HasQuestObjective": {
      "Quest": "quests/example.qst",
      "Task": 305419896,
      "Objective": 2271560481,
      "Satisfied": true
    }
  }
]
```

Events inside the QST can also self-reference:

```json
"events": [
  {
    "cond": [
      { "HasQuestObjective": { "Quest": "quests/example.qst", "Task": 305419896, "Objective": 2271560481, "Satisfied": true } }
    ],
    "act": [
      { "CompleteQuestTask": ["quests/example.qst", 305419896] }
    ]
  }
]
```

### 7.3 Choosing and Preserving uids

- The official QuestEditor uses random uids when creating tasks, so random values are themselves legal.
- **When editing an existing quest, the task / objective uids must be preserved explicitly**; otherwise conditions/actions that reference them stop working.
- To keep the output reproducible and referenceable across files, specify them explicitly in the new JSON, for example `305419896`, `2271560481`.
- task / objective uids cannot be 0: 0 is treated as “unset” and replaced by a random value.
- The root-level `id` is the exception: it takes no part in quest references, so **an explicit `0` is also preserved**; only complete omission generates a random value.

Minimal form for preserving an existing quest's IDs:

```json
{
  "format": "qst",
  "version": 9,
  "id": 305419896,          // this file's ID copied from the original .qst: kept as-is
  "group": "OLD_GROUP",
  "name": "OLD_QUEST",
  "tasks": [
    { "uid": 2271560481,    // task uid copied from the original .qst: keeps referencing conditions/actions working
      "title": "Old quest title", "desc": "Old quest description",
      "objectives": [ { "uid": 1234567890, "text": "Old objective text" } ] }
  ]
}
```

---

## 8. Four Ways to Write Conditions/Actions

QST and CNV use exactly the same condition/action system and syntax.

### 8.1 Form A: Named Type + Positional Arguments

```json
{ "HasFaction": ["SURVIVORS", 5000], "cmp": ">=" }
{ "GiveMoney": [500] }
```

> **The value must be an array or an object.** A bare scalar is not read: for example `{"GiveMoney": 500}` yields 0,
> and `{"IsHardcore": true}` yields `false`. Write `{"GiveMoney": [500]}` / `{"IsHardcore": [true]}` instead.
> The only exception is `LuaScript`, which accepts a bare function-name string (see §12.2).

The type name must be the first “non-metadata” property in the object. Metadata keys:

```text
subType, cmp, comparator, comparison, uid, args
```

Do not put keys such as `logic` or `note` before the type name. It is best to put the type name on the first line.

### 8.2 Form B: Named Type + Named Arguments

```json
{ "HasFaction": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
{ "GiveItem": { "ItemRecord": "records/items/a.dbr", "NumItems": 2 } }
```

- Field names are case-insensitive;
- Missing fields are filled with zero values;
- Extra fields are ignored;
- See §12 for the P/L forms.

### 8.3 Form C: Raw Type Number

```json
{ "type": 6, "args": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `type` uses the type numbers from the condition/action tables (conditions 0–29, actions 1–28, with 0/16 unusable).
- Normally the official type names are all you need; raw type numbers suit bulk generation or cases that need precise control.

### 8.4 Form D: Raw Type Number + Named Arguments

```json
{ "type": 6, "args": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

### 8.5 Precedence and Conflicts

- If both `type` and a named type are present, `type` wins.
- If both a named type and `args` are present, the named type's values win.
- The legacy field `uid` is not a comparator; only `cmp` / `comparator` / `comparison` are recognized.
- `cmp` is ignored for actions.
- Parameterless conditions (WaitForCompletion) can be written as `{}`, `[]`, or just `{"type":17}`.
- `subType` is an advanced compatibility field whose default is already correct; do not write it unless you specifically know you need to.

### 8.6 `logic` and Condition Lists

`logic` applies only to `cond`, never to `act`:

```json
{ "logic": "OR", "cond": [ … ] }   // any condition satisfied
{ "logic": "AND", "cond": [ … ] }  // all satisfied (default)
```

- `logic` accepts the strings `"AND"`/`"OR"` (case-insensitive), or the numbers 0/1.
- When the condition list is empty, `logic` has no effect.
- Actions have no AND/OR; they run in array order.

---

## 9. The Comparison Operator `cmp`

`cmp` is the `Comparator` dropdown in the official editor.

| Number | String (case-insensitive) | Symbol | Official enum |
|---|---|---|---|
| 0 | `lt` / `less` / `<` | `<` | Less |
| 1 | `le` / `lessequal` / `<=` | `<=` | LessEqual |
| 2 | `eq` / `equal` / `=` / `==` | `=` | Equal (default) |
| 3 | `ge` / `greaterequal` / `>=` | `>=` | GreaterEqual |
| 4 | `gt` / `greater` / `>` | `>` | Greater |
| 5 | `ne` / `notequal` / `!=` / `<>` | `!=` | NotEqual |

```json
{ "HasMoney": [1000], "cmp": ">=" }
{ "HasMoney": [1000], "comparator": 3 }
{ "HasMoney": [1000], "comparison": "GreaterEqual" }
```

The three key names are equivalent, checked in the order `cmp` → `comparator` → `comparison`; when none is written the default is `2` (`=`).

**Which conditions use `cmp`**:

- Those with numeric comparison semantics: HasExperience, IsLevel, IsDifficulty, HasFaction, HasMoney, HasItem, HasKilled, UsedQuestItem, HasKilledProxy, CheckRandomValue, OwnsDLC, HasTribute, EarnedDevotion, IsChallenge, HasKilledMonsterClass, HasKilledFaction.
- Boolean conditions, quest-step conditions and Lua conditions: the official dialog shows no Comparator, so leave the default `=`.
- Actions: no `cmp`.

---

## 10. Condition Reference (30 Types)

Condition type numbers range from **0–29**, 30 types in total. Shared by QST and CNV. The “JSON fields” column lists the fields in positional order.

| type | Official name | JSON fields (positional order) | Notes / enums | Example |
|---:|---|---|---|---|
| 0 | OnQuest | `QuestFile`, `OnQuest` | quest file string + boolean | `{"OnQuest":["quests/a.qst",true]}` |
| 1 | QuestTaskComplete | `QuestFile`, `Task`, `TaskComplete` | `Task` is the task uid | `{"QuestTaskComplete":["quests/a.qst",305419896,true]}` |
| 2 | HasExperience | `Experience` | used with `cmp` | `{"HasExperience":[10000],"cmp":">="}` |
| 3 | IsLevel | `IsLevel` | used with `cmp` | `{"IsLevel":[10],"cmp":">="}` |
| 4 | IsDifficulty | `IsDifficulty` | 0 Normal / 1 Epic / 2 Legendary | `{"IsDifficulty":[2],"cmp":"="}` |
| 5 | IsHardcore | `IsHardcore` | boolean | `{"IsHardcore":[true]}` |
| 6 | HasFaction | `FactionName`, `Value` | used with `cmp` | `{"HasFaction":["SURVIVORS",5000],"cmp":">="}` |
| 7 | HasToken | `TokenName`, `HasToken` | boolean | `{"HasToken":["TOKEN_A",true]}` |
| 8 | HasMoney | `MoneyAmount` | used with `cmp` | `{"HasMoney":[1000],"cmp":">="}` |
| 9 | HasItem | `ItemRecord`, `ItemCount`, `CompleteOnly` | `CompleteOnly` may be omitted | `{"HasItem":["records/items/a.dbr",1,false],"cmp":">="}` |
| 10 | HasKilled | `MonsterRecords` (path list), `KillCount` (trailing value) | used with `cmp` | `{"HasKilled":{"MonsterRecords":["records/monsters/a.dbr"],"KillCount":1}}` |
| 11 | HasQuestObjective | `Quest`, `Task`, `Objective`, `Satisfied` | `Task`/`Objective` are persistent uids | see §10.1 |
| 12 | LuaScript (condition) | `call`, `args`, `tail` | see §12.2 | `{"LuaScript":{"call":"gd.x.y()","args":[]}}` |
| 13 | KnowsPlayer | `KnowsPlayer` | boolean | `{"KnowsPlayer":[true]}` |
| 14 | OnQuestTask | `QuestFile`, `Task`, `OnTask` | `Task` is the task uid | `{"OnQuestTask":["quests/a.qst",305419896,true]}` |
| 15 | UsedQuestItem | `ItemRecord`, `NumUsed` | used with `cmp` | `{"UsedQuestItem":["records/items/a.dbr",1],"cmp":">="}` |
| 16 | HasKilledProxy | `ProxyRecords` (path list), `KillCount` (trailing value) | used with `cmp` | `{"HasKilledProxy":{"ProxyRecords":["records/proxies/a.dbr"],"KillCount":1}}` |
| 17 | WaitForCompletion | (none) | no parameters | `{"WaitForCompletion":{}}` |
| 18 | ServerHasToken | `TokenName`, `HasToken` | boolean | `{"ServerHasToken":["TOKEN_B",true]}` |
| 19 | QuestBlocked | `QuestFile`, `QuestBlocked` | the official dialog has two fields with the same name; the positional form is recommended | `{"QuestBlocked":["quests/a.qst",false]}` |
| 20 | AnyoneHasToken | `TokenName`, `HasToken` | boolean | `{"AnyoneHasToken":["TOKEN_C",false]}` |
| 21 | CheckRandomValue | `Name`, `Value` | used with `cmp` | `{"CheckRandomValue":["roll",50],"cmp":"<="}` |
| 22 | OwnsDLC | `DLC_ID` | DLC numeric id, used with `cmp` | `{"OwnsDLC":[1],"cmp":"="}` |
| 23 | HasTribute | `TributeAmount` | used with `cmp` | `{"HasTribute":[10],"cmp":">="}` |
| 24 | EarnedDevotion | `DevotionAmount` | used with `cmp` | `{"EarnedDevotion":[20],"cmp":">="}` |
| 25 | HasMastery | `MasteryName`, `HasMastery` | `MasteryName` index 0–29 (SkillClass01–30) + boolean | `{"HasMastery":[0,true]}` |
| 26 | IsChallenge | `IsChallenge` | 0 Normal Veteran / 1 Epic Veteran / 2 Legendary Veteran | `{"IsChallenge":[2],"cmp":">="}` |
| 27 | HasBuff | `BuffName`, `HasBuff` | buff file string + boolean | `{"HasBuff":["records/buffs/a.dbr",true]}` |
| 28 | HasKilledMonsterClass | `MonsterClass1`, `MonsterClass2`, `KillCount` | 0 Common / 1 Champion / 2 Hero / 3 Boss / 4 Quest / 5 SuperBoss | `{"HasKilledMonsterClass":[0,5,1],"cmp":">="}` |
| 29 | HasKilledFaction | `Faction1`, `Faction2`, `KillCount` | 0 None / 1 Alone / 2 GenericMonster / 3 Player / 4 Survivors / 5 Aetherials / 6 Cthonians / 7 Outlaws / 8 Beasts / 9 User0… | `{"HasKilledFaction":[4,5,100],"cmp":">="}` |

### 10.1 HasQuestObjective in Detail

```json
{
  "HasQuestObjective": {
    "Quest": "quests/example.qst",
    "Task": 305419896,
    "Objective": 2271560481,
    "Satisfied": true
  },
  "cmp": "="
}
```

Positional form (all four values required):

```json
{ "HasQuestObjective": ["quests/example.qst", 305419896, 2271560481, true] }
```

> `Task` / `Objective` must exactly match the uids stored in the QST file. See §7.

### 10.2 Value Ranges for Enum Fields

The official editor uses these fields as indexes into tables; out-of-range values cause errors:

- `IsDifficulty`: 0 / 1 / 2.
- `IsChallenge`: 0 / 1 / 2.
- `HasMastery.MasteryName`: 0–29 (SkillClass01–30).
- `HasKilledMonsterClass.MonsterClass1/2`: 0–5.
- `HasKilledFaction.Faction1/2`: at least 0–9; the official dropdown keeps scrolling further; do not invent values.
- `PlayAnimation.AnimationType`: always 9 (Chat) in the current game; confirm any other value in the official editor first.

### 10.3 Condition Type Out of Range

`type` must be 0–29; outside that range `convert` reports `condition type N out of range (valid range 0–29)`.

---

## 11. Action Reference (27 Available)

Action type numbers range from **0–28**, but **0** and **16** are unusable, leaving **27** available. Actions have no `cmp` and no AND/OR; they run in array order.

| type | Official name | JSON fields (positional order) | Notes | Example |
|---:|---|---|---|---|
| 1 | BeginQuestTask | `QuestFile`, `Task` | Task is the uid | `{"BeginQuestTask":["quests/a.qst",305419896]}` |
| 2 | CompleteQuest | `QuestFile` | completes the whole quest | `{"CompleteQuest":["quests/a.qst"]}` |
| 3 | CompleteQuestTask | `QuestFile`, `Task` | completes the specified task | `{"CompleteQuestTask":["quests/a.qst",305419896]}` |
| 4 | GiveMoney | `MoneyAmount` | grants money | `{"GiveMoney":[500]}` |
| 5 | GiveFaction | `FactionType`, `Amount` | grants faction reputation | `{"GiveFaction":["SURVIVORS",100]}` |
| 6 | GiveItem | `ItemRecord`, `NumItems` | grants an item | `{"GiveItem":["records/items/a.dbr",1]}` |
| 7 | GiveLevel | `NumLevels` | grants levels | `{"GiveLevel":[1]}` |
| 8 | GiveExperience | `Experience` | grants experience | `{"GiveExperience":[1000]}` |
| 9 | GiveToken | `TokenName` | grants a token | `{"GiveToken":["TOKEN_A"]}` |
| 10 | RemoveToken | `TokenName` | removes a token | `{"RemoveToken":["TOKEN_A"]}` |
| 11 | LuaScript (action) | `call`, `args` | no `tail` | see §12.2 |
| 12 | DebugPrint | `Text` | debug output | `{"DebugPrint":["hello"]}` |
| 13 | PlaySound | `SoundRecord` | plays a sound | `{"PlaySound":["records/sounds/a.dbr"]}` |
| 14 | PlayAnimation | `AnimationType` | animation enum, usually 9 in the current game | `{"PlayAnimation":[9]}` |
| 15 | GiveSkillPoint | `NumSkillpoints` | grants skill points | `{"GiveSkillPoint":[1]}` |
| 16 | (unavailable) | — | **do not use** | — |
| 17 | Notification | `Tag` | localization notification tag | `{"Notification":["tagAccepted"]}` |
| 18 | GiveAttribPoint | `NumAttributePoints` | grants attribute points | `{"GiveAttribPoint":[1]}` |
| 19 | ScriptEvent | `ScriptEvent` | script event name | `{"ScriptEvent":["EV_EXAMPLE"]}` |
| 20 | GiveRandomItem | `ItemRecord`, `NumItems` | grants a random item | `{"GiveRandomItem":["records/items/a.dbr",2]}` |
| 21 | GenerateRandomValue | `Name`, `Min`, `Max` | generates a random value | `{"GenerateRandomValue":["roll",1,100]}` |
| 22 | CastSkill | `SkillRecord` | casts a skill | `{"CastSkill":["records/skills/a.dbr"]}` |
| 23 | UnlockFaction | `FactionType` | unlocks a faction | `{"UnlockFaction":["SURVIVORS"]}` |
| 24 | SetFaction | `FactionType`, `Value` | sets a faction value | `{"SetFaction":["SURVIVORS",1000]}` |
| 25 | GiveDevotion | `NumDevotionPoints` | grants devotion points | `{"GiveDevotion":[1]}` |
| 26 | GiveTribute | `TributeAmount` | grants tribute | `{"GiveTribute":[10]}` |
| 27 | UnlockTutorial | `TutorialPage` | unlocks a tutorial page | `{"UnlockTutorial":[1]}` |
| 28 | PlayVideo | `Video path`, `AllowSkip` | plays a video + allows skipping | `{"PlayVideo":["video/intro.ivf",false]}` |

### 11.1 Actions 0 / 16

- Neither type is usable; `convert` reports: `action type N not implemented (factory returned null)`.
- Do not write them; the official editor may also error when opening such a file.

### 11.2 PlayVideo Field Name

The official field name is **`Video path`** (with a space, lowercase `path`). The positional form is recommended:

```json
{ "PlayVideo": ["video/intro.ivf", false] }
```

Named form:

```json
{ "PlayVideo": { "Video path": "video/intro.ivf", "AllowSkip": false } }
```

### 11.3 Action 19 ScriptEvent

```json
{ "ScriptEvent": ["EV_PLAYER_ENTERED_REGION"] }
```

The string is usually a script event name prefixed with `EV_`.

---

## 12. P / L Forms

### 12.1 P Form (Path Lists)

Used by HasKilled (10) and HasKilledProxy (16):

```json
{ "HasKilled": { "MonsterRecords": ["records/monsters/a.dbr", "records/monsters/b.dbr"], "KillCount": 3 }, "cmp": ">=" }
{ "HasKilledProxy": { "ProxyRecords": ["records/proxies/a.dbr"], "KillCount": 1 } }
```

- The path key may be the official field name or the generic `paths`; the trailing key may be `KillCount` / `trailing` / `count` (case-insensitive).
- Raw type form: `{ "type": 10, "args": { "paths": [...], "trailing": 3 } }`.
- **The positional array cannot set KillCount**:

```json
{ "HasKilled": [["records/monsters/a.dbr"]] }                 // KillCount = 0
{ "HasKilled": [{ "paths": ["records/monsters/a.dbr"], "trailing": 3 }] }  // KillCount = 3
```

### 12.2 L Form (Lua)

Recommended condition form:

```json
{
  "LuaScript": {
    "call": "gd.example.check()",
    "args": ["arg1", "arg2"],
    "tail": { "t0": 0, "t1": 0, "t2": 0, "t3": 0, "text": "TRUE" }
  }
}
```

Recommended action form:

```json
{ "LuaScript": { "call": "gd.example.run()", "args": ["arg1"] } }
```

- For `call` / `function`, always write the lowercase `call`; a bare function name also works.
- `args` is an array of strings; each entry writes one argument, and the remaining numeric fields are written as 0.
- `tail` is valid for conditions only; it may be omitted (all zeros / empty string).
- Keys inside `tail` are exactly lowercase: `t0`, `t1`, `t2`, `t3`, `text`.
- The condition tail corresponds to the official editor's **Evaluate** area (return type + comparator + expected value), but the exact one-to-one mapping is not fully confirmed; omit it, or keep the exported values, if you are not sure.
- Do not write `{"Function": "...", "args": [...]}`: `Function` is treated as a single string and `args` is lost; use `call`.

---

## 13. Path and String Conventions

| Field | Base directory (official behavior) | Example |
|---|---|---|
| `QuestFile` / `Quest` | `resources/` | `quests/example.qst` |
| `ItemRecord`, `SoundRecord`, `SkillRecord`, `BuffName` | game database (`database/`) | `records/items/gear/example.dbr` |
| `MonsterRecords`, `ProxyRecords` | game database (`database/`) | `records/monsters/example.dbr` |
| `Video path` | `resources/` or the video directory | `video/intro.ivf` |

- The tool **writes strings verbatim only**: it does not check that files exist, append extensions, or normalize paths.
- Use forward slashes `/`; path case must match the actual files.
- Strings inside the file are UTF-8; save the JSON source as UTF-8 as well.
- `\n` in JSON becomes a real line break; it may be used in `title` / `desc` / `text`, but verify how the game UI renders it.

---

## 14. Complete Runnable Example

The JSON below covers header flags, multiple tasks, OnAccept, objectives, OnComplete, persistent uids, conditions and actions:

```json
{
  "format": "qst",

  // 7/8/9 all work; 9 is recommended.
  "version": 9,

  // Header quest flags: 0 None / 1 Primary / 2 Secondary / 4 Repeatable; they can be added together.
  "flags": 1,

  // This file's own ID: written explicitly it is kept as-is (including 0); only complete omission generates a random one.
  // When editing an existing quest, copy the original file's id here; it takes no part in quest references and is unrelated to task uids.
  // For example: "id": 123456789,

  // Group name + quest name (localization slots 0 and 1).
  "group": "EXAMPLE_GROUP",
  "name": "EXAMPLE_QUEST",

  // Language block tag; defaults to enUS.
  "lang": "enUS",

  "tasks": [
    {
      // Persistent task uid; referenced by Task in CNV/QST.
      "uid": 305419896,

      // Title and description (one localization slot each).
      "title": "Quest title",
      "desc": "This is the quest description.",

      // OnAccept events: each element occupies an empty text slot.
      "incentives": [
        {
          "logic": "AND",
          "cond": [],
          "act": [
            { "Notification": ["tagQuestAccepted"] }
          ]
        }
      ],

      // Objectives: each objective occupies one text slot.
      "objectives": [
        {
          // Persistent objective uid; referenced by HasQuestObjective.
          "uid": 2271560481,
          "text": "Collect 5 items",
          "logic": "AND",
          "cond": [
            { "HasItem": ["records/items/quest/example.dbr", 5, false], "cmp": ">=" }
          ],
          "act": [
            { "Notification": ["tagObjectiveComplete"] }
          ]
        },
        {
          "uid": 286331170,
          "text": "Return to turn in the quest",
          "cond": [
            { "WaitForCompletion": {} }
          ],
          "act": []
        }
      ],

      // OnComplete events: each element occupies an empty text slot.
      "events": [
        {
          "logic": "AND",
          "cond": [
            {
              "HasQuestObjective": {
                "Quest": "quests/example.qst",
                "Task": 305419896,
                "Objective": 2271560481,
                "Satisfied": true
              }
            }
          ],
          "act": [
            { "CompleteQuestTask": ["quests/example.qst", 305419896] },
            { "GiveExperience": [2500] },
            { "GiveMoney": [1000] }
          ]
        }
      ]
    },

    {
      // A new task may omit uid (randomly generated); when editing an existing quest the original uid must be written back explicitly.
      "title": "Second Task",
      "desc": "No OnAccept / OnComplete events.",
      "objectives": [
        {
          "text": "Reach level 10",
          "cond": [
            { "IsLevel": [10], "cmp": ">=" }
          ],
          "act": []
        }
      ]
    }
  ]
}
```

Using this JSON as an example, the text slots are:

```text
T = 2, O = 3, I = 1, E = 1
S = 2 + 2*2 + 3 + 1 + 1 = 11

0 group
1 name
2 Task1 title
3 Task1 description
4 Task1 incentive #1 (empty)
5 Task1 objective #1
6 Task1 objective #2
7 Task1 event #1 (empty)
8 Task2 title
9 Task2 description
10 Task2 objective #1
```

For the complete example (all fields, 30 conditions, 27 actions and `languages`), see [`qst_full_en.json`](qst_full_en.json); it runs directly:

```bash
GDCnvQstEditor-cli.exe convert qst_full_en.json -o demo.qst
```

That file opens with a field overview and the slot formula, every field in the body has an inline comment, and each `languages` row is annotated with its slot.

---

## 15. CLI Command Reference

`GDCnvQstEditor.exe` and `GDCnvQstEditor-cli.exe` accept the same commands; the examples below use `GDCnvQstEditor-cli.exe`. To see the full usage:

```bash
GDCnvQstEditor-cli.exe help
```

### 15.1 Generating

```bash
GDCnvQstEditor-cli.exe convert <spec.json> [-o out.qst]
```

- `make` is a compatibility alias for `convert`; older scripts still work.
- The input extension must be `.json` (the content may contain `//` and `/* */` comments and trailing commas).
- `-o` also accepts `--out`.
- Omitting `-o`: the output goes next to the JSON with the same base name and a `.qst` extension (`hello.qst.json` → `hello.qst`).
- After generating, the tool reads the file back and re-validates; seeing `Three checks passed` means the file structure is intact.

### 15.2 Templates

```bash
GDCnvQstEditor-cli.exe sample qst                    # extract qst_full_en.json (English CLI) or qst_full.json (Chinese CLI) into the current directory
GDCnvQstEditor-cli.exe sample qst -o D:\mydir        # extract into the specified directory
```

`sample` extracts the built-in full example in the current language: `qst_full_en.json` / `cnv_full_en.json` (English comments) for English, `qst_full.json` / `cnv_full.json` otherwise.

### 15.3 Viewing and Verifying

```bash
GDCnvQstEditor-cli.exe view   <file.qst>       # editor-style tree text (includes the text-slot formula)
GDCnvQstEditor-cli.exe verify <file-or-dir>    # check the file structure
```

### 15.4 Same Commands in the GUI

```bash
GDCnvQstEditor.exe convert hello.qst.json -o hello.qst
```

Launching without arguments opens the graphical interface.

---

## 16. Common Errors

| Message | Cause |
|---|---|
| `spec is missing "format" (cnv or qst)` | The top level has no `format`. |
| `qst spec is missing the "tasks" array` | The top level has no `tasks`, or `tasks` is not an array. |
| `task count N exceeds the limit 4096` | Too many tasks. |
| `line N: cond must be an array` | `cond` was written as an object/string. |
| `line N: each item of cond must be an object` | An array element is not an object. |
| `unknown type name "xxx"` | A named type is misspelled; the error lists the available names. |
| `condition type N out of range (valid range 0–29)` | `type` is outside 0–29. |
| `action type N not implemented (factory returned null)` | The action `type` is 0/16, or out of range. |
| `comparator N out of range (0..5)` | The `cmp` number is out of range. |
| `unknown comparator "xxx"` | The `cmp` string is not recognized. |
| `logic accepts only AND/OR` | The `logic` string is wrong. |
| `QST version must be ≥7` | `version` is too small. |

---

## 17. Limitations and Common Pitfalls

### 17.1 Structural Limits

1. **Language block row counts must align with the slots**: each explicit `languages` block must have as many rows as the §6 formula gives; with multiple languages, all blocks must have the same row count.
2. **At most 4096 tasks**.
3. **No automatic resolution of “references between quests”**: `Quest`/`Task`/`Objective` are plain strings/u32 values; the tool does not check whether the file or uid exists.
4. **events / incentives have no persistent uid**: do not write `uid`; it is ignored.
5. **An objective has only one text**: there is no separate title/description.

### 17.2 Text Slots

1. **The slot count is determined by the formula**; slot numbers cannot be specified by hand.
2. **Each incentive/event occupies one empty slot**; text cannot be placed there.
3. **Do not hand-edit the length of the generated text table**; to change text, edit the JSON and regenerate.
4. To **modify an existing file**, it is best to open it directly with `GDCnvQstEditor.exe`; hand-written JSON is suited to building new files from scratch.

### 17.3 uid

1. Omitting task/objective uids generates random values, so the output is not reproducible.
2. Cross-file references must write uids explicitly, and task/objective uids must be unique.
3. A task/objective uid of 0 is replaced by a random value.
4. The root-level `id` is unrelated to task uids: if written in the JSON it is preserved as-is (including 0), and only complete omission makes it random; **when editing an existing quest, always copy the original `id` and `uid`s**.

### 17.4 Conditions/Actions

1. There are 30 conditions and 27 usable actions; actions 0/16 must not be written.
2. Do not hand-write `subType`: the default is already correct, and a wrong value makes the official editor read the wrong fields.
3. Do not let enum fields go out of range.
4. Keep the default `cmp` for boolean/quest-step conditions.
5. In the P form, the positional array does not carry KillCount.
6. The Lua condition tail is not fully confirmed; omit `tail` if you are unsure.
7. The named type name must be the first non-metadata key.

---

## Appendix: Quick Checklist

Check each item after writing the JSON:

- [ ] `format` is `"qst"`.
- [ ] `version` ≥7; 9 is recommended.
- [ ] `flags` is 0/1/2/4 or a combination of them.
- [ ] Every task has `title` / `desc` (they may be empty, but filling them in is recommended).
- [ ] tasks/objectives that need cross-file references have an explicit `uid`.
- [ ] Within one file uids are unique and non-zero.
- [ ] `incentives` / `objectives` / `events` are all arrays.
- [ ] Condition/action type names are spelled correctly; the type name is the object's first non-metadata key.
- [ ] Conditions that need a comparison have `cmp`; boolean/quest-step conditions keep the default.
- [ ] No hand-written `subType`; `HasItem`'s `CompleteOnly` and `HasQuestObjective`'s `Objective` follow this document.
- [ ] The P form writes `KillCount` via an object.
- [ ] Paths use forward slashes and match the actual in-game paths.
- [ ] After running `convert` the three checks pass; use `view` to check the text-slot formula and content.
- [ ] When integrating with a CNV, the `Quest` / `Task` / `Objective` values in the CNV match this file exactly.
