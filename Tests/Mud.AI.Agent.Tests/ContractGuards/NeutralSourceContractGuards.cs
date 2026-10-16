// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.ContractGuards;

/// <summary>
/// 中立源码侧的契约守卫（迁移自飞书仓 <c>AgentContractGuards</c> 的<b>中立半边</b>）：
/// 断言锚点随实现迁入 <c>Mud.AI.Agent</c> / <c>Mud.AI.Abstractions</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移说明</b>：源守卫 <c>AgentContractGuards</c>（飞书仓）是跨仓事实——产品侧
/// （<c>FeishuAgentOptions</c> / <c>FeishuAgent</c> / <c>FeishuToolBinding</c>）与中立侧
/// （回合管线 / 摘要器 / 会话门面 / 错误文案）各持一半。本文件承载<b>中立半边</b>；
/// 产品半边留在飞书仓同名守卫里（两面互补，缺一即覆盖不全）。
/// </para>
/// <para>
/// <b>为什么必须搬</b>：这些断言的锚点全在中立文件——留在飞书仓会在源目录迁出后对着不存在的文件空扫（假绿）。
/// </para>
/// </remarks>
public class NeutralSourceContractGuards
{
    /// <summary>
    /// 工具执行上下文 <c>Begin</c> 必须前置于上下文装配（P0-1）。
    /// </summary>
    /// <remarks>
    /// 顺序类缺陷最易在重构中复活，且症状是「静默零注入 + 仅一条 Warning」——
    /// 断言相对顺序（不是绝对行号），行号漂移不会失效。
    /// <b>N4 改锚</b>：回合机制已上移中立 <c>ConversationalTurnPipeline</c>——断言锚点随实现迁移
    /// （旧锚读产品薄壳会变成"断言在跑但读旧锚"的假绿）。
    /// 行为锚（替身 ITurnScopeFactory + 替身 IContextAssembler 记录调用序）由
    /// <c>Tests/Mud.AI.Agent.Tests/Events/ConversationalTurnPipelineTests</c> 承载。
    /// </remarks>
    [Fact]
    public void ToolContext_Begin_ShouldPrecedeContextAssembly()
    {
        var source = ReadSource("Mud.AI.Agent", "Events", "ConversationalTurnPipeline.cs");

        var beginIndex = source.IndexOf("BeginToolScope(new TurnScopeContext", StringComparison.Ordinal);
        var assembleCallIndex = source.IndexOf(
            "AssembleUserMessageDefaultAsync(request", StringComparison.Ordinal);

        beginIndex.Should().BeGreaterThan(-1, "必须先建立工具执行上下文");
        assembleCallIndex.Should().BeGreaterThan(-1, "管线必须装配用户消息");
        beginIndex.Should().BeLessThan(assembleCallIndex,
            "工具上下文必须先于上下文装配生效——否则知识/引用类装配器读不到租户键，RAG 注入模式永久静默失效");
    }

    /// <summary>
    /// Phase 2 新增配置属性（<c>SummaryThreshold</c>）的<b>消费点</b>（R5 规则 2）——中立半边。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：跨仓双清单拆成两半——飞书仓保留<b>投影点</b>
    /// （<c>FeishuAgentOptions.ToRuntimeOptions</c>）正向锚；中立 <c>ConversationSummarizer</c> 对
    /// <c>SummaryThreshold</c> 的<b>消费点</b>随实现迁入本仓。双清单（消费点 + 投影点）必须<b>同批</b>断言，
    /// 否则「投影漏字段」这类断链会静默生效（配置写了不生效、且不报红）。
    /// </remarks>
    [Fact]
    public void FeishuAgentOptions_Phase2Properties_ShouldHaveRealConsumptionPoints()
    {
        var summarizer = ReadSource("Mud.AI.Agent", "Conversations", "ConversationSummarizer.cs");

        summarizer.Should().Contain(
            "SummaryThreshold",
            "AgentRuntimeOptions.SummaryThreshold 必须在会话摘要器中被消费（渐进式摘要阈值，Phase 2 §3.2）");
    }

    /// <summary>
    /// 配置属性（<c>MaxHistoryTokens</c>）的<b>消费点</b>——中立半边。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：<c>MaxHistoryTokens</c> 的消费点（中立 <c>ConversationSummarizer</c>）随实现迁入本仓；
    /// 飞书仓保留产品侧三条 <c>ImConversationOptions</c> 消费点正向锚。
    /// </remarks>
    [Fact]
    public void FeishuAgentOptions_Phase12Properties_ShouldHaveRealConsumptionPoints()
    {
        var summarizer = ReadSource("Mud.AI.Agent", "Conversations", "ConversationSummarizer.cs");

        summarizer.Should().Contain(
            "MaxHistoryTokens",
            "AgentRuntimeOptions.MaxHistoryTokens 必须在会话摘要器中被消费（token 维度判定，P2D-3a）");
    }

