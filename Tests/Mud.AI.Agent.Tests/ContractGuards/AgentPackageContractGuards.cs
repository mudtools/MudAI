// -----------------------------------------------------------------------
//  作者:Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// <c>Mud.AI.*</c> 家族的<b>内部包锁</b>（迁移方案 §6：「需扫描 <c>Mud.AI.Agent</c> csproj 的家族内部锁
/// 随测试迁新仓」）。
/// </summary>
/// <remarks>
/// <para>
/// 源守卫 <c>AgentContractGuards</c>（飞书仓）是跨仓事实；迁移后家族内部锁随实现迁入本仓，
/// 飞书仓保留「MAF 只允许 <c>Mud.Feishu.AI</c> 声明」「核心 SDK 不得依赖 <c>Mud.AI.*</c>」
/// 与新增的跨仓版本契约。
/// </para>
/// <para>
/// <b>为什么家族内部锁必须在归属仓</b>：这些断言的对象是新仓自己的 csproj（单版本、零 MAF、
/// 零组件耦合、tokenizer 数据包直引）——留在飞书仓会在源目录迁出后对着不存在的工程空扫（假绿）。
/// </para>
/// </remarks>
public class AgentPackageContractGuards
{
    /// <summary>
    /// MAF 相关 PackageReference 声明必须唯一版本。混合版本的 TypeLoadException 类故障
    /// 与 MudHttpUtils 单版本守卫所守护的故障同类（「同一组件单版本」契约）。
    /// </summary>
    [Fact]
    public void FamilyPackages_MafReferences_ShouldBeSingleVersion()
    {
        var csprojFiles = ProjectFiles().ToList();
        csprojFiles.Should().NotBeEmpty("新仓中应至少有一个 csproj 文件");

        var versions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var csproj in csprojFiles)
        {
            // 先剔除 XML 注释：注释掉的旧引用不是有效声明，参与比对会产生误报。
            var content = StripXmlComments(File.ReadAllText(csproj));
            foreach (Match match in Regex.Matches(content,
                         @"Include=""(Microsoft\.Agents\.AI[^""]*)""\s+Version=""([^""]+)""",
                         RegexOptions.IgnoreCase))
            {
                versions.Add(match.Groups[2].Value);
            }
        }

