# 更新日志

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本规范](https://semver.org/lang/zh-CN/)。

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

[1.0.0]: https://github.com/mudtools/Mud.AI/releases/tag/v1.0.0
