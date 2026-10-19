// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Agent.Tests.Context;

/// <summary>
/// <see cref="AmbientScope{T}"/> 的栈式恢复 / 所有权校验 / 异步流语义用例。
/// </summary>
/// <remarks>
/// 所有权校验按<b>句柄引用</b>判定（槽位存 Scope 而非裸值）——同值嵌套（ABA）也精确，这是与"裸值 + 值相等"
/// 实现的区分点，用 <see cref="NestedSameValue_ShouldRestoreOuterExactly"/> 锁定。
/// </remarks>
public class AmbientScopeTests
{
    [Fact]
    public void Begin_ShouldSetCurrent_AndRestoreOnDispose()
    {
        AmbientScope<string>.Current.Should().BeNull("未建立时无环境量");

        using (AmbientScope<string>.Begin("corp-1"))
        {
            AmbientScope<string>.Current.Should().Be("corp-1");
        }

        AmbientScope<string>.Current.Should().BeNull("释放恢复进入前的值");
    }

    [Fact]
    public void NestedBegin_ShouldRestoreOuterOnInnerDispose()
    {
        using var _outer = AmbientScope<string>.Begin("outer");
        using var _inner = AmbientScope<string>.Begin("inner");

        AmbientScope<string>.Current.Should().Be("inner", "内层覆盖外层");
    }

    [Fact]
    public void OutOfOrderDispose_ShouldNotRollBackInner()
    {
        var outer = AmbientScope<string>.Begin("outer");
        var inner = AmbientScope<string>.Begin("inner");

        outer.Dispose();

        AmbientScope<string>.Current.Should().Be("inner",
            "乱序释放：外层提前 Dispose 不得回滚内层（所有权校验——多租户场景下这是越权读取的防线）");

        inner.Dispose();

        AmbientScope<string>.Current.Should().Be("outer", "内层释放后恢复到外层值");
    }

    [Fact]
    public void NestedSameValue_LifoDispose_ShouldRestoreExactly()
    {
        var outer = AmbientScope<string>.Begin("same");
        var inner = AmbientScope<string>.Begin("same");

        inner.Dispose();

        AmbientScope<string>.Current.Should().Be("same",
            "同值嵌套：内层释放后恢复到外层值（值相等判权会在这里把当前值误清成 null）");

        outer.Dispose();

        AmbientScope<string>.Current.Should().BeNull("外层释放后回到无环境量态");
    }

    [Fact]
    public void OutOfOrderDispose_SameValue_ShouldLeaveOuterValueResident()
    {
        var outer = AmbientScope<string>.Begin("same");
        var inner = AmbientScope<string>.Begin("same");

        outer.Dispose();

        AmbientScope<string>.Current.Should().Be("same",
            "乱序释放：所有权校验保证不覆盖内层；外层值残留是 LIFO 纪律的既有边界（类注释已声明）");

        inner.Dispose();

        AmbientScope<string>.Current.Should().Be("same", "恢复到（已释放的）外层值——乱序释放的既有边界");
    }

    [Fact]
    public void RepeatedDispose_ShouldBeNoOp()
    {
        var scope = AmbientScope<string>.Begin("once");
        scope.Dispose();
        scope.Dispose();

        AmbientScope<string>.Current.Should().BeNull();
    }

    [Fact]
    public async Task Begin_ShouldFlowIntoChildAsyncFlows()
    {
        using var _scope = AmbientScope<string>.Begin("flow-1");

        var seenInChild = await Task.Run(async () =>
        {
            await Task.Yield();
            return AmbientScope<string>.Current;
        });

        seenInChild.Should().Be("flow-1", "环境量随 async 流向下流动（子任务读到父级建立的值）");
    }

    [Fact]
    public void ValueType_ShouldRoundTrip()
    {
        AmbientScope<int>.Current.Should().Be(default);

        using (AmbientScope<int>.Begin(42))
        {
            AmbientScope<int>.Current.Should().Be(42);
        }

        AmbientScope<int>.Current.Should().Be(default);
    }
}
