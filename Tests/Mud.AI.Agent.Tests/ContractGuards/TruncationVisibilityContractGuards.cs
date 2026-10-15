// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// 截断可见性与错误载荷单一构造点的<b>中立半边</b>（迁移自飞书仓
/// <c>TruncationVisibilityContractGuards</c> 与 <c>ErrorRecoverabilityTests</c> 的中立锚点）。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：执行器骨架（<c>ToolExecutor</c>）、唯一出口（<c>ToolResultPipeline</c>）与唯一工厂
/// （<c>ToolErrorFactory</c>）随骨架中立化上移本仓——判据锚点随之迁入。飞书仓保留产品侧
/// （工具包不得自持第二份出口/构造点）的负向锚，两面互补。
/// </para>
/// </remarks>
public class TruncationVisibilityContractGuards
{
    /// <summary>
    /// ⑤' <c>ToolExecutor</c> 的预算是<b>委托</b>给唯一出口的，而不是自己再实现一遍。
    /// </summary>
    [Fact]
    public void ToolExecutor_ShouldDelegateBudgetToTheSingleEgress()
    {
        var source = ReadSource("Mud.AI.Agent", "Tools", "ToolExecutor.cs");

        source.Should().Contain("ToolResultPipeline.",
            "FromApi/FromPlainText/FromPagedResult 必须委托唯一出口（R-1）");
        source.Should().NotContain("ToolResultText.Truncate",
            "预算实现不得在执行器骨架里再写一份（形态单源）");
    }

    /// <summary>
    /// ⑥ <c>new ToolError(...)</c> 只允许出现在唯一工厂内（B-2：此前 3 处同构拷贝）。
    /// </summary>
    [Fact]
    public void ToolError_ShouldBeConstructedOnlyAtTheSingleFactory()
    {
        var withNewToolError = new List<string>();
        foreach (var file in Directory.GetFiles(
                     Path.Combine(GetSolutionRoot(), "Mud.AI.Agent"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // 唯一合法构造点：工厂自身（ToolErrorFactory.cs；ToolResultPipeline.cs 是搬迁前的历史文件名，保留排除以防回归）。
            if (string.Equals(Path.GetFileName(file), "ToolErrorFactory.cs", StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(file), "ToolResultPipeline.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            source.Should().NotContain("new ToolError(", $"{Path.GetFileName(file)} 必须经 ToolErrorFactory");
            if (source.Contains("new ToolError(", StringComparison.Ordinal))
            {
                withNewToolError.Add(file);
            }
        }

        withNewToolError.Should().BeEmpty(
            "ToolError 载荷只允许在唯一工厂 ToolErrorFactory 内构造——其他位置的拷贝会在契约演进时静默漂移（B-2）");
    }

    /// <summary>
    /// S-10（F-8 的中立半边）：唯一出口是工具结果的<b>中心序列化点</b>，必须走 <c>ToolResultJson</c>
    /// （保留中文的编码器）而不是裸 <c>ToJsonString()</c>（那会重新引入转义）。
    /// </summary>
    /// <remarks>
    /// <b>R-1 修订</b>：中心序列化点已从 <c>Internal/ToolExecutor.cs</c> 上移到唯一出口
    /// <c>Results/ToolResultPipeline.cs</c>（<c>OkJson</c>/<c>OkReceipt</c> 经 <c>ToolResultJson.ToText</c>）。
    /// 判据随实现上移——否则本守卫会盯一个"已经不再负责序列化"的文件，变成假门禁。
    /// </remarks>
    [Fact]
    public void ToolExecutor_ShouldSerializeViaToolResultJson_NotRawToJsonString()
    {
        var pipeline = ReadSource("Mud.AI.Agent", "Tools", "ToolResultPipeline.cs");

        pipeline.Should().Contain(
            "ToolResultJson.ToText",
            "唯一出口是工具结果的中心序列化点，必须走 ToolResultJson（否则中文被转义，抵消 F-8 收益）");
        pipeline.Should().NotContain(
            ".ToJsonString()",
            "唯一出口出现裸 ToJsonString() —— 会绕过保留中文的编码器（S-10 回潮）");

        // 反向自证：执行器骨架不再持有序列化职责（形态单源），故不得出现裸 ToJsonString()。
        var executor = ReadSource("Mud.AI.Agent", "Tools", "ToolExecutor.cs");
        executor.Should().NotContain(".ToJsonString()", "执行器骨架不得自行序列化（S-10 回潮）");
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
