// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Mud.AI.Abstractions.Approvals;
using Mud.AI.Abstractions.Channels;
using Mud.AI.Abstractions.Configuration;
using Mud.AI.Abstractions.Context;
using Mud.AI.Abstractions.Conversations;
using Mud.AI.Abstractions.Observability;
using Mud.AI.Agent.Conversations;
using Mud.AI.Agent.Events;
using Mud.AI.Agent.Sessions;
using Mud.AI.Agent.Tools;
using Mud.AI.Agent.Tools.Errors;

namespace Mud.AI.Agent.Extensions;

/// <summary><b>中立运行期 DI 骨架</b>：注册"任何生态都需要、且都没有产品语义"的默认件与主体机制。</summary>
/// <remarks>
/// 默认注册散落在各产品的 <c>AddXxxAgent</c> 里时，每上一个新生态就要重抄一遍装配顺序——顺序错（如遥测注册在依赖它的服务之后）
/// 不报错、只表现为"遥测缺失"，属最难查的一类问题。<b>注册语义全是 <c>TryAdd</c></b>（先注册者生效，产品薄壳与宿主自定义实现覆盖默认件；
/// 不引入第二套可替换槽位）。<b>⛔ 不含会话闸门</b>：闸门端口与实现全部留在产品侧——塞进中立骨架会同时制造第二个可替换槽位与
/// "中立包依赖产品契约"，故中立管线经③ 式委托消费。全部注册点由 <c>RuntimeRegistrationsContractGuards</c> 与冻结清单机械锁定（防清单腐烂）；注册一律显式泛型形态——服务类型可被契约守卫机械提取，推断形态等于清单外注册。
/// </remarks>
public static class AgentRuntimeServiceCollectionExtensions
{
    /// <summary>注册中立运行期的默认件（幂等；已注册的实现保持不变）。</summary>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddAgentRuntimeCore(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // 顺序即依赖顺序：遥测在最前（后续机制可选地依赖它，构造参数可空即可，不强制）。
        services.TryAddSingleton<IAgentTelemetry>(NoopAgentTelemetry.Instance);

        // 进程内默认：分布式部署由宿主先注册自己的实现覆盖（软缺席契约见 IPendingApprovalStore 注释）。
        services.TryAddSingleton<ISessionStore>(static _ => new MemorySessionStore());
        services.TryAddSingleton<IPendingApprovalStore, Mud.AI.Agent.Approvals.InMemoryPendingApprovalStore>();