        versions.Should().NotBeEmpty("应至少有一个 Microsoft.Agents.AI* 包引用（Mud.AI.Agent 的运行期框架）");
        versions.Should().ContainSingle(
            "全家族 Microsoft.Agents.AI* 必须共用一个版本，混合版本会混入两个程序集（TypeLoadException 类故障）");
    }

    /// <summary>
    /// <b>反向断言</b>：<c>Mud.AI.Abstractions</c> 必须保持<b>零 MAF</b>。
    /// </summary>
    /// <remarks>
    /// 该包是最底层的编译期契约层：源生成器按 <c>Mud.AI.*</c> 硬编码命名空间发现剖面、
    /// 声明契约（<c>[AgentTool]</c>/<c>[ToolParameter]</c>/<c>[ToolHandler]</c>）被 233 个策展接口消费。
    /// 它一旦引入 MAF，生成器被测工程与全部非 AI 工程都会被拖进 Agent 运行时的版本面。
    /// </remarks>
    [Fact]
    public void MudAiAbstractions_ShouldNotDeclareMafPackages()
    {
        var csproj = Path.Combine(GetSolutionRoot(), "Mud.AI.Abstractions", "Mud.AI.Abstractions.csproj");
        File.Exists(csproj).Should().BeTrue();

        StripXmlComments(File.ReadAllText(csproj)).Should().NotContain(
            "Microsoft.Agents.AI",
            "Mud.AI.Abstractions 是编译期契约层（被源生成器与声明契约消费），必须保持零 MAF 依赖");
    }

    /// <summary>
    /// <b>独立产品线纪律</b>：<c>Mud.AI.*</c> 工程不得声明 <c>Mud.HttpUtils</c> 组件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须机械锁定</b>：<c>Mud.HttpUtils</c> 作为"全仓单版本 HTTP 组件"曾被默认当成无害依赖，
    /// <c>Mud.AI.Agent</c> 遂长期直接判定 <c>Mud.HttpUtils.ApiException</c>。后果：独立产品线无法独立发布、
    /// 版本被「同一组件单版本」契约反向牵制、中立层认识了产品的具体实现类型。
    /// </para>
    /// <para>
    /// <b>修法</b>（不是删功能）：把"状态码"这一跨产品事实经中立端口
    /// <c>Mud.AI.Abstractions.Tools.Errors.ITransportFault</c> 传入，具体异常类型的识别归产品适配层
    /// （飞书侧 = <c>Mud.Feishu.AI.AgentTools.FeishuTransportFaultAdapter</c>）。
    /// </para>
    /// <para>
    /// <b>射程</b>：只禁<b>组件本身</b>（<c>Mud.HttpUtils</c>）。子包（<c>.Attributes</c>/<c>.Abstractions</c>）
    /// 若要引入须在此处显式扩射程并补论证（判据自证用例已把"子包不算命中"钉死）。
    /// </para>
    /// </remarks>
    [Fact]
    public void MudAiFamily_ShouldNotDeclareMudHttpUtilsComponent()
    {
        var offenders = ProjectFiles()
            .Where(static path => Path.GetFileName(path).StartsWith("Mud.AI.", StringComparison.Ordinal)
                && !IsUnderTestsDirectory(path))
            .Where(path => DeclaresMudHttpUtilsComponent(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "Mud.AI.* 是可脱离 Mud 组件独立发布的 AI 产品线：不得声明 Mud.HttpUtils 组件"
            + "（跨产品事实经中立端口传入，适配实现留在产品层）。违规工程：{0}",
            string.Join(" / ", offenders));
    }

    /// <summary>判据自证：只命中组件本身；注释掉的引用与子包不得命中（否则守卫会假红/假绿）。</summary>
    [Theory]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Mud.HttpUtils\" Version=\"3.0.6\" /></ItemGroup></Project>", true)]
    [InlineData("<Project><ItemGroup><!-- <PackageReference Include=\"Mud.HttpUtils\" Version=\"3.0.6\" /> --></ItemGroup></Project>", false)]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Mud.HttpUtils.Attributes\" Version=\"3.0.6\" /></ItemGroup></Project>", false)]
    [InlineData("<Project><ItemGroup><ProjectReference Include=\"..\\Mud.AI.Abstractions\\Mud.AI.Abstractions.csproj\" /></ItemGroup></Project>", false)]
    public void MudHttpUtilsComponentJudge_ShouldFlagOnlyTheExactComponent(string csprojContent, bool expected)
        => DeclaresMudHttpUtilsComponent(csprojContent).Should().Be(expected);

    /// <summary>
    /// <c>ChatTokenCounter</c> 的 tokenizer 数据包与 MEAI 依赖必须在 <c>Mud.AI.Agent</c> 显式声明。
    /// </summary>
    /// <remarks>
    /// 缺词表包时 <c>TiktokenTokenizer.CreateForModel</c> 抛异常并被回退路径吞掉 ⇒
    /// <c>MaxHistoryTokens</c> 维度全部退化为字符估算（仅 <c>InitializationFailure</c> 一处可观测）。
    /// <c>CountMessages(IEnumerable&lt;ChatMessage&gt;)</c> 消费 MEAI 类型，按「直接使用的 API 必须直接声明」
    /// 必须显式声明 MEAI.Abstractions。
    /// </remarks>
    [Fact]
    public void ChatTokenCounter_ShouldDeclareTokenizerDataPackage()
    {
        var csproj = Path.Combine(GetSolutionRoot(), "Mud.AI.Agent", "Mud.AI.Agent.csproj");
        File.Exists(csproj).Should().BeTrue();

        var content = File.ReadAllText(csproj);
        content.Should().Contain("Microsoft.ML.Tokenizers\"", "主包必须显式声明（直接使用的 API 直接声明）");
        content.Should().Contain("Microsoft.ML.Tokenizers.Data.O200kBase",
            "gpt-4o 的 o200k_base 词表在独立包中；缺失会让精确计数静默退化为估算");
        content.Should().Contain("Microsoft.Extensions.AI.Abstractions",
            "ChatTokenCounter.CountMessages 直接消费 MEAI 的 ChatMessage——直接使用的 API 必须直接声明");

        // 可观测面：不再静默吞掉。
        ReadSource("Mud.AI.Agent", "Conversations", "ChatTokenCounter.cs").Should().Contain("InitializationFailure",
            "编码器初始化失败必须留下可观测原因（由宿主构造期告警 + 用例断言）");
    }

    // ────────── 实现 ──────────

    /// <summary>
    /// 判据：声明了<b>组件本身</b>（<c>Include="Mud.HttpUtils"</c>——模式含闭引号，
    /// 故 <c>Mud.HttpUtils.Attributes</c> 这类子包不会命中）。先剔除 XML 注释：注释不是声明。
    /// </summary>
    private static bool DeclaresMudHttpUtilsComponent(string csprojContent)
        => Regex.IsMatch(StripXmlComments(csprojContent), @"Include=""Mud\.HttpUtils""");

    /// <summary>测试工程（<c>Tests/</c> 下）不受本纪律约束：它们要驱动组件、且不随产品线发布。</summary>
    private static bool IsUnderTestsDirectory(string path)
        => path.Contains(
            $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ProjectFiles()
        => Directory.GetFiles(GetSolutionRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("obj") && !p.Contains("bin"));

    private static string StripXmlComments(string content)
        => Regex.Replace(content, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

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
