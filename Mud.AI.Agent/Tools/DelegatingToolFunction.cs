// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.AI.Agent.Tools;

/// <summary>
/// <b>AOT 安全的工具函数装饰基座</b>：转发 <see cref="Microsoft.Extensions.AI.AIFunction"/> 全部契约成员，装饰逻辑只写在 <see cref="InvokeCoreAsync"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：预算钳制、dry-run 拦截、trace 透传这类"包在执行器外面"的装饰，产品侧只需继承本基座并实现 <see cref="InvokeCoreAsync"/> 一个点——契约成员的转发与反射规避纪律由基座单点承担。
/// </para>
/// <para>
/// <b>MEAI 契约形态（10.x 实测）</b>：框架的调用入口是 <c>InvokeAsync</c>（非虚，内置入参守卫后转调 <c>InvokeCoreAsync</c>）——装饰点因此是 <b><see cref="InvokeCoreAsync"/></b>；本基座将其保持抽象，产品子类只实现这一个点。<b>AOT 纪律</b>：全部转发经虚分派直调内层（<c>Inner.Name</c> 等），<b>零反射</b>——不触碰 <c>GetType()</c>/特性查询/动态代理；异常过滤与结构化错误转换留在产品子类（那是产品语义）。
/// </para>
/// <para>
/// <b>不变量</b>：<see cref="Name"/>/<see cref="Description"/>/<see cref="JsonSchema"/> 必须<b>逐字</b>等于内层——模型侧的工具身份由这三个成员定义，任何"顺手改写"都会让 Schema 与执行器脱钩（守卫见测试）。
/// </para>
/// </remarks>
/// <param name="inner">被装饰的内层函数（工具身份的单一真相源）。</param>
public abstract class DelegatingToolFunction(AIFunction inner) : AIFunction
{
    /// <summary>内层函数（工具身份的单一真相源）。</summary>
    protected AIFunction Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public override string Name => Inner.Name;

    /// <inheritdoc />
    public override string Description => Inner.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => Inner.JsonSchema;

    /// <summary>装饰逻辑入口（子类在此实现预算/拦截/透传，再经 <see cref="Inner"/> 或替身执行）。</summary>
    /// <param name="arguments">框架归一后的调用入参（可空形态与框架契约一致）。</param>
    /// <param name="cancellationToken">取消令牌（原样透传，不得吞取消）。</param>
    /// <returns>调用结果（形态同内层）。</returns>
    protected abstract override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments? arguments, CancellationToken cancellationToken);
}
