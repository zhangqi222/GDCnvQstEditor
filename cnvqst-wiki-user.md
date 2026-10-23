# CnvQstEditor 用户功能总览

> 面向界面重构的功能清单：只列**用户可感知**的功能，不含实现细节。
> 每项标注：做什么、当前入口、输入/输出与约束——重新规划界面时以此为准。

---

## 0. 产品定位与两种形态

《恐怖黎明》.cnv（对话）/ .qst（任务）文件的**第三方查看、编辑、新建、导入导出与批量处理**工具。

| 形态 | 入口 | 适用 |
|---|---|---|
| **图形界面** | 无参数启动 CnvQstEditor.exe | 日常编辑 |
| **命令行** | CnvQstEditor <命令> … | 脚本化、批量、自动化流水线 |

两个格式：**CNV**（对话树，版本 v6）与 **QST2**（任务，版本 v9）。

---

## 1. 核心概念（规划界面所需的心智模型）

- **文档**：一个 .cnv 或 .qst 文件。可同时打开多个（多页签）。
- **只读保护**：解析失败或完整性检查不过的文件**只能看、不能保存**——界面需明显区分。
- **三检查守卫**：任何写盘（保存/导入/生成）前自动重解析并校验“记录铺满 / 本地化偏移 / 字节 round-trip”（QST 另含串表公式）；不过就拒绝落盘，并自动生成 .bak 备份。
- **CNV 结构**：对话 = 节点树（根 → 台词/选项/跳转/随机…共 9 种类型），每节点带：类型、显示开关（仅一次/总是显示/不重要）、配音文件、跳转目标、**条件列表**、**动作列表**。
- **QST 结构**：任务 → 1..n 个 task，每个 task 带：标题/描述、接受事件、目标、完成事件；事件与目标挂**条件/动作列表**；任务头部带 uid 与 主线/支线/可重复 标志。
- **条件/动作记录**：类型（30 种条件 / 29 种动作）+ 若干字段（数值/字符串/布尔/路径列表/Lua 调用）。命名分三档：**官方已证实 / 推断 / 未命名**——界面用颜色区分（正文色 / 橙 / 灰）。
- **本地化文本**：文件内置一张字符串表（说话人名、每节点台词、任务标题/描述…），**行数与结构绑定，不可增删行**；文本里 {^n} 是换行记号。
- **多语言界面**：默认中文、可即时切换英文；lang/*.json 语言包（英文 key → 译文）放进去即生效。

---

## 2. GUI 功能清单（按用户工作流分组）

### 2.1 打开与文档管理

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 1 | 打开文件 | 从文件对话框打开 .cnv / .qst | 同一文件不重复开页签，选中已有的 |
| 2 | 拖放打开 | 文件拖进窗口即打开 | 目录拖入 = 打开批量面板 |
| 3 | 多页签 | 每文档一个页签 | 页签标题含：文件名 + * 脏标记 + 只读标注 |
| 4 | 关闭页签 | 单独关闭 | 脏文档询问“保存/不保存/取消” |
| 5 | 重新加载 | 放弃修改从磁盘重读 | 脏文档需确认 |
| 6 | 退出保护 | 关窗时逐页签检查脏标记 | 询问“仍然退出？” |

### 2.2 查看结构

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 7 | 结构树（左栏） | CNV：按层级展示对话节点（类型名+文本摘要）；QST：任务 → 接受事件/目标/完成事件 分组 | 点击树节点 → 右侧表格定位对应行 |
| 8 | 本地化字符串表入口 | 树的末项 + 工具条按钮 + 视图菜单 | 切换到字符串表视图 |
| 9 | 文档摘要（状态栏） | 格式版本 · 节点/task 数 · 条件/动作数 · 字节数 · 各项校验状态（✓/✗） | 常驻可见 |
| 10 | 页内状态行 | 每个文档视图底部：未保存标记 + 摘要 + 只读原因 | — |

### 2.3 编辑（CNV 对话）

表格为 **4 列平铺**：节点 | 属性 | 条件 | 动作。

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 11 | 编辑节点台词 | 选中行后在底部编辑器直接编辑 | 自动 {^n} 转换；行数固定 |
| 12 | 紧凑/平铺模式 | 紧凑=长文本封顶 3 行；平铺=完整铺开 | 选中行始终完整显示 |
| 13 | 添加子节点 | 选行后加新对话节点 | 弹类型选择（9 种） |
| 14 | 删除子树 | 删除节点及其子树 | 根节点不可删 |
| 15 | 节点上移/下移 | 兄弟间排序 | — |
| 16 | 节点属性编辑 | 类型 / 显示开关（仅一次、总是显示、不重要）/ 配音文件 / 跳转目标 | 双击节点或属性列；跳转仅在“跳转”类型可用；配音在 v6 起可用 |
| 17 | 条件列表编辑 | 弹模态窗：已有条件（删除/复制/上移下移）+ 添加新条件 + 逐字段编辑 | 顶部横幅提示语义；**关系逻辑 AND/OR** 可选；比较符下拉（< <= = >= > !=） |
| 18 | 动作列表编辑 | 同上（无逻辑字） | 类型切换时字段自动保位带值 |
| 19 | 字段编辑 | 数值（支持 0x 十六进制）/ 字符串 / 布尔 / 路径列表 / Lua 调用（含参数表与尾部） | 未知语义字段有警示文案 |

### 2.4 编辑（QST 任务）

表格为 **4 列平铺**：节点 | 字段 | 条件 | 动作（task/objective/event 平铺展示）。

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 20 | 添加任务 / 目标 / 完成事件 / 接受事件 | 结构增删走库层，**自动按串表公式 2+2T+O+I+E 重建本地化表**，文本随实体移动 | — |
| 21 | 删除行 | 删 task / objective / event | 同上自动维护串表 |
| 22 | 任务属性 | uid + 主线 Primary / 支线 Secondary（互斥）/ 可重复 Repeatable | 模态窗 |
| 23 | 字段编辑 | task：uid（引用标识）/flags/isBlocker/dontPropagate；objective：uid/flags；event：flags | 位含义来自官方 Common.dll |
| 24 | 条件/动作编辑 | 与 CNV 相同的模态编辑器 | — |

### 2.5 查看本地化字符串表（只读）

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 25 | 本地化视图 | 行号处显示"槽号 + 对应节点角色"（如 `1 说话人名（根节点）`），正文为该槽的实际文本 | **只读**；行数固定，不可增删 |
| 26 | 文本编辑入口 | 只能在"可视化"视图的底部编辑器或表格文本列改 | 收敛编辑渠道，防止行数错位 |

### 2.5b 槽位视图（只读）

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 26a | 槽位清单 | 按**结构规则**（不看文件里已有几行）列出每行应填什么：`行 槽位名 建议` | 顶部汇总：总槽位 N 行 · 建议填写 M 行 · 换行请用 `{^n}` |
| 26b | 填写建议 | 建议填写（说话人名/台词/结束语/主要选项、任务组名/任务名/标题/目标）· 可为空（任务描述、跳转、随机、通用）· 应为空（QST OnAccept / OnComplete 槽） | 仅提醒，不阻止保存与转换 |

### 2.6 新建

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 27 | 新建对话 | 向导：说话人名（必填）/ 首句 / 结束语 → 生成 CNV v6 骨架 | 新页签打开，未落盘 |
| 28 | 新建任务 | 向导：任务组 / 任务名（必填）/ Task 标题 / Task 描述 → 生成 QST2 v9 单 task 骨架 | 之后可在表格里增删 |

### 2.7 保存

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 29 | 保存 / 另存为 | 写盘前自动三检查；不过拒绝并提示原因 | 已有文件先备份为 .bak |
| 30 | 只读文档禁止保存 | 只读原因在状态栏可见 | — |

### 2.8 导入 / 导出（单文件）

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 31 | 导出 TXT | 官方本地化 TXT 格式 | 供翻译/回填 |
| 32 | 导出 JSON | 结构化 JSON（含全部字段） | 无损往返 |
| 33 | 导出 dump | 编辑器风格树形文本（只读查看用） | — |
| 34 | TXT 回填 | 把翻译好的 TXT 逐行写回当前文档 | **先弹预览**：逐行对照、差异标红、行数不符禁止应用 |
| 35 | JSON 导入 | JSON 模型 → 新页签 | 导入后校验，防改坏 |

### 2.9 批量面板（独立窗口）

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 36 | 扫描目录 | 列出全部 .cnv/.qst：类型/版本/节点数/txt 行数/校验结果/失败原因 | 可含子目录 |
| 37 | 勾选子集 | 复选框逐个勾选 | 状态栏实时计数 |
| 38 | 批量导出 TXT / JSON | 可选平铺（不镜像源目录结构） | — |
| 39 | 批量校验 | 通过/失败统计，失败项列表 | — |
| 40 | 批量 TXT 回填 | 源目录 × 译文目录按同名配对，输出到独立目录（不覆盖源文件） | 成功/跳过统计 |
| 41 | 导出报告 CSV | 扫描结果落盘（带 BOM） | — |

### 2.10 工具 / 校验

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 42 | 校验当前文件 | 手动触发三检查并弹结果 | QST 含串表公式 |
| 43 | anchors 工作台 | 当前文件的类型 ID 锚定报告（只读文本窗，可复制） | 需已保存的文件 |
| 44 | 崩溃保护 | 未捕获异常落日志 + 弹框，不静默闪退 | — |

### 2.11 界面设置（横切）

| # | 功能 | 说明 | 约束/提示 |
|---|---|---|---|
| 45 | 界面语言切换 | 菜单内即时切换、全界面重建刷新 | 选择持久化到 %APPDATA% |
| 46 | 语言包扩展 | lang/*.json 放入即生效，语言菜单即时切换 | zh-CN 中文字典；en 恒等映射为 key 清单 |

### 2.12 文档视图（五视图，全局生效）

工具条右侧 + "视图"菜单（Ctrl+1…Ctrl+5）切换当前文档的显示方式；**一次切换对所有已开页签生效**，
并写入配置 `%USERPROFILE%\.CnvQstEditor\CnvQstEditor.json` 的 `ViewMode`，下次启动沿用。

| 视图 | 快捷键 | 可编辑 | 说明 |
|---|---|---|---|
| 可视化 | Ctrl+1 | ✅ | 结构表格 + 底部节点文本编辑器（唯一可改内容的视图） |
| JSON | Ctrl+2 | ❌ 只读 | 当前文档的完整 JSON 模型（带行号），仅供对照查看 |
| 本地化 | Ctrl+3 | ❌ 只读 | 逐行文本，行号处标注对应节点角色（含 `{^n}` 原样显示） |
| 槽位 | Ctrl+4 | ❌ 只读 | 按结构规则生成的填写清单（占位文字 + 建议/可为空/应为空） |
| DUMP | Ctrl+5 | ❌ 只读 | 编辑器风格树形文本（带行号），排查格式细节用 |

四个非可视化视图占满整个编辑区（不再保留底部编辑器空位）。多行文本统一使用 AvaloniaEdit
（`Avalonia.AvaloniaEdit` 11.4.1）：自带行号、查找替换（Ctrl+F / Ctrl+H，只读视图不响应编辑）、
长行横向滚动与等宽字体渲染。

---

## 3. CLI 命令清单（同一 EXE）

### 面向玩家 / 任务作者

| 命令 | 功能 |
|---|---|
| view <file.cnv\|qst> | 编辑器风格树形文本（给人读；旧名 dump 仍可用） |
| convert <file> [-o 输出] | JSON ↔ QST/CNV 互转；按输入扩展名判断方向，省略 `-o` 输出到同目录；转换成功后打印**槽位明细**（总行数 / 建议填写 / 可为空 / 应为空，逐行 `行 槽位名 建议`） |
| txt <file\|dir> [-o dir] [--flat] | 官方本地化 TXT 导出（支持目录批量） |
| verify <file\|dir> | 解析 + tiling + locOffset + round-trip + 语言行数对齐校验；单文件通过时附带槽位明细，PASS 行显示"建议填写 N 行" |
| sample cnv\|qst [-o] | 输出手写 JSON（spec）模板 |
| help [命令] | 命令帮助；`help convert` 查看转换说明 |

JSON 说明：文件扩展名统一 `.json`，内容允许 `//`、`/* */` 注释与尾逗号。
`json`、`make` 是 `convert` 的旧别名；`import-json` 与 `new` 已删除。
TXT 本地化回填暂缓，仅保留在 `dev import-txt`。

### 面向逆向 / 格式研究者

| 命令 | 功能 |
|---|---|
| legacytxt <file> | v0.9 旧算法基线导出（比对用） |
| txt-diff <dir> | 新旧 TXT 导出全量比对 |
| anchors <file> | 条件/动作类型 ID 锚定报告 |
| census <dir> | 全样本类型普查（命名证据表） |
| vocab | 官方编辑器选项 ↔ 类型 ID 对照表 |
| probe cond\|act\|uid\|sub\|suball\|flags\|qst\|all | 批量生成探测文件（交官方编辑器辨认语义） |
| selftest <dir> | 结构编辑自检（增/删/移后 round-trip 三检） |

---

## 4. 功能 ↔ 现有界面入口对照（供重新规划时盘点）

| 工作流 | 当前位置 | 现状问题线索 |
|---|---|---|
| 打开/保存/新建/导入导出 | 顶部菜单 + 双份工具栏按钮 | **菜单与工具栏重复度高**，工具栏 12 钮全文字 |
| 结构浏览 | 左树 + 右表格双栏 | 树与表格定位联动，但结构表/本地化表切换藏得较深 |
| 编辑条件/动作 | 双击弹模态窗 | 可考虑改为非模态停靠面板；未知语义警示文案较长 |
| 本地化字符串表 | 树末项/工具条/视图菜单三入口 | 入口过多 |
| 批量处理 | 独立窗口（Ctrl+B / 拖目录） | 与主窗口交互割裂 |
| 语言切换 | 菜单“语言” | 切换即全界面重建 |
| 校验 | 工具菜单 + 工具栏按钮 + 保存守卫 | 校验结果只在弹窗，缺少常驻汇总 |

---

*生成于 2026-10-19，对应 GUI v1.2（WPF + WPF-UI 4.3，commit ed12195）。*


---

## cnv参数 —— CNV 对话内置条件 / 动作全量参考

适用范围：CNV 对话每个节点（台词/选项/通用等）都挂一组 **条件**（满足才显示/生效）与一组 **动作**（条件满足后执行）。CNV 与 QST 用的是**同一套** Scriptable 条件/动作系统，字段编码一致；差异只在挂载位置（见「qst参数」节）。

**记录结构**（编辑器"条件/动作"弹窗与下表参数列的对应关系）：

- 条件 = `type(u32) + body版本(u8) + 比较符(u32) + body`；动作 = `type(u32) + body版本(u8) + body`（**动作没有比较符**）。
- 比较符（官方 ScriptableCondition+Comparison，作用同 SQL 运算符）：`0=<　1=<=　2==　3=>=　4=>　5=!=`。
- 条件集合首字 logic：`0=AND（全部满足）　1=OR（满足任一）`；空集合只占计数字本身。
- body 版本（编辑器里的 subType）：`HasFaction(6) / HasItem(9) / HasQuestObjective(11) / LuaScript(12) / CheckRandomValue(21) 恒为 1`，其余恒为 0——版本错会让官方编辑器按另一种布局读、直接错位。
- 参数类型缩写：`S`=字符串　`I`=u32　`B`=u8　`i/b`=仅 body 版本=1 时存在　`P`=数量+字符串列表+尾字u32　`L`=Lua 调用（函数名+参数列表，条件形态另有固定尾部 5 字）。

**置信度说明**：下表"名称"列全部经官方编辑器证实（2026-09-28 用探测文件在编辑器中逐类型辨认 + Common.dll 类型枚举直读），**仅动作 19 标注（推断）**；"用途"列按官方类名/参数名与语料观察归纳，除注明"真机核对"外未逐项在引擎内验证——按用途改数值前建议先用官方编辑器对照确认。

### 条件（30 种，type 0–29）

| type | 名称 | 参数（body） | 用途 |
|---|---|---|---|
| 0 | OnQuest | QuestFile(S) OnQuest(B) | 玩家是否正在进行指定任务；OnQuest 作开关：1=进行中才算、0=未在进行才算 |
| 1 | QuestTaskComplete | QuestFile(S) Task(I) TaskComplete(B) | 指定任务的指定 task 是否已完成；TaskComplete 开关真/假 |
| 2 | HasExperience | Experience(I) | 玩家当前经验值与比较符指定值比较 |
| 3 | IsLevel | IsLevel(I) | 玩家等级与比较符指定值比较 |
| 4 | IsDifficulty | IsDifficulty(I) | 当前游戏难度判定；语料实证 0/1/2 = Normal/Epic/Legendary（与 GiveExperience 350/22000/54000 同现，真机核对） |
| 5 | IsHardcore | IsHardcore(B) | 是否专家（hardcore）模式 |
| 6 | HasFaction | FactionName(S) Value(I)（v1） | 玩家与指定阵营的声望值与比较符指定值比较 |
| 7 | HasToken | TokenName(S) HasToken(B) | 全局 token 是否已被设置；token 是任务/对话间传递状态的核心机制（条件 7/18/20、动作 9/10 配对使用） |
| 8 | HasMoney | MoneyAmount(I) | 玩家金币数与比较符指定值比较 |
| 9 | HasItem | ItemRecord(S) ItemCount(I) CompleteOnly(b)（v1） | 是否持有指定物品 ≥ ItemCount；CompleteOnly=1 时只统计任务物品栏 |
| 10 | HasKilled | MonsterRecords 列表(P)，尾字=KillCount(u32) | 是否击杀过列表中怪物 ≥ KillCount 次 |
| 11 | HasQuestObjective | QuestFile(S) Task(I) Objective(I) Satisfied(B)（v1） | 指定任务目标（objective）当前是否满足/完成 |
| 12 | LuaScript | Function(S)+参数表(L)（v1） | 调用 Lua 函数，返回值作真值判断（引擎经 LuaManager 解析）；条件形态末尾带固定 5 字尾部 |
| 13 | KnowsPlayer | KnowsPlayer(B) | NPC 是否已认识玩家（首次见面/熟客判定） |
| 14 | OnQuestTask | QuestFile(S) Task(I) OnTask(B) | 玩家是否正处于指定任务的指定 task 阶段 |
| 15 | UsedQuestItem | ItemRecord(S) NumUsed(I) | 指定任务物品是否已被使用 ≥ NumUsed 次 |
| 16 | HasKilledProxy | ProxyRecords 列表(P)，尾字=KillCount(u32) | 经代理列表统计击杀（同 HasKilled，走 proxy 记录） |
| 17 | WaitForCompletion | （无参数） | 等待完成（语料中作目标唯一条件；无 body，含义=等本目标/task 完成事件） |
| 18 | ServerHasToken | TokenName(S) HasToken(B) | 服务器侧 token 判定（多人环境的状态，本地单机等同 HasToken） |
| 19 | QuestBlocked | QuestFile(S) QuestBlocked(B) | 指定任务是否处于被封锁状态（未满足进入条件） |
| 20 | AnyoneHasToken | TokenName(S) HasToken(B) | 队伍/服务器中任意玩家持有 token |
| 21 | CheckRandomValue | Name(S) RandomValue(I)（v1） | 检查按 Name 存储的随机值（与动作 21 GenerateRandomValue 配对，做概率分支） |
| 22 | OwnsDLC | DLC_ID(I) | 是否拥有指定 DLC（按 DLC 的数字 ID） |
| 23 | HasTribute | TributeAmount(I) | 贡品（tribute）数量与比较符指定值比较 |
| 24 | EarnedDevotion | DevotionAmount(I) | 虔诚（devotion）值与比较符指定值比较 |
| 25 | HasMastery | MasteryName(S) HasMastery(B) | 是否拥有指定精通（mastery） |
| 26 | IsChallenge | IsChallenge(I) | 挑战（challenge）模式/等级判定 |
| 27 | HasBuff | BuffName(S) HasBuff(B) | 玩家是否带有指定 buff |
| 28 | HasKilledMonsterClass | Faction1(I) Faction2(I) KillCount(I) | 击杀某怪物职业类（monster class）计数与比较符比较（字段名照录官方字段表） |
| 29 | HasKilledFaction | Faction1(I) Faction2(I) KillCount(I) | 击杀某阵营成员计数与比较符比较 |

### 动作（29 种，type 1–28；0 与 16 引擎工厂返回 null，正式文件不使用）

| type | 名称 | 参数（body） | 用途 |
|---|---|---|---|
| 1 | BeginQuestTask | QuestFile(S) Task(I) | 开始（激活）指定任务的指定 task（推进任务阶段） |
| 2 | CompleteQuest | QuestFile(S) | 完成整个任务（触发完成奖励与后续） |
| 3 | CompleteQuestTask | QuestFile(S) Task(I) | 完成指定任务的指定 task（task 级推进） |
| 4 | GiveMoney | MoneyAmount(I) | 给予金币 |
| 5 | GiveFaction | FactionType(S) Amount(I) | 给予指定阵营声望（增减 Amount） |
| 6 | GiveItem | ItemRecord(S) NumItems(I) | 给予物品 |
| 7 | GiveLevel | NumLevels(I) | 提升玩家等级 |
| 8 | GiveExperience | Experience(I) | 给予经验值 |
| 9 | GiveToken | TokenName(S) | 设置全局 token（与条件 7/18/20 配对） |
| 10 | RemoveToken | TokenName(S) | 清除全局 token |
| 11 | LuaScript | Function(S)+参数表(L) | 调用 Lua 函数执行任意脚本逻辑（动作形态，无尾部 5 字） |
| 12 | DebugPrint | Text(S) | 调试输出（控制台/日志） |
| 13 | PlaySound | SoundRecord(S) | 播放声音 |
| 14 | PlayAnimation | AnimationType(I) | 播放动画；语料实证 9 = Chat（真机核对），其余枚举值待查 |
| 15 | GiveSkillPoint | NumSkillpoints(I) | 给予技能点 |
| 17 | Notification | Tag(S) | 弹出 UI 通知（按 tag 定位文案，语料见 tagNotification_… 前缀） |
| 18 | GiveAttribPoint | NumAttributePoints(I) | 给予属性点 |
| 19 | ScriptEvent | ScriptEvent(S) **（推断）** | 触发脚本事件（语料全部 EV_… 前缀；编辑器截图该行被弹窗遮住，未亲眼确认） |
| 20 | GiveRandomItem | ItemRecord(S) NumItems(I) | 按物品记录随机给予 |
| 21 | GenerateRandomValue | Name(S) Min(I) Max(I) | 生成 [Min,Max] 随机值并以 Name 存储（与条件 21 CheckRandomValue 配对） |
| 22 | CastSkill | SkillRecord(S) | 对玩家施放指定技能 |
| 23 | UnlockFaction | FactionType(S) | 解锁阵营（声望面板可见） |
| 24 | SetFaction | FactionType(S) Value(I) | 直接把阵营声望设为 Value（与 5 GiveFaction 增减式相对） |
| 25 | GiveDevotion | NumDevotionPoints(I) | 给予虔诚点 |
| 26 | GiveTribute | TributeAmount(I) | 给予贡品 |
| 27 | UnlockTutorial | TutorialPage(I) | 解锁指定教学页 |
| 28 | PlayVideo | VideoPath(S) AllowSkip(B) | 播放视频；AllowSkip=是否允许跳过 |

---

## qst参数 —— QST 任务内置条件 / 动作全量参考

**与 CNV 的关系**：条件/动作与 CNV **完全同一套**（同表、同编码、同比较符），本节不再重复表格，查上一节「cnv参数」即可。QST 的差异在于**挂载位置**与**用途语境**：

- **挂载位置**（本编辑器树形表中可直接右键添加）：
  - task 的 **OnAccept（接受事件）**：玩家接受任务时执行的动作序列——典型组合 `BeginQuestTask(1)` 开阶段、`GiveToken(9)` 设状态、`GiveExperience(8)/GiveMoney(4)` 给预付奖励；
  - task 的 **OnComplete（完成事件）**：task 完成时执行——典型组合 `CompleteQuest(2)` / `GiveExperience(8)` / `IsDifficulty(4)` 分难度给不同奖励（语料 mq_burrwitch 实证）；
  - **Objective（目标）**：目标的 Conditions（何时完成目标，常配 WaitForCompletion(17) / HasToken(7)）与 Actions（完成目标时的附加效果）；
  - 目标完成态可被对话侧引用：CNV 条件 `HasQuestObjective(11)`、`QuestTaskComplete(1)` 按 QuestFile+Task+Objective 定位——**Objective ID 列显示的 uid 就是官方编辑器供此引用的 ID**。
- **QST 语料统计**（248 样本）：task/objective/event 的 flags 全 0、uid 全非 0；task/objective/event 的串表槽固定为 组名(0)+任务名(1)+每 task 依次 [标题, 描述]+事件/目标文本，总量遵守公式 `2+2T+O+I+E`（本编辑器保存三检查会强制校验）。
- **QST 特有注意点**：
  - 编辑器底部面板选中任意行可编辑其串表文本（组名/任务名/标题/描述/目标文本）；
  - 结构增删（右键 ＋任务/＋目标/＋事件、－删除）会自动按公式重建本地化表，不破坏既有文本；
  - 任务头部 flags（主线/支线/可重复）在右键"任务属性"里编辑；
  - 对话侧引导玩家来接任务用 CNV 的 OnQuest(0)/BeginQuestTask 对（接任务动作一般挂在对话选项上，不在 QST 内）。

---
