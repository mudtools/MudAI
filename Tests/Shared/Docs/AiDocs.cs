// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Tests.Docs;

/// <summary>
/// 新仓受治理文档的<b>位置注册表</b>（单一真相源）——<b>测试库资产，与生产运行期无任何关系</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：契约守卫曾各自硬编码文档路径——搬一次文档要改十几处、漏一处即测试红，
/// 且没有任何机制提示"还有谁引用这个文档"。与代码闭集同理：文档位置也只许有一个真相源。
/// </para>
/// <para>
/// <b>与飞书仓 <c>FeishuDocs</c> 的关系</b>：迁移约定「两仓各一份」——飞书仓注册表只登记
/// 飞书面文档，本表只登记随 <c>Mud.AI.*</c> 迁出的中立文档，二者互不复制。
/// </para>
/// <para>
/// <b>为什么用 <c>Compile Include</c> 链接而不是建共享工程</b>：消费方与飞书仓同法（
/// <c>Mud.Feishu.AI.Tests</c> 亦以 Compile Include 链接 <c>Tests/Shared/Docs/FeishuDocs.cs</c>）；
/// 新建共享工程会落入 <c>verify-build.ps1</c> 的「每个 <c>Tests/**</c> 工程都必须产出 TRX」断言
/// （非测试工程必红）。
/// </para>
/// <para>
/// <b>使用规则</b>：
/// <list type="bullet">
/// <item>新增受守卫守护的文档 ⇒ 在此登记一条（路径相对仓库根、用 <c>/</c> 分隔）；
/// <c>AiDocsRegistryContractGuards</c> 会反射本表逐条校验路径存在；</item>
/// <item>移动/改名文档 ⇒ <b>只改这里</b>，守卫失败消息会直接指向本文件；</item>
/// <item>消费侧（守卫）只经本表取路径，不得再出现字面量。</item>
/// </list>
/// </para>
/// </remarks>
internal static class AiDocs
{
    /// <summary>
    /// 《单元测试体系过度测试分析与解决方案》——测试体系治理（TFM 收敛、门禁分档、
    /// 守卫瘦身判据）的判据来源。
    /// </summary>
    public const string TestGovernanceReport = "documents/单元测试体系过度测试分析与解决方案.md";

    /// <summary>
    /// <c>decisions/ADR-0001</c>——会话摘要器与 MAF 压缩管线的取舍（决策记录的唯一家；
    /// 代码侧只留一行路径引用）。源文件在飞书仓为
    /// <c>documents/AIAgent/decisions/ADR-0001-summarizer-vs-maf-compaction.md</c>。
    /// </summary>
    public const string AdrSummarizerVsMafCompaction =
        "documents/decisions/ADR-0001-summarizer-vs-maf-compaction.md";

    /// <summary>
    /// <c>decisions/ADR-0003</c>——回合管线的阶段次序与补偿路径（<c>ConversationalTurnPipeline</c>
    /// 类级注释只留三铁律摘要 + 本 ADR 链接）。
    /// </summary>
    public const string AdrTurnPipelineCompensation =
        "documents/decisions/ADR-0003-turn-pipeline-compensation.md";

    /// <summary>
    /// <c>comment-budget-baseline.json</c>——注释预算冻结基线（<c>Mud.AI.Agent</c> /
    /// <c>Mud.AI.Abstractions</c> 的注释行数、最长注释块、未解析评审编号与过程叙述清单），
    /// 由 <c>CommentConventionContractGuards</c> 消费并重新固化。
    /// </summary>
    public const string CommentBudgetBaseline = "documents/comment-budget-baseline.json";
}
