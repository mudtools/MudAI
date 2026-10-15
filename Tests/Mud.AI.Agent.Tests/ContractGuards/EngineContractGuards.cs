// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// 「三段分离」的<b>引擎段</b>守卫（第七式）：中立引擎不得引用产品契约
/// （信号量 / 并发字典 / 等待与回收实现归引擎，产品异常语义与契约适配归壳段）。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：源守卫 <c>EngineShellContractGuards</c> 是跨仓事实（壳 ↔ 引擎配对），
/// 迁移后引擎段落在新仓、薄壳段留在飞书仓。本文件只保留<b>引擎侧</b>断言：
/// （a）引擎文件必须住在中立工程目录；（b）引擎源码不含产品词汇。
/// 壳侧（"壳必须委托引擎且不自持引擎语义"）由飞书仓 <c>ShellDelegationContractGuards</c> 持有。
/// </para>
/// <para>
/// <b>为什么必须机械锁定</b>：退化方向是<b>单向</b>的——引擎代码顺手写回一行产品类型引用，
/// 编译通过、行为看上去也对，中立性却不再成立（无法独立发布），而<b>没有任何编译期或运行期信号</b>。
/// </para>
/// </remarks>
public class EngineContractGuards
{
    /// <summary>引擎词汇（留待飞书仓壳侧守卫使用；此处仅用于自证判据本身可区分方向）。</summary>
    private static readonly string[] EngineVocabulary =
    [
        "SemaphoreSlim", "ConcurrentDictionary", "Interlocked.Exchange", "WaitAsync(", "Task.Delay",
    ];

    /// <summary>产品词汇（引擎里出现即意味着中立引擎被拉回产品契约 —— P3 上游化的反向破坏）。</summary>
    /// <remarks>
    /// <c>Mud.Feishu</c> 是程序集/命名空间前缀：它一旦出现在引擎源码（注释除外），
    /// 就意味着引擎引用了产品类型，中立性不再成立。
    /// </remarks>
    private static readonly string[] ProductVocabulary =
    [
        "IConversationGate", "ConversationBusyException", "BusyModes", "Mud.Feishu",
    ];

    /// <summary>引擎文件（必须住在中立工程；缺失即"引擎其实还在产品侧"）。</summary>
    private static readonly string[] EngineFiles =
    [
        Path.Combine("Mud.AI.Agent", "Conversations", "KeyedAsyncGate.cs"),
        Path.Combine("Mud.AI.Agent", "Conversations", "ConcurrencyLimiter.cs"),
    ];

    /// <summary>
    /// 反向：中立引擎不得引用产品契约（第七式引擎半边）——引擎被拉回产品类型后独立发布即不成立。
    /// </summary>
    [Fact]
    public void Engine_ShouldStayProductFree()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();
        var scannedEngines = 0;

        foreach (var engine in EngineFiles)
        {
            var enginePath = Path.Combine(root, engine);
            File.Exists(enginePath).Should().BeTrue(
                $"引擎必须住在中立工程（三段分离的引擎段）；缺失即意味着引擎仍留在产品侧：{engine}");

            scannedEngines++;
            var engineSource = File.ReadAllText(enginePath);
            foreach (var token in FindVocabularyOffenders(engineSource, ProductVocabulary))
            {
                offenders.Add($"{engine} 含产品词汇「{token}」（中立引擎被拉回产品契约）");
            }
        }

        scannedEngines.Should().BePositive("扫描面为空是假绿（事实表为空 ⇒ 恒真）");
        offenders.Should().BeEmpty(
            "中立引擎不得引用 IConversationGate / ConversationBusyException / BusyModes / Mud.Feishu* —— "
            + "产品异常语义与契约适配归壳段所有（否则引擎与产品线耦合，无法独立发布）。命中：{0}",
            string.Join(" | ", offenders));
    }

    /// <summary>判据自证：两个方向的词汇判定各自独立（剔注释后匹配）。</summary>
    /// <param name="line">待判行。</param>
    /// <param name="engineInShell">期望「壳里含引擎词汇」。</param>
    /// <param name="productInEngine">期望「引擎里含产品词汇」。</param>
    [Theory]
    [InlineData("private readonly SemaphoreSlim _gate = new(1, 1);", true, false)]
    [InlineData("return await _engine.AcquireAsync(key, ct);", false, false)]
    [InlineData("// 串行语义在 SemaphoreSlim 引擎内（注释是记录）", false, false)]
    [InlineData("public sealed class X : IConversationGate", false, true)]
    [InlineData("throw new ConversationBusyException(key, msg);", false, true)]
    [InlineData("using Mud.Feishu.Abstractions.Conversations;", false, true)]
    [InlineData("// Mud.Feishu 只在文件头注释里（注释是记录）", false, false)]
    [InlineData("var lease = await limit.WaitAsync(timeout, ct);", true, false)]
    public void VocabularyJudges_ShouldIgnoreComments_AndDetectDirectionalViolations(
        string line, bool engineInShell, bool productInEngine)
    {
        FindVocabularyOffenders(line, EngineVocabulary).Length.Should().Be(engineInShell ? 1 : 0);
        FindVocabularyOffenders(line, ProductVocabulary).Length.Should().Be(productInEngine ? 1 : 0);
    }

    /// <summary>判据：源码剔除注释后含任一词汇 token 即命中（注释是记录，不是声明）。</summary>
    private static string[] FindVocabularyOffenders(string source, string[] vocabulary)
        => vocabulary
            .Where(token => StripComments(source).Contains(token, StringComparison.Ordinal))
            .ToArray();

    /// <summary>剔除整行注释与行尾注释（注释是记录，不是声明）。</summary>
    private static string StripComments(string source)
    {
        var kept = new List<string>();
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var commentAt = line.IndexOf("//", StringComparison.Ordinal);
            kept.Add(commentAt >= 0 ? line[..commentAt] : line);
        }

        return string.Join("\n", kept);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mud.AI.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"未能从 {AppContext.BaseDirectory} 向上定位仓库根目录（Mud.AI.slnx）");
    }
}
