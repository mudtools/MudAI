# Mud.AI.Agent

`Mud.AI.Agent` 是 Mud.AI 套件的**中立运行期**：承载 AI Agent 的全部产品无关机制——工具执行骨架、
会话回合管线、授权执行链尾两段、HITL 人工确认闭环、RAG 三段机制、流式通道机制与运行期装配。
契约面在 `Mud.AI.Abstractions`，产品事实经端口 / 委托注入；本包**不含任何产品语义**。

## 定位

```
消费方（如 Mud.Feishu.AI） ──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
                    ▲
                    └── Mud.AI.Generator（Analyzer，由产品侧挂载，不由本包声明）
```

- v2.3 自飞书 AI 族中立化迁入；**零 `Mud.Feishu*` 依赖**（门禁 `SingleToolSurfaceEngineContractGuards` 机械锁定），
  亦不得声明 `Mud.HttpUtils`（跨产品故障改经中立端口 `ITransportFault` 传入）。
- 「MAF 单点直引」铁律在本包**收敛式放宽为白名单** {`Mud.Feishu.AI`, `Mud.AI.Agent`}：本包可直引
  `Microsoft.Agents.AI`，但不得因此获得任何 `Mud.Feishu*` 引用。

## 工程事实

| 项 | 值 |
| --- | --- |
| 目标框架 | `netstandard2.0;net6.0;net8.0;net10.0`（net8.0+ 原生 AOT 兼容） |
| 语言 / 可空 | C# 13.0 / `Nullable` enable / `ImplicitUsings` enable |
| 工程引用 | `Mud.AI.Abstractions`（唯一，单向无环） |
| 运行期依赖 | `Microsoft.Agents.AI` 1.25.0（MAF）、`Microsoft.Extensions.AI.Abstractions` 10.10.1；M.E.* 钉版 10.0.12（DI / Logging / Options / Configuration） |
| Token 计数 | net8.0/net10.0：`Microsoft.ML.Tokenizers` 2.0.0 + `.Data.O200kBase`（Tiktoken 精确计数）；net6.0：`System.Text.Json` 10.0.12（补 `JsonNode.DeepClone`） |
| AOT | net8+ 上 `IsAotCompatible` / `EnableAotAnalyzer` / `EnableTrimAnalyzer` / `TrimMode=full`；`NU1603`（版本漂移）升级为错误 |
| 已知抑制 | `MEAI001`（`MessageCountingChatReducer` 实验性诊断，有意抑制） |

`netstandard2.0` 的 `IsExternalInit` polyfill 由本包显式 internal 注入（按铁律不借 MAF 传递依赖获得）。

## 命名空间布局

一级命名空间与文件夹一一对应：

```
Mud.AI.Agent                ←（本包无顶层公共类型）
Mud.AI.Agent.Agents         ← Agents/          内层 Agent 装配与运行期编织
Mud.AI.Agent.Approvals      ← Approvals/       HITL 审批闭环
Mud.AI.Agent.Channels       ← Channels/        流式通道实现
Mud.AI.Agent.Context        ← Context/         上下文装配器族
Mud.AI.Agent.Conversations  ← Conversations/   会话历史 / 并发 / 键控闸门
Mud.AI.Agent.Events         ← Events/          回合管线（会话管线机制唯一归属）
Mud.AI.Agent.Extensions     ← Extensions/      中立运行期 DI 骨架
Mud.AI.Agent.Guidance       ← Guidance/        域级 guidance 装配
Mud.AI.Agent.Knowledge      ← Knowledge/       RAG 切片 / 索引 / 检索
Mud.AI.Agent.Sessions       ← Sessions/        会话存取实现
Mud.AI.Agent.Tools          ← Tools/           工具执行骨架
Mud.AI.Agent.Tools.Args     ← Tools/Args/      入站参数机制
Mud.AI.Agent.Tools.Errors   ← Tools/Errors/    错误契约与分类
Mud.AI.Agent.Tools.Results  ← Tools/Results/   出站结果机制
Mud.AI.Agent.Tools.Security ← Tools/Security/  安全文本基线
```

`GlobalUsings.cs` 令全包默认拥有 MAF / M.E.AI / Logging / Options 能力，并 `global using Mud.AI.Agent.Tools.Errors`。

## 机制导览

### 1. 工具执行骨架（Tools/）

