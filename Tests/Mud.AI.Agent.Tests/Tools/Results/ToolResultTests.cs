// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.Tools.Results;

/// <summary>
/// <see cref="ToolResult"/> 工厂形态的行为用例：<see cref="ToolResult.FromUnclassifiedError"/> 的载荷 / 预算 / 空工具名边界。
/// </summary>
public class ToolResultTests
{
    [Fact]
    public void FromUnclassifiedError_ShouldCarryApiUpstreamPayload()
    {
        var result = ToolResult.FromUnclassifiedError("下游拒绝", "demo.tool");

        result.Error.Should().NotBeNull("未分类错误必须带结构化载荷——失败不得被记成 Succeeded");
        result.Error!.Category.Should().Be("api", "与产品码表回落同口径（中立兜底分类）");
        result.Error.Subtype.Should().Be("upstream_error");
        result.Error.Retryable.Should().BeFalse("api 类默认不可重试（可重试语义归 Retryable 类）");
        result.Error.Tool.Should().Be("demo.tool", "工具名进载荷锚点（宿主按工具聚合）");
        result.Text.Should().StartWith("{\"", "首行必须是结构化 JSON 载荷");
        result.Text.Should().Contain("下游拒绝", "人类可读正文保留在载荷之后");
    }

    [Fact]
    public void FromUnclassifiedError_EmptyToolName_ShouldNotThrow_AndKeepWellFormedFirstLine()
    {
        var result = ToolResult.FromUnclassifiedError("boom");

        result.Error.Should().NotBeNull();
        result.Text.Should().StartWith("{\"", "空工具名不得产出畸形首行（载荷恒为合法 JSON 对象）");
        result.Text.Should().Contain("boom");
    }

    [Fact]
    public void FromUnclassifiedError_ShouldApplyBudget_AndMarkTruncation()
    {
        var result = ToolResult.FromUnclassifiedError(new string('x', 500), "demo.tool", maxLength: 120);

        result.Truncated.Should().BeTrue("出口必须施加预算并回填标记（模型不得把不完整正文当完整事实）");
        result.TruncationReason.Should().NotBeNullOrEmpty();
        // 截断器既有语义：预算内正文 + 附加标记（标记使结果长于预算本身，同 Ok/OkJson 出口口径）。
        result.Text!.Length.Should().BeGreaterThan(120, "截断标记附在预算正文之后（与两个截断器的进入条件同源）");
        result.Text.Should().Contain("truncated", "标记文本使截断对宿主可解析");
        result.Error.Should().NotBeNull("截断不丢结构化载荷（首行载荷 ≤512 字节的设计不受影响）");
    }

    [Fact]
    public void FromUnclassifiedError_ZeroBudget_ShouldNotTruncate()
    {
        var result = ToolResult.FromUnclassifiedError("boom", "demo.tool", maxLength: 0);

        result.Truncated.Should().BeFalse("≤ 0 = 无预算（与唯一出口的同款语义）");
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void FromError_Obsolete_ShouldStillSetTextOnly()
    {
#pragma warning disable CS0618 // 旧形态：既有调用点迁移期内的行为锁定（2.0.0 移除）
        var result = ToolResult.FromError("plain");

        result.Text.Should().Be("plain");
        result.Error.Should().BeNull("旧工厂只置文本——这正是它被弃用的原因（失败被记成 Succeeded）");
#pragma warning restore CS0618
    }
}
