# qstcnv — Grim Dawn .cnv / .qst 解析 · 导出 · 写回

对《恐怖黎明》对话文件（`.cnv`）与任务文件（`.qst`）二进制格式的完整实现：
**解析（CnvQstLib）+ 编辑器风格查看 / JSON 互转 / 官方本地化 TXT 导出**。

格式权威依据：[R-Hidayatullah/gd-utility](https://github.com/R-Hidayatullah/gd-utility)（MIT）
反编译 Game.dll 加载器的结论（快照存于 `参考资料/gd-utility/`），格式详情见
**[CnvQstFormatDecoded.md](CnvQstFormatDecoded.md)**；历史逆向日志见 `CnvQstFormatSpec.md`（已被取代，留档）。

**手写 JSON 生成 `.cnv` / `.qst`**：完整字段表、30 种条件、27 个动作与可运行示例见
**[JSON转CNV.md](JSON转CNV.md)** / **[JSON转QST.md](JSON转QST.md)**；
全量 JSON 示例在 [`JSON示例/`](JSON示例/)。

## 验证结果（2026-09-09）

| 检查 | 结果 |
|---|---|
| `cnv示例/` 199 个 .cnv（含 8 个 v5） | 199/199 通过 |
| `qst示例/` 248 个 .qst（含 145 个多任务赏金） | 248/248 通过 |
| 通过标准 | 记录恰好铺满文件 ＋ 走序落在头部声明的本地化偏移 ＋ parse→write **逐字节一致** |
| dump 树形文本 | 与 3 份编辑器 UI 手工对照（alex_01 / jailor_01 / mq_burrwitch）逐节点一致 |
| selftest 结构编辑 | `cnv示例` 199/199：内存副本删/增/移后 `Parse(Write())` 三连校验（tiled/locOffset/round-trip）全过 |
| TXT 导出 | 与 GrimDawnCnvQstToTXT v0.9 输出在全部 447 样本上逐字节一致（但为按格式精准提取，不再强读二进制） |
| JSON 导出→导入 | 关键样本 round-trip 逐字节一致（含 logic 字保真） |

## 构建

```bash
dotnet build CnvQstEditor-cli/CnvQstEditor-cli.csproj     # 同时构建 CnvQstLib（net8.0，可被其他工程引用）
dotnet build qstcnv.sln                        # 全部工程（CnvQstLib/CnvQstEditor-cli/CnvQstEditor）
```

## 图形界面（CnvQstEditor，单 EXE 双模式）

`CnvQstEditor` 是同一套实现的图形界面，支持查看 / 编辑 / 新建 / 导入导出 / 批量处理，
与 CLI 共用 CnvQstLib，保证行为一致。

| 启动方式 | 行为 |
|---|---|
| `CnvQstEditor.exe`（双击 / 无参数） | 进入图形界面 |
| `CnvQstEditor.exe gui` | 强制图形界面（防变量残留） |
| `CnvQstEditor.exe <命令> …` | 进入 CLI（命令集与 CnvQstEditor-cli 完全一致） |

GUI 能力：
- 多文件页签（拖拽 .cnv/.qst 打开，拖目录打开批量面板）
- **平铺表格编辑器**（形如官方编辑器）：CNV 四列 = 节点 | 属性/字段 | 条件 | 动作，
  条件/动作弹窗编辑、确定回填；`D` 双模式切换（紧凑=自动换行封顶约 3 行 / 平铺=无上限）；
  CNV 节点行增删/移动（内置 `Renormalize` 保 Index/linkId/文本槽三位一体）；QST 四列 =
  节点 | uid | 条件 | 动作，task/objective/event **均可增删**（按公式 `2+2T+O+I+E` 重建本地化串表，文本随实体移动）
- **五视图**（工具条 / 视图菜单 / Ctrl+1…5，切换对**所有页签**生效并持久化到配置）：
  - 可视化 = 结构表格 + 底部节点文本编辑器，**唯一可编辑**的视图（`{^n}` 与真 `\n` 双向转译，空槽可留空）
  - JSON（只读，带行号）· 本地化（只读，行号处标注"槽号 + 对应节点角色"）· 槽位（只读，按结构规则生成的填写清单）· DUMP（只读，带行号）
  - 四个只读视图占满整个编辑区；多行文本统一使用 **AvaloniaEdit 11.4.1**（行号 / 查找替换 / 长行滚动）
- **填写建议（只提醒，不阻止保存与转换）**：建议填写 = CNV 说话人名 / 台词 / 结束语 / 主要选项、QST 任务组名 / 任务名 / 标题 / 目标；
  可为空 = QST 任务描述、CNV 跳转 / 随机 / 通用节点；应为空 = QST OnAccept / OnComplete 槽（格式保留）。
  同一份规则由 GUI（槽位视图、底部编辑器提示）与 CLI（`verify` / `convert` 的槽位明细）共用
- 新建对话（CNV v6）/ 新建任务（QST2 v9）向导
- 导入导出：TXT（官方本地化）/ JSON / dump 树形文本，JSON 导入
- 批量面板：目录扫描表 / 批量导出 TXT·JSON / 校验所选 / CSV 报告
- 保存三检查（铺满文件 / 本地化偏移 / round-trip）+ `.bak` 自动备份；解析失败或校验不过的文件**只读**打开（可看可导出不可保存）

**界面语言（i18n）**：英文 key → 译文的字典，语言包是 exe 旁 `lang/*.json`（随构建自动复制）：

```json
{ "code": "zh-CN", "name": "简体中文",
  "strings": { "Open": "打开", "Saved {0}": "已保存 {0}" },
  "types":   { "HasFaction": "阵营声望" } }
```

- **默认界面为中文**（`zh-CN.json` 即完整中文字典）；`en.json` 是恒等映射，作为全部可用 key 的清单；
- 缺 key 自动回退英文 key，语言包可逐步补全；
- 动作 / 条件 / 节点类型属于固定术语（`L.FixedTerms`），不进入 `strings` 字典；中文界面由 `types` 段括注
  （"speech（台词）"），无译文时只显示官方原文；
- **新增语言 = 复制一份 json 改 code/name/译文**，放回 `lang/` 即在「语言」菜单出现，无需改代码；
- 切换即时生效并记住（`%APPDATA%\CnvQstEditor\lang.txt`）。

**界面（v1.1 重做）**：扁平化主题（统一字体/配色/表格与页签样式，`Theme.cs`），主工具栏 + 状态栏 +
键盘快捷键（Ctrl+O/S/N/E/L/B、F5 校验、Ctrl+W 关页签；Alt+字母 展开对应菜单）。`CnvQstEditor.exe --smoke` 可做无头冒烟回归。

## 打包（双单文件 EXE）

```powershell
.\publish.ps1
```

产出两个**框架依赖单文件**（配置 B / lite，dist 中只有 EXE，无 DLL/PDB）：

- `dist/CnvQstEditor.exe` — 图形界面（无参数或 `gui`）；带参数时复用同一套 CLI。
  需目标机装有 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)。
