// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// B2 错误契约守卫的<b>中立半边</b>（迁移自飞书仓 <c>ToolErrorContractGuards</c>）：
/// 中立源码中错误子类常量的产出点、唯一出口/工厂的委派链。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：源守卫 <c>ToolErrorContractGuards</c>（飞书仓）是跨仓事实——错误契约族
/// （<c>ToolError</c> / <c>ToolErrorClassifier</c> / <c>ToolErrorText</c> / <c>ToolResultPipeline</c>…）
/// 随骨架中立化上移，判据锚点随之迁入本仓；飞书仓保留产品侧产出点断言，两面互补。
/// </para>
/// </remarks>
public class ToolErrorContractGuards
{
    /// <summary>
    /// 守卫 ②b（PM 裁定 DP-R7-4）：<c>ToolErrorSubtype</c> 的每个常量在中立面都必须有<b>产出点或用途</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么要机械断言</b>：错误子类是对宿主的承诺（"你可以按 subtype 分支"）。
    /// 一个声明了却从不产出的子类，等于承诺了一条走不通的分支：宿主写了 <c>if</c> 却永远不进，
    /// 而文档表格里它看起来"已实现"。
    /// </para>
    /// <para>
    /// <b>迁移拆分</b>：常量被引用一次即算"有用途"——既可以是 <c>Subtype</c> 实参，也可以是策略拒绝的
    /// <b>文案前缀</b>。产品面产出点由飞书仓 <c>ToolErrorContractGuards</c> 的同名守卫持有；
    /// 本守卫只在中立面判据，并按<b>产品侧产出集</b>减去——两面互补，并集覆盖全部常量。
    /// </para>
    /// </remarks>
    [Fact]
    public void ToolErrorSubtype_Constants_ShouldAllHaveProducers()
    {
        var root = GetSolutionRoot();

        var sources = new[] { "Mud.AI.Agent", "Mud.AI.Abstractions" }
            .Select(relative => Path.Combine(root, relative))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();

        sources.Should().NotBeEmpty("扫描面不得为空（否则本守卫恒绿）");

        var constantNames = typeof(ToolErrorSubtype)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(static field => field.Name)
            .ToArray();
        constantNames.Should().NotBeEmpty("ToolErrorSubtype 必须有常量定义（空集会让本守卫恒绿）");

        // 产品侧产出集（飞书仓 ToolErrorContractGuards 持有）：这些常量在中立面按设计可能无产出点，
        // 故从本守卫的幽灵判据中减去。减去集 ∪ 本守卫的待验证集 = 全部常量（迁移前单条守卫
        // 要求「任一面对常量的引用」；拆分后两面各自要求，并集不缩水）。
        var productProduced = new[]
        {
            "InvalidArgs", "MissingRequired", "RiskExceeded", "IdentityMismatch", "ToolNotAllowed",
            "DomainNotAllowed", "WriteNotAllowed", "ToolBudgetExceeded", "AuthorizationDenied",
            "UserAuthRequired", "UserTokenRefreshFailed", "NeedsUserConfirmation", "RateLimited",
            "UpstreamError", "InjectedContentBlocked",
        };

        // 反缩水：被减去的名称必须是真实常量——减错名会让真幽灵逃逸。
        foreach (var name in productProduced)
        {
            constantNames.Should().Contain(name, "被减去的名称必须是真实常量（否则减错名会让真幽灵逃逸）");
        }

        var candidates = constantNames.Where(
            name => !productProduced.Contains(name, StringComparer.Ordinal));
        var ghosts = FindGhostSubtypes(candidates, string.Join('\n', sources));

        ghosts.Should().BeEmpty(
            "以下错误子类常量在中立面全无引用（幽灵子类）——它们对宿主是「承诺了却永远走不到」的分支。"
            + "修法：接线到真实产出点，或删除（新增需评审 + 本守卫覆盖）：{0}",
            string.Join(" | ", ghosts));
    }

    /// <summary>守卫 ②b 的自证：判据必须真的能报出"无人引用"的常量（否则本条是假门禁）。</summary>
    [Fact]
    public void GhostSubtypeDetection_ShouldReportUnreferencedConstant()
    {
        var ghosts = FindGhostSubtypes(
            ["UsedSubtype", "GhostSubtype"],
            "var x = ToolErrorSubtype.UsedSubtype; var y = ToolErrorSubtype.GhostSubtypeX;");

        ghosts.Should().ContainSingle().Which.Should().Be(
            "GhostSubtype",
            "判据必须精确匹配（GhostSubtypeX 不得让 GhostSubtype 被误判为已引用）");
    }

    /// <summary>幽灵子类判据（纯函数，便于自证）：返回在给定源码里找不到引用的常量名。</summary>
    private static IReadOnlyList<string> FindGhostSubtypes(
        IEnumerable<string> constantNames,
        string combinedSource)
        => [.. constantNames.Where(name => !System.Text.RegularExpressions.Regex.IsMatch(
            combinedSource,
            $@"ToolErrorSubtype\.{System.Text.RegularExpressions.Regex.Escape(name)}\b"))];

    /// <summary>
    /// 守卫 ⑤：<c>ToolExecutor</c> 的错误路径必须经过 <c>FromStructuredError</c>（源码结构断言）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 金丝雀：注掉 <c>FromApiOutcomeError</c> 中的 <c>FromStructuredError</c> 调用即红。
    /// </para>
    /// <para>
    /// <b>R-1 修订</b>：载荷序列化与 <c>FromError</c> 构造<b>已下沉到唯一出口</b>
    /// （<c>ToolErrorFactory</c> / <c>ToolResultPipeline</c>），故本守卫的判据同步改为
    /// "执行器骨架<b>委派</b>唯一出口 + 出口自身确实序列化"——原判据盯的是执行器骨架里的实现细节，
    /// 收口后它会变成"要求重复实现"的反向约束。
    /// </para>
    /// </remarks>
    [Fact]
    public void ToolExecutor_ErrorPaths_ShouldUseFromStructuredError()
    {
        var source = ReadSource("Mud.AI.Agent", "Tools", "ToolExecutor.cs");

        source.Should().Contain("FromStructuredError(",
            "ToolExecutor 的错误路径必须经过 FromStructuredError（工具名 + 分类文案的唯一入口）");
        source.Should().Contain("ToolResultPipeline.Error(",
            "ToolExecutor 必须把载荷构造委派给唯一出口 ToolResultPipeline（R-1 起不再自行拼装）");

        var pipeline = ReadSource("Mud.AI.Agent", "Tools", "ToolResultPipeline.cs");

        // v2.3 P1-D：载荷构造此前就在 ToolResultPipeline 内，中立化上移时拆出了独立工厂
        // ToolErrorFactory（骨架装配的副产品）。判据随之追到事实：管道委派工厂 + 工厂确实序列化。
        pipeline.Should().Contain("ToolErrorFactory.Create(",
            "唯一出口必须把载荷构造委派给唯一工厂 ToolErrorFactory（不得自行 new ToolError）");
        pipeline.Should().Contain("ToolResult.FromError(",
            "唯一出口必须通过 ToolResult.FromError(ToolError, ...) 构造带结构化载荷的结果");

        var factory = ReadSource("Mud.AI.Agent", "Tools", "ToolErrorFactory.cs");

        factory.Should().Contain("ToolErrorPayloadSerializer.Serialize(",
            "唯一工厂必须调用 ToolErrorPayloadSerializer.Serialize 构造首行 JSON");
    }

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