    /// <summary>
    /// <b>反向断言</b>：<see cref="AgentRuntimeOptions"/> 的每个中立旋钮都必须有真实消费点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须"反向"</b>：<c>ToRuntimeOptions_ShouldProjectEveryNeutralKnob</c> 只证明
    /// 「产品 → 中立」赋值齐全；「中立 → 消费者」方向没有机械锁。新增一个中立旋钮却无人读取时，
    /// 投影断言照样绿——症状是「配置写了不生效」（配置面治理 R5 规则 2 的反向形态）。
    /// </para>
    /// <para>
    /// <b>判据</b>：对 <see cref="AgentRuntimeOptions"/> 的每个 public 属性名，中立源码集中必须存在
    /// <c>.<属性名></c> 成员访问；<b>定义处</b>除外——否则属性名自证即命中，反向断言恒真（假绿）。
    /// <b>迁移拆分</b>：产品适配层消费的中立旋钮（本仓无中立消费点）从判据中减去，其「产品 → 中立」
    /// 投影由飞书仓 <c>FeishuAgentOptionsTests</c> 的正向锚覆盖。
    /// </para>
    /// </remarks>
    [Fact]
    public void AgentRuntimeOptions_EveryProperty_ShouldHaveRealConsumptionPoint()
    {
        var root = GetSolutionRoot();
        var projects = new[] { "Mud.AI.Agent", "Mud.AI.Abstractions" };

        // 排除「属性名定义处」——否则属性名自证即命中，掩盖"没人读"。
        var excludedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "AgentRuntimeOptions.cs", // 定义处
        };

        // 产品适配层消费的中立旋钮（本仓中立面按设计无消费点）：投影点（产品 → 中立）由飞书仓
        // FeishuAgentOptionsTests.ToRuntimeOptions... 正向锚覆盖，故从本仓反向断言中减去。
        var productAdapterConsumed = new[]
        {
            "MaxStreamChunkLength", "MaxConcurrentTurns", "TurnQueueTimeoutSeconds", "ToolRetry",
        };

