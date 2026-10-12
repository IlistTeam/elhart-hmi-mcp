# 批量生成与回读

批量接口把常见的“创建草稿、配置、加入画布、逐项读取”合并为少量调用。控件仍由 FStudio 原生工厂和设计器创建，保存、重开及编译沿用现有工程流程。

使用前需要同时升级 Python MCP 服务和宿主 DLL：客户端重连用于刷新服务及工具列表，FStudio 新进程用于加载 DLL。安装脚本保留现有工作区配置。四个批量接口已在 v2026.09.12 发布；v2026.10.12 改进其速度和质量，工具数仍为 73，原有逐控件接口继续可用。

## 选择调用顺序

1. 启动或连接宿主，打开目标工程和业务页面。
2. 调用 `fstudio_canvas_snapshot`，记录 `page_uuid`，核对已有控件与名称。
3. 新控件通过 `fstudio_canvas_create_many` 一次创建；已有控件通过 `fstudio_model_set_many` 或 `fstudio_model_apply_many` 修改。
4. 用 `fstudio_model_get_many` 和布局快照核对实际值与实际边界；文字尺寸有调整时检查邻近内容。
5. 保存工程，重新打开后获取新句柄，再执行隔离编译和所需的实际界面检查。

批量创建只负责新增，不是按名称创建或更新的 upsert。名称已存在、同批名称重复或 `page_uuid` 与当前页不符时，整批会被拒绝。重开工程后原 `$ref` 失效，应重新查询；名称和 UUID 用于重新定位，`index` 只是本次集合中的位置。

## 读取页面布局

`fstudio_canvas_snapshot` 的全部参数均可省略：

| 参数 | 含义 | 限制或默认值 |
| --- | --- | --- |
| `page_uuid` | 当前页面的预期 UUID，不会自动切换页面 | 不符时拒绝 |
| `names` | 精确匹配控件名称，区分大小写 | 1～512 个不同名称 |
| `offset` | 在筛选结果中跳过的条数 | 默认为 0 |
| `limit` | 本次最多返回的条数 | 1～100，默认为 100 |

例如，向该工具提交以下参数，可查找三个具名控件：

```json
{
  "names": ["main.title", "main.panel", "main.status"],
  "offset": 0,
  "limit": 100
}
```

响应中的 `items` 按页面集合顺序给出 `name`、`uuid`、`type`、`model`、`bounds` 和 `index`。`model.$ref` 用于后续模型调用；`bounds` 包含实际 `x/y/width/height`。页面信息位于 `page`，顶层 `page_uuid` 可作为下一次创建或快照的前置条件。

`total_count` 是该页顶层控件总数，`matched_count` 是名称筛选后的总数。`next_offset` 非空时用它继续分页；`complete=true` 表示当前筛选结果已经读完。`missing_names` 和 `duplicate_names` 根据全部匹配项计算，不会因为分页而漏报。`duplicate_names` 沿用 `{name,count}` 对象数组，例如 `[{"name":"main.title","count":2}]`；`duplicate_name_count` 表示重名名称的数量，不是重名控件总数。

快照的 `scope` 为 `active_page_top_level_only`，只读获取当前页顶层控件，允许检查当前公共页或其他页面类型；不会因此放宽创建接口的页面限制。快照不合成继承的公共页，不展开组，也不是画面截图。它可以核对位置和尺寸，不能确认字体显示、遮挡、交互或设备运行效果。

## 一次创建多个控件

`fstudio_canvas_create_many` 接收 `widgets`，以及可选的 `presets` 和 `page_uuid`。创建仍限于非保留 Basic 页面，页面 ID 为 0～28999。每个控件都必须明确给出名称和边界，工具类型可以来自控件本身或其预设。

以下是该工具的完整参数示例，适用于当前页面上尚无这三个名称的情况：