- `dist/CnvQstEditor-cli.exe` — 纯 CLI，无 `AttachConsole` 接管问题，适合脚本/CI。
  需目标机装有 .NET 8 Runtime x64（Desktop Runtime 已包含）。

已知事项：
- GUI EXE 在 PowerShell/cmd 中直接以 `& exe verify …` 调用时输出可能被 `AttachConsole` 接管，回显异常；
  用 `Start-Process -RedirectStandardOutput` 或 `cmd /c` 即可获得规整输出（退出码约定 0=成功 / 1=存在失败项 / 2=用法错误）。
  纯 CLI 的 `CnvQstEditor-cli.exe` 无此问题。
- 单文件 EXE 偶发杀软误报，请加白名单。

## 用法（CnvQstEditor-cli）

```bash
T=CnvQstEditor-cli/bin/Debug/net8.0/CnvQstEditor-cli.dll   # 发布版直接用 dist/CnvQstEditor-cli.exe

dotnet $T help                                      # 命令说明；help convert / help dev 看详情
dotnet $T view        <文件.cnv|qst>                 # 查看：树形文本内容（原 dump）
dotnet $T convert     <文件> [-o 输出]               # JSON ↔ QST/CNV 互转：
                                                   #   .json → .qst/.cnv（需遵循 JSON 规范）
                                                   #   .cnv/.qst → .json（全保真模型，可改后转回）
                                                   #   省略 -o：输出到输入同目录、同名换扩展名
dotnet $T verify      <文件|目录>                    # 验证：结构 + 文本槽 + round-trip 字节一致
                                                   #   单文件另打印槽位明细（行 / 槽位名 / 建议填写）
dotnet $T txt         <文件|目录> [-o 目录] [--flat]  # 导出官方本地化 TXT
dotnet $T sample      cnv|qst [-o 模板.json]        # 生成 convert 用 JSON 模板

# 开发/分析工具（一般用户可忽略）
dotnet $T dev anchors|census|vocab|probe|selftest|hash|legacytxt|txt-diff …
```

