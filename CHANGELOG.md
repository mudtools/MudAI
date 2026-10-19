# 更新日志

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本规范](https://semver.org/lang/zh-CN/)。

## [1.1.0] - 2026-10-19

### 新增

- `Mud.AI.Agent`：`ToolResult.FromUnclassifiedError(string errorText, string toolName = "", int maxLength = 0)`——"握有一条错误文本、没有码表读数"的出口经唯一出口拿到 `api/upstream_error` 结构化载荷（与产品码表回落同口径），失败不再被记成 Succeeded。
- `Mud.AI.Agent`：`ToolExecutor.RunAsync(Func<CancellationToken, Task<ToolResult>>, CancellationToken)` 取消重载——工具执行链的取消断点；旧重载保留并委托（1.2.0 起评估弃用）。
- `Mud.AI.Abstractions`：`IAgentToolTelemetry : IAgentTelemetry`（Observability）——工具调用 / 重试 / RAG 检索 / HITL 审批的遥测扩展端口。接口拆分而非给主端口加成员（不 breaking 既有实现）；`NoopAgentTelemetry` 同时实现新接口（加接口不破坏二进制兼容）。
- `Mud.AI.Agent`：`ToolExecutor` 构造可选接收 `IAgentTelemetry` 并按构造携带读写分类（单参"写工具构造"自动为写），`RunAsync` 空安全埋点 `ToolInvoked`；`VectorRetriever` 埋点 `RetrievalPerformed`；`AgentApprovalContinuation.RunAsync` 埋点 `ApprovalRequested`（续跑轮再产出审批请求）。未实现新端口的旧遥测零埋点零异常。
- `Mud.AI.Agent`：`AgentApprovalContinuation.RunAsync` 末尾可选参数 `restoreScope`——产品环境量恢复委托，先于工具作用域建立（外层），覆盖续跑全程；置于取消令牌之后，既有按位传参调用点不错位。
- `Mud.AI.Abstractions`：`AmbientScope<T>`（Context）——产品专属环境量的官方机制：AsyncLocal 槽位 + Scope 句柄所有权校验 + 栈式恢复；产品实现 `ITurnScopeFactory` 不再各写一份恢复逻辑。
- `Mud.AI.Agent`：`TurnScopeContext` 追加 `Facts` 尾参 + `ConversationalTurnOptions.BuildScopeFacts` 委托（生产侧）——租户凭证之外的产品维度经中立端口透传，产品不必自建第二个上下文槽位。
- `Mud.AI.Agent`：运行期机制装配端口 `AddConversationalTurnPipeline` / `AddAgentSessionStore` / `AddConcurrencyLimiter` / `AddToolRetryPolicy`（`AgentRuntimeServiceCollectionExtensions`，TryAdd 语义不变）；新增中立实现 `AgentSessionStoreFacade`（`IAgentSessionFacade` 的薄适配）。
- `Mud.AI.Agent`：`DelegatingToolFunction`（Tools）——AOT 安全的工具函数装饰基座：三契约成员逐字转发 + `InvokeCoreAsync` 单装饰点，零反射。
- `Mud.AI.Agent`：`AddConversationalTurnPipeline` 的 `sessions` 允许传 `null` ⇒ 解析容器内的 `IAgentSessionFacade`——与 `AddAgentSessionStore` 组合成单条官方装配路径（门面缺位在解析期抛错，不静默造内存件）。
- `Mud.AI.Agent`：`ConversationalTurnPipeline` 把待确认项交给宿主批准通道时逐项上报 `ApprovalRequested`——首轮审批路径与续跑轮同口径（此前只有续跑轮有入口埋点）。

### 变更

- `Mud.AI.Agent`：`ToolResult.FromError(string)` 标 `[Obsolete]`（本仓零调用点，构建 0 CS0618；2.0.0 移除）。
- `Mud.AI.Agent`：`ToolResultPipeline.FromErrorText` 超预算时回填截断标记（错误出口此前截断正文但不标记——"模型把不完整正文当完整事实"的可见性缺口）。
- `Mud.AI.Agent`：`AgentSessionStore` 公开 `HistoryStateKey`（供门面层转述）。
- `Mud.AI.Abstractions`：`IAgentToolTelemetry.ApprovalResolved` 明确调用归属 = 宿主批准入口（等待时长只有宿主知道；中立侧不伪造 0）。
- 注释预算台账 `documents/comment-budget-baseline.json` 重新固化（新文件入账 + 触碰文件的换行合并；冻结文件零增长）。
- `Tests/Directory.Build.props`：测试工程增补 `Microsoft.Extensions.DependencyInjection` 10.0.12（DI 装配语义用例的容器面；版本与既有 M.E.* 定版对齐，NU1603 纪律不变）。
- 仓库清理：删除 `scripts/tmp-blocks.ps1` / `scripts/tmp-stats.ps1`（一次性注释块扫描脚本，硬编码本机绝对路径且无任何引用；正式口径由 `scripts/comment-audit.ps1` 承担）。
- 文档：`Mud.AI.Abstractions/README.md` 模块导览补齐 `AmbientScope<T>` 与 `IAgentToolTelemetry`；`Mud.AI.Agent/README.md` 修正快速开始示例（回复端口而非 options）并补埋点归属说明。

### 修复

- 工具错误出口的截断不可见（`FromErrorText` 截断后 `Truncated` 恒 false）——随 `FromError(ToolError, string, bool, string?)` 可选参数与出口逻辑修正。
- `VectorRetriever` 在"已查询但零命中"分支漏报 `RetrievalPerformed` ⇒ "向量库配错、永远零知识"在指标上完全隐形；现补 `(0, 0)` 样本。
- `AgentApprovalContinuation` 的 `ApprovalRequested` 只上报旧审批的工具名且多条新审批只记一次；现逐项按其自身工具名上报。

[1.1.0]: https://github.com/mudtools/Mud.AI/releases/tag/v1.1.0

## [1.0.1] - 2026-10-16

### 新增

- `Mud.AI.Abstractions`：`AgentToolAttribute.OutputSchema`（可空字符串）——返回结构 Schema 的**声明覆盖**。适用场景：工具真实回传结果无法从 Source 返回类型推导时（如 SDK 返回字节流、由宿主暂存后回传落盘路径/大小/内容类型等元数据），由策展接口显式声明输出契约。
- `Mud.AI.Generator`：源生成器读取 `OutputSchema`；非空时**原样采用**并跳过 Source 返回类型推导（连同其截断样本）。声明覆盖不参与「返回契约覆盖统计」（推导截断已丢弃），不污染 `OutputSchemaCoveredToolCount` 口径。
- `Mud.AI.Generator`：工具名常量发射描述注释，与契约表 / Schema 的 description 同源（取 `DocSummary`，折成单行并 XML 转义，防描述里的换行与尖括号破坏产物语法；无描述时不发射）。
- 打包脚本 `scripts/pack.ps1` / `scripts/pack_debug.ps1`：
  - Release 档：项目清单自动取自 `Mud.AI.slnx` 的 /Sources/ 段（Tests 工程 `IsPackable=false` 不产包）；同版本重打包默认被版本递增强制校验拒绝（探测 NuGet 全局缓存，`-ForceSameVersion` 逃生）；打包前只构建一次、`pack` 全程 `--no-build`，校验基准取构建产物快照，校验包集合完整性与包内 DLL 字节一致性。
  - Debug 档：产物落独立 `artifacts-debug` 目录（已入 `.gitignore`）；默认把同版本包同步到本机 NuGet 全局缓存（`.nupkg.metadata` + `.sha512` 齐全）供引用方本地调试，`-SkipCacheSync` / `-SyncOnly` 可控制。

### 变更

- 手写运行时层（`Mud.AI.Abstractions` / `Mud.AI.Agent` / `Mud.AI.Generator`）XML 文档注释压缩：多段 `<para>` 合并、`<returns>` 并入 `<summary>`、去除装饰性表述与过程性括注；读代码读不出的约束（守卫锁定点、AOT/TFM 例外、误报依据、失败语义与毒化链）全部保留。
- 注释预算台账 `documents/comment-budget-baseline.json` 按同一判据重新固化（仅下压），注释守卫的扫描面地板随实测值同步调整。
- 注释纪律守卫：评审批次编号判据收敛至编号闭集，覆盖全部既有批次形态；`P95/P99` 百分位与 `C0/C1` 控制字符块作为合法记号显式排除，守卫自证用例按同一闭集补齐。

[1.0.1]: https://github.com/mudtools/Mud.AI/releases/tag/v1.0.1

## [1.0.0] - 2026-10-16

Mud.AI 家族自飞书仓（原 `MudFeishu`）按「P3 上游化」独立后的首个正式版本，与飞书仓全局版本线解耦，自 1.0.0 起步。

### 新增

- `Mud.AI.Abstractions`（netstandard2.0 / net6.0 / net8.0 / net10.0）：平台无关端口与工具面契约——会话端口（`ConversationRequest` / `ConversationScope` / `ISessionStore`）、通道抽象（`IMessageChannel` / `IChannelTargetResolver`）、审批状态机接口（`IPendingApprovalStore` / `IToolApprovalChannel`）、声明契约族（`[AgentTool]` / `[ToolParameter]` / `[ToolHandler]`）、工具面引导契约（`ISdkToolProfile` / `[SdkToolProfile]` / `SdkTokenKind` / `InterfaceIdentity`）、遥测与配置面、RAG 宿主契约。零依赖纯契约工程。
- `Mud.AI.Agent`（同上四 TFM）：中立运行期——工具执行骨架（`ToolExecutor` / `ToolResultPipeline` 出站唯一出口 / `ToolPagination` / 错误契约族）、11 阶段会话回合管线（`ConversationalTurnPipeline`）、HITL 批准回灌闭环、RAG 三段机制、流式通道降级链、运行期 DI 骨架（`AddAgentRuntimeCore`）。net8.0+ 原生 AOT 兼容。
- `Mud.AI.Generator`（netstandard2.0，Analyzer 形态）：`[SdkToolProfile]` 剖面驱动的工具面源生成引擎，编译期产出 Schema / 强类型入参解包器 / 域注册器 / DI 装配 / Guidance / 能力目录；含 SDKT 契约守卫诊断与 27 个动态诊断槽位。
- 测试与门禁：`Mud.AI.Agent.Tests`（契约守卫 + 中立化 golden 快照）、`Mud.AI.Generator.Tests`；`scripts/verify-build.ps1`、`scripts/comment-audit.ps1`。

### 工程约定

- 统一 C# 13.0、`Nullable` enable；XML 文档注释随包分发。
- net8.0+ 启用 AOT 分析（`TrimMode=full`），ns2.0 / net6.0 按 TFM 精确豁免；版本漂移（NU1603）即构建失败。
- 依赖方向单向无环：消费方 → `Mud.AI.Agent` → `Mud.AI.Abstractions`；本仓零 `Mud.Feishu*` 依赖，产品侧只经 NuGet 包显式钉版引用。
- 注释纪律（手写运行时层，Tests 同遵）：评审批次编号必须能在 `documents/**` 或本文件里被解析；过程叙述必须链接存在的文档；注释行数与最长叙述块冻结在 `documents/comment-budget-baseline.json`（只允许下压），由 `CommentConventionContractGuards` 机械校验。编号闭集覆盖全部批次形态（中介方案 / 条目 / 策略轴 / 错误契约 / 工作组 / 任务分支 / 结论 / 裸轮次 / 缺陷号 / 文档版本 / 方案条目引用；`P95/P99` 百分位与 `C0/C1` 控制字符块是合法记号，显式排除）。台账清理与重新固化走 `scripts/comment-audit.ps1`，作用域实况扫描走 `scripts/comment-live-scan.ps1`。
- PowerShell 脚本编码：Windows PowerShell 5.1 把无 BOM 的 UTF-8 当 ANSI 解析，含中文字面量的脚本必须带 UTF-8 BOM（或纯 ASCII），否则直接解析失败。

[1.0.0]: https://github.com/mudtools/Mud.AI/releases/tag/v1.0.0
