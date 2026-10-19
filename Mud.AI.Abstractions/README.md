# Mud.AI.Abstractions

`Mud.AI.Abstractions` 是 Mud.AI 套件的**零依赖纯契约工程**：以平台无关端口（Ports）与工具面契约的形式，
固化「中立 AI 运行期」的全部接缝——会话端口、通道抽象、审批状态机接口、声明契约族、工具面引导契约。
本包只含接口 / record / 枚举 / 特性，不含任何实现机制（实现由 `Mud.AI.Agent` 提供）。

## 定位

```
消费方（如 Mud.Feishu.AI） ──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
```

- 上游独立发布，**零 `Mud.Feishu*` 依赖**；产品侧只经 NuGet 包引用并显式钉版。
- v2.3 自飞书 AI 族与组件仓 `Mud.HttpUtils` 中立化迁入。
- 与产品侧同名同形的类型（如 `ConversationScope`、`ISessionStore` vs `IConversationStore`）是**刻意接缝**：
  形状相同、生命周期与归属不同 ⇒ 不可合并，由架构守卫测试机械锁定双向引用禁令。

## 工程事实

| 项 | 值 |
| --- | --- |
| 目标框架 | `netstandard2.0;net6.0;net8.0;net10.0` |
| 语言 / 可空 | C# 13.0 / `Nullable` enable / `ImplicitUsings` enable |
| 包依赖 | 仅 `Microsoft.Extensions.*.Abstractions`（DI / Logging / Configuration.Binder / Options，net8+ 另加 Hosting.Abstractions）10.0.12；netstandard2.0 额外 `System.Text.Json` 10.0.12 |
| AOT | net8.0+ 上 `IsAotCompatible=true`、`EnableAotAnalyzer=true`、`TrimMode=full`（ns2.0/net6.0 按 TFM 精确豁免 AOT006） |
| 文档 | `GenerateDocumentationFile=true`，全量 XML 注释进 nupkg |

`netstandard2.0` 的 record / init 访问器支撑类型 `IsExternalInit` 由 `IsExternalInitPolyfill.cs`
在 `#if NETSTANDARD2_0` 下以 **internal** 注入（不与消费方编译单元内的同类 polyfill 冲突）。

## 命名空间布局

一级命名空间与文件夹一一对应；**唯一例外**是 `ToolProfile/` 文件夹——「命名空间即契约」：

| 文件夹 | 命名空间 | 内容 |
| --- | --- | --- |
| `Approvals/` | `Mud.AI.Abstractions.Approvals` | HITL 人工审批端口族 |
| `Conversations/` | `Mud.AI.Abstractions.Conversations` | 会话端口族 |
| `Context/` | `Mud.AI.Abstractions.Context` | 上下文装配插件契约 |
| `Channels/` | `Mud.AI.Abstractions.Channels` | 流式通道抽象 |
| `Configuration/` | `Mud.AI.Abstractions.Configuration` | 中立运行期配置 |
| `Observability/` | `Mud.AI.Abstractions.Observability` | 中立遥测端口 |
| `Media/` | `Mud.AI.Abstractions.Media` | 媒体转写契约 |
| `Knowledge/` | `Mud.AI.Abstractions.Knowledge` | 知识 / RAG 宿主契约 |
| `Tools/` | `Mud.AI.Abstractions.Tools`（含 `.Tools.Handlers` / `.Tools.Errors`） | 声明契约族 |
| `ToolProfile/` | **`Mud.AI`** / **`Mud.AI.Attributes`** | 工具面引导契约（生成器剖面） |

> `ToolProfile/` 下的契约位于 `Mud.AI` / `Mud.AI.Attributes` 而非 `Mud.AI.Abstractions.*`：
> 源生成器 `Mud.AI.Generator` 以**硬编码全名** `Mud.AI.ISdkToolProfile` 与**硬编码命名空间**
> `Mud.AI.Attributes` 做 `GetTypeByMetadataName` 发现。改名须同批修改引擎常量。

## 模块导览

### Approvals — HITL 人工审批（三层分工）

