// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// R3-12：降级路径指标化元守卫的<b>中立半边</b>（迁移自飞书仓 <c>DegradationMetricsContractGuards</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：通道更新失败的降级计数从 <c>BufferedMessageChannel</c> 基类**上移到产品子类**
/// （<c>EditMessageChannel</c> / <c>CardStreamMessageChannel</c> 的 <c>OnUpdateDegraded</c> 覆写）——
/// 中立基类经 <c>IAgentTelemetry</c> 端口记 reason，产品指标名与 app_key 维度留在产品侧。
/// 故本仓断言中立基类的<b>钩子被调用</b>；飞书仓对产品子类各断言一次，两面互补。
/// </para>
/// </remarks>
public class DegradationMetricsContractGuards
{
    /// <summary>
    /// N2-b：中立缓冲基类必须在更新失败路径调用 <c>OnUpdateDegraded</c> 钩子——
    /// 否则产品子类的覆写永不触发，降级指标静默消失（迁移最典型的"接了线但没通电"）。
    /// </summary>
    [Fact]
    public void NeutralBufferedBase_ShouldInvokeDegradedHook()
    {
        var root = GetSolutionRoot();
        var filePath = FindSourceFile(root, "BufferedMessageChannel.cs");
        File.Exists(filePath).Should().BeTrue("BufferedMessageChannel.cs 必须存在（扫描面不得静默为空）");

        var source = File.ReadAllText(filePath);
        source.Should().Contain("OnUpdateDegraded(",
            "中立基类必须在异常隔离路径调用 OnUpdateDegraded 钩子（产品子类经它补降级指标）");
    }

    private static string FindSourceFile(string root, string fileName)
        => Directory
            .GetFiles(root, fileName, SearchOption.AllDirectories)
            .First(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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
