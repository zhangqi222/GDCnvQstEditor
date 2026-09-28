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

与 CNV 相同；串表条目 **0 = 任务组名、1 = 任务名**（`GetGroup`/`GetName`）。

**串表条数公式（2026-09-28 解出，本地 247 个 QST 样本零误差，并在官方 QuestEditor 中核实）**：

```
S = 2 + 2T + O + I + E
  2  = 组名 + 任务名
  2T = 每个 task 的标题 + 描述
  O  = 每个 objective 一条
  I  = 每个 incentive（编辑器 OnAccept）一条
  E  = 每个 event（编辑器 OnComplete）一条
```

- 证据一（语料）：`census` 的"QST 本地化槽位配平"对 247 个文件算 `S − (2+2T+O+I+E)`，
  结果**全部为 0**（残差直方图 `0×247`）。此前误以为"每 task 若干空槽不定"。
- 证据二（编辑器）：串数**不足**时官方 QuestEditor 抛
  `ArgumentOutOfRangeException`（`LocalizationInfo(Quest, version)` → `List.get_Item`）——
  这也解释了为什么第一版探测文件（1 task / 2 objective / 1 event 只给了 6 条，实际需 7 条）直接崩溃；
  按公式补到 7 条后正常打开，且标题/描述/目标文本全部落在预期节点上（见 `反馈截图/qst_1task.qst.png`）。
- 空槽位 = 长度前缀 0 的空串，游戏按位置回填，导出/导入必须原样保留空行。
- **槽位先后顺序**（objective 与 incentive/event 谁在前）由 `probe qst` 生成的 `qst_order.qst` 确认中：
  该文件 1 task + 1 incentive + 2 objectives + 1 event，串表恰好 8 条、文本写着槽位号。
- 增删 task/objective/event 后必须按此式重建串表 —— 这就是"多任务编辑"的全部约束所在，
  也是本项目 2026-08 卡住的"QST 边界"问题的最终答案。

---

## 四、Scriptable 条件/动作系统（CNV/QST 共用）

```
ConditionCollection(n)            ActionCollection(n)
  u32 logic（仅 n>0 时读）          × n:  u32 type; u8 subType; <body>
  × n: u32 type; u8 subType; u32 比较符; <body>
```

**两个易错的不对称**：动作无比较符字、无前导字；条件集合的前导 logic 字在集合自己的 Read 里，
调用方只在 count>0 时才调 Read——**空列表只占计数字本身**。

**logic 字**（语义未经引擎证实，本地观测+金样直接证据）：0=AND（jailor_01 多条条件均为 AND 列表）、
1=OR（mq_burrwitch 双条件处 `{HasToken, ServerHasToken}` OR 列表；446 样本全量一致）。

### 4.1 条件的第 3 个字 = 比较符（2026-09-28 修正）

条件记录里 `u32 type + u8 subType` 之后那个 u32，gd-utility 称为 **uid**——**实测它不是 uid，是比较符**：

- 造探测文件：同一条件（HasFaction / "SURVIVORS" / 10000）、同一 subType=1，**只改这个字**，
  官方编辑器显示的比较符随之变化（该字=1 → 显示 `<=`）。
- 反证：jailor_01 的 `HasFaction "SURVIVORS" >= 10000`（编辑器真机显示 `>=`）该字=3，
  而探测文件同条件该字=1 显示 `<=`——两者只差这一个字。
- `census` 分布与该解释吻合：**没有比较符的条件**（OnQuest / HasToken / ServerHasToken / QuestBlocked /
  QuestTaskComplete / OnQuestTask / LuaScript / HasQuestObjective / CheckRandomValue / OwnsDLC）
  该字恒为 2；**有比较符的条件**取 0/3/4/5（HasItem 3×191 + 0×51、HasFaction 3×57 + 0×18 + 4×5、
  HasMoney 3×37 + 0×15、IsDifficulty 2×22 + 5×1）。
- 动作没有这个字；**QST 的条件也不落盘这个字**（"uid 不落盘"在 QST 侧成立），
  故 QST 条件无从表达比较符——与语料相符。
- 0..5 的完整符号对应由 `probe uid` 交官方编辑器读出（待回填，见 §7）。

`ScriptRecord.Uid` 字段名沿用，含义按此更正；GUI 里该输入框已改标"比较符（条件）"。

### 4.2 body 规格

`S`=字符串、`I`=u32、`B`=u8；小写 `i`/`b` = 仅当 subType==1 时存在；
`P` = `i32 n + n×字符串 + u32 尾字`；
`L`（Lua 形态）= `串(调用名) + i32 n + n×{u32,u8,u32,串} +（条件另有固定尾部 {u32,u32,u8,u32,串}）`。

### 4.3 条件（26 类，官方编辑器逐类型辨认完毕）