        var sources = projects
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !excludedFiles.Contains(Path.GetFileName(p)))
            .ToList();

        // 反缩水：扫描面与反射面任一为空，本断言都恒真（假绿）。
        sources.Should().NotBeEmpty("扫描面为空说明目录/路径解析坏了");
        var propertyNames = typeof(AgentRuntimeOptions).GetProperties().Select(static p => p.Name).ToList();
        propertyNames.Should().NotBeEmpty("反射面为空说明类型解析坏了");

        // 反缩水：被减去的名称必须是真实的中立旋钮——减错名会让真幽灵逃逸。
        foreach (var name in productAdapterConsumed)
        {
            propertyNames.Should().Contain(name, "被减去的名称必须是真实的中立旋钮");
        }

        var texts = sources.Select(File.ReadAllText).ToList();
        var missing = propertyNames
            .Where(name => !productAdapterConsumed.Contains(name, StringComparer.Ordinal))
            .Where(name => !texts.Any(text => text.Contains("." + name, StringComparison.Ordinal)))
            .ToList();

        missing.Should().BeEmpty(
            "AgentRuntimeOptions 的每个中立旋钮都必须被中立源码真实读取（.<属性名> 成员访问）——"
            + "投影了却无人读 = 配置写了不生效。缺少消费点的属性：{0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// MAF 状态键的中立半边：中立装配工厂把历史状态键以参数装入 Provider、
    /// 中立 Agents/ 目录与中立会话门面不得硬编码任何产品状态键前缀。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：产品侧（状态键字面量 <c>feishu.agent.*</c>、经 <c>AgentAssemblyFactory.Create</c>
    /// 装配、把键注入门面）留在飞书仓；中立侧的「键以参数接收 + 零产品前缀」随实现迁入本仓。
    /// MAF <c>ChatClientAgent.ValidateAndCollectStateKeys</c> 在运行期对重复状态键直接抛异常。
    /// </remarks>
    [Fact]
    public void AgentStateKeys_ShouldBeUniqueAndConsumed()
    {
        // A1（v3）：中立工厂把键以参数装入 Provider（不得硬编码字面量）——参数名即产品→中立的注入面。
        ReadSource("Mud.AI.Agent", "Agents", "AgentAssemblyFactory.cs").Should().Contain(
            "StateKey = historyStateKey",
            "历史状态键必须被中立工厂真实装入 ChatHistoryProvider");

        // A-4（v3）：负向断言面覆盖整个中立 Agents/ 目录——运行期编织基类 ObservedDelegatingAgent
        // 同样是「顺手塞产品状态键」的高危点，单文件锚在它诞生后即失守。
        var neutralAgentsDir = Path.Combine(GetSolutionRoot(), "Mud.AI.Agent", "Agents");
        Directory.Exists(neutralAgentsDir).Should().BeTrue(
            $"扫描目录必须存在（否则扫描面静默缩水 → 假绿）：{neutralAgentsDir}");
        var neutralAgentFiles = Directory.EnumerateFiles(neutralAgentsDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains("obj") && !p.Contains("bin"))
            .ToList();
        neutralAgentFiles.Should().NotBeEmpty("扫描面为空是假绿（目录存在但枚举不到源码）");
        foreach (var neutralAgentFile in neutralAgentFiles)
        {
            File.ReadAllText(neutralAgentFile).Should().NotContain("\"feishu.",
                $"中立 Agents/ 不得硬编码任何产品状态键前缀（{Path.GetFileName(neutralAgentFile)}）——字面量归产品侧所有");
        }

        // 机制（读取状态袋）在**中立门面**里——门面必须以参数接收该键，
        // 且绝不能出现任何 `feishu.` 前缀（否则等于把产品命名面焊进包族底层，上游化时它会跟着一起走）。
        var storeSource = ReadSource("Mud.AI.Agent", "Sessions", "AgentSessionStore.cs");
        storeSource.Should().Contain("_historyStateKey", "中立门面必须以参数接收历史状态键（D4：来源参数化）");
        storeSource.Should().NotContain("\"feishu.",
            "中立包不得硬编码任何产品状态键前缀——键的字面量归产品侧所有");
    }

    /// <summary>
    /// SDK 不签发/不校验任何确认凭据——模型可见载荷中
    /// <b>绝不</b>出现 confirm_token、令牌明文或自批复指示——中立半边。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：判据 ①（模型可见文案的凭据零命中）的锚点原文案已随错误契约族中立化上移
    /// <c>Mud.AI.Agent/Tools/ToolErrorText.cs</c>，该半边的括号配平切片断言随实现迁入本仓；
    /// 判据 ②（宿主批准通道）仍是飞书侧契约，留在飞书仓绑定守卫里。
    /// </remarks>
    [Fact]
    public void ConfirmationToken_ShouldNeverEnterModelVisiblePayload()
    {
        var source = ReadSource("Mud.AI.Agent", "Tools", "ToolErrorText.cs");

        var needsConfirmationBranch = source.IndexOf(
            "ToolErrorCategory.Confirmation =>", StringComparison.Ordinal);
        needsConfirmationBranch.Should().BeGreaterThan(-1);

        // 括号配平切片：从 `=>` 后的 `$"` 开始，配平到语句结束（分号或下一个 case）——
        // 被守护文案跨两行，单行切片会漏掉续行内容。
        var sliceStart = source.IndexOf('"', needsConfirmationBranch);
        sliceStart.Should().BeGreaterThan(-1, "NeedsConfirmation 分支必须有字符串字面量");
        var depth = 0;
        var sliceEnd = sliceStart;
        for (var i = sliceStart; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '(') depth++;
            else if (ch == ')') { if (depth == 0) break; depth--; }
            else if (ch == ';' && depth == 0) { sliceEnd = i; break; }
            sliceEnd = i;
        }
        var branch = source[needsConfirmationBranch..(sliceEnd + 1)];

        branch.Should().NotContain("确认令牌", "模型可见文案不得携带/提示确认令牌（R2-1）");
        branch.Should().NotContain("confirm_token", "不得再指示模型以 confirm_token 重试（那等于把批准要素交给模型）");
        branch.Should().NotContain("confirmation_token", "不得以任何变体形式回灌令牌提示");
        branch.Should().Contain("需要用户确认", "待确认语义必须保留（三态文案不得退化）");
    }

    /// <summary>
    /// R3-02 自证报红：合成缺陷源码（令牌提示放在续行）必须被发现——证明上面的括号配平切片判据
    /// 真的覆盖跨行内容（否则该判据是假门禁）。
    /// </summary>
    [Fact]
    public void ConfirmationToken_Guard_ShouldDetectCredentialInContinuationLine()
    {
        // 合成缺陷源码：NeedsConfirmation 分支跨两行，续行含 confirm_token。
        var defectiveSource = @"
        ToolErrorCategory.Confirmation => $""[tool_error] {toolName} (needs_confirmation): {reason}——该操作需要用户确认后方可执行；""
            + ""可使用 confirm_token=xxx 重试"",
";
        var needsConfirmationBranch = defectiveSource.IndexOf(
            "ToolErrorCategory.Confirmation =>", StringComparison.Ordinal);
        needsConfirmationBranch.Should().BeGreaterThan(-1);

        // 用与主守卫相同的括号配平切片逻辑。
        var sliceStart = defectiveSource.IndexOf('"', needsConfirmationBranch);
        var depth = 0;
        var sliceEnd = sliceStart;
        for (var i = sliceStart; i < defectiveSource.Length; i++)
        {
            var ch = defectiveSource[i];
            if (ch == '(') depth++;
            else if (ch == ')') { if (depth == 0) break; depth--; }
            else if (ch == ';' && depth == 0) { sliceEnd = i; break; }
            sliceEnd = i;
        }
        var branch = defectiveSource[needsConfirmationBranch..(sliceEnd + 1)];

        branch.Should().Contain("confirm_token",
            "合成缺陷源码的续行含 confirm_token——切片必须覆盖跨行内容（自证报红）");
    }

    /// <summary>
    /// R2-5：中立内存会话存储必须清扫不再被读取的过期条目——中立半边。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：清扫机制（<c>SweepExpired</c> / <c>SweepThreshold</c>）随实现迁入中立包
    /// <c>Mud.AI.Agent.Sessions.MemorySessionStore</c>，其回收路径断言随实现迁入本仓；
    /// 飞书仓保留产品壳的「桥接而非重抄机制」正/负向锚。
    /// </remarks>
    [Fact]
    public void MemorySessionStore_ShouldSweepExpiredEntries()
    {
        var source = ReadSource("Mud.AI.Agent", "Sessions", "MemorySessionStore.cs");

        source.Should().Contain("SweepExpired", "必须有过期回收路径（未读键永不到达读侧软过期）");

        var save = source.IndexOf("public Task SaveAsync(", StringComparison.Ordinal);
        save.Should().BeGreaterThan(-1);
        var body = source[save..];
        body.Should().Contain("SweepExpired()", "回收必须由写入路径触发（惰性分摊，不引入后台线程/定时器）");
        body.Should().Contain("SweepThreshold", "必须按阈值分摊（均摊 O(1)），不得每次写入都全量扫描");
    }

    /// <summary>
    /// <c>MaxHistoryMessages</c> 的<b>消费点</b>——中立工厂的裁剪窗注入（中立半边）。
    /// </summary>
    /// <remarks>
    /// <b>迁移拆分</b>：中立工厂的消费点（<c>MessageCountingChatReducer</c> 裁剪窗）随实现迁入本仓；
    /// 飞书仓保留「文档语义 + 产品侧注入」两半正向锚。
    /// </remarks>
    [Fact]
    public void HistoryReducerSemantics_ShouldBeDocumented()
    {
        ReadSource("Mud.AI.Agent", "Agents", "AgentAssemblyFactory.cs").Should().Contain(
            "new MessageCountingChatReducer(maxHistoryMessages)",
            "MaxHistoryMessages 必须被真实消费（中立工厂把上限装入 MessageCountingChatReducer 裁剪窗）");
    }

    /// <summary>
    /// R2-2 / R4-7：会话恢复必须<b>急切</b>校验历史状态（惰性反序列化纳入守护区），
    /// 且坏值自愈的 catch 面<b>不得是枚举白名单</b>。
    /// </summary>
    /// <remarks>
    /// R4-7 订正：原判据锁定字面量 <c>catch (Exception ex) when (ex is JsonException</c>——
    /// 它固化了"枚举白名单"这一缺陷形态（白名单不含 FormatException/KeyNotFoundException/
    /// IndexOutOfRangeException/NullReferenceException ⇒ 残余异常类型逃出守护区 ⇒ 坏值永不删除）。
    /// 判据改为「catch 面等价于 <c>when (ex is not OperationCanceledException)</c>」，并<b>显式禁止</b>白名单写法复活。
    /// 实现已搬到中立门面 <c>Mud.AI.Agent/Sessions/AgentSessionStore.cs</c>——锚点必须跟着实现走。
    /// </remarks>
    [Fact]
    public void SessionRestore_ShouldEagerlyValidateHistoryState()
    {
        var source = ReadSource("Mud.AI.Agent", "Sessions", "AgentSessionStore.cs");

        var restoreMethod = source.IndexOf("GetOrCreateAsync(string key", StringComparison.Ordinal);
        restoreMethod.Should().BeGreaterThan(-1);
        var body = source[restoreMethod..];

        var eagerRead = body.IndexOf("TryGetInMemoryChatHistory", StringComparison.Ordinal);
        var guard = body.IndexOf("catch (Exception ex) when (ex is not OperationCanceledException)", StringComparison.Ordinal);

        body.Should().NotContain("ex is JsonException or ArgumentException",
            "坏值自愈的 catch 面必须是收口形态（非枚举白名单）——白名单会漏掉残余异常类型，"
            + "使坏载荷每次重投递都在守护区外再抛（R4-7）");

        eagerRead.Should().BeGreaterThan(-1, "会话恢复必须急切触发一次历史状态读取（惰性解析否则逃逸守护区）");
        guard.Should().BeGreaterThan(-1,
            "catch 面必须收口为 `when (ex is not OperationCanceledException)`（R4-7）");
        eagerRead.Should().BeLessThan(guard,
            "急切读取必须落在坏值自愈的 catch 面**之内**——否则内层损坏在摘要器/Provider 内抛出，坏值永不删除");

        // 清库必须用 CancellationToken.None（自愈动作不得被调用方取消中断，R4-7 第 2 条语义）。
        body.Should().Contain("DeleteAsync(key, CancellationToken.None)",
            "清库不得用调用方 token：调用方一取消就留下坏值，会话在其 TTL 内继续毒化");
    }

    /// <summary>
    /// R3-02 行为断言：StructuredError 全部分支的<b>字符串字面量</b>经正则断言零命中凭据类信息。
    /// </summary>
    /// <remarks>
    /// 本测试工程可反射驱动中立实现；本守卫退化为"只扫描字符串字面量、剥离注释"的源码断言——
    /// 注释中的"确认令牌"（解释为什么删除了令牌提示）不得触发假红。R3-10 纪律：文本断言仅限无法行为化的项。
    /// </remarks>
    [Fact]
    public void StructuredError_NeedsConfirmation_ShouldNotLeakCredentialInBehavior()
    {
        var source = ReadSource("Mud.AI.Agent", "Tools", "ToolErrorText.cs");

        // 提取 StructuredError 方法体整体（从方法签名到下一个方法声明）。
        // v2.3 P1-D：visibility-agnostic——锚点只匹配 `static string StructuredError(…)`，改名/改可见性都不会让守卫假红。
        var methodStart = source.IndexOf(
            "static string StructuredError(string toolName, ToolErrorCategory category", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThan(-1, "StructuredError 方法必须存在");
        var methodEnd = source.IndexOf(
            "\n    static string StructuredError(string toolName, int? apiCode", StringComparison.Ordinal);
        if (methodEnd < 0)
        {
            methodEnd = source.IndexOf(
                "\n    public static string StructuredError(string toolName, int? apiCode", StringComparison.Ordinal);
        }
        if (methodEnd < 0)
        {
            // fallback: 找下一个方法声明
            methodEnd = source.IndexOf("\n    /// <summary>", methodStart + 100, StringComparison.Ordinal);
        }
        methodEnd.Should().BeGreaterThan(methodStart, "必须能定位到 StructuredError 方法体结束");
        var methodBody = source[methodStart..methodEnd];

        // 剥离注释行（// 开头的行）——注释中解释"为什么删除了确认令牌"不应触发假红。
        var lines = methodBody.Split('\n');
        var codeOnly = string.Join('\n', lines.Where(static l =>
        {
            var trimmed = l.AsSpan().TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("///", StringComparison.Ordinal);
        }));

        // 行为断言：剥离注释后的代码体（含所有分支文案字面量）经正则零命中凭据类信息。
        var credentialPattern = new System.Text.RegularExpressions.Regex(
            @"(confirm_token|confirmation_token|确认令牌|token\s*=)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        credentialPattern.IsMatch(codeOnly).Should().BeFalse(
            "StructuredError 的全部分支文案（字符串字面量）不得出现凭据类信息（已剥离注释）");

        // 待确认语义必须存在于方法体中。
        codeOnly.Should().Contain("需要用户确认", "待确认语义必须保留");
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
