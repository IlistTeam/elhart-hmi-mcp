# FStudio_MCP

**让 AI 能够根据自然语言描述，自动生成和修改繁易触摸屏的界面。**

通过 MCP 协议连接 AI 客户端与 FStudio 3，调用原生工程、控件和编译接口，完成页面布局、属性配置、工程保存与隔离编译。

> 本项目采用 [MIT 许可证](LICENSE)，允许商用、修改、分发和售卖，须保留版权及许可声明。

## 功能

- 创建、打开和保存原生工程，管理页面和画布控件。
- 按自然语言需求生成布局，查询和修改控件位置、尺寸、外观及属性。
- 一次创建多个控件并复用样式预设，批量读取和修改属性，直接回读页面实际布局。
- 配置本地寄存器地址，以及工程中的采样、定时和数据传输模型。
- 导入图片、检查模型，保存并重新打开工程。
- 对已保存工程创建快照，运行原生隔离编译，查询或取消任务。

例如，可以向已连接 MCP 的 AI 提出：

> 创建一个用于学习的电机监控界面，包含标题、启动和停止按钮、状态灯与转速显示。使用本地寄存器，保存到新工程并检查原生编译结果。

当前注册 73 个 MCP 接口，完整工具和参数以 `tools/list` 返回内容为准。新增批量操作的用法见 [批量生成与回读](docs/batch-generation.md)，具体控件支持情况见下方验证范围。

## 环境要求

- Windows、Python 3.11+（服务仅依赖标准库）。
- .NET Framework 4.x 的 x86 编译环境。
- 已合法安装的 FStudio 3；当前验证版本为 `3.0.15685.0`。
- 支持 stdio MCP 的 AI 客户端。

本仓库不分发 FStudio、厂商 DLL、模板、字体或其他第三方资源。

## 安装

建议克隆到较短的目录，例如 `C:\MCP\FStudio_MCP`。原生工程目录最多 90 个 UTF-16 单元，超过限制会提前拒绝创建或打开。

```powershell
git clone https://github.com/TingRuazhang/FStudio_MCP.git
cd FStudio_MCP
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File '.\build.ps1'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -File '.\install-visible-host.ps1'
```

默认安装位置为 `C:\Program Files (x86)\Flexem\FStudio 3.x`。自定义安装路径时使用：

```powershell
.\build.ps1 -FStudioBin 'D:\FStudio\Bin'
.\install-visible-host.ps1 -FStudioRoot 'D:\FStudio'
```

插件生成在 `artifacts/visible-addin`，通过 FStudio 的 `-addindir:` 参数加载，不复制到厂商安装目录。安装脚本保留已有 `host-settings.json` 中的工作区和状态目录。

升级后有两个生效步骤：在 AI 客户端中重启或重新连接 MCP 服务，使 Python 服务重新加载工具列表；保存工程后退出旧 FStudio 宿主，再启动新进程以加载更新的 DLL。只重连 MCP 不会替换已经加载的宿主 DLL。DLL 被占用时，安装脚本会保存待更新副本并报告 `pending_host_restart`；关闭旧宿主后重新运行安装脚本即可部署。

## 连接 AI 客户端

按实际路径修改 [mcp.example.json](mcp.example.json)，加入客户端的 MCP 配置：

```json
{
  "mcpServers": {
    "fstudio": {
      "command": "python",
      "args": [
        "C:/MCP/FStudio_MCP/server.py",
        "--workspace",
        "C:/MCP/FStudio_MCP/projects",
        "--fstudio-bin",
        "C:/Program Files (x86)/Flexem/FStudio 3.x/Bin"
      ]
    }
  }
}
```

`command` 可替换为 Python 解释器的完整路径。自定义工作区时，应同步修改 `artifacts/visible-addin/host-settings.json` 的 `workspace` 并重启宿主。

## 操作流程