**一切回填模型的文本都必须经 `ToolResultPipeline` 单一出口**（序列化 → `MaxToolResultLength` 截断 →
`Truncated`/`TruncationReason` 标记）；`ToolErrorFactory` 是 `ToolError` 载荷的唯一构造点；
`ToolPagination` 是唯一翻页实现；`ToolResultJsons` 是信封单源构造。骨架核心 `ToolExecutor`（readonly struct，
per-method 实例）把三段机械骨架从执行器方法中抽出：

```
参数校验失败回填 → JSON 解析失败回填 → ApiResult 解包 + 失败回填 + 成功投影 + 截断
```

| 机制 | 承载类型 |
| --- | --- |
| 出站唯一出口 | `ToolResultPipeline`、`ToolResultText`、`ToolResultJson`（`UnsafeRelaxedJsonEscaping`，防中文 `\uXXXX` 膨胀） |
| 分页聚合 | `ToolPagination` / `PagedFetchResult`（`HardMaxItems=1000`，触达预算带 `truncated:true` + `next_page_token`） |
| 写操作预演 | `ToolDryRun`（返回「将下发什么」但不调用下游；摘要不回原文） |
| 错误契约 | `ToolError`、`ToolErrorClassifier`（8 类闭集，纯函数，不认识任何产品码表）、`ToolErrorText`、`ToolErrorHintRenderer`、`ToolErrorPayloadSerializer` |
| 重试策略 | `ToolRetryPolicy`（只读工具 retryable 错误自动重试，写工具默认零重试） |
| 策略匹配 | `ToolPolicyMatcher` / `ToolNamePattern` / `ToolPolicyRules` / `ToolPolicyDecision`（glob / 域级） |
| 入站净化 | `ToolArgumentNormalizer`（参数值形态唯一认知入口）、`ToolArgumentSanitizer`、`ToolArgsDigester`（敏感键名一律掩码） |
| 出站净化 / 安全 | `ToolResultSanitizer`、`ToolResultContentSafety`、`SecurityTextPrimitives`（安全文本基线单源） |
| 预算 | `ToolCallBudget`（`AsyncLocal`，默认 10，耗尽短路返回结构化错误不抛异常） |

### 2. 会话管线（Events/）— 11 阶段回合状态机

`ConversationalTurnPipeline.RunTurnAsync` 是**中立回合管线**（会话管线机制唯一归属）：

```
① 请求准入校验 → ② 会话键 → ③ 遥测作用域 → ④ 会话闸门 → ⑤ 取/建会话 → ⑥ outbox 补发
→ ⑦ 放弃孤儿审批（修史早于历史进管线）→ ⑧ 下发可行性前置判定 → ⑨ 工具执行上下文/预算作用域
→ ⑩ 上下文装配 → ⑪ Run / RunStreaming（含流式补偿收尾）
```

产品事实与产品策略经 `ConversationalTurnOptions` 注入（outbox 状态键、准入校验、下发可行性、
流式目标解析、「等待人工确认」文案等委托槽）；产品作用域经 `ITurnScopeFactory` / `TurnScopeContext` 接缝。
配套端口：`IAgentSessionFacade`（会话取/建/存）、`IConversationReplySink`（回复下发，失败照常传播、
幂等回滚由事件层 at-least-once 承接）；结果载体 `TurnOutcome`（`ResponseText` / `Streamed` / `Replayed`）。

并发与历史：`KeyedAsyncGate`（同键串行、跨键并行，键空闲回收信号量）、`ConcurrencyLimiter`
（全局削峰 + 队列超时 + 幂等 `Lease`）、`ConversationSummarizer`（渐进式摘要，工具轨迹只存摘要行）、
`ChatTokenCounter`（net8+ Tiktoken 精确计数，低 TFM 回退字符估算）。

### 3. 授权执行链 — 只承载尾两段

决策⑥「强类型直连 + 结果裁剪 / 错误回填」：`ToolApiOutcome<T>` / `ToolApiFailure` + `ToolExecutor.FromApi*`
+ `ToolResultPipeline`。授权门禁与租户切换的 `FeishuToolBinding` **留在产品侧**。HITL 侧闭环：

| 类型 | 职责 |
| --- | --- |
| `AgentApprovalContinuation` | 批准回灌续跑闭环：同键加载会话 → 取原始审批请求 → 重建工具执行上下文 → 续跑并落库 |
| `ApprovalRequestProjector` | 框架 `ToolApprovalRequestContent` → 宿主投影的唯一实现；fail-closed（未注册即降级纯提示） |
| `PendingApprovalState` | MAF 待确认工具调用状态桥；坏值 ⇒ 空集合绝不抛 |
| `InMemoryPendingApprovalStore` | `IPendingApprovalStore` 进程内默认实现，按 `(TenantKey, RequestId)` 索引 |

