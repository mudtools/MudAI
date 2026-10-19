// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using Mud.AI.Abstractions.Approvals;
using Mud.AI.Abstractions.Context;
using Mud.AI.Abstractions.Observability;
using Mud.AI.Agent.Events;

namespace Mud.AI.Agent.Tests.Approvals;

/// <summary>
/// <see cref="AgentApprovalContinuation"/> 的行为用例（作用域恢复 + HITL 埋点）。
/// </summary>
/// <remarks>
/// 本仓首个覆盖：种子会话（框架待审批记录经 StateBag 注入）+ 脚本化 Agent 替身，
/// 断言续跑全程的作用域时序、环境量可见性与遥测埋点。
/// </remarks>
public class AgentApprovalContinuationTests
{
    private const string HistoryStateKey = "test.history";
    private const string RequestId = "req-1";

    private sealed class TestSession : AgentSession
    {
    }

    /// <summary>脚本化 Agent 替身：记录调用序号、捕获续跑期的环境量、按脚本产出审批请求。</summary>
    private sealed class StubAgent : AIAgent
    {
        public List<string> Trace { get; } = [];
        public string? AmbientDuringRun;

        /// <summary>续跑轮要产出的新审批请求（空 = 本轮无新审批）。</summary>
        public List<ToolApprovalRequestContent> NewApprovals { get; } = [];

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new TestSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(new TestSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            Trace.Add("model");
            AmbientDuringRun = AmbientScope<string>.Current;

            if (NewApprovals.Count > 0)
            {
                return Task.FromResult(new AgentResponse(
                    new ChatMessage(ChatRole.Assistant, [.. NewApprovals.Cast<AIContent>()])));
            }

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "续跑回答")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentResponseUpdate(ChatRole.Assistant, "续跑回答");
        }
    }

    /// <summary>会话门面替身：单会话 + 取/存计数。</summary>
    private sealed class StubSessions : IAgentSessionFacade
    {
        public AgentSession Session { get; } = new TestSession();
        public int GetCount { get; private set; }
        public int SaveCount { get; private set; }

        public string HistoryStateKey => "test.history";

        public Task<AgentSession> GetOrCreateSessionAsync(string conversationKey, CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(Session);
        }

        public Task SaveSessionAsync(string conversationKey, AgentSession session, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>作用域端口替身：记录收到的上下文。</summary>
    private sealed class StubScopeFactory : ITurnScopeFactory
    {
        public List<TurnScopeContext> Contexts { get; } = [];

        public IDisposable BeginToolScope(TurnScopeContext context)
        {
            Contexts.Add(context);
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>记录型工具遥测替身。</summary>
    private sealed class RecordingToolTelemetry : IAgentToolTelemetry
    {
        public List<string> ApprovalRequestedTools { get; } = [];

        public void ToolInvoked(string toolName, bool isWrite, long durationMs, bool truncated, string? errorCategory)
        {
        }

        public void ToolRetried(string toolName, int attempt)
        {
        }

        public void RetrievalPerformed(int chunks, int injectedChars)
        {
        }

        public void ApprovalRequested(string toolName) => ApprovalRequestedTools.Add(toolName);

        public void ApprovalResolved(string toolName, bool approved, long waitMs)
        {
        }

        public IDisposable? StartTurn(string conversationKey, string? tenantKey) => null;
        public void GateWaited(IDisposable? turn, double milliseconds)
        {
        }

        public void Replayed(IDisposable? turn)
        {
        }

        public void Streamed(IDisposable? turn)
        {
        }

        public void LlmDuration(string agentName, long milliseconds)
        {
        }

        public void Degraded(string reason)
        {
        }

        public IDisposable? StartSummarize(int summarizedCount, int retainedCount) => null;
        public void SummarizeCompleted(IDisposable? scope, bool converged)
        {
        }

        public IDisposable? StartRun(string agentName, bool streaming) => null;
        public void RunCompleted(IDisposable? scope, bool failed, long? inputTokens, long? outputTokens, long? totalTokens)
        {
        }
    }

    private static FrameworkToolApprovalRequest NewApproval()
        => new(
            RequestId: RequestId,
            ToolName: "demo.write",
            ToolCallId: "call-1",
            TenantKey: "app-1",
            UserId: "user-1",
            ConversationKey: "conv-1",
            ArgumentsDigest: null,
            RequiredScopes: [],
            ChatId: "chat-1");

    /// <summary>续跑轮产出的新审批请求（工具名可区分，便于断言逐项上报）。</summary>
    private static ToolApprovalRequestContent NewPending(string requestId, string toolName)
        => new(requestId, new FunctionCallContent($"call-{requestId}", toolName, new Dictionary<string, object?> { ["x"] = 1 }));

    private static void SeedPendingApproval(AgentSession session)
    {
        session.StateBag.SetValue(
            PendingApprovalState.FrameworkStateKey,
            new List<ToolApprovalRequestContent>
            {
                new(RequestId, new FunctionCallContent("call-1", "demo.write", new Dictionary<string, object?> { ["x"] = 1 })),
            },
            null);
    }

    // ────────────────────────────────────────────────────────────────────
    // 环境量恢复（restoreScope）
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithRestoreScope_ShouldEstablishBeforeModelCall_AndCoverContinuation()
    {
        var agent = new StubAgent();
        var sessions = new StubSessions();
        var scopeFactory = new StubScopeFactory();
        SeedPendingApproval(sessions.Session);

        TurnScopeContext? restoreContext = null;
        var response = await AgentApprovalContinuation.RunAsync(
            agent,
            sessions,
            scopeFactory,
            NewApproval(),
            approved: true,
            restoreScope: context =>
            {
                restoreContext = context;
                agent.Trace.Add("restore:begin");
                var ambient = AmbientScope<string>.Begin("corp-1");
                return new RestoreScope(agent.Trace, ambient);
            });

        response.Messages.Should().ContainSingle();
        restoreContext.Should().NotBeNull();
        var restored = restoreContext.Value;
        restored.TenantKey.Should().Be("app-1", "恢复委托收到与工具作用域同源的上下文要素");
        restored.ConversationKey.Should().Be("conv-1");

        agent.Trace.Should().Equal(new[] { "restore:begin", "model", "restore:end" },
            "恢复的作用域必须先于模型调用建立、在续跑全程之后释放");
        agent.AmbientDuringRun.Should().Be("corp-1", "恢复的产品环境量在模型调用期可读（覆盖续跑全程）");
        sessions.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WithoutRestoreScope_ShouldBehaveAsBefore()
    {
        var agent = new StubAgent();
        var sessions = new StubSessions();
        var scopeFactory = new StubScopeFactory();
        SeedPendingApproval(sessions.Session);

        var response = await AgentApprovalContinuation.RunAsync(
            agent,
            sessions,
            scopeFactory,
            NewApproval(),
            approved: false,
            reason: "用户拒绝");

        agent.AmbientDuringRun.Should().BeNull("不传恢复委托时无环境量（同现状）");
        agent.Trace.Should().Equal(new[] { "model" });
        sessions.SaveCount.Should().Be(1, "拒绝同样走完整流程（拒绝结果回填模型）");
        scopeFactory.Contexts.Should().ContainSingle("工具作用域照常重建");
    }

    // ────────────────────────────────────────────────────────────────────
    // HITL 入口埋点
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ContinuationProducesNewApproval_ShouldReportApprovalRequested()
    {
        var agent = new StubAgent();
        agent.NewApprovals.Add(NewPending("req-2", "demo.write"));
        var sessions = new StubSessions();
        var scopeFactory = new StubScopeFactory();
        var telemetry = new RecordingToolTelemetry();
        SeedPendingApproval(sessions.Session);

        await AgentApprovalContinuation.RunAsync(
            agent,
            sessions,
            scopeFactory,
            NewApproval(),
            approved: true,
            telemetry: telemetry);

        telemetry.ApprovalRequestedTools.Should().ContainSingle().Which.Should().Be("demo.write",
            "批准引出新审批是多级审批链的可观测点");
    }

    [Fact]
    public async Task RunAsync_MultipleNewApprovals_ShouldReportEachTool()
    {
        var agent = new StubAgent();
        agent.NewApprovals.Add(NewPending("req-2", "demo.write"));
        agent.NewApprovals.Add(NewPending("req-3", "demo.delete"));
        var sessions = new StubSessions();
        var scopeFactory = new StubScopeFactory();
        var telemetry = new RecordingToolTelemetry();
        SeedPendingApproval(sessions.Session);

        await AgentApprovalContinuation.RunAsync(
            agent,
            sessions,
            scopeFactory,
            NewApproval(),
            approved: true,
            telemetry: telemetry);

        telemetry.ApprovalRequestedTools.Should().Equal(
            new[] { "demo.write", "demo.delete" },
            "一次批准引出多个待确认写工具时逐项上报（按各自工具名，不得只记首个或复用旧审批的工具名）");
    }

    [Fact]
    public async Task RunAsync_NoNewApproval_ShouldNotReportApprovalRequested()
    {
        var agent = new StubAgent();
        var sessions = new StubSessions();
        var scopeFactory = new StubScopeFactory();
        var telemetry = new RecordingToolTelemetry();
        SeedPendingApproval(sessions.Session);

        await AgentApprovalContinuation.RunAsync(
            agent,
            sessions,
            scopeFactory,
            NewApproval(),
            approved: true,
            telemetry: telemetry);

        telemetry.ApprovalRequestedTools.Should().BeEmpty("无新审批即无入口埋点（不得虚报）");
    }

    private sealed class RestoreScope(List<string> trace, IDisposable inner) : IDisposable
    {
        public void Dispose()
        {
            inner.Dispose();
            trace.Add("restore:end");
        }
    }
}