| 类型 | 种类 | 职责 |
| --- | --- | --- |
| `IPendingApprovalStore` | interface | 待确认项宿主存储：`SaveAsync` / `ListPendingAsync` / `FindAsync` / `TryConsumeAsync`（幂等消费，仅首次且未过期返回 true，fail-closed）/ `RemoveAsync` / `RemoveExpiredAsync` |
| `IToolApprovalChannel` | interface | 宿主批准通道：`RequestApprovalAsync` 与 `RequestApprovalFrameworkAsync`（均返回宿主关联 ID，可空） |
| `PendingApprovalSnapshot` | sealed record | 可持久化待确认快照；`static DefaultTtl`（10 分钟）、`IsExpired(now)`、工厂 `FromRequest`（执行链自动登记的唯一入口） |
| `FrameworkToolApprovalRequest` | sealed record | 框架原生审批要素（RequestId / ToolName / ToolCallId / TenantKey / RequiredScopes……），**不含确认令牌** |
| `ToolApprovalRequest` | sealed record | 待确认要素（ToolName / TenantKey / ArgumentsDigest / RequiredScopes / Reason / ExpiresAt） |

核心不变式：**批准状态所有权归宿主授权器，SDK 不签发凭据**；`IPendingApprovalStore` 只登记待确认项、
不持有 / 校验 / 签发批准状态，快照不含凭据；未注册通道 ⇒ 降级纯提示，通道抛异常 ⇒ fail-closed，
**绝不降级为自动批准**。

### Conversations — 会话端口族

| 类型 | 种类 | 职责 |
| --- | --- | --- |
| `ConversationRequest` | sealed record | Agent 管线统一输入：`AppKey, Scope, SubjectId, SenderId, MessageId, MentionedText?, ChatId?, ParentId?, ThreadId?, EventKey?, EventFacts?`。不携带最终 prompt 文本（由 `IContextAssembler` 装配） |
| `ConversationScope` | readonly record struct | 会话维度（群聊按 chat / 单聊按 user）：`ChatType, IsGroup`；工厂 `P2P()` / `Group()` |
| `ISessionStore` | interface | 会话持久化端口（Get / Save / Delete 三方法，零依赖）：载荷为 `AIAgent.SerializeSessionAsync` 产出的会话 JSON **字符串** |

### Context — 上下文装配

| 类型 | 种类 | 职责 |
| --- | --- | --- |
| `IContextAssembler` | interface | 上下文装配插件：`int Order { get; }` + `Task<string?> AssembleAsync(ConversationRequest, CancellationToken)`；单装配器失败被隔离吞为 Warning |
| `EventFact` | readonly record struct | 有序键值事实（生产者→装配器最小载体）；有序列表保证 prompt 顺序确定 |
| `AssemblerBudgets` | static class | 装配预算常量：`MaxFactsPerAssembler=16`、`TruncationMarker="…（内容超预算已截断）"` |
| `AmbientScope<T>` | static class | 产品专属环境量的官方机制：`AsyncLocal` 槽位存 **Scope 句柄** + `Begin(value)` 栈式恢复 + 引用所有权校验（乱序释放不回滚内层）。产品实现 `ITurnScopeFactory` 时只用它，不另写一套恢复语义 |

### Channels — 流式通道抽象

| 类型 | 种类 | 职责 |
| --- | --- | --- |
| `IMessageChannel` | interface | 流式回复通道：`BeginAsync`（建占位）→ `WriteStreamAsync`（增量）→ `FlushAsync`（收尾）。同 messageId 上写/刷必须顺序 await；Singleton 实现的 per-messageId 状态必须在 `FlushAsync` 内清理 |
| `IChannelTargetResolver` | interface | 可选能力：`ResolveStreamTarget(ConversationRequest)`；缺省回退 `request.ChatId` → 群聊 SubjectId |
| `StreamingRequestContext` | static class | 经 `AsyncLocal<ConversationRequest?>` 把会话请求传给通道降级链；`Begin(request)` 返回可嵌套恢复的 `IDisposable` |

### Configuration — 中立运行期配置