        return services;
    }

    /// <summary>注册 <see cref="IAgentSessionFacade"/> 的中立默认实现（<see cref="AgentSessionStoreFacade"/>；幂等，先注册者生效）。</summary>
    /// <remarks>会话持久化经容器解析 <see cref="ISessionStore"/>（可空 ⇒ 仅内存会话）；历史状态键是<b>产品事实</b>，本方法无默认值兜底，缺席即抛。</remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="agent">序列化 / 反序列化的归属方（持有 <c>AgentSession</c> 的 Agent）。</param>
    /// <param name="historyStateKey">会话历史在状态袋内的状态键（产品事实，须显式注入）。</param>
    /// <param name="logger">日志（可空；未传时按 <c>ILogger&lt;AgentSessionStoreFacade&gt;</c> 解析）。</param>
    public static IServiceCollection AddAgentSessionStore(
        this IServiceCollection services,
        AIAgent agent,
        string historyStateKey,
        ILogger? logger = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (agent is null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        if (string.IsNullOrWhiteSpace(historyStateKey))
        {
            throw new ArgumentException(
                "历史状态键不能为空白——它是状态袋读写的同源锚点（中立包不得硬编码产品前缀，须由产品显式注入；急切失败，留到解析期才抛会与根因脱钩）",
                nameof(historyStateKey));
        }

        services.TryAddSingleton<IAgentSessionFacade>(sp =>
            new AgentSessionStoreFacade(
                agent,
                sp.GetService<ISessionStore>(),
                historyStateKey,
                logger ?? sp.GetService<ILogger<AgentSessionStoreFacade>>()));

        return services;
    }

    /// <summary>注册中立并发削峰机制：<see cref="ConcurrencyLimiter"/>（进程内在途上限）+ <see cref="KeyedAsyncGate"/>（同键串行闸门）。</summary>
    /// <remarks>旋钮机械映射 <c>AgentRuntimeOptions.MaxConcurrentTurns</c>/<c>TurnQueueTimeoutSeconds</c>（单一阈值源）；须先注册 <c>AgentRuntimeOptions</c>，缺失即 fail-fast。宿主的闸门外壳（委托形态）可同时消费这两个引擎——本注册只提供件，不代替产品装配。</remarks>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddConcurrencyLimiter(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<ConcurrencyLimiter>(sp =>
        {
            var options = sp.GetRequiredService<AgentRuntimeOptions>();
            return new ConcurrencyLimiter(
                options.MaxConcurrentTurns,
                TimeSpan.FromSeconds(options.TurnQueueTimeoutSeconds));
        });

        services.TryAddSingleton<KeyedAsyncGate>();

        return services;
    }

    /// <summary>注册 <see cref="ToolRetryPolicy"/>（幂等）：重试旋钮取 <c>AgentRuntimeOptions.ToolRetry</c> 单一阈值源。</summary>
    /// <remarks>本策略是<b>无环判定件</b>（谓词 + 退避计算），重试循环由调用方持有，<c>ToolRetried</c> 埋点归循环所有者。前置条件同 <see cref="AddConcurrencyLimiter"/>。</remarks>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddToolRetryPolicy(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<ToolRetryPolicy>(sp =>
        {
            var options = sp.GetRequiredService<AgentRuntimeOptions>();
            return new ToolRetryPolicy(options.ToolRetry);
        });

        return services;
    }

    /// <summary>注册 <see cref="Events.ConversationalTurnPipeline"/> 单例：容器不可能知道的两个依赖显式传入（Agent 实例、会话键治理委托），其余从容器解析。</summary>
    /// <remarks>容器解析：<c>AgentRuntimeOptions</c>（必填，缺失即抛）/ <c>IAgentTelemetry</c> / <c>ILogger</c> / <c>IContextAssembler</c>（集合，零注册即空）/ <c>IMessageChannel</c> / <c>IToolApprovalChannel</c> / <c>IPendingApprovalStore</c> / <c>ITurnScopeFactory</c>（后六项可空——软缺席契约不变）。TryAdd 语义：宿主先注册了自己的实例时本注册让位。</remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="agent">模型轮次执行器（MAF <c>AIAgent</c>）。</param>
    /// <param name="sessions">会话取/建/存端口：产品门面实例，或 <see langword="null"/> ⇒ 解析容器内的 <see cref="IAgentSessionFacade"/>（<see cref="AddAgentSessionStore"/> 注册的中立默认件或宿主实现，两者都缺席即解析期抛错——注册期无法从容器取实例，故以 null 表示"走容器"）。</param>
    /// <param name="keyBuilder">会话键治理委托（键命名空间是产品策略）。</param>
    /// <param name="acquireGate">会话闸门获取委托（可空 ⇒ 不串行化；闸门实现留在产品侧）。</param>
    /// <param name="turnOptions">产品事实与产品策略注入面（可空 = 全默认）。</param>
    public static IServiceCollection AddConversationalTurnPipeline(
        this IServiceCollection services,
        AIAgent agent,
        IAgentSessionFacade? sessions,
        Func<ConversationRequest, string> keyBuilder,
        Func<string, CancellationToken, Task<IDisposable?>>? acquireGate = null,
        ConversationalTurnOptions? turnOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (agent is null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        if (keyBuilder is null)
        {
            throw new ArgumentNullException(nameof(keyBuilder));
        }

        services.TryAddSingleton<ConversationalTurnPipeline>(sp => new ConversationalTurnPipeline(
            agent,
            sp.GetRequiredService<AgentRuntimeOptions>(),
            sessions ?? sp.GetRequiredService<IAgentSessionFacade>(),
            keyBuilder,
            sp.GetService<IAgentTelemetry>(),
            sp.GetService<ILogger<ConversationalTurnPipeline>>(),
            [.. sp.GetServices<IContextAssembler>()],
            acquireGate,
            sp.GetService<IMessageChannel>(),
            sp.GetService<IToolApprovalChannel>(),
            sp.GetService<IPendingApprovalStore>(),
            sp.GetService<ITurnScopeFactory>(),
            turnOptions));

        return services;
    }
}
