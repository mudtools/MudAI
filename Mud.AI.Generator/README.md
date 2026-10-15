# Mud.AI.Generator

`Mud.AI.Generator` 是 Mud.AI 套件的**工具面源生成引擎**（Roslyn 增量源生成器 + 诊断分析器）：
把 `[SdkToolProfile]` 剖面驱动的工具特性接口，在**编译期**生成为「模型可调用工具」的全部产物——
Schema、强类型入参解包器、域注册器、DI 装配、guidance 固化与能力目录。

本工程是**编译器扩展**，不是运行期程序集：编译进 `analyzers/dotnet/cs/`，由产品侧以 Analyzer 形态挂载。

## 定位

```
产品侧 SDK 接口（[AgentTool] / [ToolHandler] 标注）
        │  剖面（[SdkToolProfile]）驱动
        ▼
Mud.AI.Generator（编译期，netstandard2.0）
        │  .g.cs 产物
        ▼
产品侧运行期（Mud.AI.Agent + 宿主实现）
```

- v2.3 自组件仓 `Mud.HttpUtils.Generator` 迁入，命名空间 `Mud.HttpUtils.*` → `Mud.AI.*`。
- 引擎**永不内联任何 SDK 命名前缀字符串**：命名差异全部由各 SDK 剖面（`[SdkToolProfile]`）吸收，
  同一引擎可同时服务飞书（`MUDFT*` 诊断）、微信（`MUDWX*` 诊断）等多个产品剖面。

## 工程事实

| 项 | 值 | 说明 |
| --- | --- | --- |
| 目标框架 | `netstandard2.0`（单 TFM） | Roslyn 组件须为 ns2.0（编译器进程加载） |
| Roslyn 标记 | `IsRoslynComponent=true`、`EnforceExtendedAnalyzerRules=true` | 扩展分析器强规则 |
| 分发形态 | `IncludeBuildOutput=false`，引擎 dll 显式打包进 `analyzers/dotnet/cs/` | 消费方以 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 挂载；资产路径错 ⇒「编译全绿但零生成产物」 |
| 分析级别 | `AnalysisLevel=6.0-all` | |
| 依赖 | `Microsoft.CodeAnalysis.CSharp` 4.11.0（PrivateAssets=all）、`Microsoft.CodeAnalysis.Analyzers` 5.6.0 | **禁止**引用 `Microsoft.CodeAnalysis.CSharp.Workspaces`（RS1038，命令行编译进程不提供 Workspaces） |
| AOT | 显式关闭（`IsAotCompatible` 等均为 false） | 单 TFM 下根 props 条件求值会误设，故显式关 |
| NoWarn | `CA1031`（生成器必须吞异常转诊断）、`RS1035`（DEBUG 验证开关读环境变量）、`CA1707/CA1021/CA1822` | |
| InternalsVisibleTo | `Mud.AI.Generator.Tests` | 测试可注入故障钩子 |

## 命名空间竖井（四层，对应增量管线 L1→L2→L4）

| 命名空间 | 层次职责 | 位置 |
| --- | --- | --- |
| `Mud.AI` | 引擎基座与横切关注点 | 顶层 + `Consts/` |
| `Mud.AI.ToolSurface` | 引擎入口、剖面模型、诊断槽位表、契约守卫 | `ToolSurface/` 根 |
| `Mud.AI.ToolSurface.Extraction` | L1 抽取层（符号 → 值模型） | `ToolSurface/Extraction/` |
| `Mud.AI.ToolSurface.Schema` | L2 渲染 + L4 校验 + 类型 Schema 推导 + affordance 资产 | `ToolSurface/Schema/` |
| `Mud.AI.ToolSurface.Emit` | L2/L4 发射器家族（各类 `.g.cs` 产物） | `ToolSurface/Emit/` |

### 引擎基座（顶层，命名空间 `Mud.AI`）

| 类型 | 职责 |
| --- | --- |
| `TransitiveCodeGenerator` | 增量生成器基类（`IIncrementalGenerator`）：文件头生成（固定 `\n` 换行 + `#nullable enable` + using 清单）、XML 文档提取、诊断上报、`[Conditional("DEBUG")]` 生成代码语法校验（环境变量 `MudHttpSkipGeneratedValidation` 可跳过） |
| `SyntaxPrefilter` | 语法级保守预筛器：昂贵的语义绑定前用纯 token 扫描判断「编译中是否可能出现目标标记」；按 `SyntaxTree` 做 `ConditionalWeakTable` 缓存；保守不漏报 |
| `GeneratorDebugLogger` | 调试日志：`Log` 仅 DEBUG（Release 零开销）；`LogError` 走 `Trace`（异常不被静默吞噬） |
| `GeneratedCodeConsts` | `[CompilerGenerated]` / `[GeneratedCode]` 标记文本单一真相源；版本号经 `Lazy` 反射解析，用作增量 salt |

## 工作流程（输入 → 管道 → 输出）

### 输入

1. `[SdkToolProfile(...)]` 特性标注且实现 `ISdkToolProfile` 的类（剖面数据载体，编译期字符串常量，约 25 个槽位）。
2. 标注工具特性（如 `[AgentTool]`，名字/命名空间来自剖面槽）的接口——**唯一「产工具」声明处**。
3. 标注 handler 特性（如 `[ToolHandler]`）的执行器方法。
4. AdditionalFiles：golden 快照、域 guidance（`{GuidanceDirectory}{domain}.md`）、per-tool affordance（`{AffordanceDirectory}{工具名}.md`）。
5. build_property：`{CapabilityCatalogPropertyName}=true` 开启能力目录。