旧命令 `dump` / `json` / `make` 仍可用，分别是 `view` / `convert` 的兼容别名。

### 由 JSON 生成（convert / sample）

`convert` 读一份**手写友好**的 spec（JSON，条件动作可用官方名书写），生成 .cnv/.qst；
与全保真 JSON 模型（`convert 文件.cnv -o model.json` 导出，供 round-trip）不同，spec 只描述你要写什么：

```json
{ "format": "cnv", "speaker": "Alex",
  "rows": [
    { "level": 1, "kind": "speech", "text": "你终于来了。", "flags": 0,
      "cond": [ { "HasToken": ["MY_TOKEN", 1] } ],
      "act":  [ { "GiveMoney": [500] } ] },
    { "level": 2, "kind": "continue", "text": "继续" }
  ] }
```

- `level` = 缩进层级（父是最近一个 level−1 的行）；`kind` = speech/continue/accept/decline/end/link/selector
- 条件/动作：`{"HasFaction": ["SURVIVORS", 10000]}`、`{"HasFaction": {"FactionName": "SURVIVORS", "Value": 10000}}`、
  或原始形态 `{"type": 6, "subType": 1, "args": [...]}`（探测未知类型用）
- `flags` 写 step 的 unknown 原值；`logic` 写 "AND"/"OR"
- 改旧任务必须保留 ID：QST 根级 `id` 写了就用（含 0），完全省略才随机；task/objective `uid` 不能省，条件/动作里的引用 ID 必须照抄目标文件

`dev probe` 是**用官方编辑器反查字段名**的工具：它批量造出最小文件，让编辑器自己去显示对应的
选项名（覆盖本地语料里样本为 0 的类型）。默认把一类探测合并成一个文件的多个节点，
节点文本里写着我们当前的猜测名，打开即可逐条对照；`--split` 可拆成一类一文件以隔离报错项。

| 子命令 | 用途 |
|---|---|
| `probe cond` / `probe act` | 26 个条件 / 27 个动作类型各一节点，读官方名（**已用它辨认完毕**）|
| `probe uid [--type N] [--sub N]` | 固定条件只改 type/subType 之后的字，读出**比较符编码** |
| `probe sub [--type N]` / `probe suball` | subType 0..5 扫变体，用于定位剩余 4 项官方选项 |
| `probe flags` | step.unknown 各位 ↔ 编辑器 {Once Only / Always Show / Unimportant} |
| `probe qst` | 1/3 个 task 的任务文件，反推 QST 串表槽位映射 |

> 生成物都会先过"铺满 / locOffset / round-trip"三检查，再写进目录，并输出 `索引.txt`。
> 2026-09-28 首轮探测结果（官方编辑器截图存于 `反馈截图/`）：26 个条件类型与 27 个动作类型
> **全部辨认完毕**，导出了两个重要修正——条件记录里那个被称作 uid 的字其实是**比较符**；
> 动作 9/10 是 GiveToken/RemoveToken（与原先推断相反）。详见 CnvQstFormatDecoded.md §4。

### 翻译管线（与现有工作流衔接）

