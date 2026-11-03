# Usus

> Usus 机械设计经验知识库：经验条目按语义与关键词检索，供 AI 调用与积累

## 定位

机械设计的经验库：踩过的坑、评审意见、选型取舍、工艺约束，一条经验写成一个 Markdown 文件，放在本仓 `z-经验库/` 里长期维护。
模块给这些文件建关键词与语义索引，AI 按问题检索出最相关的几条，带出处地用进设计里。名字取自贺拉斯把 usus（惯例、实践）与 norma（规范）并举：Norma 管公式，Usus 管经验。

- 不做计算和查表，那是 Norma 的事；经验里需要算的地方引用 Norma 的指令名。
- 不做建模、出图，那是 Strenua 与 SolidEdgeMCP 的事。
- 经验正文不进运行区：运行区只放能从 `z-经验库/` 重建的索引。

## 概况

| 项 | 值 |
| --- | --- |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `usus` |
| 界面 | Aurora 描述式页面（场景 `HistoryUsus`） |
| MCP 投影 | `readonly` |
| 版本与宿主下限 | [`UsusVersion.props`](./b-Code/Usus/UsusVersion.props) |

## 能力

| 按钮 | 指令 | 用途 |
| --- | --- | --- |
| 打招呼 | `usus.hello.ping` | 示范：改状态的无参指令，刷新页面表格 |
| — | `usus.hello.echo text=…` | 示范：带参数的只读指令 |
| — | `usus.hello.list` | 示范：只读列表，投影到 MCP |

指令说明就是注册时的自描述（`diana.docs.read domain=usus`，宿主 6.1.0 起没有消费文档）；自描述装不下的取舍写进 [现行约定](./b-Office/现行约定.md)。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [现行约定](./b-Office/现行约定.md) | 唯一的长期文档：模板规则与模块起步约定 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/Usus/` | 模块源码、manifest 与 `eng/` 构建脚本 |
| `b-Code/Usus.Tests/` | 离线自动验证（含宿主装载冒烟） |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 唯一长期文档 `现行约定.md` |
| `z-经验库/` | 经验正文，一条一个 Markdown 文件，长期维护、纳入 git |
| `z-Publish/` | 正式快照与 `history/` 归档，由宿主管线写入 |

## 构建与验证

```powershell
dotnet build .\b-Code\Usus\Usus.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\b-Code\Usus.Tests\Usus.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

离线测试最后一组用宿主真正的 `ModuleHost` 装一遍打好的包：版本漂移、manifest 写错在宿主那边
都是静默跳过整个模块，这组用例在出包前拦住它们。

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布；`eng/Build-UsusPackage.ps1` 只用于本地候选构建。

## 要点

- 页面 owner 由指令域推出（`usus` → `HistoryUsus`），合同检查与离线测试都守着这条。
- 版本只改 `UsusVersion.props`，再同步两份 manifest；三处不一致构建前就失败。
- 经验是数据不是指令：加一条经验就是在 `z-经验库/` 加一个文件，不发版；索引发现文件变了自动增量重建。
- 条目格式见 [`z-经验库/README.md`](./z-经验库/README.md)。

## 保留内容

### logo与作者

- 作者：pinavia
- ![logo](./Logo.png)

### 学习许可与权利保留

- 本项目允许个人、学校和研究者进行非商业性的学习、研究和引用。
- 未经著作权人书面许可，不得将项目内容用于商业开发、产品制造、商业服务、商业宣传、投标交付、毕业设计成品交付或再授权。
- 不得删除作者、来源、版本历史和第三方许可证声明，不得将本项目或其衍生成果宣称为与作者无关的原创项目。
- 软件源码适用 [PolyForm Noncommercial 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0/)；机械模型、PCB、原理图、图片、文档、Unity 工程和其他非软件资料适用 [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)。
- SE、SW、STEP/STP 及其他三维模型仅允许个人或学校进行非商业学习、评审和研究；未经著作权人书面许可，不得用于加工、打样、制造、销售、量产、投标、产品交付、商业宣传、改作或再授权。
- 第三方组件、模板、元件库、外购件模型和工具仍受其原始许可证约束；本声明不扩大作者对第三方内容的权利。

商业使用、产品化、公司内部长期使用、转让或排他性授权需另行取得著作权人的书面许可。
