// -----------------------------------------------------------------------
//  作者:Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.Knowledge;

/// <summary>
/// R7 / C5 RAG-B：<see cref="VectorRetriever"/> 的预算裁剪与短路语义、
/// <see cref="CorpusIndexer"/> 的编排语义。
/// </summary>
/// <remarks>
/// <para>
/// N2-e：随类型自 <c>Mud.Feishu.AI.Tests</c> 迁入中性包自带的测试工程（Q1「测试随类型走」）。
/// 产品 DI 装配面（<c>AddFeishuVectorKnowledge</c> 的"先注册者生效"互斥口径）用例留在
/// 飞书侧测试工程——它断言的是产品装配入口，不是中立机制。
/// </para>
/// <para>
/// <b>为什么"空查询不触达向量库"也要断言</b>：那是省一次网络往返的优化，但同时是<b>安全语义</b>
/// ——空向量在多数向量库里"相似度"无意义，容易召回一批噪声并把它们当引用喂给模型。
/// </para>
/// </remarks>
public class VectorRetrieverTests
{
    private static Mock<IVectorStore> Store(params RetrievedChunk[] hits)
    {
        var store = new Mock<IVectorStore>();
        store
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);
        return store;
    }

    /// <summary>记录型工具遥测替身（实现新端口）。</summary>
    private sealed class RecordingToolTelemetry : IAgentToolTelemetry
    {
        public List<(int Chunks, int InjectedChars)> Retrievals { get; } = [];

        public void ToolInvoked(string toolName, bool isWrite, long durationMs, bool truncated, string? errorCategory)
        {
        }

        public void ToolRetried(string toolName, int attempt)
        {
        }

        public void RetrievalPerformed(int chunks, int injectedChars) => Retrievals.Add((chunks, injectedChars));

        public void ApprovalRequested(string toolName)
        {
        }

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RetrieveAsync_Should_SkipStore_ForBlankQuery(string? query)
    {
        var store = Store(new RetrievedChunk("不该被召回的内容"));
        var retriever = new VectorRetriever(store.Object);

        var results = await retriever.RetrieveAsync(query!);

        results.Should().BeEmpty();
        store.Verify(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "空查询不得触达向量库（省往返 + 避免空向量召回噪声）");
    }

    [Fact]
    public async Task RetrieveAsync_Should_PassTopK_AndBoundResultCount()
    {
        var hits = Enumerable.Range(0, 10)
            .Select(i => new RetrievedChunk($"命中{i}的内容，足够长以通过长度闸。"))
            .ToArray();
        var store = Store(hits);

        var results = await new VectorRetriever(store.Object, topK: 3).RetrieveAsync("问题");

        store.Verify(s => s.QueryAsync("问题", 3, It.IsAny<CancellationToken>()), Times.Once,
            "召回条数必须按检索器配置透传（不是硬编码）");
        results.Should().HaveCount(3, "返回条数必须受 topK 约束（否则向量库返回多少就注入多少）");
    }

    [Fact]
    public async Task RetrieveAsync_Should_TruncateChunk_AndBoundTotalLength()
    {
        var hits = Enumerable.Range(0, 10)
            .Select(i => new RetrievedChunk(new string('字', 100)))
            .ToArray();
        var store = Store(hits);

        var results = await new VectorRetriever(store.Object, topK: 10, maxChunkLength: 30, maxTotalLength: 100)
            .RetrieveAsync("问题");

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(c => c.Text.Length <= 31, "单条必须截断（+1 是省略号位）");
        results.Sum(c => c.Text.Length).Should().BeLessThanOrEqualTo(130, "总长度闸必须生效（留出截断余量）");
    }

    [Fact]
    public async Task RetrieveAsync_Should_SkipBlankHits_AndPreserveBacklink()
    {
        var store = Store(
            new RetrievedChunk("   "),
            new RetrievedChunk("有效内容", "https://wiki/n/1", 0.82, "手册 / 部署"));

        var results = await new VectorRetriever(store.Object).RetrieveAsync("问题");

        results.Should().ContainSingle("空白命中必须跳过（否则引用编号与内容错位）");
        results[0].Source.Should().Be("https://wiki/n/1");
        results[0].TitlePath.Should().Be("手册 / 部署", "回链字段必须原样透传（截断不得牵连它们）");
        results[0].Score.Should().Be(0.82);
    }

    [Fact]
    public async Task RetrieveAsync_Should_ReturnEmpty_WhenStoreHasNoHit()
    {
        var results = await new VectorRetriever(Store().Object).RetrieveAsync("问题");

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Should_Reject_InvalidThresholds(int topK)
    {
        var act = () => new VectorRetriever(Store().Object, topK);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_Reject_NullStore()
        => ((Action)(() => new VectorRetriever(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task RetrieveAsync_Should_ReportRetrievalPerformed_WhenToolTelemetryOptsIn()
    {
        var telemetry = new RecordingToolTelemetry();
        var hits = new[]
        {
            new RetrievedChunk("命中内容一，足够长以通过长度闸。"),
            new RetrievedChunk("命中内容二，足够长以通过长度闸。"),
        };

        var results = await new VectorRetriever(Store(hits).Object, topK: 5, telemetry: telemetry)
            .RetrieveAsync("问题");

        results.Should().HaveCount(2);
        telemetry.Retrievals.Should().ContainSingle("每次检索恰好一次埋点");
        telemetry.Retrievals.Single().Chunks.Should().Be(2, "召回条数进埋点");
        telemetry.Retrievals.Single().InjectedChars.Should().Be(results.Sum(c => c.Text.Length),
            "注入字符数进埋点（三级闸全拦截时为 0——静默零知识因此可观测）");
    }

    [Fact]
    public async Task RetrieveAsync_ZeroHits_ShouldStillReportRetrievalPerformedWithZero()
    {
        var telemetry = new RecordingToolTelemetry();

        var results = await new VectorRetriever(Store().Object, telemetry: telemetry).RetrieveAsync("问题");

        results.Should().BeEmpty();
        telemetry.Retrievals.Should().ContainSingle(
            "已查询但零命中同样是「检索完成」样本——漏报会让「向量库配错 ⇒ 永远零知识」在指标上完全隐形");
        telemetry.Retrievals.Single().Chunks.Should().Be(0);
        telemetry.Retrievals.Single().InjectedChars.Should().Be(0);
    }

    [Fact]
    public async Task RetrieveAsync_LegacyTelemetryWithoutNewInterface_ShouldBeSilentAndSafe()
    {
        var store = Store(new RetrievedChunk("命中内容，足够长以通过长度闸。"));

        var results = await new VectorRetriever(store.Object, telemetry: new LegacyTelemetry()).RetrieveAsync("问题");

        results.Should().ContainSingle("未 opt-in 新端口的旧替身：零埋点、零异常、行为不变");
    }

    // ────────── 索引管线 ──────────

    [Fact]
    public async Task IndexAsync_Should_PullChunkAndUpsert()
    {
        var source = new Mock<ICorpusSource>();
        source.Setup(s => s.PullAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CorpusDocument("node_1", "第一段正文内容，足够长以通过阈值。\n\n第二段正文内容，足够长。", "标题", "https://wiki/1", "co_1")]);

        var store = new Mock<IVectorStore>();
        IReadOnlyList<CorpusChunk>? written = null;
        store.Setup(s => s.UpsertAsync(It.IsAny<IReadOnlyList<CorpusChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<CorpusChunk>, CancellationToken>((chunks, _) => written = chunks)
            .Returns(Task.CompletedTask);

        var count = await new CorpusIndexer(source.Object, store.Object, new ChunkingOptions { MinChunkLength = 1 })
            .IndexAsync();

        count.Should().BeGreaterThan(0);
        written.Should().NotBeNull();
        written!.Should().OnlyContain(c => c.SourceToken == "node_1" && c.ScopeKey == "co_1");
    }

    [Fact]
    public async Task IndexAsync_Should_SkipUpsert_WhenCorpusIsEmpty()
    {
        var source = new Mock<ICorpusSource>();
        source.Setup(s => s.PullAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var store = new Mock<IVectorStore>();
        var count = await new CorpusIndexer(source.Object, store.Object).IndexAsync();

        count.Should().Be(0);
        store.Verify(s => s.UpsertAsync(It.IsAny<IReadOnlyList<CorpusChunk>>(), It.IsAny<CancellationToken>()), Times.Never,
            "空语料不写库（否则会触发一次「把所有旧向量标记为陈旧」的无效写入）");
    }

    [Fact]
    public async Task IndexAsync_Should_SkipDocuments_WithoutSourceToken()
    {
        var source = new Mock<ICorpusSource>();
        source.Setup(s => s.PullAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CorpusDocument(" ", "无来源标识的正文内容，足够长。")]);

        var store = new Mock<IVectorStore>();
        var count = await new CorpusIndexer(source.Object, store.Object).IndexAsync();

        count.Should().Be(0, "不可回链的文档不得进入索引");
        store.Verify(s => s.UpsertAsync(It.IsAny<IReadOnlyList<CorpusChunk>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