1. `txt cnv示例 -o text_zh && txt qst示例 -o text_zh` —— 导出待翻译文本（与
   [GrimDawnCnvQstToTXT](https://github.com/zhangqi222/GrimDawnCnvQstToTXT) 输出逐字节兼容）；
2. 翻译各 txt（一行一条文本，空行是空槽位，**不可增删**）；
3. 二选一：
   - `json` 导出 → 改 JSON → `make` 生成翻译版 .cnv/.qst（结构级精准写回）；
   - 沿用官方位置回填：翻译后的 txt 交 arzedit-GUI 打包成 `text_zh.arc` 放入游戏 `resources`。

## 工程结构

```
CnvQstLib/     解析+序列化库（net8.0，GUI/CLI 共用）：BinaryReaderEx/WriterEx、CnvFile、QstFile、
               Scriptable（26 条件+27 动作 body 表+参数字段名）、Names（类型命名三层：引擎证实 /
               金样锚定 / 官方选项推断，推断带"（推断）"后缀）、EditorVocab（官方编辑器选项词表
               + type 对照，`vocab` 命令输出）、
               Dump、TextExport（官方 TXT + v0.9 基线）、JsonIO（全保真双向）、
               Cli/CliApp（CLI 命令）、Cli/Census（语料普查）、Cli/BatchOps（批量服务）、Creation（新建工厂）
CnvQstEditor-cli/    CLI 薄壳（Main → CliApp.Run），发布为 dist/CnvQstEditor-cli.exe
CnvQstEditor/  Avalonia GUI（net8.0-windows）：Program（双模式入口）、MainWindow、DocSession、
               Editors（条件/动作/节点/QST 编辑器）、Dialogs（新建向导/JSON）、
               BatchWindow（批量面板）、I18n（中英界面切换）
README.md      使用说明与打包（双单文件 EXE）
cnvqst-wiki-dev.md  开发侧 wiki：构建/发布、CLI 规范、GUI 约定、决策记录
publish.ps1    一键打包 dist/CnvQstEditor.exe 与 dist/CnvQstEditor-cli.exe（配置 B / lite 单文件）
参考资料/gd-utility/   上游文档与 C++ 参考解析器（MIT，commit 1d468f5）
cnv示例/ qst示例/      199+248 个真实游戏样本与编辑器 UI 手工对照
EditorProbe/           官方编辑器程序集只读探针（types/fields/il/xref/res/ex）：解枚举常量、字段表、方法 IL
BinAnalyzer/           早期分析工具（留档）
完善实施计划.md         本轮完善的计划与全部调研结论（已执行完毕）
```

## 验证结果

- 格式基线：`cnv示例` 199/199、`qst示例` 248/248（解析 + 铺满 + locOffset + round-trip 逐字节一致）
- CLI 回归：`CnvQstEditor-cli` 与 `CnvQstEditor.exe`（双模式 CLI）在 447 样本上均 199/248 全过
- GUI：447 样本经 `DocSession.Open`（GUI 实际打开路径）+ **平铺表格编辑器绑定**（CNV 199 / QST 248 行数全部对齐承载对象，0 崩溃）无异常；GUI 进程可正常启动
- JSON 生成：`JSON示例/*.json` 四个示例经 `convert` 生成，三检查全部通过
- JSON 往返：`dist/` 中全部 QST/CNV 样本 → JSON → 二进制，逐字节一致
- 构建：`dotnet build qstcnv.sln -c Release` 0 警告 0 错误

## 已解 / 未解（详见 CnvQstFormatDecoded.md 第七、八章）

**2026-09-28 已解**（手段：探测文件交官方编辑器辨认 + 只读反汇编官方编辑器，见格式文档第八章）
- **条件的 30 种、动作的 29 槽全部拿到官方名**（含本地语料里样本为 0 的类型）；
- **比较符**：条件记录里被 gd-utility 称作 uid 的那个字就是它——`<`=0 `<=`=1 `=`=2 `>=`=3 `>`=4 `!=`=5
  （官方 `ScriptableCondition+Comparison` 枚举 + 编辑器实测，两路互证）；那个字前面的字节是 **body 版本**；
- **step flags**：低 3 位 = OnceOnly(1) / AlwaysShow(2) / Unimportant(4)，正是编辑器的三个开关；
- **对话 id** = 根节点文本（NPC 名）的 FNV1 哈希（`CnvQstEditor-cli hash <文本>` 可直接算）；
- **节点类型**补齐 5=Generic、8=Random；linkId = 目标节点的展平序号（官方 `EstablishIds` 就是按下标编号）；
- **QST 本地化串表公式**：`S = 2 + 2T + O + I + E`，槽位顺序为
  每 task `[标题, 描述, OnAccept 事件…, Objectives…, OnComplete 事件…]`（248 个样本零误差 +
  官方 QuestEditor 逐槽核实）——多任务编辑的约束就此完全明确。

**仍未解**（不影响解析与写回，均按"保留原字节"处理）
- `flags` 的高 3 位（语料 0x8/0x10/0x20）：官方编辑器只用低 3 位，高位属游戏侧语义；
- QST 头部 +0x10、task 的 +0xA8/0xAC、objective/event 的若干 u32、两个版本门控 flag 位；
- 条件里"任务引用标识"要哈希的字符串（编辑器自身也显示 `?`）；
- bodyVersion 除 0/1 之外是否还有别的版本。

`CnvQstEditor-cli vocab` 打印逐项证据等级；`probe` 造探测文件；`EditorProbe` 只读反汇编官方编辑器
（`types` / `fields` / `il` / `xref` / `res` / `ex`）。
