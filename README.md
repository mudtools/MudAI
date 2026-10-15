# Mud.AI

`Mud.AI` 是 Mud Studio 的中立（产品无关）AI 运行期套件，独立于任何具体 IM / 协作平台发布。
本仓由飞书仓（原 `MudFeishu`）的 `Mud.AI.*` 系列按「P3 上游化」独立而来：
**契约（Abstractions）→ 运行期（Agent）→ 编译期引擎（Generator）** 三层单向依赖，
产品侧（如 `Mud.Feishu.AI`）只经 NuGet 包消费，显式钉版，禁止浮动。

## 仓库结构

```
Mud.AI/
├── Mud.AI.Abstractions/      # 零依赖纯契约工程（端口 / 特性 / 剖面）
├── Mud.AI.Agent/             # 中立运行期（机制实现，net8.0+ 原生 AOT 兼容）
├── Mud.AI.Generator/         # 工具面源生成引擎（Roslyn Analyzer 形态）
├── Tests/
│   ├── Mud.AI.Agent.Tests/       # 运行期测试 + 契约守卫（ContractGuards / Golden）
│   └── Mud.AI.Generator.Tests/   # 引擎测试（ToolSurface / Guards / Infrastructure）
├── scripts/                  # 构建门禁与注释预算账本
├── documents/                # 架构决策记录（ADR）与过程文档
├── Mud.AI.slnx               # 解决方案
├── Directory.Build.props     # 仓级构建约定（TFM / AOT / 版本 / 打包元数据）
└── LICENSE-MIT
```

## 包一览

| 包 | 目标框架 | 说明 |
| --- | --- | --- |
| [`Mud.AI.Abstractions`](Mud.AI.Abstractions/README.md) | `netstandard2.0;net6.0;net8.0;net10.0` | 平台无关端口与工具面契约：会话端口 / 通道抽象 / 审批状态机接口 / 声明契约族 / 工具面引导契约。零依赖纯契约工程。 |
| [`Mud.AI.Agent`](Mud.AI.Agent/README.md) | `netstandard2.0;net6.0;net8.0;net10.0` | 中立运行期：工具执行骨架、会话回合管线（11 阶段状态机）、授权执行链尾两段、HITL 审批闭环、RAG 三段机制、流式通道降级链。net8.0+ 原生 AOT 兼容。 |
| [`Mud.AI.Generator`](Mud.AI.Generator/README.md) | `netstandard2.0`（Roslyn 组件） | 工具面源生成引擎：把 `[SdkToolProfile]` 剖面驱动的工具特性接口编译为模型可调用工具的全部编译期产物（Schema / Args / 域注册器 / DI 装配 / Guidance / 能力目录）。 |

各包 README 含完整的命名空间布局、类型清单、机制导览与设计不变量。

## 依赖方向（单向，无环）

```
（消费方，如 Mud.Feishu.AI）──(NuGet 包)──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
                              Mud.AI.Generator（作为 Analyzer，被产品侧挂载）
```

本仓**零** `Mud.Feishu*` 依赖；产品侧对 `Mud.AI.*` **只经包**引用（显式钉版，禁止浮动）。
`Mud.AI.Agent` 对 `Microsoft.Agents.AI`（MAF）的直引为白名单内收敛式放宽，且不得因此获得任何
`Mud.Feishu*` 引用——上述纪律由 `Tests/Mud.AI.Agent.Tests/ContractGuards/` 机械锁定。

## 构建与测试

```pwsh
dotnet build Mud.AI.slnx -c Release
dotnet test  Tests/Mud.AI.Agent.Tests
dotnet test  Tests/Mud.AI.Generator.Tests
dotnet pack  Mud.AI.slnx -c Release
```

- 统一 C# 13.0 / `Nullable` enable / `ImplicitUsings` enable；XML 文档注释进包（`GenerateDocumentationFile`）。
- 版本漂移防护：`NU1603` 升级为错误（声明版本与解析版本不一致即失败）。
- AOT：net8.0+ TFM 启用 `IsAotCompatible` / `EnableAotAnalyzer` / `EnableTrimAnalyzer`（`TrimMode=full`）；
  ns2.0 / net6.0 按 TFM 精确豁免。可选 `-p:AotStrictMode=true` 将 IL2xxx/IL3xxx 升级为错误。
- 测试除行为用例外含契约守卫与中立化 golden 快照（`NeutralizationGoldenTests`），机制归属纪律可机械断言。

## 门禁

```pwsh
pwsh ./scripts/verify-build.ps1            # 标准档
pwsh ./scripts/verify-build.ps1 -Full      # 完整档（Timing 测试 + strict format）
pwsh ./scripts/comment-audit.ps1           # 注释预算账本
```

## 文档

- 架构决策记录：[`documents/decisions/`](documents/decisions/)
  - ADR-0001 摘要器 vs MAF Compaction
  - ADR-0003 回合管线补偿
  - ADR-0005 Mud.AI 家族抽取
- 过程文档：[`documents/单元测试体系过度测试分析与解决方案.md`](documents/单元测试体系过度测试分析与解决方案.md)

## 许可证

MIT，见 `LICENSE-MIT`。