### 4. RAG 机制（Knowledge/ + Context/）

```
索引侧：ICorpusSource → DocumentChunker（结构优先切片：标题层级 → 块边界 → 定长窗口含重叠）
        → IVectorStore（宿主契约，按 ScopeKey 隔离）
检索侧：VectorRetriever（IRetriever，向量召回 → 预算裁剪）→ RetrievalContextAssembler（编号参考块注入 Prompt）
```

`TokenEstimator` 刻意不精确但确定、可测、AOT 友好。SDK 不引入向量库依赖，嵌入由宿主负责。

### 5. 通道机制（Channels/ + Events/）

`StreamingChannelChain`（降级链：`BeginAsync` 按注册顺序尝试各子通道，失败自动降级，实现 `IChannelTargetResolver`）
+ `BufferedMessageChannel`（分片缓冲基类：增量缓冲达 chunk 阈值**且**距上次更新 ≥ 800ms 才落地，
防平台频率限制；`FlushAsync` 终态无条件落地）。管线经 Begin → 增量 → Flush；Begin 失败回退非流式，
流中失败做补偿 Flush。

### 6. 上下文装配器族（Context/）

| 装配器 | 职责 |
| --- | --- |
| `ContextAssemblerAIContextProvider` | `IContextAssembler` → MAF 原生 `AIContextProvider` 桥接（宿主可二选一） |
| `FactListContextAssembler` | 事件事实列表 → 结构化 prompt 片段中立基类（机制一份，产品事实子类给） |
| `MediaContextAssembler` | 媒体转写为文本注入 prompt（模型只见文本结果） |
| `RetrievalContextAssembler` | 检索切片以编号参考块注入 Prompt |
| `SenderInfoContextAssembler` | 发送者 ID 形态注入 Prompt（`DefaultOrder = 10`） |

### 7. 运行期装配（Extensions/ + Agents/ + Guidance/ + Sessions/）

- `AddAgentRuntimeCore`：中立 DI 骨架，全部 `TryAdd`（先注册者生效）。默认件：
  `IAgentTelemetry → NoopAgentTelemetry.Instance`、`ISessionStore → MemorySessionStore`（单实例）、
  `IPendingApprovalStore → InMemoryPendingApprovalStore`。**刻意不含会话闸门**（留产品侧）。
- **主体机制装配官方路径**（新接入应走扩展方法；手工 `new` 仍合法）：
  - `AddAgentSessionStore(agent, historyStateKey)`：注册 `IAgentSessionFacade` 中立默认件（`AgentSessionStoreFacade`，薄委派 `AgentSessionStore`）。
  - `AddConcurrencyLimiter()`：注册 `ConcurrencyLimiter`（在途上限）+ `KeyedAsyncGate`（同键串行），旋钮机械映射 `AgentRuntimeOptions.MaxConcurrentTurns` / `TurnQueueTimeoutSeconds`。
  - `AddToolRetryPolicy()`：注册 `ToolRetryPolicy`，旋钮取 `AgentRuntimeOptions.ToolRetry`。
  - `AddConversationalTurnPipeline(agent, sessions, keyBuilder, acquireGate?, turnOptions?)`：注册 `ConversationalTurnPipeline` 单例——两个"容器不可能知道"的依赖（Agent 实例、会话键治理委托）显式传入，其余（Options / 遥测 / 装配器 / 通道 / 审批端口 / 作用域端口 / 会话门面）从容器解析。
    `sessions` 传 `null` 即取容器内的 `IAgentSessionFacade`（`AddAgentSessionStore` 的中立默认件或宿主实现）——两个官方扩展因此可组合；门面缺位在**解析期**抛错（注册期取不到容器实例，不静默造内存件）。
  - 前置条件：须先注册 `AgentRuntimeOptions`（产品 Options 投影）；缺失即 fail-fast，不静默取默认值。
  - 全部注册点由 `RuntimeRegistrationsContractGuards` 与冻结清单机械锁定（防清单腐烂）；注册一律显式泛型形态（服务类型可被机械提取）。
