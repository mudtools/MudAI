// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// 参数形态认知契约守卫的<b>中立半边</b>（迁移自飞书仓 <c>ToolArgumentShapeContractGuards</c>）：
/// 断言 <c>JsonValueKind.String</c> 的形态认知只允许出现在中立归一化中心 <c>ToolArgumentNormalizer</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：B 组形态认知相关类型（含唯一认知中心 <c>ToolArgumentNormalizer</c>）随中立化迁入
/// <c>Mud.AI.Agent/Tools</c>，认知边界必须覆盖中立侧——漏掉即假绿（该目录新出现的形态认知无人拦截）。
/// 飞书仓保留产品侧关注面目录的对应守卫，两面互补。
/// </para>
/// <para>
/// <b>为什么必须机械锁定</b>：S1 的根因是"四处各自实现形态认知、漏一处即静默失效"。
/// 收敛后判定原语（<c>IsJsonString</c>/<c>IsJsonArray</c>）只住在 <c>ToolArgumentNormalizer</c>，
/// 其余位置只能薄委托——本守卫机械兜底该收敛。
/// </para>
/// </remarks>
public class ToolArgumentShapeContractGuards
{
    /// <summary>
    /// <c>JsonValueKind.String</c> 的参数值形态认知只允许出现在中立唯一认知中心 <c>ToolArgumentNormalizer</c>。
    /// </summary>
    [Fact]
    public void JsonValueKindString_ShouldOnlyAppearInNormalizerAndAllowedFiles()
    {
        // 中立侧扫描面：B 组形态认知相关类型全部住在 Mud.AI.Agent/Tools（含 Args/ 子目录）。
        var scanDir = Path.Combine(GetSolutionRoot(), "Mud.AI.Agent", "Tools");

        // 反缩水（§8.2「扫描面悄悄缩水」）：扫描目录必须真实存在——目录被删/改名会让枚举
        // 静默退化成空集，"无违规"恒真（假绿）。
        Directory.Exists(scanDir).Should().BeTrue($"扫描目录必须存在（否则扫描面静态缩水 → 假绿）：{scanDir}");

        // 白名单（迁移后为**单文件**）：形态判定原语（IsJsonString/IsJsonArray）住在
        // ToolArgumentNormalizer 内，其余位置（取值门面、摘要、分页/干跑助手）只能薄委托——
        // "同一入参在不同工具里被判成不同形态"是 S1 的同类根因，收敛后该形态在源码层不可能再出现第二处。
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "ToolArgumentNormalizer.cs", // 唯一形态认知中心（含判定原语）
        };

        var scannedPaths = Directory
            .EnumerateFiles(scanDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        // 反缩水：扫描面不得为空（目录存在但枚举不到源码 = 判据恒真 → 假绿）。
        scannedPaths.Should().NotBeEmpty("扫描面为空说明路径解析坏了（本守卫会恒绿）");

        // 反缩水：白名单文件必须实际落在扫描面内——文件名从扫描面消失（迁移出目录）会让
        // "允许一处"退化为"允许任意处"（白名单恒不命中 ⇒ 假绿）。
        foreach (var allowed in allowedFiles)
        {
            scannedPaths.Should().Contain(
                p => Path.GetFileName(p) == allowed,
                $"白名单文件 {allowed} 必须在扫描面内（否则该白名单失效 → 假绿）");
        }

        var offenders = scannedPaths
            .Where(p => !allowedFiles.Contains(Path.GetFileName(p)))
            .Where(p => File.ReadAllText(p).Contains("JsonValueKind.String", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            "JsonValueKind.String 的形态认知只允许出现在中立唯一认知中心 ToolArgumentNormalizer；"
            + "取值/摘要位置必须薄委托 IsJsonString/IsJsonArray——各自 switch 是 S1 的根因"
            + "（漏一处即静默失效）——违规文件: " + string.Join(", ", offenders));
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
