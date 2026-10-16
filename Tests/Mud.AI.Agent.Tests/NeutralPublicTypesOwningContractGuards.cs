// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests;

/// <summary>
/// <b>NeutralPublicTypes_ShouldHaveOwningTests</b>（上线登记）：
/// <see cref="Mud.AI.Agent"/> 的每个公开类型必须「有归属测试」——机械防「迁了类型、忘了迁用例」。
/// </summary>
/// <remarks>
/// <para>
/// <b>判据</b>：类型名在<b>任一</b>测试工程存在命名对应的 <c>&lt;TypeName&gt;Tests</c> 类
/// （首选形态：测试随类型走，P3 上游化时覆盖跟着中立包离开飞书仓）；
/// 或该类型登记在本守卫的 <see cref="OwningEvidence"/> 映射——其行为测试 legitimately
/// 以其他类名落在产品测试工程（通道与产品子类共生、审批投影与产品载荷绑定等），逐条登记并
/// <b>可验证</b>（守卫自证断言映射指向的测试类真实存在，防映射腐烂）。
/// </para>
/// <para>
/// <b>为什么默认报红</b>：新增中立公开类型时，「补一个自有测试类」是最低门槛——
/// 连名字都不取的行为测试等于没有归属；不报红则 Q4 的缺口会无声扩大。
/// </para>
/// <para>
/// 范围：非接口类型（接口由实现方测试覆盖，机械登记属噪声；枚举同理）。
/// <c>Mud.AI.Abstractions</c> 的契约类型按「或声明契约的既有归属工程」处理——
/// 由实现方测试覆盖（如 <c>ISessionStore</c> ⇐ <c>MemorySessionStoreTests</c>），不逐条登记。
/// </para>
/// </remarks>
public class NeutralPublicTypesOwningContractGuards
{
    /// <summary>
    /// 行为测试随中立类型迁入本仓、但落在<b>非同名测试类</b>的映射 → 归属测试类（守卫自证其存在）。
    /// </summary>
    private static readonly Dictionary<string, string> OwningEvidence = new(StringComparer.Ordinal)
    {
        // 回合管线值/选项类型：行为已由管线专属用例承载。
        ["ConversationalTurnOptions"] = "ConversationalTurnPipelineTests",
        ["TurnScopeContext"] = "ConversationalTurnPipelineTests",
        ["TurnOutcome"] = "ConversationalTurnPipelineTests",

        // 知识族：选项/估算/索引随 Chunker 与 Retriever 的测试承载。
        ["ChunkingOptions"] = "DocumentChunkerTests",
        ["TokenEstimator"] = "DocumentChunkerTests",
        ["CorpusIndexer"] = "VectorRetrieverTests",

        // 引导族：块/结果值随 Composer 测试承载。
        ["GuidanceBlock"] = "GuidanceComposerTests",
        ["GuidanceResult"] = "GuidanceComposerTests",

        // 工具面机制：行为测试随工具面迁入本仓。
        ["ToolApiOutcome"] = "ToolApiFailureTests",
        ["ToolResult"] = "ToolResultContentSafetyTests",
        ["ToolResultJson"] = "ToolResultSanitizerTests",
        ["ToolResultJsons"] = "ToolResultSanitizerTests",
        ["ToolResultPipeline"] = "ToolResultContentSafetyTests",
        ["ToolResultText"] = "ToolResultSanitizerTests",
        ["SecurityTextPrimitives"] = "ToolResultSanitizerTests",
    };

    /// <summary>
    /// <b>P3 上游化跨仓账本</b>：行为测试<b>仍留在产品仓</b>（<c>Mud.Feishu.AI.Tests</c> /
    /// <c>Mud.Feishu.AI.Tools.Tests</c>）的中立公开类型 → 产品侧归属测试类。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些类型与产品载荷/子类共生（审批投影与飞书载荷绑定、通道机制与产品子类共生、工具面门面以产品
    /// <c>ToolArgs</c> 为基准、会话摘要器持有产品提示词与 idKeys 等），其行为用例天然留在产品仓。
    /// 跨仓无法在本仓自证其存在，故本表<b>只登记归属、不做本地存在性校验</b>——存在性由产品仓自身的门禁保证。
    /// </para>
    /// <para>
    /// 与 <see cref="OwningEvidence"/> 的分工：本表是「覆盖已随产品侧离开本仓」的显式账本，防止本守卫在
    /// 跨仓切分后把「测试不在此仓」误判为「类型无归属」。任何新增到本表的条目都意味着该中立公开类型
    /// <b>在本仓没有行为覆盖</b>，必须人工确认产品侧确实有对应用例。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> ProductOwningEvidence = new(StringComparer.Ordinal)
    {
        // 审批闭环：状态机与投影与产品载荷绑定，行为测试在产品仓。
        ["AgentApprovalContinuation"] = "FeishuAgentApprovalContinuationTests",
        ["ApprovalRequestProjector"] = "ConversationalFeishuEventHandlerApprovalTests",
        ["InMemoryPendingApprovalStore"] = "PendingApprovalStoreTests",
        ["PendingApprovalState"] = "PendingApprovalStoreTests",

        // 通道机制与产品子类共生（EditMessage/CardStream 子类各有测试 + 生命周期契约守卫）。
        ["BufferedMessageChannel"] = "ChannelStateLifecycleContractGuards",

        // DI 骨架：装配语义在产品侧守卫锚（会话闸门/存储替换链）。
        ["AgentRuntimeServiceCollectionExtensions"] = "AgentContractGuards",

        // 会话摘要器：提示词与 idKeys 属产品事实，行为测试在产品仓。
        ["ConversationSummarizer"] = "ConversationSummarizerTests",

        // 工具面门面：行为/守卫测试以产品 ToolArgs 门面为基准，留在产品仓。
        ["ToolArgumentException"] = "ToolArgsContractGuards",
        ["ToolDryRun"] = "DryRunContractGuards",
        ["ToolError"] = "ToolErrorContractGuards",
        ["ToolErrorFactory"] = "ToolErrorContractGuards",
        ["ToolErrorText"] = "ToolErrorContractGuards",
        ["ToolErrorHintRenderer"] = "ToolErrorContractGuards",
        ["ToolErrorPayloadSerializer"] = "ToolErrorPayloadTests",
        ["ToolErrorSubtype"] = "ToolErrorClassifierTests",
        ["ToolErrorTextFormatter"] = "ToolErrorClassifierTests",
        ["ToolErrorClassifier"] = "ToolErrorClassifierTests",
        ["ToolExecutor"] = "ToolExecutorSkeletonGuards",
        ["ToolNamePattern"] = "ToolPolicyContractGuards",
        ["ToolPagination"] = "PaginationContractGuards",
        ["PagedFetchResult"] = "PaginationContractGuards",
        ["ToolPolicyMatcher"] = "ToolPolicyContractGuards",
        ["ToolPolicyRules"] = "ToolPolicyContractGuards",

        // 阶段 A-1：形态认知（ToolArgumentNormalizer / ToolArgsDigester）的等价性矩阵以产品
        // ToolArgs 门面为基准，故留在产品测试工程。
        ["ToolArgumentNormalizer"] = "ToolArgumentFormEquivalenceTests",
        ["ToolArgsDigester"] = "ToolArgumentFormEquivalenceTests",
    };