```csharp
// 产品侧剖面声明（示例）
[SdkToolProfile("Feishu",
    SdkNamespaceRoot = "Mud.Feishu",
    ToolAttributeName = "AgentTool",
    ToolHandlerAttributeName = "ToolHandler",
    ProductPrefix = "FeishuTool",
    DiagnosticPrefix = "MUDFT",
    SchemaExtensionKey = "x-feishu",
    OwnerAssembly = "Mud.Feishu.AI.Tools")]
public sealed class FeishuToolProfile : ISdkToolProfile { }
```

### 管道（剖面扇出 → L1 → L2/L4）

```
ProfileDiscovery ──▶ SdkToolProfileSet（按 Name 排序，值相等包装）
   │                  空剖面短路：无 ISdkToolProfile 实现 ⇒ 0 诊断 0 产物
   ├─ L1 抽取：ToolSurfaceScanner（接口符号 → ToolSchemaModel，Source 与 SDK 事实交叉校验）
   │           ToolHandlerScanner（handler 方法 → ToolHandlerBinding，DI 依赖分类）
   ├─ L2 渲染：SchemaWriter（描述符 JSON，risk 权威字段）/ TypeSchemaResolver（递归类型推导）
   ├─ L4 校验：DescriptorValidator（结构/类型/跨字段一致）→ 动态槽位诊断
   └─ 发射：Emit 家族 → .g.cs 产物 + golden 快照比对
   故障隔离：每条输出路径经 Guard.WrapUnchecked 兜底；多剖面逐 profile try/catch（单剖面异常不阻断其余）
```

大量值模型实现 `IEquatable<>`（符号不进模型）是**增量生成器缓存**纪律：剖面未变但编译变了 ⇒ 退化为 Unchanged，
避免每次击键重跑。

### 输出产物（全部 `.g.cs`，标注 `[CompilerGenerated]` + `[GeneratedCode]`）

| 产物 | 发射器 | 内容 |
| --- | --- | --- |
| `{P}Schemas.g.cs` | `SchemaEmitter` | Schema 常量 + 名字注册表 |
| `{P}Names.g.cs` | `SchemaEmitter` | 工具名契约表（**仅** `OwnerAssembly`，防 CS0433） |
| `{P}Contracts.g.cs` | `SchemaEmitter` | 类型化契约表 |
| `{Tool}Args.g.cs` / `{P}Args` | `ToolArgsEmitter` | 每工具强类型入参解包器（一次 `Unpack` 收敛逐参读取，运行期漂降级为编译期错误） |
| `{P}DomainRegistrars/*.g.cs` + `{Plural}ServiceCollectionCoreExtensions.g.cs` | `ToolRegistrarEmitter` | 每执行器一域注册器 + DI 装配（分组键 = 执行器类） |
| `{P}Guidance.g.cs` | `GuidanceEmitter` | 域 guidance 资产（AdditionalFiles）固化 |
| `{P}MethodCatalog` + 能力目录 | `MethodCatalogEmitter` / `CapabilityCatalogEmitter` | 逐方法轻量元数据 / 全接口聚合目录（可选 Tier R，build_property 开启） |

## 诊断体系

| 档位 | ID | 说明 |
| --- | --- | --- |
| 静态（SDKT） | `SDKT001` | `ISdkToolProfile` ↔ `[SdkToolProfile]` 剖面契约成对守卫（`ProfileContractGuardAnalyzer`，用全名单字符串 + `GetTypeByMetadataName` 双重判定） |
| 静态（SDKT） | `SDKT002` | 剖面缺引擎必需槽位（剖面被跳过 ⇒ 工具面静默消失） |
| 动态（剖面注入） | `{prefix}{slot:D3}`（001~031 共 27 槽） | 槽位表 `ToolSurfaceDiagnostics` 运行时构造描述符：飞书 `MUDFT001..` / 微信 `MUDWX001..` 同构；槽 026 为多剖面故障隔离兜底 |

静态诊断必须在 `AnalyzerReleases.Shipped.md` / `Unshipped.md` 登记（否则 RS2008）；
修改既有诊断 Category/Severity 必须同步（否则 RS2001）。剖面注入的动态诊断不进本表。

## 编译器扩展铁律（贡献者必读）

1. **不得引用任何 `Mud.*` 运行时程序集**——符号判定一律用字符串全名 + `GetTypeByMetadataName`。
2. 所有 `Safe*` / `Try*` / `Execute*` 路径**必须吞异常转诊断**（逸出到 csc 会导致宿主编译失败 AD0001）；
   `OperationCanceledException` 必须放行（用户取消构建不算故障）。
3. `netstandard2.0` **禁用 record**——值模型手写 `IEquatable<>`。
4. 零依赖 JSON——生成器内 JSON 拼装统一走自研 `JsonText`。
5. 生成代码必须经 `AddSourceValidated` 的 DEBUG 语法校验，hintName 由 `ProductPrefix` 天然隔离多剖面。

## 消费方挂载

```xml
<!-- 产品侧 csproj：以 Analyzer 形态挂载引擎（不引用程序集） -->
<ProjectReference Include="...Mud.AI.Generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## 相关包

- [`Mud.AI.Abstractions`](../Mud.AI.Abstractions/README.md) — 引擎消费的声明契约族（`[AgentTool]` 等）与剖面契约（`ISdkToolProfile` / `[SdkToolProfile]`）。
- [`Mud.AI.Agent`](../Mud.AI.Agent/README.md) — 生成产物在运行期的消费方（工具执行骨架）。

## 许可证

MIT，见仓库根目录 [`LICENSE-MIT`](../LICENSE-MIT)。
