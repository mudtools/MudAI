// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
//  任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.AI.Abstractions.Tools.Errors;

namespace Mud.AI.Agent.Tools.Errors;

/// <summary>工具错误大类（8 类闭集）：对齐官方 <c>errs/ERROR_CONTRACT.md</c> 的 9 类语义后，收敛为工具面所需。</summary>
public enum ToolErrorCategory
{
    /// <summary>参数验证失败：参数缺失/类型错/filter、sort 文法错——模型应换参数而非重试。</summary>
    Validation,

    /// <summary>策略拒绝：风险超限/身份不匹配/工具未启用——模型应放弃或换工具。</summary>
    Policy,

    /// <summary>授权拒绝：授权器拒绝/缺少 scope——模型应放弃或改用只读方案。</summary>
    Authorization,

    /// <summary>待用户确认（HITL）：需人工介入，既不是"权限被拒"也不是"参数错"。</summary>
    Confirmation,

    /// <summary>可重试：HTTP 5xx / 429 / 网络异常——模型可稍后重试同一调用。</summary>
    Retryable,

    /// <summary>下游业务错误（code != 0 且非权限段位）——模型应报告或换参数。</summary>
    Api,

    /// <summary>内部错误：未预期异常/净化器拒绝——模型应报告追踪号。</summary>
    Internal,

    /// <summary>内容安全阻断：工具结果命中内容安全规则——模型应放弃。</summary>
    ContentSafety,
}

/// <summary>工具错误子类常量：与 <see cref="ToolErrorCategory"/> 配对使用，提供更细粒度的错误原因；新增常量须评审
/// + 守卫断言（消费方按字符串匹配，改动即破坏契约）。</summary>
public static class ToolErrorSubtype
{
    // Validation
    /// <summary>参数未通过校验（类型/格式非法）。</summary>
    public const string InvalidArgs = "invalid_args";
    /// <summary>缺少必填参数。</summary>
    public const string MissingRequired = "missing_required";
    /// <summary>参数形态不符合声明（如应为数组却传入标量）。</summary>
    public const string ShapeMismatch = "shape_mismatch";

    // Policy
    /// <summary>触达策略上限（预算/速率/深度等）。</summary>
    public const string RiskExceeded = "risk_exceeded";
    /// <summary>身份维度不匹配（租户/用户维度校验未通过）。</summary>
    public const string IdentityMismatch = "identity_mismatch";
    /// <summary>该工具未被允许（策略未列明）。</summary>
    public const string ToolNotAllowed = "tool_not_allowed";

    // glob 策略轴：域级拒绝与写工具拒绝各占一个 reason_code。
    /// <summary>域级策略拒绝（<c>ToolPolicy:Domains</c> 命中；比"工具未列明"更精确）。</summary>
    public const string DomainNotAllowed = "domain_not_allowed";
    /// <summary>写工具被策略拒绝（命中 Deny glob 或未列明于 Allow；与只读拒绝区分，便于宿主定位）。</summary>
    public const string WriteNotAllowed = "write_not_allowed";

    // ⚠️ **不得**新增 `risk_not_annotated`（官方 taxonomy 有、本仓不适用）：所有工具的风险都由生成器
    //    产出（产品侧 Profile 的危险词注解 + 人工注解），不存在"无注解的工具"这一形态——
    //    补一个永不产出的常量就是幽灵子类（本仓明文禁止）；若将来出现"运行期动态注册的工具"，
    //    再补该码并附产出点。

    /// <summary>本轮工具调用预算已用尽——不可重试，模型应基于已有信息直接回答。</summary>
    public const string ToolBudgetExceeded = "tool_budget_exceeded";

    // Authorization
    /// <summary>平台侧权限不足（forbidden）：换工具或收窄参数可能成功，重试原请求无意义。</summary>
    public const string AuthorizationDenied = "authorization_denied";

    /// <summary>用户未授权（用户令牌就绪语义闭环，与 forbidden 权限不足语义区分）：无有效令牌且无 refresh token——模型应转告用户去完成授权，不得换工具或重试。</summary>
    public const string UserAuthRequired = "user_auth_required";
    /// <summary>用户令牌续期失败：下游 401 且 TokenRecoveryExecutor 恢复后仍失败——探测漏网路径的兜底出口。</summary>
    public const string UserTokenRefreshFailed = "user_token_refresh_failed";

    // ⚠️ 此处**不得**新增 `missing_scope`（平台权限未开通）：全仓无产出点，且平台侧无可靠判据——
    //    权限段的同一业务码在工具层/WebSocket 层/Abstractions 层解释不同，拿它当"控制台未开通权限"是猜测。
    //    依据与复评条件（需实证样本 + 生产者守卫）见 documents/AIAgent/工具错误契约.md 的「子类」表。

    // Confirmation
    /// <summary>需要用户人工确认后才能执行的写操作——模型应转告用户，不得重试或改道。</summary>
    public const string NeedsUserConfirmation = "needs_user_confirmation";