```json
{
  "presets": {
    "heading": {
      "tool": "Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool",
      "text_style": {"font_size": 24, "color": "#FF143D59", "bold": true}
    },
    "panel": {
      "tool": "Flexem.Studio.GraphicsDesigner.Tools.RectangleTool"
    }
  },
  "widgets": [
    {
      "name": "main.title", "preset": "heading",
      "text": "设备状态",
      "x": 24, "y": 20, "width": 320, "height": 48
    },
    {
      "name": "main.panel", "preset": "panel",
      "x": 24, "y": 88, "width": 592, "height": 224
    },
    {
      "name": "main.status", "preset": "heading",
      "text": "等待连接",
      "text_style": {"font_size": 18, "bold": false},
      "x": 40, "y": 112, "width": 240, "height": 40
    }
  ]
}
```

上例展示声明方式，不包含设备连接逻辑。实际边界以返回值为准。接续既有工程时，应先查询名称，并在请求中使用刚读到的 `page_uuid` 防止切错页面。

### 字段与预设覆盖

| 控件字段 | 用途 | 合并与设置规则 |
| --- | --- | --- |
| `name` | 唯一名称，写入原生控件备注及显示名称 | 每个控件独立指定 |
| `x/y/width/height` | 请求的位置和尺寸 | 每个控件独立指定；坐标 0～16384，尺寸 1～16384 |
| `tool` | 完整原生工具类型名称 | 控件值覆盖预设值 |
| `tool_properties` | 工具自身的公开标量属性 | 按字段合并，控件值优先 |
| `properties` | 控件模型的点路径属性，条目为 `{member,value}` | 按 `member` 合并，控件值优先 |
| `local_addresses` | 本地地址，条目为 `{member,address}` | 按 `member` 合并，控件值优先 |
| `text` | 初始化项目语言项的静态文字，`StaticTextTool` 必填且不能全为空白 | 可从预设继承，控件值覆盖预设值 |
| `text_style` | 当前文字的字体设置 | 按字段合并，控件值优先 |

预设不能包含名称、边界或其他预设引用。不存在的预设会被拒绝。同一 `properties` 或 `local_addresses` 数组内重复声明相同路径会被拒绝；控件对预设同路径的覆盖是允许的。覆盖不会把原路径移到数组末尾，新路径追加在已有路径后面。

创建草稿时，`properties` 按合并后的顺序执行，每项重新解析当前路径，因此依赖模式的属性应放在模式设置之后。随后配置本地地址和文字，最后设置名称及请求边界。模式或原生文字处理仍可能改变最终尺寸。

`properties.value` 和 `tool_properties` 只接受 JSON 标量：字符串、布尔、有限数字和属性允许的 `null`。不接受数组、对象或 `$ref`，也不能借此替换原生子模型。这是 v2026.10.12 对批量创建声明的有意收紧；需要复杂对象设置的控件，应使用原有的 `fstudio_canvas_prepare` 与模型接口。

批量创建的 `properties` 不能覆盖身份、几何或归属路径，包括 `Position`、`Comment`、`UniqueId`、`ComponentId`、`Parent`、`Project`、`Services`、`View` 等；名称和几何使用专门字段。本地地址的 `member` 指向已有原生地址对象，`address` 是 0～4294967295 的整数，沿用现有本地设备/寄存器解析和直接地址限制，不执行硬件通信。

### 静态文字

`text` 与 `text_style` 仅用于 `Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool`，并要求文字处于 `Label` 模式。沿用 v2026.09.12 的创建行为：初始化项目所需语言项，并赋予同一 `text` 内容。显式提供 `text_style` 时使用 Graphic 字库，未提供时沿用各语言原生字体。它不是自动翻译；需要不同语言内容时，创建后分别配置各语言的 `LabelContent`。

批量创建 `StaticTextTool` 必须提供非空白 `text`，可以写在控件中，也可以从预设继承。v2026.10.12 有意收紧这一要求：预设展开后，缺少 `text`、空字符串或只有空白的文字会在原生创建前被拒绝；仅提供 `text_style` 不能代替文字内容。这样可避免留下能保存、却被原生编译器判定为标签内容为空的控件。需要先创建草稿再逐步填写文字时，使用原来的 `fstudio_canvas_prepare` 和后续配置、提交接口。

| `text_style` 字段 | 类型 | 取值 |
| --- | --- | --- |
| `font_size` | 数字 | 8～120 |
| `color` | 字符串 | `#AARRGGBB`，例如 `#FF143D59` |
| `bold` | 布尔 | `true` 或 `false` |
| `font_name` | 字符串 | 原生字体名称，应在目标环境可用 |

