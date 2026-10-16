# AGENTS.md — Mud.AI 仓库协作约定

本文件面向在本仓库工作的 AI 助手与人类贡献者，是改动代码前必须遵守的约定。

## 注释纪律（强制）

> 注释解释"**现在必须怎样**"与"**读代码读不出的陷阱**"；演进史属于提交信息、CHANGELOG 与 ADR。

### 白名单（保留，不改写）

- 并发/顺序契约（如"同一 messageId 必须顺序 await"）、失败语义与降级路径；
- 平台/框架怪癖与 TFM 裁剪理由（`#if` 的必要性、AOT/IL 警告规避）；
- 守卫机械锁定的锚点说明（"由 XxxGuard 防回归"）；
- 已存在的 `documents/…` / `ADR-nnnn` 链接。

### 黑名单（禁止新增，存量已清零）

- 评审批次编号（`R-1`、`R5-11`、`AT-F13`、`AI-FD-D12`、`P2D-2a`、`T1 / D1`、`WP3`、`BUG-8` 等）——写约束本身；
- 过程叙述词（此前/原先/曾经/旧实现/上一版/不再/墓碑式"此处原有…"）；
- 计数型论据（"取代 24+2 处"、"N 个调用点零改动"）——随时间腐化。

### 硬性门禁（`CommentConventionContractGuards`，29+ 用例）

- 注释中的评审编号必须能在 `documents/**`、`CHANGELOG.md`、`README*.md` 里解析；
- 含过程叙述的注释块必须带存在的文档链接，否则留在提交信息里；
- 注释行数与最长叙述块不得越过冻结台账 `documents/comment-budget-baseline.json`（只允许下压）；
- 新文件：最长叙述块 ≤ 20 行、注释 ≤ 120 行；
- **重新固化只能下压**：树上仍有真实违规时固化会被拒绝。命令：
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/comment-audit.ps1 -UpdateBaseline`
  （固化 diff 必须连同 CHANGELOG 一并提交评审）。

### 决策外移

值得留证的取舍写成 `documents/decisions/ADR-nnnn-*.md`，代码里只留一行链接。
**注意**：`.docs/` 不是守卫解析源——被代码引用的文档必须放 `documents/` 下。

### CHANGELOG 警示

CHANGELOG 描述历史批次时**只写内容、不写批次编号**——在解析源里出现编号等于给代码里的同号引用"发通行证"（守卫判据被自我认证，实测踩过两次）。

## 工程约定

- 统一 C# 13.0、`Nullable` enable；XML 文档注释随包分发（公开成员必须有 XML 注释，`cref` 必须可解析）。
- 依赖方向单向无环：消费方 → `Mud.AI.Agent` → `Mud.AI.Abstractions`；本仓零 `Mud.Feishu*` 依赖。
- 构建与验证：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-build.ps1`（含注释守卫）。
- 手写运行时层 = `Mud.AI.Abstractions` / `Mud.AI.Agent` / `Mud.AI.Generator`；Tests 不在注释预算强制作用域内，但同样适用黑名单。

## 脚本运行环境注意

- 仓库脚本必须 **ASCII-only**：Windows PowerShell 5.1 将无 BOM 的 UTF-8 脚本按 ANSI 解析，中文字面量会乱码甚至解析失败（`scripts/comment-audit.ps1`、`scripts/comment-live-scan.ps1` 均按此约定用码位构造中文正则）。确需中文输出的脚本（如 `scripts/verify-build.ps1`）必须保存为 **UTF-8 with BOM**——无 BOM 时 PS 5.1 下解析错误 6 处、脚本完全不可执行；新脚本落盘后按 `[Parser]::ParseFile` 自检解析错误为 0。
- `Select-String` 匹配中文常因编码返回 False 误报：验证中文改动用英文关键字或直接读文件。
