# Grim Dawn CNV/QST 格式权威文档（已解码）

> 本文整合 [R-Hidayatullah/gd-utility](https://github.com/R-Hidayatullah/gd-utility)（MIT）反编译
> Game.dll 加载器得出的格式结论与本项目 446 个本地样本的验证结果，取代
> `CnvQstFormatSpec.md` 中与之冲突的早期推断（勘误对照见文末）。
> 参考解析器原文存于 `参考资料/gd-utility/`（commit `1d468f5`）。
>
> 实现库：`CnvQstLib`；CLI：`CnvQstTool`。
> 验证（2026-09-09）：**cnv示例 199/199、qst示例 247/247** —— 记录恰好铺满文件、
> 走序落在头部声明的本地化偏移、parse→write 逐字节一致（含全部 CNV v5 与多任务 QST）。

---

## 一、总体

| 格式 | 魔数 | 版本 | 加载器入口（Game.dll, base 0x180000000） |
|---|---|---|---|
| .cnv | `"CNV "` | ≥5（语料 6 为主、8 个 v5） | `Conversation::Build` @`0013E090`、`ConversationStep::Read` @`0013F770` |
| .qst | `"QST2"` | ≥7（语料全 9） | `Quest2::Load` @`00367D90`、`Quest2Task::Read` @`0036CD60` |

两种格式共用同一套 Scriptable 条件/动作系统，且本地化块相同（`Engine.dll` 的
`PortableLocalization::Read` @`001877D0`）。所有读取均为**小端、紧凑、无对齐**
（`*(T*)cursor; cursor += sizeof(T)`）。`Build`/`Load` 另对整块做 MD5（多人校验用，非布局）。

**两道独立完整性检查**（全部文件通过）：
1. 记录必须恰好铺满整个文件（`pos == size`）；
2. 记录走序终点必须等于头部 +0x08 声明的本地化块偏移——**该字段由工具链写入、加载器从不读**，
   因此是布局正确性的独立见证。

---

## 二、CNV（对话）

### 2.1 头部（仅 16 字节）

```
+0x00  char[4]  "CNV "
+0x04  u32      version            加载器拒绝 <5
+0x08  u32      本地化块偏移       写而不读（独立见证）
+0x0C  u32      对话 id
+0x10           根节点（内联记录，每文件恰 1 个 type=0）
```

### 2.2 节点（Step，深度前序递归）

```
i32     childCount            上限 100000，递归深度上限 512
u32     type
u32     unknown               → ConversationStep+0x50，语义未知
u32     linkId                仅 type==7
string  voPath                仅 version>=6（配音路径；v5 无此字段——写回时必须按版本门控）
i32 n ; ConditionCollection(n)
i32 m ; ActionCollection(m)
childCount × step
```

节点没有额外尾字段：一个节点的范围 = 自身字段 + 整棵子树。

**节点类型**（`GetTypeTag`/`GetSortOrder`；语料分布：1×6169、6×5897、2×3783、7×3119、3×990、4×848、8×4、0×480）：

| type | 标签 | 含义 |
|---|---|---|
| 0 | — | 根（每文件恰一） |
| 1 | — | NPC 台词（speech），可用性检查挂在这类节点上 |
| 2 | `tagContinue` | 玩家选项 |
| 3 | `tagAccept` | 玩家选项 |
| 4 | `tagDecline` | 玩家选项 |
| 6 | `tagEndConversation` | 结束对话 |
| 7 | （跟随链接） | 跳转，携带 linkId |
| 8 | — | selector：文本经由可用的 type 1 子节点解析 |

type 5 存在于枚举但无文件使用。加载器运行时会把"无子节点的 type 2"重映射为 6（仅运行时；解析器报告文件原貌）。

### 2.3 图与文本的联结（无显式索引）

- `ConversationStep::Read` 每读一个节点 `++counter` → **节点在深度前序走序中的位置即其身份**：
  - `GetText(step)` = `Localization.GetText(step->GetLocalizationIndex())`，即
    **本地化块是按走序索引的扁平数组**（串 n = 节点 n 的文本；串 0 = 根 = 说话人名，如 "Alex"）。
  - `EstablishLinks`：`steps[i]->link = steps[steps[i]->linkId]`。
- type 7 无自有文本时回退到链接目标的文本（`GetText` 显式回退）。

### 2.4 本地化块

```
u32 languageCount（上限 64）
× languageCount:
    string  tag                 "enUS"/"deDE"…
    i32     stringCount         cnv 上限 200000
    × stringCount: string
```

### 2.5 结构编辑不变式（编辑器增删移的实现约束）

- **Index / linkId / 本地化槽 三位一体**：`Step.Index`、本节点在深度前序走序中的位置、本地化表槽号三者始终相等。
  `Renormalize()` 重建 Steps 为深度前序、按"节点身份"重映射 `linkId`（若目标被删除则沿原始祖先链 clamp），
  并让每个语言块的 Texts 长度对齐节点数、按身份跟随节点移动——使 `多个语言块都在跟随节点走`、
  `甚至节点本身的文本槽也跟随`，从而 `增删移` 不破坏任一对齐。
  编辑前后必须 `Parse(Write())` 三连校验：tiled / locOffset / round-trip。
- **type 7（link）节点在流里恒带 LinkId**：`Write` 仅当 `HasLink` 才写出 LinkId，而解析器总是读一个。
  因此新建 link 节点必须 `HasLink=true` 并给一个合法目标（`InsertChild` 已初始化自指 LinkId），
  否则 write→parse 错位导致整个文件解析失败（已实测修复）。
- **本地化正文行号 = 槽号，不可增删**：`Languages[li].Texts` 是扁平字符串表，游戏按行号严格匹配同名
  txt（`text_zh.arc` 流程）。编辑器"本地化文本"列直接读写该内存表（enUS 槽），单元格值 `{^n}`→真 `\n`，
  提交反译 `\n`→`{^n}`；空槽灰字可留空。

---

## 三、QST（任务）

### 3.1 头部（仅 24 字节）

```
+0x00  char[4]  "QST2"
+0x04  u32      version            拒绝 <7；全部文件为 9
+0x08  u32      本地化块偏移       写而不读（独立见证）
+0x0C  u32      任务 id
+0x10  u32      unknown（0..4）
+0x14  u32      taskCount（上限 4096）
+0x18           首个 task 记录
```

### 3.2 Task / Objective / Event

```
Task
  u32 unknown          → Quest2Task+0xA8
  u32 unknown          → Quest2Task+0xAC
  i32 n ; n × Event    "incentives"
  i32 n ; n × Objective
  i32 n ; n × Event
  u8  if version >= 8  → +0xC0
  u8  if version >= 9  → +0xC2

Objective                    Event
  u32 unknown → +0x10          u32 unknown → +0x2C
  u32 unknown → +0x34          i32 n ; ConditionCollection(n)
  i32 n ; ConditionCollection  i32 m ; ActionCollection(m)
  i32 m ; ActionCollection
```

**两个版本门控尾字节是多任务文件的生命线**：漏读即每个 task 静默错位 2 字节
（首个 task 看起来仍然正确——这正是本项目 2026-08 分析 mq_burrwitch 时
"enUS 出现在 0x308 而 0x38+DataSize=0x341" 之谜的根源）。

**uid 不落盘**：`Load` 从 1 起置计数器并按文档顺序穿线分配（每个 task 占 2 个、objective/event 各 1 个）。
**目标列表只有一份**，运行时克隆成三个难度槽（`+0x08 + 24*(difficulty+1)`）。

**task 行只读不增删**：task 增删会牵连整张本地化块逐槽映射（task 与文本表按槽对应，
增删 task 后"2 之后对应关系仍未定"），故 QST 表格化编辑器对 task 行抑制增删；
objective/event 沿用既有增删能力。QST 本地化逐 task 映射未解，故 QST 表不含"文本"列。

观测补充（本地锚定）：task 的 +0xA8 常等于前一个 task 的 `BeginQuestTask` 的 I 参数；
该 I 与 CNV `QuestTaskComplete` 的 I 均为"文件引用 hash 左移 8 位"
（例：mq_makingadeal.qst 引用 hash `0x01876A2A` → I = `0x876A2A00`；编辑器显示为 "?"）。

### 3.3 本地化块

与 CNV 相同；串表条目 **0 = 任务组名、1 = 任务名**（`GetGroup`/`GetName`），
2 起按位置对应 task 标题/描述/目标文本与空槽位（空槽位 = 长度前缀 0 的空串，
游戏按位置回填，导出/导入必须原样保留空行）。2 之后与 task 的精确对应关系仍未定，
浏览器/导出按 "text" 呈现（mq_burrwitch 观测：每 task 依次 [标题, 描述, 目标…] + 若干空槽）。

---

## 四、Scriptable 条件/动作系统（CNV/QST 共用）

```
ConditionCollection(n)            ActionCollection(n)
  u32 logic（仅 n>0 时读）          × n:  u32 type; u8 subType; <body>
  × n: u32 type; u8 subType; u32 uid; <body>
```

**两个易错的不对称**：动作无 uid、无前导字；条件集合的前导 logic 字在集合自己的 Read 里，
调用方只在 count>0 时才调 Read——**空列表只占计数字本身**。

**logic 字**（语义未经引擎证实，本地观测+金样直接证据）：0=AND（jailor_01 多条条件均为 AND 列表）、
1=OR（mq_burrwitch 双条件处 `{HasToken, ServerHasToken}` OR 列表；446 样本全量一致）。

### 4.1 body 规格

`S`=字符串、`I`=u32、`B`=u8；小写 `i`/`b` = 仅当 subType==1 时存在；
`P` = `i32 n + n×字符串 + u32 尾字`；
`L`（Lua 形态）= `串(调用名) + i32 n + n×{u32,u8,u32,串} +（条件另有固定尾部 {u32,u32,u8,u32,串}）`。

### 4.2 条件（26 类，`ScriptableCondition::Create` 跳转表）

| type | body | 类名（gd-utility 证实） | 本地锚定 |
|---|---|---|---|
| 0 | `S B` | OnQuest | |
| 1 | `S I B` | QuestTaskComplete |（I=任务标识=引用hash<<8，编辑器显示 "?"）|
| 2 | `I` | 未命名 | 样本 0 |
| 3 | `I` | 未命名 | **IsLevel**（推断；语料值仅 {5,10} 3 例）|
| 4 | `I` | 未命名 | **IsDifficulty**（0=Normal/1=Epic/2=Legendary，mq_burrwitch task4 三事件同现证实）|
| 5 | `B` | 未命名 | 候选 IsHardcore/KnowsPlayer（样本 0）|
| 6 | `S I` | HasFaction |（S=阵营名，I=数值；编辑器比较符与 subType 的关系未定）|
| 7 | `S B` | 未命名 | **HasToken**（refugee_01 `BLACKSMITH_APPRENTICE`）|
| 8 | `I` | 未命名 | **HasMoney**（推断；值域 100–12000，52 例中 46 例与 HasItem 同列表，疑似购买/交换检查）|
| 9 | `S I b` | HasItem |（b=CompleteOnly，推断，编辑器签名）|
| 10, 16 | `P` | HasKilled / HasKilled(变体) |（P 尾字=KillCount，推断，编辑器签名）|
| 11 | `S I i B` | HasQuestObjective | |
| 12 | `L` | LuaScript（条件）| |
| 13 | `B` | 未命名 | 候选 IsHardcore/KnowsPlayer（样本 0）|
| 14 | `S I B` | OnQuestTask | |
| 15 | `S I` | 未命名 | 候选 UsedQuestItem（样本 0）|
| 17 | （空）| 未命名 | **WaitForCompletion**（mq_burrwitch task4 目标唯一条件，无参数）|
| 18 | `S B` | 未命名 | **ServerHasToken**（burrwitch OR 对的右侧）|
| 19 | `S B` | QuestBlocked | |
| 20 | `S B` | 未命名 | 候选 AnyoneHasToken（样本 0）|
| 21 | `S I` | CheckRandomValue | |
| 22 | `I` | 未命名 | **哈希条件**（仅选择步、全部 OR 列、hash 连续簇；奖励/忠诚包商人门控，如 npc_bonusitems_01；编辑器词表无对应条目）|
| 23 | `I` | 未命名 | 样本 0 |
| 24 | `I` | 未命名 | 样本 0 |
| 25 | `I B` | 未命名 | 候选 HasMastery（样本 0）|

### 4.3 动作（27 类；工厂对 0 和 16 返回 null，无文件使用）

| type | body | 类名（gd-utility 证实） | 本地锚定 |
|---|---|---|---|
| 0, 16 | — | 未实现 | 无文件使用 |
| 1 | `S I` | BeginQuestTask | |
| 2 | `S` | 未命名 | **CompleteQuest**（burrwitch task4 OnComplete 第二事件）|
| 3 | `S I` | 未命名 | **CompleteQuestTask**（推断；body=任务文件+hash，常与 BeginQuestTask 同为大列表）|
| 4 | `I` | GiveMoney | |
| 5 | `S I` | 未命名 | **GiveFaction**（推断；S=阵营名、I=数值 50–400，QST 792 例像声望奖励；候选 SetFaction 未排除）|
| 6 | `S I` | GiveItem | |
| 7 | `I` | GiveLevel | |
| 8 | `I` | GiveExperience |（350/22000/54000 金额已对上编辑器）|
| 9 | `S` | 未命名 | **RemoveToken**（推断；与 10 可能互换）|
| 10 | `S` | 未命名 | **GiveToken**（推断；与 9 可能互换）|
| 11 | `L` | LuaScript（动作）| |
| 12 | `S` | 未命名 | 样本 0 |
| 13 | `S` | （预载 SoundPak）| 疑似 PlaySound，待证据 |
| 14 | `I` | 未命名 | **PlayAnimation**（jailor_01 根节点唯一动作，I=9 ↔ 编辑器枚举 "Chat"）|
| 15 | `I` | GiveSkillPoint | |
| 17 | `S` | 未命名 | **Notification**（burrwitch `tagNotification_…` 串）|
| 18 | `I` | GiveAttribPoint | |
| 19 | `S` | 未命名 | **ScriptEvent**（推断；串全部 `EV_…` 前缀）|
| 20 | `S I` | GiveRandomItem | |
| 21 | `S I I` | GenerateRandomValue | |
| 22 | `S` | CastSkill | |
| 23 | `S` | 未命名 | 样本 0 |
| 24 | `S I` | 未命名 | 样本 0 |
| 25 | `I` | GiveDevotion | |
| 26 | `I` | GiveTribute | |
| 27 | `I` | UnlockTutorial | |
| 28 | `S B` | PlayVideo | |

### 4.4 语料类型分布（本地 446 文件，条件+动作混合计数）

type4×2084、1×907、9×853、8×835、5×792、11×532、14×509、7×484、10×415、3×381、6×343、
21×297、17×266、2×258、19×146、22×130、20×115、18×108、0×24、12×23、15×2、25×1。
CNV 侧 21,290 节点 / 11,326 条件+动作来自 gd 语料（480 文件）。

---

## 五、官方本地化 TXT（text_zh.arc 流程）

- 一行 = 本地化字符串表一条；CNV 首行 = 说话人名；空槽位 → 空行（位置不可增删——游戏按行号回填）。
- UTF-8 无 BOM、CRLF、文件末尾再补一个 CRLF；行中保留 `{^n}`；行尾 `{^n}`/`{^n` 剥除；
  串内换行归一为 `{^n}`；行首尾 Trim。
- 本项目 `CnvQstTool txt` 为**按格式精准提取**，输出与 GrimDawnCnvQstToTXT v0.9
  （"首个 enUS+12 截断 + 逐 null 强插换行"的强读算法，移植版见 `legacytxt` 命令）在
  全部 446 个样本上**逐字节一致**，同时修复了旧算法的理论缺陷（无结构校验、依赖文件尾含 enUS、
  按 char 切割的多字节风险）。
- `import-txt` 把翻译行回填进语言块并重序列化（重算长度/偏移），行数必须与字符串表严格一致；
  原文回填为字节一致的恒等操作。
- 完整翻译管线：`txt` 导出 → 翻译 → `import-txt` 或（官方同款位置回填方式）arzedit-GUI 打包 `text_zh.arc`。

---

## 六、对 CnvQstFormatSpec.md 的勘误对照

| 旧结论 | 权威修正 |
|---|---|
| CNV Header 64 字节、0x24~0x3C 七个 Unknown 字段 | CNV Header 仅 16 字节；0x10 起即根节点记录，"Unknown"实为根节点体 |
| 0x08=DataSize、0x0C=CRC32 | 0x08=本地化块偏移（写而不读）、0x0C=对话 id |
| LocSection 恒占 DataSize 末尾 60 字节 | 位置由走序自然决定，终点=头部声明偏移；"-60"系样本过拟合 |
| `06 00 00 00` 语义未定 | 节点类型 u32=6 = tagEndConversation |
| QST Header 56 字节（DataSize/双 Hash/5 Flags） | QST Header 仅 24 字节；DataSize 概念不存在 |
| QST LocSection 边界"需重新界定" | task 记录含 v≥8/v≥9 两个门控尾字节，漏读即错位 2 字节/task |
| 条件类型 ID ≠ 编辑器下拉序号 | 正确，且已锚定 8 条件 + 8 动作（含推断；见 §4.2/§4.3 本地锚定列）|

## 七、仍然未知（按"保留原字节"策略贯穿解析与写回）

step 的 +0x50 字、task 的 +0xA8/+0xAC、两个版本门控 flag 位、objective 的两个 u32、event 的一个 u32、
头部 +0x10、条件集合 logic 字的引擎语义（本地观测 0=AND/1=OR）、条件 {2,23,24} 与动作 {12,23,24}
的语义名（样本 0）、条件 22 哈希门控所引的字符串、条件 5/13/15/20/25 候选名、动作 13 名称证据、
推断名的真机核对（条件 3/8，动作 3/5/9/10/19；9/10、3/5 两对可能互换/未排除）、
比较符（编辑器 `>=` 等）的编码位置、step unknown 位 0x4/0x8/0x10/0x20 与编辑器
{Once Only/Always Show/Unimportant} 的对应。