    /// <summary>扫描范围：Mud.AI.Agent 的公开类型声明（非接口）。</summary>
    [Fact]
    public void NeutralPublicTypes_ShouldHaveOwningTests()
    {
        var root = FindRepositoryRoot();
        var testClassNames = CollectTestClassNames(root);
        var neutralTypes = CollectPublicTypes(Path.Combine(root, "Mud.AI.Agent"));

        neutralTypes.Should().NotBeEmpty("扫描器必须能读到 Mud.AI.Agent 的公开类型声明（读空 = 假绿）");

        var unowned = neutralTypes
            .Where(type => !testClassNames.Contains(type + "Tests")
                           && !OwningEvidence.ContainsKey(type)
                           && !ProductOwningEvidence.ContainsKey(type))
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToList();

        unowned.Should().BeEmpty(
            "以下中立公开类型没有归属测试：既不存在 <Type>Tests 类，也未在 OwningEvidence / "
            + "ProductOwningEvidence 登记「行为测试落在哪个类」——补一个自有测试类（首选，测试随类型走）"
            + "或登记证据：\n{0}",
            string.Join("\n", unowned));
    }

    /// <summary>守卫自证：证据映射指向的测试类必须真实存在（映射腐烂 = 报红）。</summary>
    [Fact]
    public void OwningEvidence_ShouldPointToExistingTestClasses()
    {
        var testClassNames = CollectTestClassNames(FindRepositoryRoot());

        var stale = OwningEvidence.Values
            .Where(testClass => !testClassNames.Contains(testClass))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "以下 OwningEvidence 指向的本地测试类已不存在（改名的测试要同批改映射，否则归属声明失守）：{0}",
            string.Join(", ", stale));

        // 反向：两个映射的键都必须真的是 Mud.AI.Agent 的公开类型（复制粘贴错键 = 报红）。
        // 注意：ProductOwningEvidence 的值（产品侧测试类）不在本仓，故对值不做本地存在性校验；
        // 但其<b>键</b>同样必须是真实的中立公开类型（防账本里堆砌僵尸条目）。
        var neutralTypes = CollectPublicTypes(Path.Combine(FindRepositoryRoot(), "Mud.AI.Agent"));
        var phantom = OwningEvidence.Keys
            .Concat(ProductOwningEvidence.Keys)
            .Where(type => !neutralTypes.Contains(type))
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToList();

        phantom.Should().BeEmpty("OwningEvidence / ProductOwningEvidence 含非中立公开类型的键：{0}", string.Join(", ", phantom));
    }

    /// <summary>Mud.AI.Agent 源码的公开类型名（class/record/struct，排除接口与嵌套）。</summary>
    private static HashSet<string> CollectPublicTypes(string projectDirectory)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var declarationPattern = new System.Text.RegularExpressions.Regex(
            @"(?m)^\s*public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:record\s+struct|record\s+class|record|struct|class)\s+(\w+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var source = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in declarationPattern.Matches(source))
            {
                types.Add(match.Groups[1].Value);
            }
        }

        return types;
    }

    /// <summary>全部测试工程源码中的测试类名（class X / X 类声明，取类型名集合）。</summary>
    private static HashSet<string> CollectTestClassNames(string root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var declarationPattern = new System.Text.RegularExpressions.Regex(
            @"(?m)^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var testProject in Directory.EnumerateDirectories(
                     Path.Combine(root, "Tests"), "Mud.*Tests", SearchOption.TopDirectoryOnly))
        {
            foreach (var file in Directory.EnumerateFiles(testProject, "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                var source = File.ReadAllText(file);
                foreach (System.Text.RegularExpressions.Match match in declarationPattern.Matches(source))
                {
                    names.Add(match.Groups[1].Value);
                }
            }
        }

        return names;
    }

    private static string FindRepositoryRoot()
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