文字长度最多 4096 字符。返回 `text_scope: all_initialized_languages` 表示对已初始化语言条目赋值。原生文字测量可能扩大控件尺寸，字体属性设置成功不代表排版已通过验收。

### 数量限制与结果

| 项目 | 上限 |
| --- | --- |
| 每次创建的 `widgets` | 30 个，至少 1 个 |
| 可声明的 `presets` | 20 个 |
| 每控件展开后的 `properties` | 60 项 |
| 每控件展开后的 `local_addresses` | 8 项 |
| 每控件展开后的 `tool_properties` | 20 项 |
| 全批展开后的 `properties` 总量 | 1200 项 |
| 普通点路径 | 512 字符，最多 16 段公开属性名 |

限制按预设展开后的实际内容检查。整批会先配置独立草稿，再通过一个原生插入操作和一个撤销组加入页面。失败时尝试撤销插入、关闭设置生命周期并清理临时句柄；若回滚或清理不完整，错误会明确说明，应先检查当前页，不能直接重发。

成功结果中的 `created` 与请求顺序一致，每项包含 `name`、`uuid`、`type`、`model`、`requested_bounds`、`actual_bounds`、`bounds_adjusted` 和 `index`。还会返回 `count`、`warnings`、`native_undo_transaction`、`placement_operations`、`dirty`、`persistence`、`elapsed_ms` 和分阶段 `timings_ms`。

`bounds_adjusted=true` 表示原生尺寸与请求不同，`warnings` 也会提示控件超出页面。创建成功只表示原生页面已修改，仍须 `fstudio_visible_save` 才能保存到磁盘。

## 批量读取和修改属性

| 接口 | 参数结构 | 适用情况 |
| --- | --- | --- |
| `fstudio_model_get_many` | `reads`：每项含 `ref`，可含 `member/index/offset` | 一次回读多个已知属性或集合页，1～100 项 |
| `fstudio_model_set_many` | `changes`：每项含 `ref/member/value` | 对不同目标写入不同值，1～100 项 |
| `fstudio_model_apply_many` | `refs` 加 `properties`，属性项含 `member/value` | 多个目标使用同一组设置，展开后最多 100 项 |

原有 `fstudio_model_get/set/set_many` 同样支持 `Position.Width`、`NormalInfo.UsingType` 等点路径，不必先创建每个中间对象的句柄。路径只导航公开属性；集合索引通过 `index` 指定，分页通过 `offset` 指定，不能把索引表达式写进路径。

`get_many` 返回按输入顺序排列的 `results`，每项含 `ref`、请求提供的 `member/index` 及 `value`。复杂对象以 `$ref` 描述；读取集合时默认也只返回句柄，保持 v2026.09.12 的紧凑返回约定。显式提供 `index` 才读取单项，显式提供 `offset` 才返回集合页的 `items` 和 `offset`，每页最多 100 项。原有 `model_get` 继续保留其默认集合分页行为。属性值每次从当前模型读取，元数据缓存不会缓存动态属性值。

下面的 Python 片段用于组装参数：`snapshot` 是已经完成的 `fstudio_canvas_snapshot` 业务结果，且已检查 `complete=true` 与名称唯一性。

```python
refs = [item["model"]["$ref"] for item in snapshot["items"][:10]]
read_arguments = {
    "reads": [
        {"ref": ref, "member": member}
        for ref in refs
        for member in ("Position.X", "Position.Y", "Position.Width", "Position.Height")
    ]
}
apply_arguments = {
    "refs": refs,
    "properties": [{"member": "Position.X", "value": 24}]
}
```

有控件时，分别把这两个参数对象交给 `fstudio_model_get_many` 和 `fstudio_model_apply_many`。示例最多读取 40 项，再把所选控件的 X 坐标统一为 24；空页面无需提交空批次。

批量写入要求所有目标属于同一页面或同一草稿。宿主预先检查路径、值转换、实际属性重复以及路径经过的中间对象（`PathOwners`）；不同句柄或不同路径如果最终落到同一对象的同一属性，也会拒绝。同批替换父对象并修改其子属性的冲突在写入前拒绝。页面写入共享原生撤销事务，未上画布草稿在出错时尝试恢复原值。