| 类型 | 默认值要点 |
| --- | --- |
| `AgentRuntimeOptions` | `MaxHistoryMessages=50`、`SummaryThreshold=30`、`MaxHistoryTokens=8000`、`MaxStreamChunkLength=200`、`MaxToolCallsPerTurn=10`、`MaxConcurrentTurns=32`、`TurnQueueTimeoutSeconds=30`、`DeferTextUntilTurnComplete`、`ToolRetry`；`Validate()` 兜底校验 |
| `ToolRetryOptions` | `Enabled=true`、`MaxAttempts=3`、`BaseDelayMilliseconds=200`、`MaxDelayMilliseconds=5000`、`Jitter=true`、`AllowWriteRetry=false`（指数退避 + 抖动，尊重 retry_after） |

### Observability — 中立遥测

| 类型 | 职责 |
| --- | --- |
| `IAgentTelemetry` | 埋点与命名面解耦的遥测端口：`StartTurn` / `GateWaited` / `Replayed` / `Streamed` / `LlmDuration` / `Degraded` / `StartSummarize` / `SummarizeCompleted` / `StartRun` / `RunCompleted`。返回 `IDisposable?` 为阶段作用域（null=不采集，丢失零差异）；租户/会话键只进 Span 属性不进 Metrics tag |
| `IAgentToolTelemetry` | **能力拆分**（继承主端口，不加成员以保二进制兼容）：`ToolInvoked` / `ToolRetried` / `RetrievalPerformed` / `ApprovalRequested` / `ApprovalResolved`。调用点 `as` 空安全消费——未实现它的既有遥测零埋点零异常 |
| `NoopAgentTelemetry` | 全空实现（同时实现 `IAgentToolTelemetry`）+ 无状态共享单例 `Instance`（默认零开销） |

### Media — 媒体转写

| 类型 | 职责 |
| --- | --- |
| `IMediaTranscriber` | `Task<MediaTranscription?> TranscribeAsync(MediaTranscriptionRequest, ct)`；未注册降级占位文本；禁止把 base64 / 转写文本写日志 |
| `MediaTranscriptionRequest` | `TenantKey, MessageId, FileKey, MediaKind, FileName?` |
| `MediaTranscription` | `Text, Kind（ocr/stt/metadata）, Truncated` |

### Knowledge — 知识 / RAG 宿主契约

| 类型 | 职责 |
| --- | --- |
| `IKnowledgeBase` | 知识库数据面门面：`Task<KnowledgeAnswer> AskAsync(query, ct)`，宿主不感知托管/自建差异 |
| `IRetriever` | 知识检索面（与数据面正交）：`Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(query, ct)` |
| `RetrievedChunk` | 召回切片：`Text, Source?, Score?, TitlePath?` |
| `KnowledgeAnswer` | `Question, AnswerText?, HasAnswer, Chunks`；静态 `NoAnswer(question)`；`Sources` 生成 `[n] source` 编号回链 |
| `CorpusDocument` | 待索引文档：`SourceToken`（稳定来源标识，溯源唯一凭据）, `Text, Title?, Url?, ScopeKey?` |
| `CorpusChunk` | 语料切片：「回链三件套」+ `ScopeKey` 缺一不可 |
| `ICorpusSource` | 语料来源宿主契约：`PullAsync(ct)`；未注册 ⇒ 索引管线不可用（软缺席纪律） |
| `IVectorStore` | 向量存储宿主契约：`UpsertAsync`（按 SourceToken+Index 幂等）/ `QueryAsync`（**必须按 ScopeKey 隔离**）；SDK 不引向量库依赖，嵌入由宿主负责 |
| `RetrievalBudgets` | 检索注入三级预算闸单一源：`ChunkPreviewLength=500`、`TotalLength=3000`、`MaxChunks=8` |

### Tools — 声明契约族