- `AgentAssemblyFactory`：把 Agent 名 / 指令 / 工具 / 历史裁剪窗组装为 `ChatClientAgentOptions`。
- `ObservedDelegatingAgent`：运行期编织基类（摘要先于 Run → 运行 Span → 耗时直方图 → token 用量 → 失败标注）。
- `AgentSessionStore`：产品中立会话门面，承担坏值自愈；`MemorySessionStore`：软过期 + 分摊节流全量清扫。
- `GuidanceComposer`：把「已启用工具所属域」的 guidance 追加到宿主指令之后；截断与否落在返回值上（可断言信号）。

### 8. 产品上下文与环境量（Events/ITurnScopeFactory + Abstractions/Context/AmbientScope）

- **产品专属环境量的官方形态 = `AmbientScope<T>`**（中立机制）：AsyncLocal 槽位 + Scope 句柄所有权校验 + 栈式恢复。产品实现 `ITurnScopeFactory` 时只用它，不另写一套恢复逻辑（"不得自建第二套恢复语义"）。
- **产品维度透传 = `TurnScopeContext.Facts`**：租户凭证之外的维度（企业标识、应用凭证槽位等）由产品经 `ConversationalTurnOptions.BuildScopeFacts` 委托生产，管线在建立工具作用域时搬运；中立管线不认识任何键的语义。
- **HITL 续跑的环境量恢复 = `AgentApprovalContinuation.RunAsync` 的 `restoreScope`**：先于工具作用域建立（外层），覆盖续跑全程。
- **工具面遥测 = `IAgentToolTelemetry`**（Abstractions）：接口拆分而非给 `IAgentTelemetry` 加成员；`ToolExecutor`（`ToolInvoked`，读写分类由构造携带）、`VectorRetriever`（`RetrievalPerformed`，**零命中同样上报 0/0**——否则"向量库配错 ⇒ 永远零知识"在指标上隐形）、`ConversationalTurnPipeline` + `AgentApprovalContinuation`（`ApprovalRequested`，逐项按其自身工具名上报，覆盖首轮与续跑轮两条路径）空安全消费。
- **归下游的埋点**：`ToolRetried` 由重试循环所有者调用（中立 `ToolRetryPolicy` 是无环判定件，不埋）；`ApprovalResolved` 由宿主批准入口在收到人类响应时调用（等待时长只有宿主知道，中立侧不伪造 0）。

## 契约守卫测试

`Tests/Mud.AI.Agent.Tests/ContractGuards/` 下 14+ 个守卫文件 + `Golden/NeutralizationGoldenTests.cs`
（22KB 中立化 golden 锁定）使「机制归属纪律」可机械断言：`AgentPackageContractGuards`、
`NeutralSourceContractGuards`、`NeutralVocabularyContractGuards`、`SilentCatchContractGuards`、
`TruncationVisibilityContractGuards`、`TurnPipelineContractGuards`、`ToolErrorContractGuards`、
`ToolArgumentShapeContractGuards`、`CommentConventionContractGuards` 等。

## 快速开始

```csharp
// 中立运行期骨架（默认件 + 宿主覆盖）
services.AddAgentRuntimeCore();
services.AddSingleton<ISessionStore, MyRedisSessionStore>();     // 覆盖默认内存实现

// 主体机制装配（官方路径；TryAdd 语义，先注册者生效）
services.AddSingleton(new AgentRuntimeOptions { /* 产品 Options 投影 */ });
services.AddAgentSessionStore(agent, historyStateKey: "myapp.agent.history");
services.AddConcurrencyLimiter();
services.AddToolRetryPolicy();
// sessions 传 null ⇒ 解析上面注册的 IAgentSessionFacade（产品自有门面则直接传实例）
services.AddConversationalTurnPipeline(agent, sessions: null, request => $"{request.AppKey}|{request.SubjectId}");

// 跑一个回合（回复端口由产品实现：投递失败照常传播，幂等回滚由事件层 at-least-once 承接）
var pipeline = provider.GetRequiredService<ConversationalTurnPipeline>();
var outcome = await pipeline.RunTurnAsync(request, replySink, ct);
```

## 相关包

- [`Mud.AI.Abstractions`](../Mud.AI.Abstractions/README.md) — 本包实现的全部端口契约。
- [`Mud.AI.Generator`](../Mud.AI.Generator/README.md) — 工具面源生成引擎（由产品侧挂载）。

## 许可证

MIT，见仓库根目录 [`LICENSE-MIT`](../LICENSE-MIT)。
