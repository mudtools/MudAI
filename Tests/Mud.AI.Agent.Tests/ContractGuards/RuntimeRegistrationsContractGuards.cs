// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// <b>RuntimeRegistrations_ShouldMatchDocumentedList</b>：DI 骨架的全部 <c>TryAdd</c> 服务类型
/// 必须与冻结清单机械相等——防"注册清单腐烂"（文档说有、代码没有；或反之）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么机械比对</b>：扩展方法的 remarks 是散文，新增/删除注册点时散文不会自己转红；宿主按文档装配，
/// 漏注册的失败模式是"运行期静默走默认件"（最难查的一类）。冻结清单即文档的机械镜像。
/// </para>
/// <para>
/// <b>形态纪律</b>：注册必须写成显式泛型形态（<c>TryAddSingleton&lt;TService&gt;</c>）——否则服务类型只存在于
/// lambda 返回类型里，机械提取不可能（提取不了的注册等于清单外注册）。
/// </para>
/// </remarks>
public class RuntimeRegistrationsContractGuards
{
    /// <summary>冻结注册清单（服务类型全名简单名；顺序无关，集合相等）。</summary>
    private static readonly string[] FrozenRegistrations =
    [
        "IAgentTelemetry",
        "ISessionStore",
        "IPendingApprovalStore",
        "IAgentSessionFacade",
        "ConcurrencyLimiter",
        "KeyedAsyncGate",
        "ToolRetryPolicy",
        "ConversationalTurnPipeline",
    ];

    [Fact]
    public void RuntimeRegistrations_ShouldMatchFrozenList()
    {
        var source = ReadSource("Mud.AI.Agent", "Extensions", "AgentRuntimeServiceCollectionExtensions.cs");

        var registered = ExtractTryAddServiceTypes(source);

        registered.Should().NotBeEmpty("扫描面为空说明提取式坏了（假绿）");
        registered.Should().BeEquivalentTo(FrozenRegistrations,
            "DI 骨架的注册点必须与冻结清单集合相等——新增/删除注册点须同批改清单并说明理由");
    }

    /// <summary>
    /// 自证：判据必须真的能报出"多注册 / 少注册"（否则本守卫是假门禁）。
    /// </summary>
    [Fact]
    public void MatchesFrozen_ShouldDetectBothDirections()
    {
        var source = ReadSource("Mud.AI.Agent", "Extensions", "AgentRuntimeServiceCollectionExtensions.cs");
        var actual = ExtractTryAddServiceTypes(source);

        // 少一项（假装漏注册）⇒ 不相等。
        var missing = actual.Where(t => t != "KeyedAsyncGate").ToArray();
        MatchesFrozen(missing, FrozenRegistrations).Should().BeFalse();

        // 多一项（清单外注册）⇒ 不相等。
        var extra = actual.Append("UnregisteredService").ToArray();
        MatchesFrozen(extra, FrozenRegistrations).Should().BeFalse();

        // 原样 ⇒ 相等（判据不是恒 false）。
        MatchesFrozen(actual, FrozenRegistrations).Should().BeTrue();
    }

    /// <summary>从扩展源码提取全部 <c>TryAdd{Singleton,Scoped,Transient}&lt;TService&gt;</c> 的服务类型（首泛型参数）。</summary>
    internal static IReadOnlyList<string> ExtractTryAddServiceTypes(string source)
        => [.. Regex.Matches(source, @"TryAdd(?:Singleton|Scoped|Transient)\s*<\s*([A-Za-z_]\w*)")
            .Select(static m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>集合相等判据（纯函数，便于自证）。</summary>
    internal static bool MatchesFrozen(IReadOnlyList<string> actual, IReadOnlyList<string> frozen)
        => actual.Count == frozen.Count
            && !actual.Except(frozen, StringComparer.Ordinal).Any()
            && !frozen.Except(actual, StringComparer.Ordinal).Any();

    private static string ReadSource(string projectName, params string[] relativeParts)
    {
        var path = Path.Combine(new[] { GetSolutionRoot(), projectName }.Concat(relativeParts).ToArray());
        File.Exists(path).Should().BeTrue($"断言锚点源文件必须存在：{path}");
        return File.ReadAllText(path);
    }

    private static string GetSolutionRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory) && !File.Exists(Path.Combine(directory, "Mud.AI.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        directory.Should().NotBeNullOrEmpty("测试必须能定位仓库根目录（以 Mud.AI.slnx 为锚）");
        return directory!;
    }
}
