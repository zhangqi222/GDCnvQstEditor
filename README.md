# qstcnv — Grim Dawn .cnv / .qst 解析 · 导出 · 写回

对《恐怖黎明》对话文件（`.cnv`）与任务文件（`.qst`）二进制格式的完整实现：
**解析（CnvQstLib）+ 编辑器风格 dump / JSON / 官方本地化 TXT 导出 + 翻译回写（import-txt / import-json）**。

格式权威依据：[R-Hidayatullah/gd-utility](https://github.com/R-Hidayatullah/gd-utility)（MIT）
反编译 Game.dll 加载器的结论（快照存于 `参考资料/gd-utility/`），格式详情见
**[CnvQstFormatDecoded.md](CnvQstFormatDecoded.md)**；历史逆向日志见 `CnvQstFormatSpec.md`（已被取代，留档）。

## 验证结果（2026-09-09）

| 检查 | 结果 |
|---|---|
| `cnv示例/` 199 个 .cnv（含 8 个 v5） | 199/199 通过 |
| `qst示例/` 247 个 .qst（含 145 个多任务赏金） | 247/247 通过 |
| 通过标准 | 记录恰好铺满文件 ＋ 走序落在头部声明的本地化偏移 ＋ parse→write **逐字节一致** |
| dump 树形文本 | 与 3 份编辑器 UI 手工对照（alex_01 / jailor_01 / mq_burrwitch）逐节点一致 |
| selftest 结构编辑 | `cnv示例` 199/199：内存副本删/增/移后 `Parse(Write())` 三连校验（tiled/locOffset/round-trip）全过 |
| TXT 导出 | 与 GrimDawnCnvQstToTXT v0.9 输出在全部 446 样本上逐字节一致（但为按格式精准提取，不再强读二进制） |
| JSON 导出→导入 | 关键样本 round-trip 逐字节一致（含 logic 字保真） |
| import-txt 原文回填 | 字节一致的恒等操作；中文翻译回填后重解析通过 |

## 构建

```bash
dotnet build CnvQstTool/CnvQstTool.csproj     # 同时构建 CnvQstLib（net8.0，可被其他工程引用）
dotnet build qstcnv.sln                        # 全部工程（CnvQstLib/CnvQstTool/CnvQstStudio）
```

## 图形界面（CnvQstStudio，单 EXE 双模式）

`CnvQstStudio` 是同一套实现的 WinForms 图形界面，支持查看 / 编辑 / 新建 / 导入导出 / 批量处理，
与 CLI 共用 CnvQstLib，保证行为一致。

| 启动方式 | 行为 |
|---|---|
| `CnvQstStudio.exe`（双击 / 无参数） | 进入图形界面 |
| `CnvQstStudio.exe gui` | 强制图形界面（防变量残留） |
| `CnvQstStudio.exe <命令> …` | 进入 CLI（命令集与 CnvQstTool 一致，另含 `new`） |

GUI 能力：
- 多文件页签（拖拽 .cnv/.qst 打开，拖目录打开批量面板）
- **平铺表格编辑器**（形如官方编辑器）：CNV 五列 = 节点 | 属性/字段 | 本地化文本 | 条件 | 动作，
  条件/动作弹窗编辑、确定回填；`D` 双模式切换（紧凑=自动换行封顶约 3 行 / 平铺=无上限）；
  CNV 节点行增删/移动（内置 `Renormalize` 保 Index/linkId/文本槽三位一体）；QST 四列平铺
  task/objective/event（task 行受本地化映射限制只读不增删）
- 本地化表列直接读写内嵌 enUS 串表，`{^n}` 与真 `\n` 双向转译；空槽灰字可留空
- 新建对话（CNV v6）/ 新建任务（QST2 v9）向导
- 导入导出：TXT（官方本地化）/ JSON / dump 树形文本，TXT 回填预览、JSON 导入
- 批量面板：目录扫描表 / 批量导出 TXT·JSON / 校验所选 / TXT 回填 / CSV 报告
- 保存三检查（铺满文件 / 本地化偏移 / round-trip）+ `.bak` 自动备份；解析失败或校验不过的文件**只读**打开（可看可导出不可保存）

**UI 语言**：默认简体中文；菜单「工具 → 语言 / Language」可切换到英文，即时生效并被记住（存于 `%APPDATA%\CnvQstStudio\lang.txt`）。

## 打包（单文件 EXE）

```powershell
.\publish.ps1
```

产出 `dist/CnvQstStudio.exe`（**配置 B / lite**：框架依赖单文件，约 0.3 MB，需目标机装有
[.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)）。

已知事项：
- PowerShell/cmd 中直接以 `& exe verify …` 调用时输出可能被 `AttachConsole` 接管，回显异常；
  用 `Start-Process -RedirectStandardOutput` 或 `cmd /c` 即可获得规整输出（退出码约定 0=成功 / 1=存在失败项 / 2=用法错误）。
- 单文件 EXE 偶发杀软误报，请加白名单。

## 用法（CnvQstTool）

```bash
T=CnvQstTool/bin/Debug/net8.0/CnvQstTool.dll

dotnet $T dump        <file.cnv|.qst>               # 编辑器风格树形文本（含 tiling/locOffset 检查）
dotnet $T json        <file> [out.json]             # 全保真 JSON 导出（可编辑后 import-json 回写）
dotnet $T txt         <file|目录> [-o 目录] [--flat]  # 官方本地化 TXT（text_zh.arc 流程，默认镜像目录结构）
dotnet $T import-txt  <file> <翻译.txt> [-o 输出]     # 翻译行回填 → 新 .cnv/.qst（行数必须与原表一致）
dotnet $T import-json <file> <编辑过的.json> [-o 输出]# JSON 模型 → 重序列化
dotnet $T verify      <file|目录>                    # 批量：解析 + tiling/locOffset + round-trip 字节一致
dotnet $T anchors     <file>                        # 条件/动作类型 ID 锚定工作台
dotnet $T legacytxt   <file>                        # GrimDawnCnvQstToTXT v0.9 算法基线（仅比对用）
dotnet $T txt-diff    <目录>                        # 新 TXT 导出 vs v0.9 基线全量比对
dotnet $T vocab                                    # 官方编辑器选项 ↔ 类型 ID 对照表（命名证据）
dotnet $T sample cnv|qst [-o spec.json]            # 输出编写用 spec 模板
dotnet $T make   <spec.json> [-o out.cnv|out.qst]  # 由 spec 生成文件（生成后同样过三检查）
dotnet $T probe  cond|act|cmp|flags|qst|all [-o 目录] [--type N] [--split]
                                                   # 批量生成探测文件，交官方编辑器辨认字段
dotnet $T new cnv     -o out.cnv [--speaker 名] [--text 首句] [--end 结束语]  # 新建对话 v6
dotnet $T new qst     -o out.qst [--group 组] [--name 任务名] [--task 标题] [--desc 描述]  # 新建任务 v9
```

### 由 spec 生成（make / sample / probe）

`make` 读一份**手写友好**的 spec（JSON，条件动作可用官方名书写），生成 .cnv/.qst；
与 `import-json`（全保真模型，供 round-trip）不同，spec 只描述你要写什么：

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

`probe` 是**用官方编辑器反查字段名**的工具：它批量造出最小文件，让编辑器自己去显示对应的
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
   - `import-txt` 生成翻译版 .cnv/.qst（本方案，结构级精准写回）；
   - 沿用官方位置回填：翻译后的 txt 交 arzedit-GUI 打包成 `text_zh.arc` 放入游戏 `resources`。

## 工程结构

```
CnvQstLib/     解析+序列化库（net8.0，GUI/CLI 共用）：BinaryReaderEx/WriterEx、CnvFile、QstFile、
               Scriptable（26 条件+27 动作 body 表+参数字段名）、Names（类型命名三层：引擎证实 /
               金样锚定 / 官方选项推断，推断带"（推断）"后缀）、EditorVocab（官方编辑器选项词表
               + type 对照，`vocab` 命令输出）、
               Dump、TextExport（官方 TXT + v0.9 基线）、JsonIO（全保真双向）、
               Cli/CliApp（CLI 命令）、Cli/Census（语料普查）、Cli/BatchOps（批量服务）、Creation（新建工厂）
CnvQstTool/    CLI 薄壳（Main → CliApp.Run），开发/CI/脚本用
CnvQstStudio/  WinForms GUI（net8.0-windows）：Program（双模式入口）、MainForm、DocSession、
               Editors（条件/动作/节点/QST 编辑器）、Dialogs（新建向导/回填预览/JSON）、
               BatchForm（批量面板）、UiLang（中英界面切换）
publish.ps1    一键打包 dist/CnvQstStudio.exe（配置 B / lite 单文件）
参考资料/gd-utility/   上游文档与 C++ 参考解析器（MIT，commit 1d468f5）
cnv示例/ qst示例/      199+247 个真实游戏样本与编辑器 UI 手工对照
BinAnalyzer/           早期分析工具（留档）
完善实施计划.md         本轮完善的计划与全部调研结论（已执行完毕）
```

## 验证结果

- 格式基线：`cnv示例` 199/199、`qst示例` 247/247（解析 + 铺满 + locOffset + round-trip 逐字节一致）
- CLI 回归：`CnvQstTool` 与 `CnvQstStudio.exe`（双模式 CLI）在 446 样本上均 199/247 全过
- GUI：446 样本经 `DocSession.Open`（GUI 实际打开路径）+ **平铺表格编辑器绑定**（CNV 199 / QST 247 行数全部对齐承载对象，0 崩溃）无异常；GUI 进程可正常启动
- 新建：`new cnv` / `new qst` 产物通过三检查
- 构建：`dotnet build qstcnv.sln -c Release` 0 警告 0 错误

## 已知未解（不影响解析与写回，均按"保留原字节"处理）

若干字段语义（step+0x50、task+0xA8/0xAC、版本门控 flag 位等）、
条件 6 类的语义名（2/5/13/16/23/24，另有 Hasbuff、OperMerchantWindow 未定位）、
条件集合 logic 字的引擎定义（本地观测 0=AND、1=OR）、比较符编码、
QST 本地化串 2 之后与 task 的精确映射——详见 CnvQstFormatDecoded.md 第七章。
（动作 27 个可用槽已全部给出名字，其中 9 个为推断；`vocab` 命令可打印逐项证据与待核对清单。）