每项实际写入前还会重新解析路径。若前项模式切换等原生副作用使后项路径不再指向预校验的对象，整批会失败并回滚。应把此类模式切换单独提交，重新读取模型后再修改新子对象。回滚时，原生模式 setter 可能创建新的子实例，因此失败后也应重新读取路径与句柄。

## 任务状态、保护与错误处理

宿主管道线程最多等待约 40 ms，让短任务可以在首次响应中带回 `completed/result` 或 `failed/error`。这是首次应答的短等待窗口，不是控件生成的耗时保证，也不会取消尚未完成的任务。

调用方应先检查已经收到的终态。尚在排队或执行时，仅用 `fstudio_visible_job` 查询原 `job_id`；观察超时不能证明操作没有执行。写操作不能通过重新提交来轮询。读取接口会优先消费首次终态，长时间未完成时仍可能返回原任务供继续查询。

只读任务继续使用 5、10、20、40、50 ms 退避，较慢任务后续间隔保持最多 50 ms。首次约 40 ms 等待与后续只读轮询是两个阶段；等待期限结束时保留同一 `job_id`，不会重发业务操作。

批量操作保留现有保护：仅访问配置工作区；创建仅支持非保留 Basic 页面，页面 ID 必须在 0～28999；只读快照允许检查其他当前页面；`page_uuid` 不匹配即拒绝。模型写入检查每一级中间对象和最终目标，不能经由点路径改写 UI 对象或交互命令。已知会使原生进程栈溢出的 `EllipseInfo.Normal` getter 继续被禁止，相关配置应使用已检查的 `NormalInfo` 等正常接口。