| 类型 | 目标 | 职责 |
| --- | --- | --- |
| `[AgentTool]` | Interface | 标注工具接口为 LLM FunctionCall 工具：`Name`、`Description`、`RequiredScopes`、`IsWrite`、`Source?`（形如 `"IFeishuTenantV3User.GetBatchUsersAsync"`，源生成器编译期交叉校验，解析失败即构建失败）、`AnyOf`（条件必填组，渲染为 JSON Schema anyOf）。工具默认「进注册表不启用」，须显式 MapTool |
| `[ToolParameter]` | Method / Parameter | 覆盖参数模型侧描述 / 必填性：`Name`、`Description`、`Required`、`EnumType?`（取值闭集：真 enum 或 `static class` + `const int` 均可，渲染 `"enum":[…]`） |
| `[ToolHandler]` | Method | 声明「执行器方法 ↔ [AgentTool] 接口」绑定：构造参数 `Type toolInterface`。用 Type 符号链接而非字符串（写错即编译错误、接口改名自动跟随） |
| `ITransportFault` | — | 传输层故障端口：`int? StatusCode { get; }`（刻意只暴露状态码，不含 Retry-After 头），供中立错误分类器判定 429/5xx |

三者必须**分处不同命名空间**（引擎靠 (命名空间, 特性名) 二元组区分声明面与执行器绑定面）。

### ToolProfile — 工具面引导契约（`Mud.AI` / `Mud.AI.Attributes`）

供 `Mud.AI.Generator` 引擎按剖面驱动发现，「类型即剖面」的编译期声明：

| 类型 | 职责 |
| --- | --- |
| `ISdkToolProfile` | 空标记接口；引擎以硬编码全名 `Mud.AI.ISdkToolProfile` 发现；必须与 `[SdkToolProfile]` 成对出现（诊断 SDKT001 强制） |
| `[SdkToolProfile]` | 剖面数据载体：约 25 个编译期命名事实常量槽（识别面 / 源符号解析 / 令牌推导 / 产物 / 诊断 / 契约产物 / 执行器 / 开关与资产）。构造参数 `name`（SDK 名，编译内唯一） |
| `SdkTokenKind` / `SdkTokenKindContract` | 令牌身份枚举 `Unspecified/Tenant/User/ThirdParty/Internal/Both` 及其字面量（`third_party` 等）单源；渲染进 golden 快照，是逐字节契约 |
| `TokenKindDerivationStrategy` | 令牌身份推导策略：`NamePrefix`（飞书 `IFeishuTenant`）/ `SuffixMarker`（微信 `…_Provider`）/ `InfixMarker` |
| `InterfaceIdentity` | 接口名解析结果值对象：`InterfaceName, Kind, Domain, Resource, Version?, RawTokenMarker`；仅携带归一化语义，不带 SDK 命名前缀 |

契约不变量：**引擎永不内联任何 SDK 命名前缀字符串，命名差异全部由各 SDK 剖面吸收**。

## 设计不变量（全包统一纪律）

1. **软缺席语义**：未注册实现 ⇒ 优雅降级（`IPendingApprovalStore` → 只通知不落库；`IMediaTranscriber` → 占位文本；
   `ICorpusSource` → 索引管线不可用），绝不 fail。
2. **fail-closed**：审批相关一律「缺省即拒绝」——消费快照 ≠ 放行，过期批准绝不生效。
3. **命名空间即契约**：剖面契约的命名空间 / 全名由生成器硬编码依赖，属跨仓机械契约。
4. **中立接缝不可合并**：与产品侧同名类型是刻意接缝，双向引用禁令由守卫测试锁定。
5. **零实现机制**：本包不含执行逻辑；一切行为实现归 `Mud.AI.Agent`。

## 快速开始

```csharp
// 宿主侧实现契约（以会话存储为例；实现默认件见 Mud.AI.Agent）
services.AddSingleton<ISessionStore, MyRedisSessionStore>();

// 声明工具面（供 Mud.AI.Generator 编译期发现）
[AgentTool(Name = "get_user", Description = "查询用户信息", RequiredScopes = "user:read")]
public interface IUserQueryTool
{
    [ToolParameter(Description = "用户 ID")]
    Task<string> GetUserAsync(string userId);
}
```

## 相关包

- [`Mud.AI.Agent`](../Mud.AI.Agent/README.md) — 中立运行期：上述端口的实现与机制。
- [`Mud.AI.Generator`](../Mud.AI.Generator/README.md) — 工具面源生成引擎：消费本包的声明契约族与剖面契约。

## 许可证

MIT，见仓库根目录 [`LICENSE-MIT`](../LICENSE-MIT)。
