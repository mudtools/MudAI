// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.AI.Agent.Tests.Tools;

/// <summary>
/// <see cref="DelegatingToolFunction"/> 的转发契约用例：三成员逐字相等 + 调用转调 + 零反射。
/// </summary>
public class DelegatingToolFunctionTests
{
    private sealed class StubFunction : AIFunction
    {
        public override string Name => "demo.tool";

        public override string Description => "演示工具";

        public override JsonElement JsonSchema
            => JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}}}").RootElement.Clone();

        public int InvokeCount;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments? arguments, CancellationToken cancellationToken)
        {
            InvokeCount++;
            return new ValueTask<object?>("inner-result");
        }
    }

    private sealed class RecordingDecorator(AIFunction inner) : DelegatingToolFunction(inner)
    {
        public int CoreCalls { get; private set; }
        public AIFunctionArguments? LastArguments;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments? arguments, CancellationToken cancellationToken)
        {
            CoreCalls++;
            LastArguments = arguments;
            return Inner.InvokeAsync(arguments, cancellationToken);
        }
    }

    private static AIFunctionArguments EmptyArguments()
        => new(new Dictionary<string, object?>());

    [Fact]
    public void Name_Description_Schema_ShouldForwardVerbatim()
    {
        AIFunction inner = new StubFunction();
        AIFunction decorator = new RecordingDecorator(inner);

        decorator.Name.Should().Be("demo.tool", "工具名逐字转发（模型侧工具身份不得被装饰改写）");
        decorator.Description.Should().Be("演示工具");
        decorator.JsonSchema.ToString().Should().Be(inner.JsonSchema.ToString(), "Schema 逐字转发");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRouteThroughInvokeCoreAsync()
    {
        var inner = new StubFunction();
        var decorator = new RecordingDecorator(inner);

        var result = await decorator.InvokeAsync(EmptyArguments(), CancellationToken.None);

        result.Should().Be("inner-result", "装饰器的 InvokeCoreAsync 经内层完成实际调用");
        decorator.CoreCalls.Should().Be(1, "框架入口（非虚 InvokeAsync）必须转调 InvokeCoreAsync");
        inner.InvokeCount.Should().Be(1);
        decorator.LastArguments.Should().NotBeNull("入参原样透传");
    }

    [Fact]
    public void NullInner_ShouldThrow()
    {
        var act = () => new RecordingDecorator(null!);

        act.Should().Throw<ArgumentNullException>("内层是工具身份的单一真相源，缺席即装配缺陷");
    }
}