> 2026-09-28：`probe cond`（26 个节点 = 26 个 type）在官方 ConversationEditor 中全部显示出官方名，
> 下表"编辑器实显"一列即读到的结果，证据见 `反馈截图/probe_cond.cnv.png`。

| type | body | 名字 | 编辑器实显 / 备注 |
|---|---|---|---|
| 0 | `S B` | OnQuest | `OnQuest T0, True` |
| 1 | `S I B` | QuestTaskComplete | `QuestTaskComplete T1, ?, True`（任务标识显示 "?"）|
| 2 | `I` | **HasExperience** | `HasExperience <= 10000` |
| 3 | `I` | **IsLevel** | `IsLevel <= 10000`（语料值仅 {5,10}）|
| 4 | `I` | IsDifficulty | `IsDifficulty <= 10000`（I 值 0/1/2 = Normal/Epic/Legendary）|
| 5 | `B` | **IsHardcore** | `IsHardcore True` |
| 6 | `S I` | HasFaction | `HasFaction T6, <= 10000` |
| 7 | `S B` | HasToken | `HasToken T7, True` |
| 8 | `I` | **HasMoney** | `HasMoney <= 10000`（值域 100–12000，常与 HasItem 同列表）|
| 9 | `S I b` | HasItem | `HasItem T9, <= 10000, True`（b=CompleteOnly）|
| 10 | `P` | HasKilled | `HasKilled T10, <= 1`（尾字=KillCount）|
| 11 | `S I i B` | HasQuestObjective | `HasQuestObjective T11, ?, [ALL], True` |
| 12 | `L` | LuaScript | `LuaScript gd.probe.test() <> FALSE`（Evaluate 段）|
| 13 | `B` | **KnowsPlayer** | `KnowsPlayer True` |
| 14 | `S I B` | OnQuestTask | `OnQuestTask T14, ?, True` |
| 15 | `S I` | **UsedQuestItem** | `UsedQuestItem T15, <= 10000` |
| 16 | `P` | **HasKilledProxy** | `HasKilledProxy T16, <= 1`（不是"HasKilled 变体"）|
| 17 | （空）| WaitForCompletion | `WaitForCompletion` |
| 18 | `S B` | ServerHasToken | `ServerHasToken T18, True` |
| 19 | `S B` | QuestBlocked | `QuestBlocked T19, True` |
| 20 | `S B` | **AnyoneHasToken** | `AnyoneHasToken T20, True` |
| 21 | `S I` | CheckRandomValue | `CheckRandomValue T21 <= 10000` |
| 22 | `I` | **OwnsDLC** | `OwnsDLC <= 10000`（118 例全在 OR 长列表，门控奖励/忠诚包商人；旧称"哈希条件"作废）|
| 23 | `I` | **HasTribute** | `HasTribute <= 10000` |
| 24 | `I` | **EarnedDevotion** | `EarnedDevotion <= 10000` |
| 25 | `I B` | **HasMastery** | `HasMastery 10000 True`（MasteryName = SkillClass 枚举）|

官方条件下拉共 30 项，二进制 26 槽已全部落位，**尚余 4 项未定位**：
IsChallenge（I+比较符）、Hasbuff（S+B）、HasKilledFaction / HasKilledMonsterClass（两个数组+KillCount）。
槽位已排满 → 它们只能是某个 type 的 **subType 变体**，用 `probe sub` / `probe suball` 扫（§7）。

### 4.4 动作（27 个可用槽，官方编辑器逐类型辨认完毕）

> 证据见 `反馈截图/probe_act.cnv.png`。（动作 0/16：工厂返回 null，无文件使用。）

