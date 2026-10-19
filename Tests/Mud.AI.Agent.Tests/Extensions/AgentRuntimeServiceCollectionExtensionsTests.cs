// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mud.AI.Abstractions.Approvals;
using Mud.AI.Abstractions.Channels;
using Mud.AI.Abstractions.Configuration;
using Mud.AI.Abstractions.Context;
using Mud.AI.Abstractions.Conversations;
using Mud.AI.Abstractions.Observability;
using Mud.AI.Agent.Events;
using Mud.AI.Agent.Extensions;

namespace Mud.AI.Agent.Tests.Extensions;

/// <summary>
/// <see cref="AgentRuntimeServiceCollectionExtensions"/> 的装配语义用例：
/// 容器解析、TryAdd 优先级、null 守卫、选项机械映射。
/// </summary>
public class AgentRuntimeServiceCollectionExtensionsTests
{
    private sealed class TestSession : AgentSession
    {
    }

    private sealed class StubAgent : AIAgent
    {
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
            => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentResponseUpdate(ChatRole.Assistant, "ok");
        }
    }

    private sealed class StubSessions : IAgentSessionFacade
    {
        public string HistoryStateKey => "test.history";

        public Task<AgentSession> GetOrCreateSessionAsync(string conversationKey, CancellationToken cancellationToken = default)
            => Task.FromResult<AgentSession>(new TestSession());

        public Task SaveSessionAsync(string conversationKey, AgentSession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static ServiceProvider NewProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StubAgent());
        services.AddSingleton(new StubSessions());
        services.AddSingleton(new AgentRuntimeOptions());
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddAgentRuntimeCore_ShouldRegisterTheThreeDefaults()
    {
        using var provider = NewProvider(static services => services.AddAgentRuntimeCore());

        provider.GetService<IAgentTelemetry>().Should().BeOfType<NoopAgentTelemetry>();
        provider.GetService<ISessionStore>().Should().BeOfType<MemorySessionStore>();
        provider.GetService<IPendingApprovalStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddConversationalTurnPipeline_ShouldResolvePipelineWithContainerDependencies()
    {
        using var provider = NewProvider(services => services
            .AddAgentRuntimeCore()
            .AddAgentSessionStore(new StubAgent(), "test.history")
            .AddConversationalTurnPipeline(
                new StubAgent(),
                new StubSessions(),
                request => $"{request.AppKey}|{request.SubjectId}"));

        var pipeline = provider.GetService<ConversationalTurnPipeline>();

        pipeline.Should().NotBeNull("主体机制经扩展方法装配后必须可从容器解析");
        provider.GetService<ConversationalTurnPipeline>().Should().BeSameAs(pipeline, "单例语义");
    }

    [Fact]
    public void HostRegisteredImplementations_ShouldWinOverDefaults()
    {
        var customTelemetry = new StubTelemetry();
        var customStore = new RecordingSessionStore();

        using var provider = NewProvider(services => services
            .AddSingleton<IAgentTelemetry>(customTelemetry)
            .AddSingleton<ISessionStore>(customStore)
            .AddAgentRuntimeCore());

        provider.GetService<IAgentTelemetry>().Should().BeSameAs(customTelemetry,
            "先注册者生效——产品的薄壳与宿主自定义实现覆盖默认件");
        provider.GetService<ISessionStore>().Should().BeSameAs(customStore);
    }

    [Fact]
    public async Task AddAgentSessionStore_ShouldResolveFacade_AndShareHostStore()
    {
        var customStore = new RecordingSessionStore();

        using var provider = NewProvider(services => services
            .AddAgentRuntimeCore()
            .AddSingleton<ISessionStore>(customStore)
            .AddAgentSessionStore(new StubAgent(), "test.history"));

        var facade = provider.GetService<IAgentSessionFacade>();

        facade.Should().BeOfType<AgentSessionStoreFacade>();
        facade!.HistoryStateKey.Should().Be("test.history", "历史状态键经门面转述（端口方法名与存储实现解耦）");

        // 门面必须真的走宿主注册的存储（不是自己另造一个内存件）：读与写各打一次。
        var session = await facade.GetOrCreateSessionAsync("conv-1");
        await facade.SaveSessionAsync("conv-1", session);

        customStore.Reads.Should().Be(1, "取会话经宿主 ISessionStore");
        customStore.Writes.Should().Be(1, "存会话经宿主 ISessionStore");
    }

    [Fact]
    public void AddConversationalTurnPipeline_NullSessions_ShouldResolveFromContainer()
    {
        using var provider = NewProvider(services => services
            .AddAgentRuntimeCore()
            .AddAgentSessionStore(new StubAgent(), "test.history")
            .AddConversationalTurnPipeline(
                new StubAgent(),
                sessions: null,
                request => $"{request.AppKey}|{request.SubjectId}"));

        provider.GetService<ConversationalTurnPipeline>().Should().NotBeNull(
            "官方装配路径必须可组合：sessions 省略即取容器内 AddAgentSessionStore 注册的门面");
    }

    [Fact]
    public void AddConversationalTurnPipeline_NullSessionsWithoutRegistration_ShouldThrowOnResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StubAgent());
        services.AddSingleton(new AgentRuntimeOptions());

        using var provider = services
            .AddConversationalTurnPipeline(new StubAgent(), sessions: null, _ => "k")
            .BuildServiceProvider();

        // 门面缺位是装配缺陷：解析期抛错（注册期无法从容器取实例，故不在此处静默造内存门面）。
        var act = () => provider.GetService<ConversationalTurnPipeline>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConcurrencyLimiter_ShouldMapRuntimeOptions()
    {
        using var provider = NewProvider(services => services
            .AddSingleton(new AgentRuntimeOptions { MaxConcurrentTurns = 7, TurnQueueTimeoutSeconds = 11 })
            .AddConcurrencyLimiter());

        var limiter = provider.GetService<ConcurrencyLimiter>();

        limiter.Should().NotBeNull();
        limiter!.MaxConcurrency.Should().Be(7, "并发上限机械映射 AgentRuntimeOptions.MaxConcurrentTurns（单一阈值源）");
        provider.GetService<KeyedAsyncGate>().Should().NotBeNull("同键串行闸门随削峰机制一并注册");
    }

    [Fact]
    public void AddToolRetryPolicy_ShouldMapRuntimeOptions()
    {
        using var provider = NewProvider(services => services
            .AddSingleton(new AgentRuntimeOptions
            {
                ToolRetry = new ToolRetryOptions { Enabled = true, MaxAttempts = 5 },
            })
            .AddToolRetryPolicy());

        var policy = provider.GetService<ToolRetryPolicy>();

        policy.Should().NotBeNull();
        policy!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddConcurrencyLimiter_WithoutOptions_ShouldFailFast()
    {
        var services = new ServiceCollection();
        services.AddConcurrencyLimiter();
        using var provider = services.BuildServiceProvider();

        // 缺 AgentRuntimeOptions 是装配缺陷：fail-fast，不静默取默认值。
        var act = () => provider.GetService<ConcurrencyLimiter>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddAgentSessionStore_BlankHistoryStateKey_ShouldThrow(string? key)
    {
        var services = new ServiceCollection();
        var agent = new StubAgent();

        var act = () => services.AddAgentSessionStore(agent, key!);

        act.Should().Throw<ArgumentException>("历史状态键是产品事实，急切失败（留到解析期才抛会与根因脱钩）");
    }

    [Fact]
    public void Extensions_NullServices_ShouldThrow()
    {
        IServiceCollection? services = null;

        var act1 = () => services!.AddAgentRuntimeCore();
        var act2 = () => services!.AddAgentSessionStore(new StubAgent(), "test.history");
        var act3 = () => services!.AddConcurrencyLimiter();
        var act4 = () => services!.AddToolRetryPolicy();
        var act5 = () => services!.AddConversationalTurnPipeline(new StubAgent(), new StubSessions(), _ => "k");

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
        act5.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddConversationalTurnPipeline_NullArguments_ShouldThrow()
    {
        var services = new ServiceCollection();
        var agent = new StubAgent();
        var sessions = new StubSessions();
        Func<ConversationRequest, string> keyBuilder = _ => "k";

        var actAgent = () => services.AddConversationalTurnPipeline(null!, sessions, keyBuilder);
        var actKeyBuilder = () => services.AddConversationalTurnPipeline(agent, sessions, null!);

        actAgent.Should().Throw<ArgumentNullException>();
        actKeyBuilder.Should().Throw<ArgumentNullException>(
            "键治理委托没有容器来源，缺席即装配缺陷（sessions 可空是例外：它由容器提供）");
    }

    private sealed class StubTelemetry : IAgentTelemetry
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

    /// <summary>记录型会话存储替身：读/写各计一次（验证门面确实走宿主注册的实现）。</summary>
    private sealed class RecordingSessionStore : ISessionStore
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<string?>(null);
        }

        public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
