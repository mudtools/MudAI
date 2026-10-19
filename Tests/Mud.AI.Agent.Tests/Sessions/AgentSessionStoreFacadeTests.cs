// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.AI.Agent.Tests.Sessions;

/// <summary>
/// <see cref="AgentSessionStoreFacade"/> 的薄委派语义：端口方法名与存储实现的桥接、
/// 历史状态键转述、空键急切失败。
/// </summary>
public class AgentSessionStoreFacadeTests
{
    private sealed class TestSession : AgentSession
    {
    }

    private sealed class StubAgent : AIAgent
    {
        public int CreateCount { get; private set; }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return new(new TestSession());
        }

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

    [Fact]
    public async Task GetOrCreateSessionAsync_ShouldDelegateToStore()
    {
        var facade = new AgentSessionStoreFacade(new StubAgent(), store: null, "test.history");

        var first = await facade.GetOrCreateSessionAsync("conv-1");
        var second = await facade.GetOrCreateSessionAsync("conv-1");

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second.Should().NotBeSameAs(first, "无持久化存储时每次 miss 都重建（内存会话语义）");
    }

    [Fact]
    public async Task SaveSessionAsync_WithoutStore_ShouldBeNoOp()
    {
        var facade = new AgentSessionStoreFacade(new StubAgent(), store: null, "test.history");
        var session = await facade.GetOrCreateSessionAsync("conv-1");

        // 无存储 ⇒ 保存是 no-op（不抛、不落任何东西）。
        await facade.SaveSessionAsync("conv-1", session);
    }

    [Fact]
    public void HistoryStateKey_ShouldBeTranscribedFromConstructor()
    {
        var facade = new AgentSessionStoreFacade(new StubAgent(), store: null, "test.history");

        facade.HistoryStateKey.Should().Be("test.history", "端口属性由构造注入的产品事实转述（不硬编码）");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankHistoryStateKey_ShouldThrow(string? key)
    {
        var act = () => new AgentSessionStoreFacade(new StubAgent(), store: null, key!);

        act.Should().Throw<ArgumentException>("空键 = 状态袋锚点缺失，构造期即暴露");
    }

    [Fact]
    public void NullCodec_ShouldThrow()
    {
        var act = () => new AgentSessionStoreFacade(null!, store: null, "test.history");

        act.Should().Throw<ArgumentNullException>();
    }
}
