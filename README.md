# Mud.AI

`Mud.AI` 是 Mud Studio 的中立（产品无关）AI 运行期套件，独立于任何具体 IM / 协作平台发布。
本仓由飞书仓（原 `MudFeishu`）的 `Mud.AI.*` 系列按「P3 上游化」独立而来。

## 包一览

| 包 | 说明 |
| --- | --- |
| `Mud.AI.Abstractions` | 平台无关端口与工具面契约（会话端口 / 通道抽象 / 审批状态机接口 / 声明契约族 / 工具面引导契约）。零依赖纯契约工程。 |
| `Mud.AI.Agent` | 中立运行期：工具执行骨架、会话管线、授权执行链骨架、RAG 机制、通道机制等。net8.0+ 原生 AOT 兼容。 |
| `Mud.AI.Generator` | 工具面源生成引擎（Roslyn Analyzer 形态）：把 `[SdkToolProfile]` 剖面驱动的工具特性接口编译为模型可调用工具的全部编译期产物。 |

## 依赖方向（单向，无环）

```
（消费方，如 Mud.Feishu.AI）──(NuGet 包)──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
                              Mud.AI.Generator（作为 Analyzer，被产品侧挂载）
```

本仓**零** `Mud.Feishu*` 依赖；产品侧对 `Mud.AI.*` **只经包**引用（显式钉版，禁止浮动）。

## 构建与测试

```pwsh
dotnet build Mud.AI.slnx -c Release
dotnet test  Tests/Mud.AI.Agent.Tests
dotnet test  Tests/Mud.AI.Generator.Tests
dotnet pack  Mud.AI.slnx -c Release
```

## 门禁

```pwsh
pwsh ./scripts/verify-build.ps1            # 标准档
pwsh ./scripts/verify-build.ps1 -Full      # 完整档（Timing 测试 + strict format）
pwsh ./scripts/comment-audit.ps1           # 注释预算账本
```

## 许可证

MIT，见 `LICENSE-MIT`。