| type | body | 名字 | 编辑器实显 |
|---|---|---|---|
| 1 | `S I` | BeginQuestTask | `BeginQuestTask T1, ?` |
| 2 | `S` | CompleteQuest | `CompleteQuest T2` |
| 3 | `S I` | CompleteQuestTask | `CompleteQuestTask T3, ?` |
| 4 | `I` | GiveMoney | `GiveMoney 10000` |
| 5 | `S I` | GiveFaction | `GiveFaction T5, 10000` |
| 6 | `S I` | GiveItem | `GiveItem T6, 10000` |
| 7 | `I` | GiveLevel | `GiveLevel 10000` |
| 8 | `I` | GiveExperience | `GiveExperience 10000` |
| 9 | `S` | **GiveToken** | `GiveToken T9`（与 10 相对原先推断**互换**，已改正）|
| 10 | `S` | **RemoveToken** | `RemoveToken T10` |
| 11 | `L` | LuaScript | `LuaScript gd.probe.test(!)` |
| 12 | `S` | **DebugPrint** | `DebugPrint T12` |
| 13 | `S` | **PlaySound** | `PlaySound T13`（gd-utility 记作 SoundPakPreload，作废）|
| 14 | `I` | PlayAnimation | `PlayAnimation`（AnimationType，语料恒为 9）|
| 15 | `I` | GiveSkillPoint | `GiveSkillPoint 10000` |
| 17 | `S` | Notification | `Notification T17` |
| 18 | `I` | GiveAttribPoint | `GiveAttribPoint 10000` |
| 19 | `S` | ScriptEvent（推断）| 截图该行被弹窗遮住，未亲眼确认 |
| 20 | `S I` | GiveRandomItem | `GiveRandomItem …, 10000` |
| 21 | `S I I` | GenerateRandomValue | `GenerateRandomValue` |
| 22 | `S` | CastSkill | `CastSkill` |
| 23 | `S` | **UnlockFaction** | `UnlockFaction` |
| 24 | `S I` | **SetFaction** | `SetFaction`（与 5 GiveFaction 的区分已按编辑器确认）|
| 25 | `I` | GiveDevotion | `GiveDevotion` |
| 26 | `I` | GiveTribute | `GiveTribute` |
| 27 | `I` | UnlockTutorial | `UnlockTutorial` |
| 28 | `S B` | PlayVideo | `PlayVideo` |

**枚举型字段要给合法值**：编辑器拿它当下标查表，越界会弹 ArgumentOutOfRange 对话框
（首次探测时 PlayAnimation 的 I=10000 即如此）。


### 4.5 语料类型分布（本地 446 文件，条件+动作混合计数）

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

**字段语义类**
- step 的 +0x50 字：仅 speech 步非零，语料 0x8×83、0x10×57、0x20×43、0x4×3（四个位、三个开关）
  ——对应编辑器的 {Once Only / Always Show / Unimportant}，位的分配待 `probe flags` 结果回填
- task 的 +0xA8/+0xAC、objective 的两个 u32、event 的一个 u32、头部 +0x10
- 两个版本门控 flag 位（v≥8 / v≥9 尾字节）的语义

**比较符编码**：位置已定（条件记录里 type/subType 之后的 u32，见 §4.1），
但 0..5 各对应哪个符号（= / > / < / >= / <= / !=）尚未读出——`probe uid` 回填。
反向推理的约束：无比较符的条件该字恒为 2（那么 2 很可能是"＝"），有比较符的取 0/3/4/5，
而我们实测 1 → `<=`、3 → `>=`。

**4 项官方选项未定位**（26 个条件槽已排满 → 只能是某 type 的 subType 变体）：
- IsChallenge（body 为 I+比较符）：候选 2/3/4/8/22/23/24 的某个 subType
- Hasbuff（S+B：BuffName + HasBuff）：候选 7 HasToken / 18 ServerHasToken / 20 AnyoneHasToken
- HasKilledFaction、HasKilledMonsterClass（两个数组 + KillCount）：候选 10 HasKilled / 16 HasKilledProxy
  ——编辑器里 HasKilledFaction/MonsterClass 各有两个"Faction"数组，而二进制的 `P` 形态只有一组数组，
  差额很可能正是 subType 区分的两种布局
- OperMerchantWindow（动作，编辑器点击即报错）：疑为工厂返回 null 的 type 0/16
- 待回填手段：`probe sub` / `probe suball`（一类型一文件，subType 0..5）

**动作 19 = ScriptEvent**：仍是推断（编辑器截图该行被弹窗遮住）。
**动作 0/16**：工厂返回 null，无文件使用。

**QST 本地化槽位 ↔ task 映射（仍未解）**
- 官方 QuestEditor 打开我们生成的 `qst_1task.qst` / `qst_3task.qst` 时抛
  `ArgumentOutOfRangeException`（`LocalizationInfo(Quest, version)` → `List.get_Item`），
  即**编辑器按自己的公式索引串表，串数不足就越界**；已给探测文件补 18 条空槽再测。
- 反向配平（`census` / `dump` + `txt` 实测）：`mq_burrwitch` T=4 O=4 E=10 串=23、
  `sq_harts_amulet` T=2 O=4 串=27、`mq_wakingtomisery` T=4 O=4 串=25、
  `sq_medicalpractice` T=1 O=2 串=18——`2 + 2T + O` 远小于实际串数，**空槽数量既不等于 T 也不等于 E**，
  说明每个 task/objective/event 各自还有固定预留槽。待 `qst_3task.qst` 在编辑器里的显示结果反推。

> 官方条件 30 项 / 动作 28 项；二进制 26 条件槽（已全部落位）+ 27 可用动作槽（已全部落位）。
> 逐项对照与证据等级：CLI `CnvQstTool vocab`；探测文件由 `CnvQstTool probe …` 生成。
