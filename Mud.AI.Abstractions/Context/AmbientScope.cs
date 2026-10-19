// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.AI.Abstractions.Context;

/// <summary>
/// <b>产品专属环境量的官方形态</b>：经 <see cref="System.Threading.AsyncLocal{T}"/> 在异步流内传递的产品上下文（租户凭证、企业标识等），<see cref="Begin"/> 返回的作用域释放时恢复进入前的值。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么中立侧提供它</b>：产品实现 <c>ITurnScopeFactory</c>（运行期）需要一套"进/出"语义，而每产品各写一份 <c>AsyncLocal</c> + 恢复逻辑，等于把"栈式恢复 + 所有权校验"这件易错的事复制 N 遍（乱序释放覆盖内层 = 多租户越权读取）。本类型是机制，产品只声明 <c>T</c> 与生命周期，不实现恢复。
/// </para>
/// <para>
/// <b>所有权校验按引用判定</b>：槽位存的是 Scope 句柄而非裸值——裸值 + 值相等校验在同值嵌套（同一凭据连开两层）时会误回滚外层；句柄每次 <see cref="Begin"/> 唯一，乱序 Dispose 只对"当前仍是我设置的"生效。<b>释放必须 LIFO</b>（<c>using</c> 声明天然保证）：乱序释放时所有权校验保证<b>不覆盖内层</b>，但已提前释放的外层值会残留在环境量里——调用方不得依赖乱序释放后的完全恢复。<b>并发语义</b>：值随 async 流向下流动（子任务读到的副本互不影响写回）；不得把不可变假设建立在跨流共享上——需要跨流共享的事实经端口参数传递，不经环境量。
/// </para>
/// <typeparam name="T">环境量类型（引用或值类型均可）。</typeparam>
/// </remarks>
public static class AmbientScope<T>
{
    // 每个 T 独享一份静态槽位（泛型静态字段按封闭类型实例化）。
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    /// <summary>当前异步流的环境量（未建立时为 <see langword="default"/>——引用类型为 null）。</summary>
    public static T? Current => CurrentScope.Value is { } scope ? scope.Value : default;

    /// <summary>建立环境量作用域（嵌套时内层覆盖外层，释放时逆序恢复）。</summary>
    /// <param name="value">环境量值。</param>
    /// <returns>作用域（<see cref="IDisposable.Dispose"/> 恢复进入前的值；重复 Dispose 无副作用）。</returns>
    public static IDisposable Begin(T value)
    {
        var scope = new Scope(value, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    private sealed class Scope : IDisposable
    {
        private readonly T _value;
        private readonly Scope? _previous;
        private int _disposed;

        public Scope(T value, Scope? previous)
        {
            _value = value;
            _previous = previous;
        }

        public T Value => _value;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // 所有权校验：只有「当前值仍是我写入的」才回滚——乱序释放（内层后建先退、外层提前退）不覆盖内层。
            if (ReferenceEquals(CurrentScope.Value, this))
            {
                CurrentScope.Value = _previous;
            }
        }
    }
}
