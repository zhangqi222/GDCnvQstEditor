# JSON → QST 用户手册

本文介绍如何**手写 JSON 生成 Grim Dawn `.qst` 任务文件**。

- 工具：
  - `CnvQstEditor.exe`：图形界面；带参数运行时执行同样的命令行命令。
  - `CnvQstEditor-cli.exe`：纯命令行，适合脚本与批量处理。
  - 两个程序的命令完全一致，下文统一用 `CnvQstEditor-cli.exe` 示例。
- 配套示例：
  - [`JSON示例/qst_full.json`](JSON示例/qst_full.json) —— 头部 flags、多 task、OnAccept/目标/OnComplete、持久化 uid、条件/动作。
  - [`JSON示例/qst_all_conditions_actions.json`](JSON示例/qst_all_conditions_actions.json) —— 30 种条件 + 27 个可用动作各写一次。
- 对话文件的写法见 [`JSON转CNV.md`](JSON转CNV.md)；两种文件共用同一套条件/动作系统。

> 目标：读完本文后，你可以不依赖 GUI，直接写出合法的 QST JSON，并生成可被游戏/官方 QuestEditor 读取的 `.qst`。

---

## 目录

1. [快速开始](#1-快速开始)
2. [JSON 写法规则](#2-json-写法规则)
3. [顶层字段](#3-顶层字段)
4. [tasks：任务](#4-tasks任务)
5. [incentives / objectives / events](#5-incentives--objectives--events)
6. [本地化文本槽](#6-本地化文本槽最关键的部分)
7. [uid 与跨文件引用](#7-uid-与跨文件引用)
8. [条件/动作的四种写法](#8-条件动作的四种写法)
9. [比较符 cmp](#9-比较符-cmp)
10. [条件全表（30 种）](#10-条件全表30-种)
11. [动作全表（27 个可用）](#11-动作全表27-个可用)
12. [P / L 形态](#12-p--l-形态)
13. [路径与字符串约定](#13-路径与字符串约定)
14. [完整可运行示例](#14-完整可运行示例)
15. [CLI 命令参考](#15-cli-命令参考)
16. [常见错误](#16-常见错误)
17. [限制与常见坑](#17-限制与常见坑)
- [附录：快速检查清单](#附录快速检查清单)

---

## 1. 快速开始

### 1.1 最小可运行 JSON

新建 `hello.qst.json`：

```json
{
  // "qst":生成任务文件。"cnv":生成对话文件。
  "format": "qst",
  // 9:推荐版本。
  "version": 9,
  "group": "MY_GROUP",
  "name": "MY_QUEST",
  "tasks": [
    {
      "title": "第一个任务",
      "desc": "任务描述。",
      "objectives": [
        {
          "text": "达到 10 级",
          "cond": [ { "IsLevel": [10], "cmp": ">=" } ],
          "act": [ { "Notification": ["tagObjectiveDone"] } ]
        }
      ]
    }
  ]
}
```

### 1.2 生成

```bash
CnvQstEditor-cli.exe convert hello.qst.json -o hello.qst
```

输出：

```text
已生成 hello.qst（... 字节，format=qst）
三检查通过：铺满 / locOffset / round-trip
```

`-o` 省略时，输出路径为 JSON 文件同目录、同名 `.qst`（`hello.qst.json` → `hello.qst`）。

### 1.3 检查生成物

```bash
CnvQstEditor-cli.exe view hello.qst
CnvQstEditor-cli.exe verify hello.qst
```

也可以直接用 `CnvQstEditor.exe` 打开，或用官方 QuestEditor 核对。

---

## 2. JSON 写法规则

文件扩展名统一用 `.json`；解析器允许注释与尾逗号，方便手写：

| 能力 | 是否支持 | 说明 |
|---|---|---|
| `//` 行注释 | 支持 | |
| `/* … */` 块注释 | 支持 | |
| 尾逗号 | 支持 | |
| UTF-8 BOM | 支持 | |
| 单引号字符串 | **不支持** | 必须双引号 |
| 未加引号的键 | **不支持** | |
| 多行字符串 | **不支持** | 用 `\n` |
| `NaN` / `Infinity` | **不支持** | |

### 2.1 键名大小写

- 本文列出的 JSON 键名必须按原样书写（`format`、`tasks`、`objectives`、`title` 等），大小写敏感。
- 条件/动作的官方类型名大小写不敏感。
- 具名参数字段名大小写不敏感。
- Lua 的 `call` / `args` / `tail` 及其内部键使用精确小写。

### 2.2 字符串与数字

- 字符串按 UTF-8 写入文件，中文可以直接写。
- `u32` 字段必须是 **0…4294967295 的整数**，不要写小数或负数。
- `u8` 字段必须是 **0…255 的整数**；布尔字段可写 `true`/`false` 或 `1`/`0`。

---

## 3. 顶层字段

```json
{
  "format": "qst",
  "version": 9,
  "flags": 0,
  "id": 0,
  "group": "MY_GROUP",
  "name": "MY_QUEST",
  "lang": "enUS",
  "tasks": [ /* … */ ]
}
```

| 键 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `format` | string | **是** | — | 必须是 `"qst"`（大小写不敏感）。 |
| `version` | u32 | 否 | `9` | 必须 ≥7。推荐 `9`。 |
| `flags` | u32 | 否 | `0` | 任务类型标志位掩码，见 §3.1。 |
| `id` | u32 | 否 | 省略时随机非 0 | **本文件自有 ID**。显式写了就用（包括 `0`）；完全省略才随机生成。与 task uid 无关。 |
| `group` | string | 否 | `""` | 任务组名，本地化槽 0。 |
| `name` | string | 否 | `""` | 任务名，本地化槽 1。 |
| `lang` | string | 否 | `"enUS"` | 本地化块标签；也可写 `language`（仅当没有 `lang` 时生效）。 |
| `tasks` | array | **是** | — | 任务数组；缺失报错。 |

### 3.1 头部 `flags`（任务类型）

官方 `Common.Quest+Flag`：

| 值 | 名称 | 含义 |
|---:|---|---|
| 0 | None | 无标记。 |
| 1 | Primary | 主任务。 |
| 2 | Secondary | 支线任务。 |
| 4 | Repeatable | 可重复。 |
| 3 / 5 / 6 / 7 | 组合 | 按位或；官方编辑器通常只选单项，但写入组合是合法的。 |

### 3.2 `id` 与 `group` / `name`

- `id` 是**本文件自有 ID**，任务引用**不使用**它。
  - JSON 中**写了 `id`：原样写入**，包括显式 `0`；
  - **完全没写 `id`：自动生成随机非 0 值**。
- 修改已有任务时，务必把原 QST 的 `id` 显式写进 JSON；否则重新生成会换掉文件 ID。
- 这个规则只作用于本文件的 `id`。条件/动作里引用的**其它文件路径与 ID** 始终按你写的内容落盘，工具不会生成或改写。
- `group` / `name` 不是必填，但即使为空也会各占一个本地化槽（槽 0、槽 1）。
- 官方编辑器的任务列表按组名分组、显示任务名；新任务建议都填写。

### 3.3 `lang`

- 只生成一个语言块，标签默认 `enUS`。
- JSON 不支持多语言；需要多语言时用 GUI 处理。
- 标签只是字符串，不校验官方语言代码。

---

## 4. `tasks`：任务

```json
{
  "uid": 305419896,
  "title": "任务标题",
  "desc": "任务描述。",
  "incentives": [ /* OnAccept 事件 */ ],
  "objectives": [ /* 目标 */ ],
  "events": [ /* OnComplete 事件 */ ]
}
```

| 键 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `uid` | u32 | 否 | 随机非 0 | **持久化 task uid**。跨文件引用用它，见 §7。`0`/省略会随机生成。 |
| `title` | string | 否 | `""` | 任务标题，占一个本地化槽。 |
| `desc` | string | 否 | `""` | 任务描述，占一个本地化槽。 |
| `incentives` | array | 否 | `[]` | 接受任务时触发（编辑器 OnAccept）。每个元素占一个空文本槽。 |
| `objectives` | array | 否 | `[]` | 任务目标。每个元素占一个文本槽。 |
| `events` | array | 否 | `[]` | 完成任务时触发（编辑器 OnComplete）。每个元素占一个空文本槽。 |

> `unknownAC`、`flag8`、`flag9` 是格式中的兼容字段，语义尚未确认。省略即可（默认 `0`/`false`），不要为了“补全”而随意填写。

### 4.1 task 数量上限

单个文件最多 **4096 个 task**。超过报错：

```text
task 数量 N 超过上限 4096
```

objective / incentive / event 没有单独的硬性上限，但实际使用中不要构造超大数组。

### 4.2 task uid 的随机生成

- `uid` 省略或为 `0` 时，工具会生成一个非 0 随机值。
- 随机值每次运行都不同，生成物不可复现。
- 只要你需要跨文件引用（CNV 的 `BeginQuestTask`、`HasQuestObjective` 等），就**必须显式写 uid**。

---

## 5. `incentives` / `objectives` / `events`

### 5.1 incentives（OnAccept）

玩家接受任务时触发的一组条件/动作：

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

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `logic` | string / u32 | `"AND"` | 条件列表关系：`"AND"`=0，`"OR"`=1。 |
| `cond` | array | `[]` | 条件列表；空数组表示无条件触发。 |
| `act` | array | `[]` | 动作列表，按顺序执行。 |

- 每个 incentive 占一个本地化槽（该槽为空，工具自动补空串）。
- incentive 没有持久化 uid；不要尝试在 JSON 里写 `uid`（会被忽略）。

### 5.2 objectives（目标）

```json
"objectives": [
  {
    "uid": 2271560481,
    "text": "收集 5 个物品",
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

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `uid` | u32 | 随机非 0 | **持久化 objective uid**。被 `HasQuestObjective` 等引用。 |
| `text` | string | `""` | 目标文本，占一个本地化槽。 |
| `logic` | string / u32 | `"AND"` | 条件列表关系。 |
| `cond` | array | `[]` | 目标完成/可交互的判定条件。 |
| `act` | array | `[]` | 目标条件满足时执行的动作。 |

- objective 的 `uid` 省略/为 0 时随机生成；跨文件引用时必须显式写。
- objective 的 `logic` 只影响 `cond`，不是任务属性。
- objective 没有 `title` / `desc`，只有一条 `text`。
- `unknown1` 是兼容字段，省略即可。

### 5.3 events（OnComplete）

玩家完成任务时触发的一组条件/动作，结构同 incentive：

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

- 每个 event 占一个本地化槽（工具补空串）。
- event 本身没有持久化 uid；`unknown` 是兼容字段，省略即可。

### 5.4 数组键写错的后果

`incentives` / `objectives` / `events` 如果写成非数组（例如对象、字符串），这些内容会被**静默忽略**，不报错：

```json
"objectives": { "text": "x" }   // 被当成没有 objectives，不会报错
```

`cond` / `act` 则不同：写成非数组会直接报错 `必须是数组`。写完 QST 后建议用 `view` 检查目标/事件数量是否符合预期。

---

## 6. 本地化文本槽（最关键的部分）

QST 的本地化表不是“有多少文本就写多少”，而是按固定公式分配槽位。官方 QuestEditor 按这个公式索引；槽数不对会直接报错。

### 6.1 公式

```text
S = 2 + 2T + O + I + E

T = task 数量
O = objective 总数
I = incentive 总数
E = event 总数
2 = 组名 + 任务名
```

### 6.2 槽位顺序

```text
槽 0 : group
槽 1 : name

然后按 task 顺序，每个 task:
  标题
  描述
  该 task 的 incentive 槽（每个 1 条，空）
  该 task 的 objective 文本（每个 1 条）
  该 task 的 event 槽（每个 1 条，空）
```

### 6.3 一个完整例子

假设：

- Task 1：1 个 incentive、2 个 objective、1 个 event
- Task 2：0 个 incentive、1 个 objective、0 个 event

计算：

```text
T = 2
O = 2 + 1 = 3
I = 1 + 0 = 1
E = 1 + 0 = 1
S = 2 + 2*2 + 3 + 1 + 1 = 11
```

槽位：

| 槽 | 内容 |
|---:|---|
| 0 | group |
| 1 | name |
| 2 | Task 1 标题 |
| 3 | Task 1 描述 |
| 4 | Task 1 incentive #1（空） |
| 5 | Task 1 objective #1 文本 |
| 6 | Task 1 objective #2 文本 |
| 7 | Task 1 event #1（空） |
| 8 | Task 2 标题 |
| 9 | Task 2 描述 |
| 10 | Task 2 objective #1 文本 |

### 6.4 自动生成，不需要也不能指定槽号

JSON 里只写：

```json
"incentives": [ ... ],
"objectives": [ { "text": "..." } ],
"events": [ ... ]
```

工具会按公式与顺序自动补空槽。你**不能**在 JSON 里写“把这段文本放到槽 7”；写了额外键也会被忽略。

### 6.5 为什么 incentive/event 的文本是空的

官方文件里 OnAccept / OnComplete 事件没有可见文本，但每个事件仍占一个槽。工具固定写空字符串。不要试图给事件塞文本——event 对象没有 `text` 键，写了也不会写入。

### 6.6 不要手动改文本条数

生成、保存或批量处理时，工具会按公式重建文本表。不要手工增删生成后的文本，否则官方编辑器会因槽数不匹配而报错；需要改文本时请直接改 JSON 里的 `group` / `name` / `title` / `desc` / `text`。

---

## 7. uid 与跨文件引用

> **改旧任务时最重要的一条：先分清“本文件自有 ID”和“引用 ID”。**
>
> - **本文件自有 ID**：根级 `id`，以及 task / objective 的 `uid`。它们定义在当前文件里，优先保留你写的值（例外：task / objective 的 `0` 视为未设置）。
> - **根级 `id`**：JSON 写了就原样使用（包括显式 `0`）；完全省略才自动生成随机非 0 值。
> - **task / objective `uid`**：显式写非 0 值就原样保留；省略或写 `0` 视为未设置，自动生成随机值。
> - **引用 ID**：条件/动作参数里的 `QuestFile` / `Task` / `Objective` 等，是**目标文件**里已经落盘的 ID。
>   工具不生成、不猜测、不改写，必须照抄；写错会导致任务无法推进。
>
> 因此修改已有任务时，务必先把原文件的根级 `id` 和用到的 task / objective `uid` 记录下来，
> 再显式写回 JSON，不要依赖自动生成。

### 7.1 哪些 ID 会落盘

| 对象 | JSON 键 | 用途 |
|---|---|---|
| task | `tasks[].uid` | 被 BeginQuestTask / CompleteQuestTask / QuestTaskComplete / OnQuestTask 等引用 |
| objective | `tasks[].objectives[].uid` | 被 HasQuestObjective 引用 |
| 文件 | 根级 `id` | **本文件自有 ID**；任务引用不使用它。写了就用（含 0），没写才随机 |

- incentive / event 没有持久化 uid，不要写也不要引用。
- 同一个 QST 文件里 task uid 应互不相同；objective uid 也应互不相同。工具不会替你检查重复。

### 7.2 引用示例

QST 文件 A（`quests/example.qst`）：

```json
{
  "format": "qst",
  "group": "EXAMPLE_GROUP",
  "name": "EXAMPLE_QUEST",
  "tasks": [
    {
      "uid": 305419896,
      "title": "任务一",
      "objectives": [
        { "uid": 2271560481, "text": "收集 5 个物品" }
      ]
    }
  ]
}
```

CNV 引用它：

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

QST 内部的事件也可以自引用：

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

### 7.3 uid 的选择与保留

- 官方 QuestEditor 新建 task 时使用随机 uid，因此随机值本身是合法的。
- **修改旧任务时，task / objective uid 必须显式保留原值**；否则引用这些 uid 的条件/动作会失效。
- 为了让生成物可复现、可跨文件引用，建议在新 JSON 中显式指定，例如 `305419896`、`2271560481`。
- task / objective 的 uid 不能为 0：0 会被当作“未设置”并替换为随机值。
- 根级 `id` 是例外：它不参与任务引用，**显式写 `0` 也会原样保留**；只有完全省略才随机生成。

保留旧任务 ID 的最小写法：

```json
{
  "format": "qst",
  "version": 9,
  "id": 305419896,          // 从原 .qst 抄来的本文件 ID：原样保留
  "group": "OLD_GROUP",
  "name": "OLD_QUEST",
  "tasks": [
    { "uid": 2271560481,    // 从原 .qst 抄来的 task uid：引用它的条件/动作才不会失效
      "title": "旧任务标题", "desc": "旧任务描述",
      "objectives": [ { "uid": 1234567890, "text": "旧目标文本" } ] }
  ]
}
```

---

## 8. 条件/动作的四种写法

QST 与 CNV 使用完全相同的条件/动作系统与写法。

### 8.1 写法 A：具名类型 + 位置参数

```json
{ "HasFaction": ["SURVIVORS", 5000], "cmp": ">=" }
{ "GiveMoney": [500] }
```

> **值必须是数组或对象。** 直接写标量不会被读取，例如 `{"GiveMoney": 500}` 会得到 0，
> `{"IsHardcore": true}` 会得到 `false`。请写成 `{"GiveMoney": [500]}` / `{"IsHardcore": [true]}`。
> 唯一例外是 `LuaScript`：它允许直接写函数名字符串（见 §12.2）。

类型名必须是对象里第一个“非元数据”属性。元数据键：

```text
subType, cmp, comparator, comparison, uid, args
```

不要把 `logic`、`note` 等键写在类型名之前。推荐把类型名放在第一行。

### 8.2 写法 B：具名类型 + 具名参数

```json
{ "HasFaction": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
{ "GiveItem": { "ItemRecord": "records/items/a.dbr", "NumItems": 2 } }
```

- 字段名大小写不敏感；
- 缺失字段填零值；
- 多余字段忽略；
- P/L 形态见 §12。

### 8.3 写法 C：原始类型号

```json
{ "type": 6, "args": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `type` 使用条件/动作表中的类型号（条件 0–29，动作 1–28，0/16 不可用）。
- 一般直接用官方类型名即可；原始类型号适合批量生成或需要精确控制的场景。

### 8.4 写法 D：原始类型号 + 具名参数

```json
{ "type": 6, "args": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

### 8.5 优先级与冲突

- `type` 与具名类型同时存在：`type` 优先。
- 具名类型与 `args` 同时存在：具名类型的值优先。
- 旧字段 `uid` 不是比较符；比较符只认 `cmp` / `comparator` / `comparison`。
- 动作的 `cmp` 被忽略。
- 无字段条件（WaitForCompletion）可写 `{}`、`[]` 或只写 `{"type":17}`。
- `subType` 是高级兼容字段，默认值已经正确；除非明确知道需要，否则不要写。

### 8.6 `logic` 与条件列表

`logic` 只作用于 `cond`，不作用于 `act`：

```json
{ "logic": "OR", "cond": [ … ] }   // 任一条件满足
{ "logic": "AND", "cond": [ … ] }  // 全部满足（默认）
```

- `logic` 可写字符串 `"AND"`/`"OR"`（大小写不敏感），也可写数字 0/1。
- 条件列表为空时，`logic` 不生效。
- 动作没有 AND/OR，按数组顺序执行。

---

## 9. 比较符 `cmp`

`cmp` 就是官方编辑器里的 `Comparator` 下拉。

| 数字 | 字符串（大小写不敏感） | 符号 | 官方枚举 |
|---|---|---|---|
| 0 | `lt` / `less` / `<` | `<` | Less |
| 1 | `le` / `lessequal` / `<=` | `<=` | LessEqual |
| 2 | `eq` / `equal` / `=` / `==` | `=` | Equal（默认） |
| 3 | `ge` / `greaterequal` / `>=` | `>=` | GreaterEqual |
| 4 | `gt` / `greater` / `>` | `>` | Greater |
| 5 | `ne` / `notequal` / `!=` / `<>` | `!=` | NotEqual |

```json
{ "HasMoney": [1000], "cmp": ">=" }
{ "HasMoney": [1000], "comparator": 3 }
{ "HasMoney": [1000], "comparison": "GreaterEqual" }
```

三种键名等价，检查顺序 `cmp` → `comparator` → `comparison`；都没写时默认 `2`（`=`）。

**哪些条件使用 `cmp`**：

- 有数值比较语义的：HasExperience、IsLevel、IsDifficulty、HasFaction、HasMoney、HasItem、HasKilled、UsedQuestItem、HasKilledProxy、CheckRandomValue、OwnsDLC、HasTribute、EarnedDevotion、IsChallenge、HasKilledMonsterClass、HasKilledFaction。
- 布尔条件、任务步骤条件、Lua 条件：官方弹窗不显示 Comparator，保持默认 `=`。
- 动作：没有 `cmp`。

---

## 10. 条件全表（30 种）

条件类型号范围 **0–29**，共 30 种。QST 与 CNV 共用。表中“JSON 字段”按位置写法时的顺序排列。

| type | 官方名 | JSON 字段（位置顺序） | 说明 / 枚举 | 示例 |
|---:|---|---|---|---|
| 0 | OnQuest | `QuestFile`, `OnQuest` | 任务文件串 + 布尔 | `{"OnQuest":["quests/a.qst",true]}` |
| 1 | QuestTaskComplete | `QuestFile`, `Task`, `TaskComplete` | `Task` 为 task uid | `{"QuestTaskComplete":["quests/a.qst",305419896,true]}` |
| 2 | HasExperience | `Experience` | 配 `cmp` | `{"HasExperience":[10000],"cmp":">="}` |
| 3 | IsLevel | `IsLevel` | 配 `cmp` | `{"IsLevel":[10],"cmp":">="}` |
| 4 | IsDifficulty | `IsDifficulty` | 0 Normal / 1 Epic / 2 Legendary | `{"IsDifficulty":[2],"cmp":"="}` |
| 5 | IsHardcore | `IsHardcore` | 布尔 | `{"IsHardcore":[true]}` |
| 6 | HasFaction | `FactionName`, `Value` | 配 `cmp` | `{"HasFaction":["SURVIVORS",5000],"cmp":">="}` |
| 7 | HasToken | `TokenName`, `HasToken` | 布尔 | `{"HasToken":["TOKEN_A",true]}` |
| 8 | HasMoney | `MoneyAmount` | 配 `cmp` | `{"HasMoney":[1000],"cmp":">="}` |
| 9 | HasItem | `ItemRecord`, `ItemCount`, `CompleteOnly` | `CompleteOnly` 可省略 | `{"HasItem":["records/items/a.dbr",1,false],"cmp":">="}` |
| 10 | HasKilled | `MonsterRecords`（路径列表）, `KillCount`（尾字） | 配 `cmp` | `{"HasKilled":{"MonsterRecords":["records/monsters/a.dbr"],"KillCount":1}}` |
| 11 | HasQuestObjective | `Quest`, `Task`, `Objective`, `Satisfied` | `Task`/`Objective` 为持久化 uid | 见 §10.1 |
| 12 | LuaScript（条件） | `call`, `args`, `tail` | 见 §12.2 | `{"LuaScript":{"call":"gd.x.y()","args":[]}}` |
| 13 | KnowsPlayer | `KnowsPlayer` | 布尔 | `{"KnowsPlayer":[true]}` |
| 14 | OnQuestTask | `QuestFile`, `Task`, `OnTask` | `Task` 为 task uid | `{"OnQuestTask":["quests/a.qst",305419896,true]}` |
| 15 | UsedQuestItem | `ItemRecord`, `NumUsed` | 配 `cmp` | `{"UsedQuestItem":["records/items/a.dbr",1],"cmp":">="}` |
| 16 | HasKilledProxy | `ProxyRecords`（路径列表）, `KillCount`（尾字） | 配 `cmp` | `{"HasKilledProxy":{"ProxyRecords":["records/proxies/a.dbr"],"KillCount":1}}` |
| 17 | WaitForCompletion | （无） | 无参数 | `{"WaitForCompletion":{}}` |
| 18 | ServerHasToken | `TokenName`, `HasToken` | 布尔 | `{"ServerHasToken":["TOKEN_B",true]}` |
| 19 | QuestBlocked | `QuestFile`, `QuestBlocked` | 官方弹窗两字段同名；建议位置写法 | `{"QuestBlocked":["quests/a.qst",false]}` |
| 20 | AnyoneHasToken | `TokenName`, `HasToken` | 布尔 | `{"AnyoneHasToken":["TOKEN_C",false]}` |
| 21 | CheckRandomValue | `Name`, `Value` | 配 `cmp` | `{"CheckRandomValue":["roll",50],"cmp":"<="}` |
| 22 | OwnsDLC | `DLC_ID` | DLC 数字 id，配 `cmp` | `{"OwnsDLC":[1],"cmp":"="}` |
| 23 | HasTribute | `TributeAmount` | 配 `cmp` | `{"HasTribute":[10],"cmp":">="}` |
| 24 | EarnedDevotion | `DevotionAmount` | 配 `cmp` | `{"EarnedDevotion":[20],"cmp":">="}` |
| 25 | HasMastery | `MasteryName`, `HasMastery` | `MasteryName` 下标 0–29（SkillClass01–30）+ 布尔 | `{"HasMastery":[0,true]}` |
| 26 | IsChallenge | `IsChallenge` | 0 Normal Veteran / 1 Epic Veteran / 2 Legendary Veteran | `{"IsChallenge":[2],"cmp":">="}` |
| 27 | HasBuff | `BuffName`, `HasBuff` | Buff 文件串 + 布尔 | `{"HasBuff":["records/buffs/a.dbr",true]}` |
| 28 | HasKilledMonsterClass | `MonsterClass1`, `MonsterClass2`, `KillCount` | 0 Common / 1 Champion / 2 Hero / 3 Boss / 4 Quest / 5 SuperBoss | `{"HasKilledMonsterClass":[0,5,1],"cmp":">="}` |
| 29 | HasKilledFaction | `Faction1`, `Faction2`, `KillCount` | 0 None / 1 Alone / 2 GenericMonster / 3 Player / 4 Survivors / 5 Aetherials / 6 Cthonians / 7 Outlaws / 8 Beasts / 9 User0… | `{"HasKilledFaction":[4,5,100],"cmp":">="}` |

### 10.1 HasQuestObjective 详细写法

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

位置写法（四个值都要给）：

```json
{ "HasQuestObjective": ["quests/example.qst", 305419896, 2271560481, true] }
```

> `Task` / `Objective` 必须与 QST 文件里落盘的 uid 完全一致。见 §7。

### 10.2 枚举字段的取值范围

官方编辑器把这些字段当下标查表，越界会报错：

- `IsDifficulty`：0 / 1 / 2。
- `IsChallenge`：0 / 1 / 2。
- `HasMastery.MasteryName`：0–29（SkillClass01–30）。
- `HasKilledMonsterClass.MonsterClass1/2`：0–5。
- `HasKilledFaction.Faction1/2`：至少 0–9，官方下拉可继续滚动；不要凭空编造。
- `PlayAnimation.AnimationType`：现有游戏中恒为 9（Chat）；其它值请先用官方编辑器确认。

### 10.3 条件类型越界

`type` 必须是 0–29；超出范围时 `convert` 报错 `条件类型 N 越界（有效范围 0–29）`。

---

## 11. 动作全表（27 个可用）

动作类型号范围 **0–28**，但 **0** 和 **16** 不可用，可用的是 **27 个**。动作没有 `cmp`、AND/OR；按数组顺序执行。

| type | 官方名 | JSON 字段（位置顺序） | 说明 | 示例 |
|---:|---|---|---|---|
| 1 | BeginQuestTask | `QuestFile`, `Task` | Task 为 uid | `{"BeginQuestTask":["quests/a.qst",305419896]}` |
| 2 | CompleteQuest | `QuestFile` | 完成整个任务 | `{"CompleteQuest":["quests/a.qst"]}` |
| 3 | CompleteQuestTask | `QuestFile`, `Task` | 完成指定 task | `{"CompleteQuestTask":["quests/a.qst",305419896]}` |
| 4 | GiveMoney | `MoneyAmount` | 给金币 | `{"GiveMoney":[500]}` |
| 5 | GiveFaction | `FactionType`, `Amount` | 给阵营声望 | `{"GiveFaction":["SURVIVORS",100]}` |
| 6 | GiveItem | `ItemRecord`, `NumItems` | 给物品 | `{"GiveItem":["records/items/a.dbr",1]}` |
| 7 | GiveLevel | `NumLevels` | 给等级 | `{"GiveLevel":[1]}` |
| 8 | GiveExperience | `Experience` | 给经验 | `{"GiveExperience":[1000]}` |
| 9 | GiveToken | `TokenName` | 给令牌 | `{"GiveToken":["TOKEN_A"]}` |
| 10 | RemoveToken | `TokenName` | 移除令牌 | `{"RemoveToken":["TOKEN_A"]}` |
| 11 | LuaScript（动作） | `call`, `args` | 无 `tail` | 见 §12.2 |
| 12 | DebugPrint | `Text` | 调试输出 | `{"DebugPrint":["hello"]}` |
| 13 | PlaySound | `SoundRecord` | 播放声音 | `{"PlaySound":["records/sounds/a.dbr"]}` |
| 14 | PlayAnimation | `AnimationType` | 动画枚举，现有游戏中多为 9 | `{"PlayAnimation":[9]}` |
| 15 | GiveSkillPoint | `NumSkillpoints` | 给技能点 | `{"GiveSkillPoint":[1]}` |
| 16 | （不可用） | — | **不要使用** | — |
| 17 | Notification | `Tag` | 本地化通知标签 | `{"Notification":["tagAccepted"]}` |
| 18 | GiveAttribPoint | `NumAttributePoints` | 给属性点 | `{"GiveAttribPoint":[1]}` |
| 19 | ScriptEvent | `ScriptEvent` | 脚本事件名 | `{"ScriptEvent":["EV_EXAMPLE"]}` |
| 20 | GiveRandomItem | `ItemRecord`, `NumItems` | 给随机物品 | `{"GiveRandomItem":["records/items/a.dbr",2]}` |
| 21 | GenerateRandomValue | `Name`, `Min`, `Max` | 生成随机值 | `{"GenerateRandomValue":["roll",1,100]}` |
| 22 | CastSkill | `SkillRecord` | 施放技能 | `{"CastSkill":["records/skills/a.dbr"]}` |
| 23 | UnlockFaction | `FactionType` | 解锁阵营 | `{"UnlockFaction":["SURVIVORS"]}` |
| 24 | SetFaction | `FactionType`, `Value` | 设置阵营值 | `{"SetFaction":["SURVIVORS",1000]}` |
| 25 | GiveDevotion | `NumDevotionPoints` | 给虔诚点 | `{"GiveDevotion":[1]}` |
| 26 | GiveTribute | `TributeAmount` | 给贡品 | `{"GiveTribute":[10]}` |
| 27 | UnlockTutorial | `TutorialPage` | 解锁教程页 | `{"UnlockTutorial":[1]}` |
| 28 | PlayVideo | `Video path`, `AllowSkip` | 播放视频 + 允许跳过 | `{"PlayVideo":["video/intro.ivf",false]}` |

### 11.1 动作 0 / 16

- 两个类型都不可用，`convert` 报错：`动作类型 N 未实现（工厂返回 null）`。
- 不要写入，官方编辑器打开也可能报错。

### 11.2 PlayVideo 字段名

官方字段名是 **`Video path`**（有空格，`path` 小写）。推荐位置写法：

```json
{ "PlayVideo": ["video/intro.ivf", false] }
```

具名写法：

```json
{ "PlayVideo": { "Video path": "video/intro.ivf", "AllowSkip": false } }
```

### 11.3 动作 19 ScriptEvent

```json
{ "ScriptEvent": ["EV_PLAYER_ENTERED_REGION"] }
```

字符串通常是 `EV_` 前缀的脚本事件名。

---

## 12. P / L 形态

### 12.1 P 形态（路径列表）

用于 HasKilled（10）和 HasKilledProxy（16）：

```json
{ "HasKilled": { "MonsterRecords": ["records/monsters/a.dbr", "records/monsters/b.dbr"], "KillCount": 3 }, "cmp": ">=" }
{ "HasKilledProxy": { "ProxyRecords": ["records/proxies/a.dbr"], "KillCount": 1 } }
```

- 路径键可用官方字段名或通用 `paths`；尾字键可用 `KillCount` / `trailing` / `count`（大小写不敏感）。
- 原始 type 写法：`{ "type": 10, "args": { "paths": [...], "trailing": 3 } }`。
- **位置数组无法设置 KillCount**：

```json
{ "HasKilled": [["records/monsters/a.dbr"]] }                 // KillCount = 0
{ "HasKilled": [{ "paths": ["records/monsters/a.dbr"], "trailing": 3 }] }  // KillCount = 3
```

### 12.2 L 形态（Lua）

条件推荐写法：

```json
{
  "LuaScript": {
    "call": "gd.example.check()",
    "args": ["arg1", "arg2"],
    "tail": { "t0": 0, "t1": 0, "t2": 0, "t3": 0, "text": "TRUE" }
  }
}
```

动作推荐写法：

```json
{ "LuaScript": { "call": "gd.example.run()", "args": ["arg1"] } }
```

- `call` / `function` 建议固定写小写 `call`；只写函数名也可。
- `args` 是字符串数组；每项写入一个参数，其余数值字段写 0。
- `tail` 仅条件有效；可省略（全 0/空串）。
- `tail` 内部键精确小写：`t0`、`t1`、`t2`、`t3`、`text`。
- 条件尾部对应官方编辑器的 **Evaluate** 区（返回类型 + 比较符 + 期望值），但逐位对应关系尚未完全确认；没有把握就省略或保留导出值。
- 不要写 `{"Function": "...", "args": [...]}`：`Function` 会被当成单字符串，`args` 丢失；请用 `call`。

---

## 13. 路径与字符串约定

| 字段 | 基准目录（官方行为） | 示例 |
|---|---|---|
| `QuestFile` / `Quest` | `resources/` | `quests/example.qst` |
| `ItemRecord`、`SoundRecord`、`SkillRecord`、`BuffName` | 游戏数据库（`database/`） | `records/items/gear/example.dbr` |
| `MonsterRecords`、`ProxyRecords` | 游戏数据库（`database/`） | `records/monsters/example.dbr` |
| `Video path` | `resources/` 或视频目录 | `video/intro.ivf` |

- 工具**只把字符串原样写入**，不检查文件存在性、不补扩展名、不做路径规范化。
- 使用正斜杠 `/`；路径大小写要与实际文件一致。
- 文件内字符串为 UTF-8；JSON 源文件建议 UTF-8。
- JSON 的 `\n` 会写成真正换行；`title` / `desc` / `text` 中可以使用，但请确认游戏 UI 的显示效果。

---

## 14. 完整可运行示例

下面这份 JSON 覆盖头部 flags、多 task、OnAccept、目标、OnComplete、持久化 uid、条件/动作：

```json
{
  "format": "qst",

  // 7/8/9 均可；推荐 9。
  "version": 9,

  // 头部任务 flags：0 None / 1 Primary / 2 Secondary / 4 Repeatable；可相加。
  "flags": 1,

  // 本文件自有 ID：显式写就原样保留（包括 0）；完全省略才随机生成。
  // 修改旧任务时，把原文件的 id 抄到这里；它不参与任务引用，与 task uid 无关。
  // 例如："id": 123456789,

  // 组名 + 任务名（本地化槽 0、1）。
  "group": "EXAMPLE_GROUP",
  "name": "EXAMPLE_QUEST",

  // 语言块标签，默认 enUS。
  "lang": "enUS",

  "tasks": [
    {
      // 持久化 task uid；被 CNV/QST 的 Task 引用。
      "uid": 305419896,

      // 标题与描述（各占一个本地化槽）。
      "title": "任务标题",
      "desc": "这是任务描述。",

      // OnAccept 事件：每个元素占一个空文本槽。
      "incentives": [
        {
          "logic": "AND",
          "cond": [],
          "act": [
            { "Notification": ["tagQuestAccepted"] }
          ]
        }
      ],

      // 目标：每个目标占一个文本槽。
      "objectives": [
        {
          // 持久化 objective uid；被 HasQuestObjective 引用。
          "uid": 2271560481,
          "text": "收集 5 个物品",
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
          "text": "返回交任务",
          "cond": [
            { "WaitForCompletion": {} }
          ],
          "act": []
        }
      ],

      // OnComplete 事件：每个元素占一个空文本槽。
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
      // 新任务可省略 uid（随机生成）；修改旧任务时必须显式写回原 uid。
      "title": "第二个任务",
      "desc": "没有 OnAccept / OnComplete 事件。",
      "objectives": [
        {
          "text": "达到 10 级",
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

以这份 JSON 为例，文本槽为：

```text
T = 2, O = 3, I = 1, E = 1
S = 2 + 2*2 + 3 + 1 + 1 = 11

0 group
1 name
2 Task1 标题
3 Task1 描述
4 Task1 incentive #1（空）
5 Task1 objective #1
6 Task1 objective #2
7 Task1 event #1（空）
8 Task2 标题
9 Task2 描述
10 Task2 objective #1
```

完整版（含全部类型）见 [`JSON示例/qst_all_conditions_actions.json`](JSON示例/qst_all_conditions_actions.json)。

---

## 15. CLI 命令参考

`CnvQstEditor.exe` 与 `CnvQstEditor-cli.exe` 命令相同；下文用 `CnvQstEditor-cli.exe`。查看完整用法：

```bash
CnvQstEditor-cli.exe help
```

### 15.1 生成

```bash
CnvQstEditor-cli.exe convert <spec.json> [-o out.qst]
```

- `make` 是 `convert` 的兼容别名，旧脚本仍可用。
- 输入扩展名必须是 `.json`（内容允许 `//`、`/* */` 注释与尾逗号）。
- `-o` 也接受 `--out`。
- 省略 `-o`：输出到 JSON 同目录、同名 `.qst`（`hello.qst.json` → `hello.qst`）。
- 生成后会自动回读校验；看到 `三检查通过` 表示文件结构完整。

### 15.2 模板

```bash
CnvQstEditor-cli.exe sample qst                 # 打印到 stdout
CnvQstEditor-cli.exe sample qst -o sample.json  # 写到文件
```

### 15.3 查看与验证

```bash
CnvQstEditor-cli.exe view   <file.qst>       # 编辑器风格树形文本（含文本槽公式）
CnvQstEditor-cli.exe verify <file或目录>      # 检查文件结构
```

### 15.4 GUI 同名命令

```bash
CnvQstEditor.exe convert hello.qst.json -o hello.qst
```

无参数启动进入图形界面。

---

## 16. 常见错误

| 信息 | 原因 |
|---|---|
| `spec 缺少 "format"（cnv 或 qst）` | 顶层没有 `format`。 |
| `qst spec 缺少 "tasks" 数组` | 顶层缺少 `tasks`，或 `tasks` 不是数组。 |
| `task 数量 N 超过上限 4096` | task 太多。 |
| `第 N 行 cond 必须是数组` | `cond` 写成了对象/字符串。 |
| `第 N 行 cond 的每一项必须是对象` | 数组元素不是对象。 |
| `未知类型名 "xxx"` | 具名类型拼错；错误会列出可用名。 |
| `条件类型 N 越界（有效范围 0–29）` | `type` 超出 0–29。 |
| `动作类型 N 未实现（工厂返回 null）` | 动作 type 用了 0/16，或越界。 |
| `比较符 N 越界（0..5）` | `cmp` 数字超范围。 |
| `未知比较符 "xxx"` | `cmp` 字符串不认识。 |
| `logic 只接受 AND/OR` | `logic` 字符串写错。 |
| `QST 版本必须 ≥7` | `version` 太小。 |

---

## 17. 限制与常见坑

### 17.1 结构限制

1. **单文件、单语言块**：JSON 只生成一个语言块；多语言请用 GUI。
2. **task 数量 ≤4096**。
3. **没有“任务间引用”的自动解析**：`Quest`/`Task`/`Objective` 都是普通字符串/u32，工具不检查文件与 uid 是否存在。
4. **event / incentive 没有持久化 uid**：不要写 `uid`，写了会被忽略。
5. **objective 只有一条文本**：没有独立标题/描述。

### 17.2 文本槽

1. **槽数由公式决定**，不能手工指定槽号。
2. **incentive/event 各占一个空槽**，不能塞文本。
3. **不要手改生成后的文本表长度**；需要改文本时直接改 JSON 再重新生成。
4. 要**修改现有文件**，建议直接用 `CnvQstEditor.exe` 打开；手写 JSON 适合从零新建。

### 17.3 uid

1. 省略 task/objective uid 会随机生成，生成物不可复现。
2. 跨文件引用必须显式写 uid，并保证 task/objective uid 唯一。
3. task/objective 的 uid=0 会被替换成随机值。
4. 根级 `id` 与 task uid 无关：JSON 写了就原样保留（包括 0），完全省略才随机；**改旧任务时务必照抄原 `id` 和 `uid`**。

### 17.4 条件/动作

1. 条件 30 种、动作 27 个可用；动作 0/16 不可写。
2. 不要手写 `subType`：默认值已经正确，改错会让官方编辑器读错字段。
3. 枚举字段不要越界。
4. 布尔/任务步骤条件保持 `cmp` 默认。
5. P 形态位置数组不写 KillCount。
6. Lua 条件尾部未完全确认；没把握就省略 `tail`。
7. 具名类型名必须是第一个非元数据键。

---

## 附录：快速检查清单

写完后逐项确认：

- [ ] `format` 是 `"qst"`。
- [ ] `version` ≥7，推荐 9。
- [ ] `flags` 是 0/1/2/4 或其组合。
- [ ] 每个 task 有 `title` / `desc`（可以为空，但建议填写）。
- [ ] 需要跨文件引用的 task/objective 写了显式 `uid`。
- [ ] 同一文件内 uid 不重复，且不为 0。
- [ ] `incentives` / `objectives` / `events` 都是数组。
- [ ] 条件/动作类型名拼写正确；类型名是对象第一个非元数据键。
- [ ] 条件需要比较的写了 `cmp`；布尔/任务步骤条件保持默认。
- [ ] 没有手写 `subType`；`HasItem` 的 `CompleteOnly`、`HasQuestObjective` 的 `Objective` 按本文写法即可。
- [ ] P 形态用对象写 `KillCount`。
- [ ] 路径为正斜杠，且与游戏内实际路径一致。
- [ ] 运行 `convert` 后三检查通过；用 `view` 检查文本槽公式与内容。
- [ ] 需要与 CNV 联调时，CNV 里的 `Quest` / `Task` / `Objective` 与本文件完全一致。