1. 调用 `fstudio_visible_start` 连接或启动宿主，再创建或打开工作区工程。
2. 使用 `fstudio_canvas_snapshot` 读取当前页的控件名称、UUID 和实际边界。创建常用控件时，优先用 `fstudio_canvas_create_many` 一次提交布局与预设；特殊配置仍可使用 `fstudio_canvas_prepare`、模型接口和 `fstudio_canvas_commit`，不用的草稿用 `fstudio_canvas_discard` 清理。
3. 已知属性用 `fstudio_model_get_many` 批量回读；同一组设置用 `fstudio_model_apply_many`，不同设置用 `fstudio_model_set_many`。属性支持 `Position.Width` 这样的点路径。先检查响应状态：已完成就消费 `result`，尚未完成则用 `fstudio_visible_job` 查询同一 `job_id`，不要重复提交写操作。
4. 调用 `fstudio_visible_save` 保存工程。模型句柄属于当前会话，重开工程后重新获取。
5. 调用 `fstudio_build_start`，再用 `fstudio_build_status` 查询。检查 `compiler_success`、`cleanup_complete`、`cleanup_errors` 和产物哈希；取消后也应继续查询至终态。

后台页面接口支持非保留的 Basic 页面；离线编辑前应关闭目标工程。编译使用已保存工程的快照，产物位于 `artifacts/isolated-jobs`，不会覆盖编辑器工程。

批量创建是新增操作，遇到已有名称会拒绝，不会自动更新。继续已有工程前先按名称查询并取得当前句柄。文字可能按原生字体规则调整尺寸，需检查 `actual_bounds`、`bounds_adjusted` 和警告；布局快照不能代替截图与实际运行检查。

批量创建静态文字时，必须提供非空白 `text`，也可以从预设继承。缺少文字会在创建前拒绝，以避免生成可保存但无法通过原生编译的空文字控件；需要分步填写内容时使用 `fstudio_canvas_prepare`。

完整批量参数与示例见 [批量生成与回读](docs/batch-generation.md)，数值与文本控件的逐项配置见 [配置说明](docs/numeric-background-workflow.md)。

## 本轮实测结果

在同一环境串行测试，固定旧版基线为提交 `3a03219`，每轮创建 24 个矩形并读取布局，共 3 轮。下表为均值：

| 操作 | 旧版 | 优化版 | 每轮逻辑操作数 |
| --- | --- | --- | --- |
| 创建 24 个矩形 | 763.82 ms | 262.69 ms | 48 → 1 |
| 回读名称、UUID 和边界 | 2081.67 ms | 18.84 ms | 170 → 1 |

本地控件创建速度约为原来的 **2.91 倍**，耗时减少 **65.6%**。计时包含任务等待及测试脚本的 10 ms 短轮询，不等于原生算法的纯执行时间，会随系统负载波动；不包含 AI 推理和网络时间，也不代表所有控件或完整工程都具有相同比例的提升。

