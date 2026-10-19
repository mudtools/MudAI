// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Abstractions.Observability;

/// <summary>
/// <b>工具面遥测扩展端口</b>（<see cref="IAgentTelemetry"/> 的能力拆分）：工具调用 / RAG 检索 / HITL 审批三类埋点的调用入口。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是继承而不是给 <see cref="IAgentTelemetry"/> 加成员</b>：主端口已随包分发，加成员即 breaking 全部既有实现
/// （含下游已有的产品遥测实现）。能力拆分的形态是"新接口继承旧接口 + 调用点 <c>as</c> 空安全消费"——
/// 未实现新端口的既有遥测实现零埋点零异常，新实现按需 opt-in。
/// </para>
/// <para>
/// <b>维度纪律</b>（与主端口同）：租户/会话键只进 Span 属性，<b>不得</b>进 Metrics tag；本端口各方法的形参里
/// 只有 <c>toolName</c> 是维度值（基数 = 工具数，有界），<c>durationMs</c>/<c>attempt</c>/<c>chunks</c> 等是数值样本。
/// </para>
/// </remarks>
public interface IAgentToolTelemetry : IAgentTelemetry
{
    /// <summary>记录一次工具调用完成（含被转换为结构化错误的失败——失败样本不得缺席）。</summary>
    /// <param name="toolName">工具名（有界维度）。</param>
    /// <param name="isWrite">是否写工具（读写分维度聚合；写工具的失败率与只读工具不同量级）。</param>
    /// <param name="durationMs">耗时毫秒（数值样本，不设上界断言）。</param>
    /// <param name="truncated">结果是否被截断（出口标记；监控截断率）。</param>
    /// <param name="errorCategory">失败分类字面量（可空 = 成功；取值见 <c>ToolErrorText.CategoryLiteral</c>）。</param>
    void ToolInvoked(string toolName, bool isWrite, long durationMs, bool truncated, string? errorCategory);

    /// <summary>记录一次工具重试发生（重试循环所有者调用；中立 <c>ToolRetryPolicy</c> 为无环判定件，不在其内埋点）。</summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="attempt">第几次重试（1 起）。</param>
    void ToolRetried(string toolName, int attempt);

    /// <summary>记录一次 RAG 检索完成（召回条数与注入字符数——注入量为 0 时说明三级闸全拦截，须可观测）。</summary>
    /// <param name="chunks">召回并注入的条数。</param>
    /// <param name="injectedChars">注入 prompt 的字符数。</param>
    void RetrievalPerformed(int chunks, int injectedChars);

    /// <summary>记录一次审批请求已提交宿主（写工具停在「等待确认」——HITL 入口的可观测点）。</summary>
    /// <param name="toolName">工具名。</param>
    void ApprovalRequested(string toolName);

    /// <summary>记录一次人工审批已解决（批准或拒绝都记——拒绝率与等待时长是 HITL 健康度指标）。</summary>
    /// <remarks>调用归属是<b>宿主的批准入口</b>（收到人类响应的那一处）：等待时长只有宿主知道；中立侧 <c>AgentApprovalContinuation</c> 只看到"已批准"结论、取不到起止时刻，故不在那里伪造 0。</remarks>
    /// <param name="toolName">工具名。</param>
    /// <param name="approved">是否批准（<see langword="false"/> = 拒绝结果回填模型）。</param>
    /// <param name="waitMs">人类等待毫秒（数值样本；无法取得时传 0 并注明，不得杜撰）。</param>
    void ApprovalResolved(string toolName, bool approved, long waitMs);
}