FStudio `3.0.15685.0` 的 `WindowSelectorTool` 仍因保存重开问题被保护性拒绝。批量接口不会扩大其他控件的原生编译支持范围；完整历史限制见 [README](../README.md#验证范围与已知限制)。

## 验证与复现

在仓库根目录执行离线回归：

```powershell
python -m unittest discover -s tests -p 'test_*.py'
```

这些测试覆盖 stdio MCP 协议、预设合并、展开后限制、参数拒绝、首次终态消费、原任务轮询、观察期限边界及完整 ZIP 分发。发布前统一入口的 16 项测试全部通过，其中真实 `LiveModels.cs` 的独立 C# 回归包含 26 个场景，绘图契约检查包含 55 个场景，完整 ZIP 分发检查包含 2 项。

C# 回归需要 Windows .NET Framework 编译器。绘图契约检查还需要已构建的 `artifacts/FStudioMcp.Visible.dll` 与 PowerShell 7；依赖缺失时会跳过，应检查测试输出中的 `skipped`。这些检查使用独立进程和内存模型，不启动 FStudio 或打开用户工程。发布前已完整重建宿主，独立编译固定基线 `3a03219` 和当时 `HEAD`，并从独立目录解压完整技能包、运行内置服务的 `--help`。

真实宿主对照测试采用同样的 24 个矩形、3 轮页面生成和布局回读。优化版还检查单次原生撤销/重做、文字、原生数值输入、本地地址、错误拒绝及批量读写，并核验保存重开后的控件身份、同类编号和原生隔离编译。原生画布渲染图保存为 `quality-native.png` 供视觉复核。运行命令：

```powershell
python tests/native_generation.py --mode baseline --baseline-ref 3a03219
python tests/native_generation.py --mode optimized
```

`baseline` 默认使用固定提交 `3a03219` 的宿主源码，`optimized` 使用工作树源码。`--baseline-ref` 可以显式指定其他提交，报告会记录解析后的完整提交号。两次测试各自编译独立 DLL，在私有桌面启动新的 FStudio 进程和测试工程，不覆盖已安装插件、宿主设置或用户当前工程。默认输出位于 `artifacts/generation-benchmark`，也可用 `--output` 指定报告目录。

`3a03219` 是更早的逐控件流程基线，不是已提供批量接口的 v2026.09.12；本页计时不能解释为此次发布相对 v2026.09.12 的提升。默认基线不会随 `HEAD` 变化，但工作树、运行环境和系统负载会变化；精确复核历史数值时仍需当次报告目录内保留的源码副本和日志。

查看各次输出的 `report.json`、日志与画布渲染图，确认实际成功项目和失败原因，再比较耗时、逻辑操作数及管道往返次数。报告仅反映本机原生调用链，不含 AI 推理和网络时间。几何断言不能代替视觉检查；触摸交互、PLC 通信和真实设备运行还需单独验收。

原有 69 个接口的历史代表性验证继续保持原范围，不能据此声明当前 73 个接口已全部完成同等实机验证。

发布前的最终真实宿主兼容测试报告为 `artifacts/release-20261012/native-verification/optimized-deac749e/report.json`，结果 `success=true`。默认 F010 工程中两个语言项的 `LabelContent` 与请求一致，显式文字样式使用 Graphic 字库；保存重开、整批撤销/重做、旧草稿与批量创建交错提交均通过，原生编译返回 `compiler_success=true`。这是最终兼容验证，不用于改写下方先前测量的性能样本；报告保留本机，不包含在下载包中。

## 本轮验证记录

以下是发布准备前的原生测量记录。对照环境为本机 FStudio `3.0.15685.0`、F010 工程，固定旧逐项提交为 `3a03219`，不与 v2026.09.12 比较。两版在独立宿主中串行运行，每版创建 3 个测试页面，每页 24 个矩形；表中为 3 轮均值，操作数为每轮逻辑操作数。

| 操作 | 旧版均值 | 优化版均值 | 旧版操作数 | 优化版操作数 |
| --- | --- | --- | --- | --- |
| 创建 24 个矩形 | 763.82 ms | 262.69 ms | 48 | 1 |
| 回读名称、UUID 和实际边界 | 2081.67 ms | 18.84 ms | 170 | 1 |

本地创建速度约为原来的 **2.91 倍**，耗时减少 **65.6%**。计时包含任务等待及测试脚本的 10 ms 短轮询，不等于原生算法的纯执行时间，并会随系统负载波动。读取提升同时来自合并调用和紧凑快照，不能把该比例解释为单次反射或完整 AI 生成流程的加速。测试不包含 AI 推理、网络往返、用户审阅和真实设备下载运行。

两份最终报告均为 `success=true`。优化版另外确认：

- 页面保存后可以重新打开，控件 UUID 保持有效，同类型控件的原生编号无重复。
- 一次原生撤销移除整批 24 个控件，一次重做恢复相同 UUID 的整批控件。
- 中英文静态文字、两个原生数值输入及本地地址配置成功。
- 静态文字缺省或为空时在创建前拒绝；旧接口草稿与新批量创建交错提交时，两个数值控件取得不同的原生编号 1 和 2。
- 后项非法时草稿和画布无残留；重复名称、错页请求被拒绝；批量属性修改后回读一致。
- 原生编译返回 `compiler_success=true`。
- `quality-native.png` 使用原生背景和画布分层渲染，已查看确认本次文字无重叠；这是该测试页面的视觉检查，不扩展为全部控件和状态的验收。
- `layout-plan.json` 经已安装技能的 `check_layout.py` 检查，4 个控件、0 个问题。

本机证据路径如下，均相对于仓库根目录；报告、渲染图、源码副本和测试工程不随源码或技能下载包分发。公开仓库提供可复用测试脚本，下载者需在自己的合法安装环境中生成报告：

| 证据 | 路径 |
| --- | --- |
| 旧版最终报告及源码副本 | `artifacts/generation-final/baseline-23158b66/report.json` 及同目录 `src/` |
| 优化版最终报告及源码副本 | `artifacts/generation-final/optimized-c64362a0/report.json` 及同目录 `src/` |
| 原生画布渲染图 | `artifacts/generation-final/optimized-c64362a0/quality-native.png` |
| 布局检查输入 | `artifacts/generation-final/optimized-c64362a0/layout-plan.json` |

宿主升级保留原 `host-settings.json` 配置。已加载旧版 DLL 的 FStudio 进程不会自动切换版本，仍须保存工程后重新启动；MCP 客户端也须重连以加载新工具。