    // Retryable
    /// <summary>平台限流：按平台指示的节奏退避后重试。</summary>
    public const string RateLimited = "rate_limited";
    /// <summary>平台侧瞬时故障：直接重试通常可恢复。</summary>
    public const string Transient = "transient";
    /// <summary>下游超时：可重试，但应检查参数是否引发长耗时。</summary>
    public const string Timeout = "timeout";

    // Api
    /// <summary>上游 API 返回非预期失败（产品业务失败经读数分类后的兜底出口）。</summary>
    public const string UpstreamError = "upstream_error";

    // Internal
    /// <summary>引擎内部未分类异常——分类器的兜底出口，出现即应补分类分支。</summary>
    public const string Unexpected = "unexpected";

    // ⚠️ 此处**不得**新增 `sanitizer_rejected`：出站净化只有"整形/截断/标注"
    //    （`ToolResultSanitizer` / `ToolResultContentSafety`），不存在"净化器拒绝"路径；
    //    等内容安全轴真的出现硬拒绝时再新增（须附产出点）。依据见 documents/AIAgent/工具错误契约.md。

    // ContentSafety
    /// <summary>入站内容被判定为提示注入并阻断——模型应放弃该来源的输入。</summary>
    public const string InjectedContentBlocked = "injected_content_blocked";
}

/// <summary>工具错误分类器：纯函数——从异常类型 / 传输故障状态码（中立端口 <see cref="ITransportFault"/>）映射到
/// <see cref="ToolErrorCategory"/> 与 <see cref="ToolErrorSubtype"/>，无配置，<b>不认识任何产品的码表</b>。</summary>
/// <remarks>
/// <b>授权拒绝与参数错误必须可区分</b>——这是模型自我修正（重试 or 换参数 or 放弃）的前提。
/// <b>产品业务码不在这里（独立产品线纪律）</b>：产品码 → 段位的映射<b>由产品侧</b>在读取自家信封时完成（读数载体 =
/// <see cref="ToolApiFailure"/>），中立层只消费已分类的结果；本类<b>不持有任何产品码表</b>——否则中立包被上游化时
/// 码表会被一并带走，且只能由"知道全部产品"的中立包维护。
/// </remarks>
public static class ToolErrorClassifier
{
    /// <summary>从异常类型分类（执行链 catch 路径；取消异常由调用方先行放行，不入此分类）。</summary>
    /// <param name="exception">被分类的异常。</param>
    /// <param name="transportFault">传输层故障（产品中立端口）：由产品适配层把自身的传输异常适配后传入；<see langword="null"/> 表示该异常不携带传输层语义。</param>
    /// <remarks>
    /// <b>为什么状态码经端口传入、而不是判定异常类型</b>：本类住在产品中立的 <c>Mud.AI.Agent</c>，独立产品线纪律
    /// 要求它<b>不得认识任何具体 HTTP 组件的异常类型</b>（判定具体 HTTP 客户端异常类型即违反该纪律）；"状态码"
    /// 是唯一的跨产品事实，故经 <see cref="ITransportFault"/> 由适配层供给。<b>漏接适配的后果</b>：传输异常会落到
    /// <see cref="System.Net.Http.HttpRequestException"/> 兜底分支（<c>retryable/transient</c>）——403 会被误判为
    /// 可重试，故该接线由消费方的端到端用例锁定（不是靠这里的注释）。
    /// </remarks>
    public static (ToolErrorCategory Category, string Subtype) Classify(
        Exception exception, ITransportFault? transportFault = null)
    {
        if (transportFault is not null)
        {
            return ClassifyHttpStatus(transportFault.StatusCode);
        }

        return exception switch
        {
            ArgumentException => (ToolErrorCategory.Validation, ToolErrorSubtype.InvalidArgs),
            System.Net.Http.HttpRequestException => (ToolErrorCategory.Retryable, ToolErrorSubtype.Transient),
            TimeoutException => (ToolErrorCategory.Retryable, ToolErrorSubtype.Timeout),
            _ => (ToolErrorCategory.Internal, ToolErrorSubtype.Unexpected),
        };
    }

    /// <summary>HTTP 状态段位：5xx/429 可重试，401/403 权限类，其余业务错误。</summary>
    private static (ToolErrorCategory Category, string Subtype) ClassifyHttpStatus(int? statusCode) => statusCode switch
    {
        null => (ToolErrorCategory.Api, ToolErrorSubtype.UpstreamError),
        (int)System.Net.HttpStatusCode.Forbidden or (int)System.Net.HttpStatusCode.Unauthorized
            => (ToolErrorCategory.Authorization, ToolErrorSubtype.AuthorizationDenied),
        429 => (ToolErrorCategory.Retryable, ToolErrorSubtype.RateLimited),
        >= 500 => (ToolErrorCategory.Retryable, ToolErrorSubtype.Transient),
        _ => (ToolErrorCategory.Api, ToolErrorSubtype.UpstreamError),
    };
}
