// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.AI.Abstractions.Conversations;
using Mud.AI.Agent.Events;

namespace Mud.AI.Agent.Sessions;

/// <summary>
/// <see cref="AgentSessionStore"/> 的 <see cref="IAgentSessionFacade"/> 适配（薄委派，零自有逻辑）。
/// </summary>
/// <remarks>
/// <b>为什么需要它</b>：中立回合管线与会话存取端口（<c>GetOrCreateSessionAsync</c>/<c>SaveSessionAsync</c>）对话，
/// 而 <see cref="AgentSessionStore"/> 的方法名（<c>GetOrCreateAsync</c>/<c>SaveAsync</c>）早于该端口存在——不适配，
/// 中立侧就永远没有"开箱可用"的会话门面（产品门面如飞书侧实现继续可用，先注册者生效）。
/// <b>薄的意义</b>：坏值自愈、急切校验、TTL 全部留在 <see cref="AgentSessionStore"/>——本类只改名，
/// 不复制任何语义（复制即两处漂移）。
/// </remarks>
public sealed class AgentSessionStoreFacade : IAgentSessionFacade
{
    private readonly AgentSessionStore _store;

    /// <summary>初始化门面适配。</summary>
    /// <param name="codec">序列化 / 反序列化的归属方（持有 <see cref="AgentSession"/> 的 Agent）。</param>
    /// <param name="store">会话持久化端口；为 <see langword="null"/> 时不持久化（仅内存会话）。</param>
    /// <param name="historyStateKey">会话历史在 Agent 状态袋内的状态键（产品事实，须显式注入）。</param>
    /// <param name="logger">日志（可空）。</param>
    public AgentSessionStoreFacade(
        AIAgent codec,
        ISessionStore? store,
        string historyStateKey,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(historyStateKey))
        {
            // 急切失败：空白键会让状态袋锚点静默错位（读写两个不同键）。
            throw new ArgumentException(
                "历史状态键不能为空白——它是状态袋读写的同源锚点（中立包不得硬编码产品前缀）",
                nameof(historyStateKey));
        }

        _store = new AgentSessionStore(codec, store, historyStateKey, logger);
    }

    /// <inheritdoc />
    public string HistoryStateKey => _store.HistoryStateKey;

    /// <inheritdoc />
    public Task<AgentSession> GetOrCreateSessionAsync(string conversationKey, CancellationToken cancellationToken = default)
        => _store.GetOrCreateAsync(conversationKey, cancellationToken);

    /// <inheritdoc />
    public Task SaveSessionAsync(string conversationKey, AgentSession session, CancellationToken cancellationToken = default)
        => _store.SaveAsync(conversationKey, session, cancellationToken);
}
