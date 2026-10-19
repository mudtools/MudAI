// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.AI.Abstractions.Observability;

namespace Mud.AI.Agent.Tests.Tools;

/// <summary>
/// <see cref="ToolExecutor.RunAsync"/> 的取消传播与工具调用埋点用例。
/// </summary>
/// <remarks>
/// <b>时长不做上界断言</b>：并发负载下墙钟必抖动——只断言"埋点发生 + 参数形态"（既有计时纪律）。
/// </remarks>
public class ToolExecutorRunAsyncTests
{
    /// <summary>记录型工具遥测替身（实现新端口）。</summary>
    private sealed class RecordingToolTelemetry : IAgentToolTelemetry
    {
        public List<(string Tool, bool IsWrite, long DurationMs, bool Truncated, string? ErrorCategory)> Invoked { get; } = [];

        public void ToolInvoked(string toolName, bool isWrite, long durationMs, bool truncated, string? errorCategory)
            => Invoked.Add((toolName, isWrite, durationMs, truncated, errorCategory));

        public void ToolRetried(string toolName, int attempt)
        {
        }

        public void RetrievalPerformed(int chunks, int injectedChars)
        {
        }

        public void ApprovalRequested(string toolName)
        {
        }

        public void ApprovalResolved(string toolName, bool approved, long waitMs)
        {
        }

        // 主端口成员（本用例不关心，空实现即可）。
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

    /// <summary>旧替身：只实现主端口（未 opt-in 新端口）。</summary>
    private sealed class LegacyTelemetry : IAgentTelemetry
    {
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

    // ────────────────────────────────────────────────────────────────────
    // 取消传播
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancellationInBody_ShouldPropagateOperationCanceled()
    {
        var executor = new ToolExecutor("demo.tool");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // 取消**不得**被转换为结构化错误（它不是"模型可自我修正的失败"）。
        var act = async () => await executor.RunAsync(
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                return ToolResult.FromText("不该到达");
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_Body_ShouldReceiveTheToken()
    {
        var executor = new ToolExecutor("demo.tool");
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        await executor.RunAsync(
            ct =>
            {
                seen = ct;
                return Task.FromResult(ToolResult.FromText("ok"));
            },
            cts.Token);

        seen.Should().Be(cts.Token, "新重载必须把令牌交给 body（取消断点唯一化）");
    }

    [Fact]
    public async Task RunAsync_LegacyOverload_ShouldDelegateAndBehaveIdentically()
    {
        var executor = new ToolExecutor("demo.tool");

        var ok = await executor.RunAsync(() => Task.FromResult(ToolResult.FromText("legacy")));

        ok.ToString().Should().Be("legacy", "旧重载行为逐字节不变（闭包自行捕获令牌的调用方无感）");

        var failure = await executor.RunAsync(() => throw new ArgumentException("参数非法"));

        failure.Error.Should().NotBeNull("旧重载的异常转换路径经委托保持不变");
        failure.Error!.Category.Should().Be("validation");
    }

    [Fact]
    public async Task RunAsync_ArgumentException_ShouldConvertToStructuredError_WithTelemetry()
    {
        var telemetry = new RecordingToolTelemetry();
        var executor = new ToolExecutor("demo.tool", maxResultLength: 0, telemetry);

        var result = await executor.RunAsync(
            _ => throw new ArgumentException("参数非法"),
            CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("validation");
        telemetry.Invoked.Should().ContainSingle("失败样本不得缺席（否则错误率指标失真）");
        var invoked = telemetry.Invoked.Single();
        invoked.ErrorCategory.Should().Be("validation", "被转换的失败按分类埋点");
        invoked.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_JsonException_ShouldConvertToStructuredError()
    {
        var executor = new ToolExecutor("demo.tool");

        var result = await executor.RunAsync(
            _ => throw new JsonException("不是合法 JSON"),
            CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Subtype.Should().Be("shape_mismatch");
    }

    // ────────────────────────────────────────────────────────────────────
    // ToolInvoked 埋点
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ShouldReportToolInvoked_ForReadTool()
    {
        var telemetry = new RecordingToolTelemetry();
        var executor = new ToolExecutor("demo.read", maxResultLength: 0, telemetry);

        var result = await executor.RunAsync(_ => Task.FromResult(ToolResult.FromText("ok")));

        result.Error.Should().BeNull();
        telemetry.Invoked.Should().ContainSingle();
        var invoked = telemetry.Invoked.Single();
        invoked.Tool.Should().Be("demo.read");
        invoked.IsWrite.Should().BeFalse("预算构造 = 只读工具默认分类");
        invoked.ErrorCategory.Should().BeNull("成功无错误分类");
        invoked.DurationMs.Should().BeGreaterThanOrEqualTo(0, "只断言语义（时长上界在并发负载下必抖动，不断）");
    }

    [Fact]
    public async Task RunAsync_ShouldReportToolInvoked_ForWriteTool_WithoutExplicitFlag()
    {
        var telemetry = new RecordingToolTelemetry();

        // 单参构造（写工具构造）——调用方无需重复声明读写分类。
        var executor = new ToolExecutor("demo.write", telemetry);

        await executor.RunAsync(_ => Task.FromResult(ToolResult.FromText("ok")));

        telemetry.Invoked.Single().IsWrite.Should().BeTrue("写工具构造自动携带读写分类（下游 new 零改动）");
    }

    [Fact]
    public async Task RunAsync_TruncatedResult_ShouldReportTruncatedFlag()
    {
        var telemetry = new RecordingToolTelemetry();
        var executor = new ToolExecutor("demo.read", maxResultLength: 8, telemetry);

        await executor.RunAsync(_ => Task.FromResult(ToolResult.FromText("一二三四五六七八九十", truncated: true)));

        telemetry.Invoked.Single().Truncated.Should().BeTrue("出口截断标记进埋点（监控截断率）");
    }

    [Fact]
    public async Task RunAsync_LegacyTelemetryWithoutNewInterface_ShouldBeSilentAndSafe()
    {
        var telemetry = new LegacyTelemetry();
        var executor = new ToolExecutor("demo.read", maxResultLength: 0, telemetry);

        var result = await executor.RunAsync(_ => Task.FromResult(ToolResult.FromText("ok")));

        result.ToString().Should().Be("ok", "未 opt-in 新端口的旧替身：零埋点、零异常、行为不变");
    }
}
