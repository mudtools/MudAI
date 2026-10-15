# ADR-0005：Mud.AI 系列包独立仓（跨仓消费与版本契约）

- 状态：已接受（2026-10-15）
- 关联方案：`Mud.AI-系列包独立化迁移方案.md`（阶段 0–3、5–7；阶段 4「发布到 nuget.org」不在
  本次范围）

## 背景

`Mud.AI.Abstractions` / `Mud.AI.Agent` / `Mud.AI.Generator` 原寄居飞书仓（`MudFeishu`），随
`Mud.Feishu.AI*` 一起演进，并带两个测试工程。三者是**产品中立**的运行期 / 契约 / 引擎层，与飞书域
零耦合。继续寄居会让「中立性」缺少结构性保障，并把飞书仓的发布面与它们纠缠（`dotnet pack <飞书 slnx>`
会连带产出 AI 包）。

## 决策

1. 三个源工程 + 两个测试工程迁入独立仓 **`Mud.AI`**（`D:\Repos\MudAIAgent`）。包 ID、命名空间、程序集名
   **不变**；启用自有版本线（起点 `1.0.0`）。无 `[Obsolete]` / 兼容 shim——AI 家族从未发布，做一次性硬切。
2. 飞书仓改以 **NuGet 包**消费：`Mud.AI.Agent` / `Mud.AI.Abstractions` 为普通包引用，
   `Mud.AI.Generator` 为 **Analyzer** 形态（`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`）。
   版本显式钉死（**不得浮动**）。阶段 4 发布不在范围 ⇒ 本地开发经 **folder 源**
   `<Mud.AI 仓>/artifacts` 还原。
3. **引擎归属变更**：由「仓内引擎」改为「跨仓包」。飞书仓不再含任何源级 `[Generator]` 宿主；
   工具面仍只由 `Mud.AI.Generator` 生成（`FeishuToolProfile.cs` 仍是飞书命名事实的唯一真相源）。
4. **跨仓版本契约**：飞书仓 `Microsoft.Agents.AI`（MAF）版本必须 ≥ `Mud.AI.Agent` 包编译时的版本，
   反之亦然地对齐单点（由飞书仓
   `AgentContractGuards.MafVersion_ShouldSatisfyMudAiAgentLowerBound` 断言）。
   判据用两边程序集的**引用元数据**（`Assembly.GetReferencedAssemblies()`），不依赖 nuspec / 全局缓存
   路径，跨机可行。
5. **守卫随实现归属拆分**：家族内部包锁、中立词汇、引擎产物自由度、注释预算冻结基线、golden 快照，
   以及跨切面守卫（`SilentCatch` / `ToolArgumentShape` / `DegradationMetrics` / `TruncationVisibility` /
   `ToolErrorContract` / `AgentContractGuards` 等）的**中立半边**迁入本仓
   `Tests/Mud.AI.Agent.Tests`；飞书仓只保留**产品半边**（见两端 `ContractGuards/`）。

## 后果

- **本地闭环**：改引擎 → 本仓 `dotnet pack Mud.AI.slnx -c Release -o artifacts` → 飞书仓还原（folder 源）。
  注意 NuGet 以 `id + version` 为缓存键：**同版本覆盖打包不会让下游缓存失效**（CACHE-1 同类陷阱），
  改包必须递增版本。飞书仓 `verify-build.ps1` 步骤 0 已扩展为对 `Mud.AI.*` 包做「包内 DLL ↔ 全局缓存 DLL」
  的 SHA256 内容自检。
- 飞书仓删除 `Mud.AI.Abstractions` / `Mud.AI.Agent` / `Mud.AI.Generator` 及两个测试工程目录；
  `comment-budget-baseline.json` 的 `scope` 收窄为 `["Mud.Feishu.AI", "Mud.Feishu.AI.Mcp"]`。
- 飞书仓 `AGENTS.md` 与两端受治理文档记录跨仓版本契约与引擎归属变更；`README*` 无 `Mud.AI.*` 引用，
  无需改动。

## 未决 / 边界

- 阶段 4（发布 1.0.0 到 nuget.org 并让飞书仓改回 nuget.org 源）尚未执行；CI（`dotnet-publish.yml`）
  仍只面向飞书包，AI 包由本仓自建发布流（后续工作）。