优化版已通过保存重开、同类控件编号唯一、旧草稿与新批量创建交错提交、空文字提前拒绝、整批一次原生撤销/重做及原生编译验证；文字与数值输入的原生画布渲染图已检查，无文字重叠，4 个控件的布局检查无问题。证据位置与测试边界见 [本轮验证记录](docs/batch-generation.md#本轮验证记录)。

## 验证范围与已知限制

历史验证在 FStudio `3.0.15685.0`、F010 工程上覆盖原有 69 个 MCP 接口：68 个完成代表性成功调用，1 个兼容入口按设计拒绝；这 69 个接口经过非法参数和连接复用检查。该记录不代表目前的 73 个接口已全部完成同范围实机验证。

52 种绘图控件中，42 种通过创建、配置、提交、保存、重开及隔离编译，并通过产物与源工程保护检查。其余结果如下：

| 控件 | 当前限制 |
| --- | --- |
| Camera、DataTransmission、Pipe、Slider、StopWatch、StreamingMedia、TimePiece、Timer | 原生编译器报告图元类型不支持，已检查 16 / 32 位配置。 |
| WindowSelector | 当前原生版本反序列化时提前访问 PagingAddress，导致工程无法重开；已在创建草稿前保护性拒绝。 |
| WebCamera | 原生编译器报告 16 位色深不支持摄像头浏览；修改为 32 位并核验保存重开后仍未通过。 |

通过结果只覆盖已测配置。轨迹和饼图关闭了可选标签，GCodeEditor 使用本地设备 ID 验证编译。设备下载、PLC 通信、摄像头连接、实际触摸屏运行及全部参数组合未验证。`fstudio_validate_project` 仅执行结构检查。

`fstudio_visible_run_command` 为禁用的兼容入口，返回 `BACKGROUND_ONLY`。厂商内部接口可能随版本变化，升级 FStudio 后需重新验证。

## 运行回归测试

可复用测试源码位于 `tests/`，纳入版本管理。离线回归验证参数、预设展开、任务结果处理，以及用独立模型桩编译运行真实 C# 模型访问代码，不启动用户工程：

```powershell
python -m unittest discover -s tests -p 'test_*.py'
```

本轮统一测试入口的 14 项测试全部通过，包括 stdio 协议、预设与参数、任务终态及观察期限处理；其中真实 C# 模型访问覆盖 23 个场景，绘图契约检查覆盖 38 个场景。绘图契约检查需要先构建当前宿主 DLL，并使用 PowerShell 7；缺少依赖时该项会跳过。

真实宿主对照测试会分别启动私有桌面上的 FStudio，新建独立测试工程。脚本默认从 Git `HEAD` 编译基线，从当前工作树编译优化版；两次应在同一环境串行执行：

```powershell
python tests/native_generation.py --mode baseline
python tests/native_generation.py --mode optimized
```

本轮固定基线为 `3a03219`。提交新代码后，默认 `HEAD` 会变化，不能把再次运行得到的基线自动视为本轮旧版；精确复核本轮结果应使用已保留的源码副本和报告。

测试源码副本、原生编译日志和 `report.json` 保存在 `artifacts/generation-benchmark`，测试工程位于 `projects/perf-*`。它测量本地宿主调用与生成过程，不包含 AI 推理和网络耗时，也不替代截图、设备通信或实际触摸屏验证。具体测试方式见 [批量生成验证说明](docs/batch-generation.md#验证与复现)。

## 源码目录

- `server.py`：MCP 协议、参数校验与工具分发。
- `canvas_spec.py`：批量声明和预设展开，检查合并后的输入限制。
- `visible_host.py`：宿主命名管道客户端。
- `build_snapshot.py`、`desktop_process.py`、`isolated_*.py`：快照与隔离编译任务管理。
- `src/`：原生桥接与宿主插件源码。
- `build.ps1`、`install-visible-host.ps1`：构建与本地插件安装。
- `docs/`：使用文档。
- `tests/`：参数与模型回归，以及独立原生宿主对照测试。
- `mcp.example.json`：客户端配置示例。

可复用测试脚本随源码分发；运行生成的测试工程、报告、`artifacts/`、`projects/`、缓存和本地配置由 `.gitignore` 排除。

## 版权与使用声明

Copyright (c) 2026 TingRuazhang and FStudio_MCP contributors.

本项目采用标准 [MIT 许可证](LICENSE)，允许使用、复制、修改、合并、发布、分发、再许可和售卖；须在软件副本或实质性部分中保留版权及许可声明。

**特别声明：FStudio 软件的版权归繁易公司所有。本项目的 MIT 许可证不适用于 FStudio 软件及其厂商 DLL、模板等资源，也不授予其使用、修改或分发权利；使用者须自行取得相应授权并遵守繁易公司的许可条款。**

本项目为独立项目，并非繁易官方产品，不代表繁易公司的认可或背书。相关商标及其他第三方代码和资源的权利归各自权利人所有。

MIT 授权仅涵盖本项目有权授权的内容，不授予第三方软件或资源的使用权。软件按现状提供，AI 生成结果需自行审查，不保证适用于真实设备。
