# JSON → CNV 用户手册

本文介绍如何**手写 JSON 生成 Grim Dawn `.cnv` 对话文件**。

- 工具：
  - `CnvQstEditor.exe`：图形界面；带参数运行时执行同样的命令行命令。
  - `CnvQstEditor-cli.exe`：纯命令行，适合脚本与批量处理。
  - 两个程序的命令完全一致，下文统一用 `CnvQstEditor-cli.exe` 示例。
- 配套示例：
  - [`JSON示例/cnv_full.json`](JSON示例/cnv_full.json) —— **单个文件覆盖全部内容**：顶层字段、节点字段、全部节点类型、30 种条件、27 个可用动作、`languages` 语言块、配音、flags、跳转，每个字段都带就地说明。
- QST 任务文件的写法见 [`JSON转QST.md`](JSON转QST.md)；两种文件共用同一套条件/动作系统。

> 目标：读完本文后，你可以不依赖 GUI，直接写出合法的 CNV JSON，并生成可被游戏/官方 ConversationEditor 读取的 `.cnv`。

---

## 目录

1. [快速开始](#1-快速开始)
2. [JSON 写法规则](#2-json-写法规则)
3. [顶层字段](#3-顶层字段)
4. [rows：对话节点与树结构](#4-rows对话节点与树结构)
5. [条件/动作的四种写法](#5-条件动作的四种写法)
6. [比较符 cmp](#6-比较符-cmp)
7. [条件全表（30 种）](#7-条件全表30-种)
8. [动作全表（27 个可用）](#8-动作全表27-个可用)
9. [P 形态（路径列表）](#9-p-形态路径列表)
10. [L 形态（Lua）](#10-l-形态lua)
11. [引用值、路径与字符串](#11-引用值路径与字符串)
12. [完整可运行示例](#12-完整可运行示例)
13. [CLI 命令参考](#13-cli-命令参考)
14. [常见错误](#14-常见错误)
15. [限制与常见坑](#15-限制与常见坑)
- [附录：快速检查清单](#附录快速检查清单)

---

## 1. 快速开始

### 1.1 最小可运行 JSON

新建 `hello.json`：

```json
{
  // "cnv":生成对话文件。"qst":生成任务文件。
  "format": "cnv",
  // 6:推荐版本（支持 vo 配音路径）。
  "version": 6,
  // 根节点文本 = 说话人名，占用本地化槽 0。
  "speaker": "Alex",
  "rows": [
    // level 1 = 根的直接子节点
    { "level": 1, "kind": "speech", "text": "你好，旅行者。" },
    { "level": 2, "kind": "end", "text": "再见。" }
  ]
}
```

### 1.2 生成

```bash
CnvQstEditor-cli.exe convert hello.json -o hello.cnv
```

输出：

```text
已生成 hello.cnv（... 字节，format=cnv）
三检查通过：铺满 / locOffset / round-trip
```

`-o` 省略时，输出路径为 JSON 文件同目录、同名的 `.cnv`。

### 1.3 检查生成物

```bash
CnvQstEditor-cli.exe view hello.cnv
CnvQstEditor-cli.exe verify hello.cnv
```

也可以直接用 `CnvQstEditor.exe` 打开 `hello.cnv`，或用官方 ConversationEditor 核对。

---

## 2. JSON 写法规则

文件扩展名统一用 `.json`；解析器允许注释与尾逗号，方便手写：

| 能力 | 是否支持 | 说明 |
|---|---|---|
| `//` 行注释 | 支持 | 本文所有示例均使用 |
| `/* … */` 块注释 | 支持 | |
| 尾逗号 | 支持 | 数组、对象最后一个元素后可有逗号 |
| UTF-8 BOM | 支持 | |
| 单引号字符串 | **不支持** | 必须使用双引号 |
| 未加引号的键 | **不支持** | 必须使用双引号 |
| 多行字符串 | **不支持** | 用 `\n` 转义 |
| `NaN` / `Infinity` | **不支持** | 只能用普通十进制数字 |

### 2.1 键名大小写

- **本文列出的 JSON 键名必须按原样书写**（`format`、`version`、`speaker`、`level`、`kind`、`cond`、`act` 等），大小写敏感。
- **条件/动作的官方类型名**（`HasFaction`、`GiveMoney`…）大小写不敏感。
- **具名参数对象里的字段名**（`FactionName`、`Value`…）大小写不敏感。
- **Lua 对象的 `call` / `args` / `tail`** 使用精确小写；`tail` 内部的 `t0..t3`、`text` 也使用精确小写。

建议：统一使用本文的写法，不要依赖大小写宽容。

### 2.2 字符串与数字

- 字符串按 **UTF-8** 写入文件，中文可以直接写。
- `u32` 字段必须是 **0…4294967295 的整数**，不要写小数或负数。
- `u8` 字段必须是 **0…255 的整数**；布尔字段可以写 `true` / `false`，也可以写 `1` / `0`。

---

## 3. 顶层字段

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
    { "tag": "enUS", "texts": ["Alex", "你好。", "再见。"] }
  ],
  "rows": [ /* … */ ]
}
```

| 键 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `format` | string | **是** | — | 必须是 `"cnv"`（大小写不敏感）。缺失时报错。 |
| `version` | u32 | 否 | `6` | 必须 ≥5。`5`：无 `vo` 字段；`6`：有 `vo`。推荐 `6`。 |
| `id` | u32 | 否 | 自动 | 对话文件 ID。**省略时**按 `speaker` 自动生成；显式写 `0` 则真的是 0。 |
| `speaker` | string | 否 | `""` | 根节点文本（通常是说话人名），占用本地化槽 0。 |
| `flags` | u32 | 否 | `0` | 根节点开关，低 3 位含义见 §4.3。 |
| `vo` | string | 否 | `""` | 根节点配音路径；仅 `version>=6` 写入。 |
| `logic` | string / u32 | 否 | `"AND"` | 根节点条件列表的关系：`"AND"`=0，`"OR"`=1。 |
| `cond` | array | 否 | `[]` | 根节点条件列表。 |
| `act` | array | 否 | `[]` | 根节点动作列表。 |
| `languages` | array | 否 | — | 显式语言块，见 §3.2。写了就以它为准，`speaker`/`text` 仅作回退。 |
| `lang` | string | 否 | `"enUS"` | 单语言块标签（旧写法）。仅在**没有** `languages` 时生效；也可写 `language`。 |
| `rows` | array | **是** | — | 根节点以下的对话节点；缺失直接报错。 |

### 3.1 `id`：本文件自有 ID

- 省略 `id` 时，工具按 `speaker` 自动生成一个 ID；同名说话人会得到相同 ID，需要区分时请手动指定。
- 显式写 `id`（包括 `0`）时原样使用。
- 本文件的 `id` 只标识这个对话文件；条件/动作里引用的其它文件路径和任务/目标 ID 属于**引用 ID**，工具不会生成或改写，必须照抄目标文件。

### 3.2 `languages` 与本地化

推荐用 `languages` 显式描述语言块，可以把多语言写在一个 JSON 里：

```json
"languages": [
  {
    "tag": "enUS",
    "texts": ["Alex", "你终于来了。{^n}路上还顺利吗？", "继续", "", "再见。"]
  }
]
```

| 键 | 类型 | 说明 |
|---|---|---|
| `tag` | string | 语言标签。官方 6 种：`enUS` / `enGB` / `es` / `fr` / `de` / `it`；其它标签会有提示，游戏可能忽略该块。 |
| `texts` | array | 文本行数组；**行数必须与节点数严格对齐**，见下方规则。 |

对齐规则（CNV 的本地化槽以节点为基准）：

- 行数必须等于 `1 + rows.Length`：槽 0 = 根节点（即 `speaker`），槽 1 = `rows[0]`，槽 2 = `rows[1]`……
- `link` 节点同样占一行，通常写空串 `""`；空串是合法文本。
- 每个语言块的 `texts` 行数必须相同。
- 行数不符时 `convert` 会直接报错并拒绝生成，例如：

```text
节点 16 个，languages[enUS] 有 14 行，缺 2 行；文本行数必须与节点数一致。
可填文字的节点行（1 起）：1-16。缺少的行中可填文字：15-16。
```

- **游戏内换行写 `{^n}`**（字面量四个字符），例如 `"你终于来了。{^n}路上还顺利吗？"`。
  不要写 JSON 转义的 `\n`：那会写入真正的换行字节，不符合官方文本约定。
- 写了 `languages` 后，`speaker` / `text` 仍然必须写（用于结构定义），但输出以 `languages` 为准。
- 只写一个语言块时，也可以继续用旧写法 `"lang": "enUS"`，此时文本从 `speaker`/`text` 自动收集。

```json
// 旧写法：单语言，标签默认 enUS
"lang": "enUS"
```

- 标签只是字符串；除官方 6 种外会给出提示，但不会阻止生成。

### 3.3 根节点的条件/动作

根节点同样有 `flags`、`vo`、`cond`、`act`，可以在对话最初进入时设置门槛或执行动作：

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

根节点没有 `text` 键：它的文本固定取 `speaker`。

---

## 4. `rows`：对话节点与树结构

每个数组元素是一个对话节点：

```json
{
  "level": 1,
  "kind": "speech",
  "text": "你终于来了。",
  "flags": 0,
  "vo": "conversations/alex_01.ogg",
  "link": 5,
  "logic": "AND",
  "cond": [],
  "act": []
}
```

| 键 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `level` | int | **是** | — | 缩进层级，≥1。父节点 = 本行之前最近一个 `level-1` 的行。 |
| `kind` | string / u32 | **是** | — | 节点类型，见 §4.2。 |
| `text` | string | 否 | `""` | 节点文本，占用一条本地化槽。 |
| `flags` | u32 | 否 | `0` | 节点开关，低 3 位是编辑器开关，见 §4.3。 |
| `vo` | string | 否 | `""` | 配音文件路径，仅 `version>=6` 写入。 |
| `link` | int | 否 | 自身 | 仅 `kind=link` 有意义：目标节点序号，见 §4.4。 |
| `logic` | string / u32 | 否 | `"AND"` | 本节点条件列表的关系。 |
| `cond` | array | 否 | `[]` | 本节点条件列表。 |
| `act` | array | 否 | `[]` | 本节点动作列表。 |

### 4.1 `level` 与树的构造

规则：

1. 第一行必须是 `level=1`（根的直接子节点）；`level<1` 报错。
2. 每一行的父节点是**它之前最近一个** `level-1` 的行。
3. 不能跳级：写 `level=2` 之前必须已经出现过 `level=1`，否则报错。
4. 回到较浅层级时，更深层级的父节点记录会被清除；之后不能再用那棵旧子树当父节点。

按这套规则，`rows` 的顺序天然就是**深度前序（DFS pre-order）**：

```json
"rows": [
  { "level": 1, "kind": "speech",  "text": "A" },  // 槽 1
  { "level": 2, "kind": "speech",  "text": "B" },  // 槽 2，A 的子
  { "level": 3, "kind": "end",     "text": "C" },  // 槽 3，B 的子
  { "level": 2, "kind": "end",     "text": "D" },  // 槽 4，A 的另一个子
  { "level": 1, "kind": "speech",  "text": "E" },  // 槽 5，根的另一个子
]
```

生成的文件中：

- 节点展平顺序 = 上面的行顺序；
- 文本槽 0 = `speaker`；
- 文本槽 i = `rows[i-1].text`；
- `link` 直接使用这个展平序号（见 §4.4）。

**因此不要打乱行的前序顺序**：文本按行顺序写入，节点按树展平；两者只有在按前序书写时才一一对应。

### 4.2 `kind`：节点类型

| JSON `kind` | 数字 | 官方枚举 | 含义 |
|---|---|---|---|
| `"root"` | 0 | Root | 根节点；`rows` 中写 0 会造出第二个根样式的节点，不建议。 |
| `"speech"` | 1 | Speech | 普通台词节点。 |
| `"continue"` | 2 | Continue | 玩家选择“继续”。 |
| `"accept"` | 3 | Accept | 玩家选择“接受”。 |
| `"decline"` | 4 | Decline | 玩家选择“拒绝”。 |
| `"generic"` | 5 | Generic | 通用节点，官方未命名。 |
| `"end"` | 6 | End | 结束对话。 |
| `"link"` | 7 | Link | 跳转到本文件的另一个节点。 |
| `"random"` | 8 | Random | 随机/选择节点。 |
| `"selector"` | 8 | Random | `random` 的别名。 |
| 数字 `0`–`8` | — | — | 可直接写数字；含义同上表。 |

`kind` 字符串大小写不敏感，也会做 `Trim()`。未识别的名字报错。

### 4.3 `flags`：节点开关

官方编辑器只使用低 3 位：

| 位 | 值 | 名称 | 含义 |
|---|---|---|---|
| bit 0 | 1 | OnceOnly | 仅显示一次。 |
| bit 1 | 2 | AlwaysShow | 总是显示。 |
| bit 2 | 4 | Unimportant | 不重要（灰显/次要）。 |
| bit 3+ | 8/16/32… | 未定义 | 新文件保持 0。 |

可以按位相加，例如 `3` = OnceOnly + AlwaysShow，`7` = 三个开关全开。

### 4.4 `link`：跳转目标

仅当 `kind` 为 `link`（type 7）时生效。

- `link` 的值是**展平节点序号**：
  - `0` = 根节点；
  - `1..rows.Length` = 对应的 `rows` 行号（1 起）；
  - 省略 = 自指（本行）。
- 因为 `rows` 按前序书写时行号恰好等于展平序号，所以通常可以直接写“第几行”。
- 越界值会被限制到合法范围；请仍自行确认目标正确。
- `kind` 不是 `link` 时，`link` 键被忽略。
- link 节点通常不挂子节点；工具允许挂，但不建议。

示例：

```json
{ "level": 1, "kind": "link", "text": "", "link": 5 }  // 跳到 rows 第 5 行
```

### 4.5 `text` 与 `vo`

- `text` 会占用一条本地化槽。空字符串也会占槽，不能通过省略来“删除槽位”。
- `version=5` 时 `vo` 不写入文件；`version>=6` 时每个节点（含根）都会写一个路径字符串，空串表示无配音。
- `vo` 通常是相对 `resources/` 的路径，例如 `conversations/alex_01.ogg`。工具不检查文件是否存在。

### 4.6 空 `rows`

`"rows": []` 合法，会生成一个只有根节点的对话文件。官方编辑器可以打开这种文件，但通常没有实际对话内容。

---

## 5. 条件/动作的四种写法

`cond` / `act` 数组里的每个元素都是一个对象。支持以下四种写法，可混用。

### 5.1 写法 A：具名类型 + 位置参数

```json
{ "HasFaction": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `"HasFaction"` 是官方类型名（大小写不敏感）。
- 值可以是数组，按条件/动作表中的字段顺序给值。
- 条件可加 `cmp`；动作不加。

> **值必须是数组或对象。** 直接写标量不会被读取，例如 `{"GiveMoney": 500}` 会得到 0，
> `{"IsHardcore": true}` 会得到 `false`。请写成 `{"GiveMoney": [500]}` / `{"IsHardcore": [true]}`。
> 唯一例外是 `LuaScript`：它允许直接写函数名字符串（见 §10.2）。

**类型名必须是对象里第一个“非元数据”属性。** 元数据键包括：

```text
subType, cmp, comparator, comparison, uid, args
```

因此不要把 `logic`、`note` 等键写在类型名之前。推荐把类型名放在第一行。

### 5.2 写法 B：具名类型 + 具名参数

```json
{ "HasFaction": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

- 值对象里的键按官方字段名匹配，**大小写不敏感**。
- 缺失字段填零值（字符串 `""`、数字 `0`、布尔 `false`）。
- 多余字段忽略。
- 对 `P` / `L` 形态有专门规则，见 §9、§10。

### 5.3 写法 C：原始类型号

```json
{ "type": 6, "args": ["SURVIVORS", 5000], "cmp": ">=" }
```

- `type` 必须是数字，使用条件/动作表中的类型号（条件 0–29，动作 1–28，0/16 不可用）。
- `args` 可省略；可以是位置数组或具名字典。
- 条件可加 `cmp`。
- 一般直接用官方类型名即可；原始类型号适合批量生成或需要精确控制的场景。

### 5.4 写法 D：原始类型号 + 具名参数

```json
{ "type": 6, "args": { "FactionName": "SURVIVORS", "Value": 5000 }, "cmp": 3 }
```

### 5.5 优先级与冲突

- 同时写 `"type"` 和具名类型时，**`type` 优先**，具名属性被忽略。
- 同时写具名类型和 `"args"` 时，**具名类型的值优先**。
- 条件记录里的 `"uid"` 是旧字段名，**不会**被当作比较符；比较符只认 `cmp` / `comparator` / `comparison`。
- 动作里的 `cmp` 会被忽略。
- `subType` 是高级兼容字段，默认值已经正确；除非你明确知道需要，否则不要写。

### 5.6 空参数

无字段的类型（如 `WaitForCompletion`）有三种等价写法：

```json
{ "WaitForCompletion": {} }
{ "WaitForCompletion": [] }
{ "type": 17 }
```

---

## 6. 比较符 `cmp`

`cmp` 就是官方编辑器里的 `Comparator` 下拉。动作没有这个字段。

### 6.1 取值表

| 数字 | 字符串（大小写不敏感） | 符号 | 官方枚举 |
|---|---|---|---|
| 0 | `lt` / `less` / `<` | `<` | Less |
| 1 | `le` / `lessequal` / `<=` | `<=` | LessEqual |
| 2 | `eq` / `equal` / `=` / `==` | `=` | Equal（默认） |
| 3 | `ge` / `greaterequal` / `>=` | `>=` | GreaterEqual |
| 4 | `gt` / `greater` / `>` | `>` | Greater |
| 5 | `ne` / `notequal` / `!=` / `<>` | `!=` | NotEqual |

### 6.2 写法

```json
{ "HasMoney": [1000], "cmp": ">=" }
{ "HasMoney": [1000], "comparator": 3 }
{ "HasMoney": [1000], "comparison": "GreaterEqual" }
```

三种键名等价，优先检查顺序为 `cmp` → `comparator` → `comparison`。都没写时默认 `2`（`=`）。

### 6.3 哪些条件使用 `cmp`

- **有数值比较语义的条件**：HasExperience、IsLevel、IsDifficulty、HasFaction、HasMoney、HasItem、HasKilled、UsedQuestItem、HasKilledProxy、CheckRandomValue、OwnsDLC、HasTribute、EarnedDevotion、IsChallenge、HasKilledMonsterClass、HasKilledFaction。`cmp` 作用于其中的数值（KillCount / Value / 数量等）。
- **布尔条件**（HasToken、IsHardcore、KnowsPlayer、QuestBlocked…）：官方弹窗不显示 Comparator，保持默认 `=`。
- **任务步骤类**（QuestTaskComplete、OnQuestTask、HasQuestObjective）：官方弹窗同样不显示 Comparator，保持默认 `=`。
- **Lua 条件**：比较语义在 `tail` 里，`cmp` 保持默认即可。
- **动作**：没有 `cmp`。

---

## 7. 条件全表（30 种）

条件类型号范围 **0–29**，共 30 种。表中“JSON 字段”按位置写法时的顺序排列；`P`/`L` 形态的特殊写法见 §9、§10。

| type | 官方名 | JSON 字段（位置顺序） | 说明 / 枚举 | 示例 |
|---:|---|---|---|---|
| 0 | OnQuest | `QuestFile`, `OnQuest` | 任务文件串 + 布尔 | `{"OnQuest":["quests/a.qst",true]}` |
| 1 | QuestTaskComplete | `QuestFile`, `Task`, `TaskComplete` | `Task` 为 task uid | `{"QuestTaskComplete":["quests/a.qst",305419896,true]}` |
| 2 | HasExperience | `Experience` | 经验值，配 `cmp` | `{"HasExperience":[10000],"cmp":">="}` |
| 3 | IsLevel | `IsLevel` | 等级，配 `cmp` | `{"IsLevel":[10],"cmp":">="}` |
| 4 | IsDifficulty | `IsDifficulty` | 0 Normal / 1 Epic / 2 Legendary | `{"IsDifficulty":[2],"cmp":"="}` |
| 5 | IsHardcore | `IsHardcore` | 布尔 | `{"IsHardcore":[true]}` |
| 6 | HasFaction | `FactionName`, `Value` | 阵营名 + 数值，配 `cmp` | `{"HasFaction":["SURVIVORS",5000],"cmp":">="}` |
| 7 | HasToken | `TokenName`, `HasToken` | 令牌名 + 布尔 | `{"HasToken":["TOKEN_A",true]}` |
| 8 | HasMoney | `MoneyAmount` | 金币，配 `cmp` | `{"HasMoney":[1000],"cmp":">="}` |
| 9 | HasItem | `ItemRecord`, `ItemCount`, `CompleteOnly` | `CompleteOnly` 可省略 | `{"HasItem":["records/items/a.dbr",1,false],"cmp":">="}` |
| 10 | HasKilled | `MonsterRecords`（路径列表）, `KillCount`（尾字） | 配 `cmp` | `{"HasKilled":{"MonsterRecords":["records/monsters/a.dbr"],"KillCount":1}}` |
| 11 | HasQuestObjective | `Quest`, `Task`, `Objective`, `Satisfied` | Task/Objective 为持久化 uid | 见 §7.1 |
| 12 | LuaScript（条件） | `call`, `args`, `tail` | 见 §10 | `{"LuaScript":{"call":"gd.x.y()","args":[]}}` |
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
| 28 | HasKilledMonsterClass | `MonsterClass1`, `MonsterClass2`, `KillCount` | 前两值 0 Common / 1 Champion / 2 Hero / 3 Boss / 4 Quest / 5 SuperBoss | `{"HasKilledMonsterClass":[0,5,1],"cmp":">="}` |
| 29 | HasKilledFaction | `Faction1`, `Faction2`, `KillCount` | 0 None / 1 Alone / 2 GenericMonster / 3 Player / 4 Survivors / 5 Aetherials / 6 Cthonians / 7 Outlaws / 8 Beasts / 9 User0… | `{"HasKilledFaction":[4,5,100],"cmp":">="}` |

### 7.1 HasQuestObjective 详细写法

```json
{
  "HasQuestObjective": {
    "Quest": "quests/example.qst",   // .qst 文件相对路径
    "Task": 305419896,               // 目标 task 的持久化 uid
    "Objective": 2271560481,         // 目标 objective 的持久化 uid
    "Satisfied": true                // 布尔
  },
  "cmp": "="
}
```

位置写法（四个值都要给）：

```json
{ "HasQuestObjective": ["quests/example.qst", 305419896, 2271560481, true] }
```

> `Task` / `Objective` 的值必须与 QST 文件里 `tasks[].uid` / `tasks[].objectives[].uid` **完全一致**。
> 具体如何生成这些 uid 见 [`JSON转QST.md`](JSON转QST.md) §4、§7。
> 官方编辑器对这两个参数只显示下拉选择，不能手输；在 JSON 里就是普通 u32。

### 7.2 枚举字段的取值范围

以下字段官方编辑器是下拉框，写入越界值会让官方编辑器报错：

- `IsDifficulty`：0 / 1 / 2。
- `IsChallenge`：0 / 1 / 2。
- `HasMastery.MasteryName`：0–29（SkillClass01–30）。
- `HasKilledMonsterClass.MonsterClass1/2`：0–5。
- `HasKilledFaction.Faction1/2`：至少 0–9，官方下拉中可继续滚动；不要凭空编造。
- `PlayAnimation.AnimationType`：现有游戏中恒为 9（Chat）；其它值请先用官方编辑器确认。

### 7.3 条件类型越界

`type` 必须是 0–29；超出范围时 `convert` 报错 `条件类型 N 越界（有效范围 0–29）`。

---

## 8. 动作全表（27 个可用）

动作类型号范围 **0–28**，但 **0** 和 **16** 不可用，所以可用的是 **27 个**。动作没有 `cmp`，也没有 AND/OR；`act` 数组按书写顺序执行。

| type | 官方名 | JSON 字段（位置顺序） | 说明 | 示例 |
|---:|---|---|---|---|
| 1 | BeginQuestTask | `QuestFile`, `Task` | 开始 task；Task 为 uid | `{"BeginQuestTask":["quests/a.qst",305419896]}` |
| 2 | CompleteQuest | `QuestFile` | 完成任务文件 | `{"CompleteQuest":["quests/a.qst"]}` |
| 3 | CompleteQuestTask | `QuestFile`, `Task` | 完成指定 task | `{"CompleteQuestTask":["quests/a.qst",305419896]}` |
| 4 | GiveMoney | `MoneyAmount` | 给金币 | `{"GiveMoney":[500]}` |
| 5 | GiveFaction | `FactionType`, `Amount` | 给阵营声望 | `{"GiveFaction":["SURVIVORS",100]}` |
| 6 | GiveItem | `ItemRecord`, `NumItems` | 给物品 | `{"GiveItem":["records/items/a.dbr",1]}` |
| 7 | GiveLevel | `NumLevels` | 给等级 | `{"GiveLevel":[1]}` |
| 8 | GiveExperience | `Experience` | 给经验 | `{"GiveExperience":[1000]}` |
| 9 | GiveToken | `TokenName` | 给令牌 | `{"GiveToken":["TOKEN_A"]}` |
| 10 | RemoveToken | `TokenName` | 移除令牌 | `{"RemoveToken":["TOKEN_A"]}` |
| 11 | LuaScript（动作） | `call`, `args` | 无 `tail` | 见 §10 |
| 12 | DebugPrint | `Text` | 调试输出 | `{"DebugPrint":["hello"]}` |
| 13 | PlaySound | `SoundRecord` | 播放声音记录 | `{"PlaySound":["records/sounds/a.dbr"]}` |
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
| 28 | PlayVideo | `Video path`, `AllowSkip` | 播放视频 + 是否允许跳过 | `{"PlayVideo":["video/intro.ivf",false]}` |

### 8.1 动作 0 / 16

- 两个类型都不可用；`convert` 会报错 `动作类型 N 未实现（工厂返回 null）`。
- 不要尝试写入，官方编辑器打开也可能报错。

### 8.2 动作 19 ScriptEvent

字段名与类型名相同。字符串通常是 `EV_` 前缀的脚本事件名：

```json
{ "ScriptEvent": ["EV_PLAYER_ENTERED_REGION"] }
```

### 8.3 动作 28 PlayVideo 的字段名

官方字段名是 **`Video path`**（中间有空格，`path` 小写）。位置写法最省事：

```json
{ "PlayVideo": ["video/intro.ivf", false] }
```

具名写法：

```json
{ "PlayVideo": { "Video path": "video/intro.ivf", "AllowSkip": false } }
```

---

## 9. P 形态（路径列表）

`P` 用于 HasKilled（type 10）和 HasKilledProxy（type 16）。

### 9.1 推荐写法：具名对象

```json
{ "HasKilled": { "MonsterRecords": ["records/monsters/a.dbr", "records/monsters/b.dbr"], "KillCount": 3 }, "cmp": ">=" }
{ "HasKilledProxy": { "ProxyRecords": ["records/proxies/a.dbr"], "KillCount": 1 } }
```

- 路径数组键可以用官方字段名（`MonsterRecords` / `ProxyRecords`），也可以用通用 `paths`。
- 尾字键可以用 `KillCount` / `trailing` / `count`，大小写不敏感。
- 路径数组为空时只写尾字（KillCount=0）。

### 9.2 原始 type 写法

```json
{ "type": 10, "args": { "paths": ["records/monsters/a.dbr"], "trailing": 3 }, "cmp": ">=" }
```

### 9.3 一个容易踩的坑：位置数组无法设置 KillCount

下面这种写法**只设置路径，KillCount 永远是 0**：

```json
{ "HasKilled": [["records/monsters/a.dbr"]] }   // KillCount = 0
```

因为位置参数的第 0 项被当作路径数组，尾字没有第二个入口。要设置尾字，请用上面的具名对象，或用一个对象包住：

```json
{ "HasKilled": [{ "paths": ["records/monsters/a.dbr"], "trailing": 3 }] }
```

### 9.4 路径内容

- 路径是文件里原样保存的字符串，工具不检查存在性、不补全扩展名。
- 官方文件通常写 `records/monsters/xxx.dbr` 这类相对路径。
- 路径数组的顺序会原样写入；是否有序语义由游戏决定。

---

## 10. L 形态（Lua）

`L` 用于条件 12 与动作 11。

### 10.1 推荐写法：专用对象

条件：

```json
{
  "LuaScript": {
    "call": "gd.example.check()",
    "args": ["arg1", "arg2"],
    "tail": { "t0": 0, "t1": 0, "t2": 0, "t3": 0, "text": "TRUE" }
  }
}
```

动作：

```json
{ "LuaScript": { "call": "gd.example.run()", "args": ["arg1"] } }
```

要求：

- 外层键名是官方类型名 `LuaScript`。
- `call` / `function`：函数名或调用串，**建议固定写小写 `call`**。
- `args`：字符串数组；每个字符串写入一个参数，其余数值字段写 0。
- `tail`：仅条件有效；可以完全省略（工具写全 0 + 空串）。
- `tail` 内部键为精确小写 `t0`、`t1`、`t2`、`t3`、`text`。

### 10.2 简化写法：只给函数名

```json
{ "LuaScript": "gd.example.check()" }
{ "type": 12, "args": "gd.example.check()" }
```

只写函数名，Args 为空，条件尾部全 0/空。

### 10.3 原始形态

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

### 10.4 尾部字段（Evaluate 区）

官方编辑器把条件版尾部显示为 **Evaluate** 区：返回类型下拉、比较符下拉、期望值。`t0..t3` 与这三者的逐位对应关系尚未完全确认，因此：

- 新建普通 Lua 条件时，`tail` 可以省略，工具会写全 0/空串；
- 如果你从现有文件导出 JSON 再改，应保留原 `tail` 值；
- 不要凭猜测填写 `t0..t3`。

### 10.5 常见错误

- `{"LuaScript": {"Function": "gd.x()", "args": ["a"]}}`：`Function` 会被当成单字符串，`args` 丢失。请用 `call`。
- 动作 L 写 `tail`：被忽略（动作没有尾部）。
- `args` 里放数字：非字符串项会变成空串。

---

## 11. 引用值、路径与字符串

### 11.1 任务引用值

条件/动作里涉及 `Task` / `Objective` 的字段，填的是 QST 文件里**落盘的持久化 uid**：

- `Task` = QST 的 `tasks[].uid`。
- `Objective` = QST 的 `tasks[].objectives[].uid`。

这些值不是数组下标，也不是任务名哈希（虽然官方编辑器把它们显示成 `?` 或十六进制）。生成 QST 时请显式写 `uid`，再在 CNV 里引用同一个数：

QST：

```json
{ "uid": 305419896, "title": "……", "objectives": [ { "uid": 2271560481, "text": "……" } ] }
```

CNV：

```json
{ "BeginQuestTask": ["quests/example.qst", 305419896] }
{ "HasQuestObjective": { "Quest": "quests/example.qst", "Task": 305419896, "Objective": 2271560481, "Satisfied": true } }
```

> 若 QST 的 `uid` 省略，工具会生成随机非 0 值，生成物不可复现，也无法预先在 CNV 中引用。**需要跨文件引用时一定显式写 uid。**

### 11.2 路径字符串

| 字段 | 基准目录（官方行为） | 示例 |
|---|---|---|
| `QuestFile` / `Quest` | `resources/` | `quests/example.qst` |
| `vo`（CNV 配音） | `resources/` | `conversations/alex_01.ogg` |
| `ItemRecord`、`SoundRecord`、`SkillRecord`、`BuffName` | 游戏数据库（`database/`） | `records/items/gear/example.dbr` |
| `MonsterRecords`、`ProxyRecords` | 游戏数据库（`database/`） | `records/monsters/example.dbr` |
| `Video path` | `resources/` 或视频目录 | `video/intro.ivf` |

工具**只把字符串原样写进文件**，不校验存在性、不做路径规范化。请使用正斜杠 `/`，并与游戏内实际路径一致。

### 11.3 字符串编码与特殊字符

- 文件内字符串为 UTF-8；JSON 源文件也建议 UTF-8。
- 中文、日文、重音字符都可以直接写。
- JSON 转义 `\n` 会写入真正的换行字节；游戏内换行约定用字面量 `{^n}`，推荐在 `text` / `languages.texts` 里都写 `{^n}`。
- 路径中的反斜杠需要 JSON 转义（`\\`）；建议直接用 `/`。

---

## 12. 完整可运行示例

下面这份 JSON 覆盖了本文大部分特性，可直接运行：

```json
{
  "format": "cnv",

  // 6:支持 vo；5:不支持 vo。
  "version": 6,

  // 省略 id 会按 speaker 自动生成；这里显式给 0。
  "id": 0,

  // 根节点文本（槽 0）。
  "speaker": "Alex",

  // 根 flags：0 无；1 仅一次；2 总是显示；4 不重要。
  "flags": 0,

  // 根配音路径（version>=6）。
  "vo": "",

  // 根条件关系与列表。
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
      "text": "你终于来了。",
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

    { "level": 2, "kind": "continue", "text": "继续" },
    {
      "level": 3,
      "kind": "accept",
      "text": "我接受。",
      "act": [
        { "BeginQuestTask": ["quests/example.qst", 305419896] },
        { "GiveItem": ["records/items/gear/example.dbr", 1] }
      ]
    },
    { "level": 3, "kind": "decline", "text": "以后再说。" },
    { "level": 3, "kind": "end", "text": "结束" },

    // 跳到 rows 第 5 行（上面的 end）。
    { "level": 1, "kind": "link", "text": "", "link": 5 },

    // 随机分支。
    { "level": 1, "kind": "random", "text": "随机问候" },
    {
      "level": 2,
      "kind": "speech",
      "text": "随机结果 A",
      "logic": "OR",
      "cond": [
        { "IsDifficulty": [2], "cmp": "=" },
        { "HasMoney": [1000], "cmp": ">" }
      ]
    },
    { "level": 2, "kind": "speech", "text": "随机结果 B" },
    { "level": 2, "kind": "end", "text": "随机结束" }
  ]
}
```

完整示例（覆盖全部字段、全部节点类型、30 种条件、27 个动作与 `languages`）见 [`JSON示例/cnv_full.json`](JSON示例/cnv_full.json)，可直接运行：

```bash
CnvQstEditor-cli.exe convert JSON示例/cnv_full.json -o demo.cnv
```

该文件顶部有字段总览，正文每个字段都有就地注释；`languages` 的每一行都标注了对应节点。

---

## 13. CLI 命令参考

`CnvQstEditor.exe` 与 `CnvQstEditor-cli.exe` 命令相同；下文用 `CnvQstEditor-cli.exe`。查看完整用法：

```bash
CnvQstEditor-cli.exe help
```

### 13.1 生成

```bash
CnvQstEditor-cli.exe convert <spec.json> [-o out.cnv]
```

- `make` 是 `convert` 的兼容别名，旧脚本仍可用。
- 输入扩展名必须是 `.json`（内容允许 `//`、`/* */` 注释与尾逗号）。
- `-o` 也接受 `--out`。
- 省略 `-o`：输出到 JSON 同目录、同名 `.cnv`（`hello.cnv.json` → `hello.cnv`）。
- 生成后会自动回读校验；看到 `三检查通过` 表示文件结构完整。

### 13.2 模板

```bash
CnvQstEditor-cli.exe sample cnv                    # 释放内置 cnv_full.json 到当前目录
CnvQstEditor-cli.exe sample cnv -o D:\mydir        # 释放到指定目录
```

`sample` 释放的是内置全量示例（cnv_full.json / qst_full.json，含逐字段描述），可直接作为 convert 输入或删改起点。

### 13.3 查看与验证

```bash
CnvQstEditor-cli.exe view   <file.cnv>       # 编辑器风格树形文本
CnvQstEditor-cli.exe verify <file或目录>      # 检查文件结构
```

### 13.4 GUI 同名命令

```bash
CnvQstEditor.exe convert hello.json -o hello.cnv
```

无参数启动则进入图形界面。

---

## 14. 常见错误

| 信息 | 原因 |
|---|---|
| `spec 缺少 "format"（cnv 或 qst）` | 顶层没有 `format`。 |
| `cnv spec 缺少 "rows" 数组` | 顶层缺少 `rows`，或 `rows` 不是数组。 |
| `第 N 行缺少 level` | `rows` 元素没有 `level`。 |
| `第 N 行 level 必须 ≥1` | 写了 `0` 或负数。 |
| `第 N 行 level=X 找不到 level=X-1 的父行` | 跳级，或第一行不是 1。 |
| `第 N 行缺少 kind` | 没有 `kind`。 |
| `第 N 行未知 kind：xxx` | `kind` 字符串不在表中。 |
| `第 N 行 cond 必须是数组` | `cond` 写成了对象/字符串。 |
| `第 N 行 cond 的每一项必须是对象` | 数组元素不是对象。 |
| `未知类型名 "xxx"` | 具名类型拼错；错误信息会列出可用名。 |
| `条件类型 N 越界（有效范围 0–29）` | `type` 超出 0–29。 |
| `动作类型 N 未实现（工厂返回 null）` | 动作 type 用了 0/16，或越界。 |
| `比较符 N 越界（0..5）` | `cmp` 数字超范围。 |
| `未知比较符 "xxx"` | `cmp` 字符串不认识。 |
| `logic 只接受 AND/OR` | `logic` 字符串写错。 |
| `CNV 版本必须 ≥5` | `version` 太小。 |

---

## 15. 限制与常见坑

### 15.1 结构限制

1. **只有一棵树**：所有 `rows` 都挂在根节点下；不能创建多个根。
2. **行顺序必须是前序**：文本槽按行顺序写，节点按树展平；手工调换行会破坏文本对应关系。
3. **嵌套深度**：上限 512；实际对话建议控制在几十层以内。
4. **语言块行数必须对齐节点**：显式 `languages` 的每个块行数 = `1 + rows.Length`；多语言时各块行数一致。
5. **link 目标**：写的是展平序号（1 起行号，0 为根），请确认目标存在。

### 15.2 条件/动作限制

1. **条件 30 种、动作 27 个可用**；动作 0/16 不可写。
2. **不要手写 `subType`**：默认值已经正确，改错会让官方编辑器读错字段。
3. **枚举字段不要越界**：官方编辑器拿它当下标查表，越界会报错。
4. **布尔/任务步骤条件不需要 `cmp`**：官方固定 `=`。
5. **动作没有 `cmp`、没有 AND/OR**：按数组顺序执行。
6. **P 形态位置数组不写 KillCount**：见 §9.3。
7. **Lua 条件尾部未完全确认**：没有把握就省略 `tail`，或原样保留导出值。
8. **具名类型名的位置**：必须是第一个非元数据键。

### 15.3 路径与引用

1. 工具**不检查** `.qst`、`.dbr`、音频文件是否存在。
2. `Task` / `Objective` 引用必须与 QST 文件中的持久化 uid 完全一致；随机 uid 无法预先引用。
3. 路径原样写入，大小写、斜杠方向、扩展名都由你负责。
4. 要**修改现有文件**，建议直接用 `CnvQstEditor.exe` 打开；手写 JSON 适合从零新建。

---

## 附录：快速检查清单

写完后逐项确认：

- [ ] `format` 是 `"cnv"`。
- [ ] `version` ≥5，推荐 6。
- [ ] 每行有 `level` 和 `kind`；第一行 `level=1`，不跳级。
- [ ] `rows` 按前序排列。
- [ ] 每个 `link` 的目标在 `0..rows.Length`。
- [ ] 条件/动作类型名拼写正确；类型名是对象第一个非元数据键。
- [ ] 写了 `languages` 时，每个块的 `texts` 行数 = `1 + rows.Length`，且 `link` 行留空；换行用 `{^n}`。
- [ ] 条件需要比较的写了 `cmp`；布尔/任务步骤条件保持默认。
- [ ] 没有手写 `subType`；`HasItem` 的 `CompleteOnly`、`HasQuestObjective` 的 `Objective` 按本文写法即可。
- [ ] P 形态用对象写 `KillCount`，不是位置数组。
- [ ] 跨文件 `Task` / `Objective` 引用与 QST 的 `uid` 一致。
- [ ] 运行 `convert` 后三检查通过，再用 `view` / 官方编辑器抽查。
