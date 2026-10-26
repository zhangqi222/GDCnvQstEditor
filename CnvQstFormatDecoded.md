# Grim Dawn CNV/QST 格式权威文档（已解码）

> 本文整合 [R-Hidayatullah/gd-utility](https://github.com/R-Hidayatullah/gd-utility)（MIT）反编译
> Game.dll 加载器得出的格式结论、官方编辑器的反汇编/枚举证据，以及本项目 447 个本地样本
> （CNV 199 + QST 248）的验证结果，取代 `CnvQstFormatSpec.md` 中与之冲突的早期推断
> （勘误对照见 §6）。参考解析器原文存于 `参考资料/gd-utility/`（commit `1d468f5`）。
>
> 实现库：`GDCnvQstLib`；CLI：`GDCnvQstEditor-cli`；GUI：`GDCnvQstEditor`（二者共用 `GDCnvQstLib`）。
> 验证（2026-10-22）：**cnv示例 199/199、qst示例 248/248** —— 记录恰好铺满文件、
> 走序落在头部声明的本地化偏移、parse→write 逐字节一致（含全部 CNV v5 与多任务 QST）。
> TXT 管线另与旧工具 v0.9 做了逐字节对照：CNV 199/199 一致；QST 247/248 一致，
> 唯一差异是新增样本的尾部空槽（旧工具漏行，详见 §5）。

---

## 一、总体

| 格式 | 魔数 | 版本 | 加载器入口（Game.dll, base 0x180000000） |
|---|---|---|---|
| .cnv | `"CNV "` | ≥5（语料 6 为主、8 个 v5） | `Conversation::Build` @`0013E090`、`ConversationStep::Read` @`0013F770` |
| .qst | `"QST2"` | ≥7（语料全 9） | `Quest2::Load` @`00367D90`、`Quest2Task::Read` @`0036CD60` |

两种格式共用同一套 Scriptable 条件/动作系统，且本地化块相同（`Engine.dll` 的
`PortableLocalization::Read` @`001877D0`）。所有读取均为**小端、紧凑、无对齐**
（`*(T*)cursor; cursor += sizeof(T)`）。`Build`/`Load` 另对整块做 MD5（多人校验用，非布局）。

**字符串编码（两者共用）**：

```
u32 byteLength; byte[byteLength]
```

- 长度单位是**字节**，不是字符；末尾**没有 NUL 终止符**，也不做 4 字节对齐。
- 单串长度上限 1 MiB（`0x100000`）；语言块字符串数上限见 §2.4/§3.3。
  未特别注明时，这些“上限”是解析器拒绝畸形文件的安全阈值，不是文件中的字段；
  格式本身只受 `u32`/`i32` 范围约束。
- 解析器应保留原始字节用于回写，才能做到 parse→write 逐字节一致；显示时可按 UTF-8 解码。
  官方语料里的路径、token、文本均为 ASCII/UTF-8。编辑后写回通常按 UTF-8 重新编码，
  因此含非 UTF-8 字节的串只有在**不编辑**时才能保持原字节。

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
+0x0C  u32      对话 id            = FNV1(根节点文本)，见下
+0x10           根节点（内联记录，每文件恰 1 个 type=0）
```

**对话 id = 根文本（NPC 名）的 FNV1-32 哈希**（2026-09-28 从官方编辑器 `Conversation.Write`
与 `cmiCopyId_Click`（"Copy Id"菜单）逐指令读出，并在 3 个样本上逐值核对通过）：

- 算法：`h = 0x811C9DC5; foreach (byte b in Encoding.Unicode.GetBytes(text)) h = (h ^ b) * 16777619;`
  （编辑器 `Utility.FNV1::Hash(string, unicode)`；unicode=false 时用 ASCII 字节）
- 核对：`npc_refugee_02b.cnv` 根文本 "Marcus" → `0xFD22C016` = 文件头部实测值；
  "Alex" → `0x93EF8495`、`gdareab/npc_alicia.cnv` "Alicia" → `0x7659527A` 同样吻合。
- 该哈希**只用于对话 id**。条件/动作参数里的“任务引用”不是它的左移结果，而是目标任务
  记录中的持久化 `uid`（详见 §3.2）。
- CLI：`GDCnvQstEditor-cli hash <文本>` 可直接算出来（仅 FNV1，用于核对对话 id）。

### 2.2 节点（Step，深度前序递归）

```
i32     childCount            上限 100000，递归深度上限 512
u32     type
u32     flags                 ← 官方字段名就是 flags（旧文档称 unknown，见下）
i32     linkId                仅 type==7（值 = 目标节点的展平序号；无链接时编辑器写 -1）
string  voPath                仅 version>=6（配音路径；编辑器字段名 audioFile）
i32 n ; ConditionCollection(n)
i32 m ; ActionCollection(m)
childCount × step
```

节点没有额外尾字段：一个节点的范围 = 自身字段 + 整棵子树。

**flags 位（官方 `ConversationStep.Flag` 枚举 + 三处 IL 互证）**：

| 位 | 值 | 名字 | 编辑器树中的标记 / 菜单项 |
|---|---|---|---|
| 0 | 0x01 | OnceOnly | 文本前加 `*`；菜单 `cmiStepOnceOnly`（`flags & 1`）|
| 1 | 0x02 | AlwaysShow | 文本前加 `+`；菜单 `cmiStepAlwaysShow`（`flags & 2`）|
| 2 | 0x04 | Unimportant | 文本前加 `-`；菜单 `cmiStepUnimportant`（`flags & 4`）|

证据链（`EditorProbe` 反汇编 `G:\Grim Dawn\ConversationEditor.exe`）：
① `ConversationStep+Flag` 枚举常量直接写着 `None=0, OnceOnly=1, AlwaysShow=2, Unimportant=4`；
② 三个菜单处理器分别 `ToggleFlag(1/2/4)` 并用同值 `and` 判断勾选态；
③ `ConversationStep.Text()` 依次用 `flags & 2 → "+"`、`flags & 1 → "*"`、`flags & 4 → "-"` 拼树文本。

**但语料里出现的却是 0x8(83)、0x10(57)、0x20(43)、0x4(3)** ——即 bit3/bit4/bit5，编辑器并不暴露这几位
（读进同一个 u32、原样写回，不解释）。所以**编辑器只认低 3 位**，高位是游戏侧标志，语义未知，
按"保留原字节"处理。注意：`flags` 的三位与 step 类型无关（语料里只出现在 speech 节点上，但那是用法使然）。

**节点类型**（官方 `ConversationStep+Type` 枚举：Node=0, Speech=1, Continue=2, Accept=3, Decline=4,
Generic=5, End=6, Link=7, Random=8；语料分布：1×6169、6×5897、2×3783、7×3119、3×990、4×848、8×4、0×480）：

| type | 标签 | 含义 |
|---|---|---|
| 0 | — | 根（每文件恰一，枚举名 Node）|
| 1 | — | NPC 台词（speech），可用性检查挂在这类节点上 |
| 2 | `tagContinue` | 玩家选项 |
| 3 | `tagAccept` | 玩家选项 |
| 4 | `tagDecline` | 玩家选项 |
| 5 | — | Generic（枚举存在，语料无使用）|
| 6 | `tagEndConversation` | 结束对话 |
| 7 | （跟随链接） | 跳转，携带 linkId |
| 8 | — | **Random**：从可用子节点中随机取（旧文档称 selector，以官方枚举为准）|

**linkId = 目标节点的展平序号**：编辑器 `Conversation.EstablishIds()` 就是
`for (i = 0; i < steps.Count; i++) steps[i].SetId(i);`（`GetSteps()` 为深度前序），
写盘时 `WriteInt32(link.GetId())`，无链接写 `-1`。因此本项目把 linkId 当索引用是正确的。

加载器运行时会把"无子节点的 type 2"重映射为 6（仅运行时；解析器报告文件原貌）。

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
    i32     stringCount         CNV 上限 200000；QST 上限 100000（§3.3）
    × stringCount: string       编码见 §1
```

本地化块紧跟在步骤流之后；头部 +0x08 保存的偏移就是它的起点（写工具回填、加载器不读）。
每个语言块的 `stringCount` 必须与各自格式的槽位规则严格相等，语言之间逐槽对齐。

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
+0x0C  u32      uid                （旧文档称"任务 id"）
+0x10  u32      flags              见下（旧文档称 unknown 0..4）
+0x14  u32      taskCount（上限 4096）
+0x18           首个 task 记录
```

**头部 flags = `Common.Quest+Flag` 枚举**（2026-09-28 从官方编辑器内嵌 Common.dll 读出）：

| 值 | 名字 | 含义 |
|---|---|---|
| 0 | None | 无 |
| 1 | Primary | 主线任务 |
| 2 | Secondary | 支线任务 |
| 4 | Repeatable | 可重复 |

语料里 +0x10 的取值 {0,1,2,4} 与该枚举完全对应；`Quest.Read` 的 IL 也证实这里就是 `flags`
（顺序：version → (v≥6) 跳过 locOffset → **uid** → **flags** → taskCount）。

**Primary 与 Secondary 互斥**：官方 `Quest.SetPrimary/SetSecondary` 都是
`flags = (flags & ~3) | (on ? 1 : 0)` / `| (on ? 2 : 0)` —— 两个 setter 都先清掉 bit0+bit1。
所以"不勾 Main Quest"得到的是 `None`，不是 `Secondary`；Repeatable（bit2）独立于这两者。
编辑器里对应两个菜单项 `cmiPrimaryQuest`（Main Quest）/ `cmiSecondaryQuest`。

**task 的 isBlocker / dontPropagate 在 task 节点的右键菜单里**（`cmiBlockerTask` / `cmiDontPropagate`），
根节点菜单上没有——实测用户第一轮没找到就是这个原因。UI 上 dontPropagate 那一项的标题写字面看着
像 "Don't Repeatable"（官方命名如此，容易误解成 Repeatable），但处理器调用的是
`QuestNode_Task.SetDontPropagate`，与本题材的 v≥9 尾字节一一对应。

**实测闭环（用户标记后回读官方文件）**：在 QuestEditor 里把第 2 个 task 标 block、第 3 个标
Don't Repeatable 后存盘，本项目 dump 得到
`Task#2 isBlocker=1 dontPropagate=0`、`Task#3 isBlocker=0 dontPropagate=1` —— 两个尾字节的
位置与含义就此确认。

**uid** 由官方工具随机生成：`QuestEditor.FormMain.miQuestAddTask_Click` 就是
`quest.AddTask(App.random.UInt())`；新建 quest 同理。本项目 `QstFile.AddTask/AddObjective`
也按此给随机非 0 uid。注意：头部 +0x0C 的 `uid` 与 task 记录里的 `uid` 是两个独立字段，
条件/动作参数引用的是**后者**。

### 3.2 Task / Objective / Event

```
Task（Common.QuestNode_Task.Read 的 IL 逐条对应）
  if version >= 3:  u32 uid          本 task 的标识（他处引用它，见下）
  if version >= 3:  u32 flags        QuestNode_Task+Flag（目前只有 None=0）
  i32 n ; n × Event    "incentives"（编辑器 OnAccept）
  i32 n ; n × Objective
  i32 n ; n × Event    （编辑器 OnComplete）
  if version >= 8:  u8 isBlocker
  if version >= 9:  u8 dontPropagate

Objective（QuestNode_Objective.Read）        Event（QuestNode_Event.Read）
  if version >= 4: u32 uid                     u32 flags
  if version >= 4: u32 flags                   i32 n ; ConditionCollection(n)
  i32 n ; ConditionCollection                  i32 m ; ActionCollection(m)
  i32 m ; ActionCollection
```

即：旧文档里的 `task +0xA8/+0xAC` = **uid / flags**，`objective +0x10/+0x34` = **uid / flags**，
`event +0x2C` = **flags**，两个"版本门控尾字节" = **isBlocker（v≥8）/ dontPropagate（v≥9）**。
全部为 v≥3/4 门控——**这些字段只在较高版本才落盘**，读旧版本时要按门控跳过。

**"任务引用标识"就是被引用 task 的 uid**（编辑器里显示为 `?` 的那些 I 参数）：
- 编辑器靠 `Common.QuestUtil.GetTaskUidFromFile(path, taskName)` 打开目标 .qst 取该 task 的 uid
  来解析显示（`GetTaskObjectiveUidFromFile` 同理）；解析不出来才显示 `?`。
- 实测：jailor_01 的 `QuestTaskComplete "quests/gdareac/mq_makingadeal.qst", 0x876A2A00`
  与 `mq_makingadeal.qst` 里那个 task 的 uid **逐位相同**。
- 因此旧注"该 I = 文件引用 hash 左移 8 位"作废——它是 uid，看起来像 hash<<8 只是因为
  工具链生成的 uid 多为该形状。

**两个版本门控尾字节是多任务文件的生命线**：漏读即每个 task 静默错位 2 字节
（首个 task 看起来仍然正确——这正是本项目 2026-08 分析 mq_burrwitch 时
"enUS 出现在 0x308 而 0x38+DataSize=0x341" 之谜的根源）。

**运行时对象计数器不落盘**：`Load` 从 1 起给内部对象编号（每个 task 占 2 个、objective/event 各 1 个），
该编号只存在于内存，不写入文件。不要把它与 task/objective 记录里的持久化 `uid` 混淆——
后者真实落盘，并被 `BeginQuestTask` / `CompleteQuestTask` / `QuestTaskComplete` 等参数引用。
**目标列表只有一份**，运行时克隆成三个难度槽（`+0x08 + 24*(difficulty+1)`）。

**写回契约**：`locOffset` 回填为步骤流末尾；task/objective 的持久化 `uid`、flags 与未知字段原样写回；
v≥8/v≥9 尾字节仅在对应版本写出；语言块按 §3.3 的公式与顺序重建；运行时对象计数器不写。

**task / objective / event 均可增删（2026-09-28 起）**：增删的唯一约束就是 §3.3 的串表公式与槽位顺序
——按它重建串表即可，文本随所属实体移动（`QstFile.AddTask/RemoveTask/AddObjective/RemoveObjective/
AddEvent/RemoveEvent` 内部先做槽归属快照、再按新结构回填）。GUI 的 QST 表格已解锁 task 行增删，
且状态栏与保存守卫都加了"✓串表公式"检查（条数不符直接拒绝落盘——官方 QuestEditor 会抛异常）。

历史说明（保留）：早期因"2 之后与 task 的映射未定"，QST 表格曾禁止 task 行增删、且不含"文本"列。

### 3.3 本地化块

与 CNV 相同；串表条目 **0 = 任务组名、1 = 任务名**（`GetGroup`/`GetName`）。

**串表条数公式（2026-09-28 解出，2026-09-09 的 247 个 QST 样本零误差，并在官方 QuestEditor 中核实）**：

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
  按公式补到 7 条后正常打开，且标题/描述/目标文本全部落在预期节点上（探测文件见
  `probe_out/qst_1task.qst`，已在官方 QuestEditor 中打开核对）。
- 空槽位 = 长度前缀 0 的空串，游戏按位置回填，导出/导入必须原样保留空行。
  单语言串数上限 100000（CNV 为 200000）。
- **槽位顺序（`probe_out/qst_order.qst` 已确认）**：

  ```
  [组名, 任务名]
  再按 task 顺序，每个 task：
      [标题, 描述, OnAccept(incentives)…, Objective…, OnComplete(events)…]
  ```

  该探测文件为 1 task + 1 incentive + 2 objectives + 1 event，恰好 8 条；槽 4/5/6/7
  的文本分别为 `SLOT4`/`SLOT5`/`SLOT6`/`SLOT7`，与“incentive 在 objectives 之前、
  objectives 在 OnComplete events 之前”的顺序逐一对应。
- 增删 task/objective/event 后必须按此式重建串表 —— 这就是"多任务编辑"的全部约束所在，
  也是本项目 2026-08 卡住的"QST 边界"问题的最终答案。

---

## 四、Scriptable 条件/动作系统（CNV/QST 共用）

```
ConditionCollection(n)            ActionCollection(n)
  u32 logic（仅 n>0 时读）          × n:  u32 type; u8 bodyVersion; <body>
  × n: u32 type; u8 bodyVersion; u32 比较符; <body>
```

**两个易错的不对称**：动作无比较符字、无前导字；条件集合的前导 logic 字在集合自己的 Read 里，
调用方只在 count>0 时才调 Read——**空列表只占计数字本身**。

**logic 字**（语义未经引擎证实，本地观测+金样直接证据）：0=AND（jailor_01 多条条件均为 AND 列表）、
1=OR（mq_burrwitch 双条件处 `{HasToken, ServerHasToken}` OR 列表；2026-09-09 的 446 样本全量一致）。

### 4.1 条件的第 3 个字 = 比较符；第 2 个字节 = body 版本（2026-09-28 定案）

```
条件： u32 type; u8 bodyVersion; u32 comparison; <body>
动作： u32 type; u8 bodyVersion;               <body>   ← 动作没有 comparison
```

官方字段名（`Common.ScriptableCondition` 的字段表与其 `Read` 的 IL）：`type`（枚举 Type）、
`version`（byte）、`comparison`（枚举 Comparison）。`ScriptableCondition.Read` 全文只有两句：
`version = ReadUInt8(); comparison = ReadInt32();`；`ScriptableAction.Read` 只有 `version = ReadUInt8();`。

**比较符 = comparison，取值来自官方枚举**（`Common.ScriptableCondition+Comparison`）：

| 值 | 名字 | 符号 | | 值 | 名字 | 符号 |
|---|---|---|---|---|---|---|
| 0 | Less | `<` | | 3 | GreaterEqual | `>=` |
| 1 | LessEqual | `<=` | | 4 | Greater | `>` |
| 2 | Equal | `=` | | 5 | NotEqual | `!=` |

两路独立验证：
- **实测**：`probe uid` 造出"同条件、同 bodyVersion、只改此字"的 6 个节点，
  官方编辑器显示 `<` `<=` `=` `>=` `>` `!=`（HasFaction 与 IsDifficulty 两类型结果一致，
  见 `probe_out/probe_uid_*.cnv`，每组已在官方编辑器中打开核对）；
- **静态**：从编辑器内嵌 `Common.dll` 直接读到该枚举的常量值。
- 语料印证：无比较符概念的条件该字恒为 2（`=`）；HasItem/HasMoney/HasFaction 多为 3（`>=`）；
  IsDifficulty 为 2（`=`）——正是 mq_burrwitch 按难度分档给经验所需；jailor_01 为 3（`>=`）。
- 动作记录没有这个字；**QST 条件有，且与 CNV 一样落盘**。实测
  `qst示例/bounties/bl/bq_bl01.qst` 的多个条件分别显示 `[>=]` 与 `[=]`。
- 不要把这里落盘的 `comparison` 与 §3.2 的“运行时对象计数器不落盘”混为一谈：
  后者是内存对象编号，二者无关。

**bodyVersion（旧文档称 subType）= 条件 body 的版本号**：`i`/`b` 两类字段只在 version==1 时存在
（gd-utility 的"小写字母仅当 subType==1"由此得到解释）。语料里 HasFaction / HasItem /
HasQuestObjective / CheckRandomValue / LuaScript 恒为 1，其余多为 0。
**取错版本会让编辑器按另一套布局去读，直接错位**——首轮 `probe cmp`（把 HasFaction 的这个字节
扫 0..5）就是这么把编辑器读崩的（OutOfMemory）。

`ScriptRecord.Uid` 字段名沿用（含义为 comparison）；GUI 两个输入框已改标"比较符（条件）"与"body 版本"。

### 4.2 body 规格

`S`=字符串、`I`=u32、`B`=u8；小写 `i`/`b` = 仅当 bodyVersion==1 时存在；
`P` = `i32 n + n×字符串 + u32 尾字`；
`L`（Lua 形态）= `串(调用名) + i32 n + n×{u32,u8,u32,串} +（条件另有固定尾部 {u32,u32,u8,u32,串}）`。

### 4.3 条件（30 类，官方编辑器逐类型辨认完毕）

> 2026-09-28：`probe_out/probe_cond.cnv`（30 个节点 = 30 个 type，覆盖 0–29）在官方
> ConversationEditor 中逐类型显示出官方名；下表"编辑器实显"一列即读到的结果。
> 各类型的字段级详情见 `反馈截图/条件截图/`（§4.5）。

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
| 25 | `I B` | **HasMastery** | `HasMastery SkillClass01, TRUE`；`MasteryName` 是内置枚举下拉，落盘为 u32 下标，后随 `HasMastery` 布尔 |
| 26 | `I` | **IsChallenge** | `IsChallenge Normal Veteran, >=`；下拉 Normal Veteran / Epic Veteran / Legendary Veteran（按枚举顺序 0/1/2）+ Comparator |
| 27 | `S B` | **HasBuff** | `HasBuff <BuffName>, TRUE`；官方字段名 `BuffName`(string) + `HasBuff`(bool) |
| 28 | `I I I` | **HasKilledMonsterClass** | `MonsterClass1, MonsterClass2, KillCount, Comparator`；前两值枚举 Common/Champion/Hero/Boss/Quest/SuperBoss |
| 29 | `I I I` | **HasKilledFaction** | `Faction1, Faction2, KillCount, Comparator`；值枚举 None/Alone/GenericMonster/Player/Survivors/Aetherials/Cthonians/Outlaws/Beasts/User0…（列表可继续滚动） |

> **条件共 30 种（0–29）**，后 4 种是 gd-utility 基于旧版 Game.dll 的表所没有的——2026-09-28 从官方编辑器
> 内嵌 `Common.dll` 的 `Common.ScriptableCondition+Type` 枚举直接读到（`IsChallenge=26, HasBuff=27,
> HasKilledMonsterClass=28, HasKilledFaction=29`）。这 4 种的字段布局取自各自类的字段表与 `Read` 的 IL
> （例：`HasKilledFaction.Read` = base.Read + ReadInt32 faction1 + ReadInt32 faction2 + ReadUInt32 count）。
> 2026-09-09 的 446 样本里没有后 4 种的实例，故以 `probe_out/probe_cond.cnv`（现覆盖 0–29）交官方编辑器复核。
> 至此官方条件下拉 30 项与二进制 type 0–29 **一一对应，无遗留项**。

### 4.4 动作（27 个可用槽，官方编辑器逐类型辨认完毕）

> 证据：`probe_out/probe_act.cnv`（29 槽，0/16 不生成节点）在官方 ConversationEditor 中逐类型显示；
> 字段级详情见 `反馈截图/动作截图/`（§4.5）。**动作共 29 槽（0–28）**，官方枚举把 0 命名为
> `DEPRECATED_BeginQuest`、16 命名为 `OpenMerchantWindow`——工厂对这两项返回 null，
> 这正是编辑器里点 "OperMerchantWindow" 就报错的原因。动作枚举取自
> `Common.ScriptableAction+Type`，与本项目命名表逐项一致（含 9=GiveToken / 10=RemoveToken）。

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
| 19 | `S` | ScriptEvent | `ScriptEvent T19`（2026-10-21 详情弹窗截图确认，原“推断”标记可去除）|
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

### 4.5 官方编辑器"详情弹窗"的逐类型字段名（2026-10-21 逐类型截图核对）

> 官方编辑器中双击条件/动作条目 → 添加/编辑时弹出的**类型详情窗**，逐类型截图共 58 张，
> 存于 `反馈截图/条件截图/`、`反馈截图/动作截图/`。下表字段名为官方 UI 原文（按显示顺序），
> 作为本文的权威字段名；本项目参数名表大体已对齐，但仍有个别差异（type 16 显示名、
> type 19 首字段名等），见 §9。

**弹窗通用规则**：

- 字段以树形列表呈现：首行为主字段（粗体，前缀 `−`/`+` 为树形展开标记），其余为子字段；
  文件类串字段带 `…` 浏览按钮。
- 布尔字段渲染为 TRUE/FALSE 下拉；数值字段直接输入；枚举字段渲染为下拉（选项见备注列）。
- **含数值比较语义的条件，弹窗尾部固定多一行 `Comparator`**（下拉 `<` `<=` `=` `>=` `>` `!=`）——
  它就是记录头第 3 个字 comparison 的可视化（§4.1）。任务步骤号类（QuestTaskComplete /
  OnQuestTask / HasQuestObjective）与布尔类条件不显示此行（此时 comparison 恒为 `=`，语料印证）；
  **动作一律没有 Comparator**。
- **P 列表字段**显示为 `字段名 [N]`（N=当前项数），列表行只显序号 1,2,…，行右侧有
  `…`（浏览）与 `−`（移除）按钮。
- **Lua 条件/动作**使用专用窗（标题 Edit Condition / Edit Action）：`Function` 下拉 +
  `Args` 表格（Defaults / Nil All / Move Up / Move Down 按钮）；条件版底部另有
  `Evaluate` 区 = 返回类型下拉（Bool / Number / String / Nil）+ 比较符下拉 + 期望值，
  对应 L 形态条件的固定尾部（§4.2）。

**条件（30 类；`反馈截图/条件截图/`）**：

| type | 类型名 | 官方弹窗字段（按显示顺序） | 备注 |
|---|---|---|---|
| 0 | OnQuest | QuestFile, OnQuest | |
| 1 | QuestTaskComplete | QuestFile, Task, TaskComplete | Task 不可编辑 |
| 2 | HasExperience | Experience, Comparator | |
| 3 | IsLevel | IsLevel, Comparator | |
| 4 | IsDifficulty | IsDifficulty, Comparator | 下拉 Normal/Epic/Legendary |
| 5 | IsHardcore | IsHardcore | TRUE/FALSE |
| 6 | HasFaction | FactionName, Value, Comparator | |
| 7 | HasToken | TokenName, HasToken | |
| 8 | HasMoney | MoneyAmount, Comparator | |
| 9 | HasItem | ItemRecord, ItemCount, CompleteOnly, Comparator | |
| 10 | HasKilled | MonsterRecords [N], KillCount, Comparator | 列表行带 …/− 按钮 |
| 11 | HasQuestObjective | **Quest**, Task, Objective, Satisfied | 首字段名 Quest（旧表记 QuestFile，**修正**）；Task/Objective 不可编辑 |
| 12 | LuaScript | Function, Args；条件版另有 Evaluate 区 | 专用窗 |
| 13 | KnowsPlayer | KnowsPlayer | TRUE/FALSE |
| 14 | OnQuestTask | QuestFile, Task, OnTask | Task 不可编辑 |
| 15 | UsedQuestItem | ItemRecord, NumUsed, Comparator | |
| 16 | HasKilledProxy | ProxyRecords [N], KillCount, Comparator | |
| 17 | WaitForCompletion | （无字段行） | |
| 18 | ServerHasToken | TokenName, HasToken | |
| 19 | QuestBlocked | QuestBlocked, QuestBlocked | 两个字段同名：首字段为文件串（带 …），次字段为 TRUE/FALSE |
| 20 | AnyoneHasToken | TokenName, HasToken | |
| 21 | CheckRandomValue | Name, Value, Comparator | 第二字段名 Value（旧表记 RandomValue，**修正**） |
| 22 | OwnsDLC | DLC_ID, Comparator | |
| 23 | HasTribute | TributeAmount, Comparator | |
| 24 | EarnedDevotion | DevotionAmount, Comparator | |
| 25 | HasMastery | MasteryName, HasMastery | 下拉显示 SkillClass01…30；落盘为 u32 下标 |
| 26 | IsChallenge | IsChallenge, Comparator | 下拉 Normal Veteran/Epic Veteran/Legendary Veteran |
| 27 | HasBuff | BuffName, HasBuff | BuffName 带 … 按钮 |
| 28 | HasKilledMonsterClass | **MonsterClass1**, **MonsterClass2**, KillCount, Comparator | 前两字段名 MonsterClass1/2（旧表记 Faction1/2，**修正**）；下拉 Common/Champion/Hero/Boss/Quest/SuperBoss |
| 29 | HasKilledFaction | Faction1, Faction2, KillCount, Comparator | 下拉 None/Alone/GenericMonster/Player/Survivors/Aetherials/Cthonians/Outlaws/Beasts/User0…（截图可见到 User6，列表可继续滚动） |

**动作（27 个可用槽；`反馈截图/动作截图/`）**：

| type | 类型名 | 官方弹窗字段（按显示顺序） | 备注 |
|---|---|---|---|
| 1 | BeginQuestTask | QuestFile, Task | |
| 2 | CompleteQuest | QuestFile | |
| 3 | CompleteQuestTask | QuestFile, Task | Task 不可编辑 |
| 4 | GiveMoney | MoneyAmount | |
| 5 | GiveFaction | FactionType, Amount | |
| 6 | GiveItem | ItemRecord, NumItems | |
| 7 | GiveLevel | NumLevels | |
| 8 | GiveExperience | Experience | |
| 9 | GiveToken | TokenName | |
| 10 | RemoveToken | TokenName | |
| 11 | LuaScript | Function, Args | 专用窗，无 Evaluate 区 |
| 12 | DebugPrint | Text | |
| 13 | PlaySound | SoundRecord | |
| 14 | PlayAnimation | AnimationType | |
| 15 | GiveSkillPoint | NumSkillpoints | |
| 17 | Notification | Tag | |
| 18 | GiveAttribPoint | NumAttributePoints | 弹窗中标签被截断为 NumAttribut… |
| 19 | ScriptEvent | ScriptEvent | 本次截图确认（此前标记"推断"可去除疑义）|
| 20 | GiveRandomItem | ItemRecord, NumItems | |
| 21 | GenerateRandomValue | Name, Min, Max | |
| 22 | CastSkill | SkillRecord | |
| 23 | UnlockFaction | FactionType | |
| 24 | SetFaction | FactionType, Value | 与 5 GiveFaction 的区分再次确认 |
| 25 | GiveDevotion | NumDevotionPoints | 弹窗中标签被截断为 NumDevotion… |
| 26 | GiveTribute | TributeAmount | |
| 27 | UnlockTutorial | TutorialPage | |
| 28 | PlayVideo | **Video path**, AllowSkip | 官方字段名 "Video path"（旧表记 VideoPath，**修正**）|

### 4.6 语料类型分布（2026-09-09 快照：446 个样本，条件+动作混合计数）

type4×2084、1×907、9×853、8×835、5×792、11×532、14×509、7×484、10×415、3×381、6×343、
21×297、17×266、2×258、19×146、22×130、20×115、18×108、0×24、12×23、15×2、25×1。
CNV 侧 21,290 节点 / 11,326 条件+动作来自 gd 语料（480 文件）。2026-09-28 之后新增的
QST 样本（含 `20260928newtask.qst`）未计入本表；类型集合与上表一致。

---

## 五、官方本地化 TXT（text_zh.arc 流程）

- 一行 = 本地化字符串表一条；CNV 首行 = 说话人名；空槽位 → 空行（位置不可增删——游戏按行号回填）。
- UTF-8 无 BOM、CRLF、文件末尾再补一个 CRLF；行中保留 `{^n}`；行尾 `{^n}`/`{^n` 剥除；
  串内换行归一为 `{^n}`；行首尾 Trim。
- 本项目 `GDCnvQstEditor-cli txt` 为**按格式精准提取**，输出与 GrimDawnCnvQstToTXT v0.9
  （"首个 enUS+12 截断 + 逐 null 强插换行"的强读算法，移植版见 `legacytxt` 命令）对照：
  CNV **199/199 一致**；QST **247/248 一致**。唯一差异是新增的 `20260928newtask.qst`：
  旧工具漏掉尾部空槽并写成 `<无>`，新工具保留空行——这是旧算法的缺陷，不是回归。
  新实现同时修复了旧算法的理论缺陷（无结构校验、依赖文件尾含 enUS、按 char 切割的多字节风险）。
- TXT 回填（原 `import-txt`）已于 2026-10-23 移除：翻译请用 JSON 导入（`json`/`make`），或按官方位置回填方式交 arzedit-GUI 打包。
  留档：若原串不触发 `CleanLine` 的归一化（CR/LF、控制字符、连续空格、首尾空白、行尾 `{^n}`），
  原文写回字节一致；触发归一化的串会改变空白/控制字符，因此 TXT 不是任意二进制串的无损载体。
- 完整翻译管线：`txt` 导出 → 翻译 → `json`/`make`（JSON 路径）或 arzedit-GUI 打包 `text_zh.arc`（官方位置回填）。

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
| 条件类型 ID ≠ 编辑器下拉序号 | 正确；现已锚定 30 个条件名（type 0–29）与 27 个可用动作名（见 §4.3/§4.4）|
| 条件/动作参数里的任务引用 = 文件引用 hash 左移 8 位 | 作废；它是目标 task 记录中的持久化 uid（§3.2）|

## 七、仍然未知（按"保留原字节"策略贯穿解析与写回）

**已解（2026-09-28 / 2026-10-22）**：比较符编码（§4.1，QST 同样落盘）、flags 低 3 位（§2.2）、
节点类型含 Random（§2.2）、对话 id = FNV1(根文本)（§2.1）、条件共 30 种（§4.3）、
动作 0/16 的真名（§4.4）、动作 9/10 的方向（§4.4）、QST 串表公式与槽位顺序（§3.3）、
task/objective 的持久化 uid/flags 与版本门控尾字节（§3.2）、任务引用 = 目标 task 的 uid（§3.2）。

**仍未解 / 未完全证实**

| 未知项 | 现状 / 下一步 |
|---|---|
| CNV `flags` 的高 3 位（语料 0x8×83、0x10×57、0x20×43）| 官方编辑器读入同一 u32 却只用低 3 位；这三位属游戏侧语义，未定位 |
| `bodyVersion` 除 0/1 外是否还有别的版本 | 语料只见 0 与 1；错版本会让编辑器错位，故未盲扫 |
| QST task/objective/event 记录里的 `flags` 位定义 | 语料均为 0；只按原值回写，其他位的语义未定位 |
| QST 头部 +0x0C `uid` 的用途与生成方式 | 原值保留；未从编辑器 IL 核实其生成算法 |
| `logic` 字（0/1）的引擎语义 | 0=AND、1=OR 已由语料与金样强证，但尚未在引擎 IL 中定位；写入时原样保留 |

---

## 八、解格式的三种手段（本项目的工作方法，供后续继续挖）

按"成本由低到高"排列，前两步已把绝大多数未知项清空：

### 8.1 语料统计（`census`）——先问"语料怎么说"

`GDCnvQstEditor-cli census <目录>` 输出条件/动作的类型分布、bodyVersion/comparison 取值分布、body 字面值、
同列表共现、logic 字、step flags 分布、**QST 槽位配平**等。多数"字段语义"问题在语料层面
就能收敛（例：没有比较语义的条件 comparison 恒为 2；QST 串表条数残差 `0×247`）。

### 8.2 探测文件 + 官方编辑器辨认（`dev probe` / `convert`）——让工具自己报名字

`GDCnvQstEditor-cli dev probe <cond|act|uid|sub|flags|qst|all>` 造最小合法文件，交给官方编辑器打开：

- `dev probe cond` / `dev probe act`：一个文件 N 个节点，每个节点挂一个类型 → 编辑器直接显示官方名
  （**30 个条件类型 + 27 个可用动作槽一次性辨认完毕**，包括本地语料里样本为 0 的）；
- `dev probe uid`：同条件、只改比较符字 → 读出 0..5 的符号表；
- `dev probe flags` / `dev probe sub`：验证 flags 位与 body 版本；
- `dev probe qst` / `qst_order`：已确认串表公式与槽位顺序（§3.3）。

配套 `convert <spec.json>` 可按手写 spec 生成任意 .cnv/.qst（条件动作用官方名书写），
所有生成物先过"铺满 / locOffset / round-trip"三检查。探测文件默认写到 `probe_out/`
（该目录在 `.gitignore` 中，分享本文的读者需用 `dev probe` 自行重新生成）。

### 8.3 反汇编官方编辑器（`EditorProbe`）——直接读实现

官方工具是 .NET 程序集，`EditorProbe` 用 `System.Reflection.Metadata` **只读**其元数据
（不加载、不执行），可直接得到枚举常量、字段表与方法 IL：

```bash
P=EditorProbe/bin/Debug/net8.0/EditorProbe.dll
dotnet $P types  "G:/Grim Dawn/ConversationEditor.exe" Scriptable     # 类型清单
dotnet $P fields "_editor_extract/Common.dll" "Common.ScriptableCondition+Type"   # 枚举常量
dotnet $P il     "G:/Grim Dawn/ConversationEditor.exe" "ConversationEditor.ConversationStep" "Read"
dotnet $P xref   "G:/Grim Dawn/ConversationEditor.exe" ToggleFlag     # 谁调用了它
dotnet $P res    "G:/Grim Dawn/ConversationEditor.exe"                # 列出内嵌资源
dotnet $P ex     "…" "costura.common.dll.zip" _editor_extract/Common.dll   # 解出依赖程序集
```

要点：ConversationEditor.exe / QuestEditor.exe 本身只有 27 个类型（薄壳），真正的实现
（`Common.ScriptableCondition_*` / `Common.ScriptableAction_*`）在 Costura 内嵌的
`costura.common.dll.zip` 里，是 Deflate 压缩的托管程序集，先 `ex` 解出来再查。

已用此法拿到的结论：Condition/Comparison/Action/Flag/StepType 五个枚举的**全部常量值**、
`Conversation.Write`/`ConversationStep.Write`/`Read` 的**完整写读顺序**、
对话 id 的 FNV1 算法与"Copy Id"菜单、linkId = 展平序号（`EstablishIds`）等。

---

## 九、实现注记：与本文的差异

本文以二进制格式与官方编辑器行为为准。2026-10-22 已完成以下修复：条件 16 命名（HasKilledProxy）、
动作 19 证据等级、`Spec` 条件范围 0–29、`Spec.BuildQst` 的文本槽序与持久化 uid、
`Fnv1`/CLI `hash` 的“任务引用 = hash<<8”旧说法、`EditorVocab` 的 `Hasbuff` 拼写。

当前只剩一处**有意保留**的差异：

| 位置 | 当前实现 | 说明 |
|---|---|---|
| `Scriptable.CondParams[19]` | 首字段名 `QuestFile`（官方弹窗首字段也叫 `QuestBlocked`，与第二字段同名，§4.5） | 保留 `QuestFile` 是为了让 JSON/具名参数能区分两个同名字段；位置写法与官方字段顺序完全一致，不影响二进制。 |
