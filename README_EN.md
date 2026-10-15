# Mud.AI

`Mud.AI` is Mud Studio's neutral (product-agnostic) AI runtime suite, released independently of any
specific IM / collaboration platform. This repository was split out of the Feishu repository (former
`MudFeishu`) from its `Mud.AI.*` family as part of the "P3 upstream extraction":
**contracts (Abstractions) → runtime (Agent) → compile-time engine (Generator)** — three layers,
single-direction dependency. Product sides (e.g. `Mud.Feishu.AI`) consume `Mud.AI.*` **only via
NuGet packages**, with explicitly pinned versions; floating versions are forbidden.

## Repository layout

```
Mud.AI/
├── Mud.AI.Abstractions/      # Zero-dependency pure contract project (ports / attributes / profiles)
├── Mud.AI.Agent/             # Neutral runtime (mechanism implementations, Native-AOT compatible on net8.0+)
├── Mud.AI.Generator/         # Tool-surface source generator (Roslyn Analyzer)
├── Tests/
│   ├── Mud.AI.Agent.Tests/       # Runtime tests + contract guards (ContractGuards / Golden)
│   └── Mud.AI.Generator.Tests/   # Engine tests (ToolSurface / Guards / Infrastructure)
├── scripts/                  # Build gates and comment-budget ledger
├── documents/                # Architecture decision records (ADRs) and process docs
├── Mud.AI.slnx               # Solution
├── Directory.Build.props     # Repo-wide build conventions (TFMs / AOT / versioning / packaging metadata)
└── LICENSE-MIT
```

## Packages

| Package | Target frameworks | Description |
| --- | --- | --- |
| [`Mud.AI.Abstractions`](Mud.AI.Abstractions/README.md) | `netstandard2.0;net6.0;net8.0;net10.0` | Platform-neutral ports and tool-surface contracts: session ports, channel abstractions, approval state-machine interfaces, declaration-contract family, tool-surface guidance contracts. Zero-dependency contract project. |
| [`Mud.AI.Agent`](Mud.AI.Agent/README.md) | `netstandard2.0;net6.0;net8.0;net10.0` | Neutral runtime: tool-execution skeleton, conversational turn pipeline (11-stage state machine), tail two stages of the authorization execution chain, HITL approval loop, three-stage RAG mechanics, streaming channel degradation chain. Native-AOT compatible on net8.0+. |
| [`Mud.AI.Generator`](Mud.AI.Generator/README.md) | `netstandard2.0` (Roslyn component) | Tool-surface source generator: compiles `[SdkToolProfile]`-driven tool interfaces into all compile-time artifacts required for model-callable tools (schemas / args / domain registrars / DI wiring / guidance / capability catalog). |

Each package README contains the full namespace layout, type inventory, mechanism tour, and design invariants.

## Dependency direction (single, acyclic)

```
(consumers, e.g. Mud.Feishu.AI) ──(NuGet package)──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
                                     Mud.AI.Generator (Analyzer, mounted by product side)
```

This repository has **zero** `Mud.Feishu*` dependencies; the product side consumes `Mud.AI.*`
**only via packages** (explicitly pinned, floating versions forbidden). `Mud.AI.Agent`'s direct
reference to `Microsoft.Agents.AI` (MAF) is a whitelisted, converged relaxation — it must not thereby
acquire any `Mud.Feishu*` reference. These disciplines are mechanically enforced by
`Tests/Mud.AI.Agent.Tests/ContractGuards/`.

## Build & test

```pwsh
dotnet build Mud.AI.slnx -c Release
dotnet test  Tests/Mud.AI.Agent.Tests
dotnet test  Tests/Mud.AI.Generator.Tests
dotnet pack  Mud.AI.slnx -c Release
```

- Unified C# 13.0 / `Nullable` enable / `ImplicitUsings` enable; XML documentation comments ship in
  the packages (`GenerateDocumentationFile`).
- Version-drift guard: `NU1603` is escalated to an error (declared vs. resolved version mismatch fails the build).
- AOT: `IsAotCompatible` / `EnableAotAnalyzer` / `EnableTrimAnalyzer` (`TrimMode=full`) are enabled on
  net8.0+ TFMs; netstandard2.0 / net6.0 are exempted precisely per TFM. Optional
  `-p:AotStrictMode=true` escalates IL2xxx/IL3xxx to errors.
- Beyond behavioral tests, the suite includes contract guards and a neutralization golden snapshot
  (`NeutralizationGoldenTests`), making mechanism-ownership discipline mechanically assertable.

## Gates

```pwsh
pwsh ./scripts/verify-build.ps1            # standard
pwsh ./scripts/verify-build.ps1 -Full      # full (Timing tests + strict format)
pwsh ./scripts/comment-audit.ps1           # comment-budget ledger
```

## Documentation

- Architecture decision records: [`documents/decisions/`](documents/decisions/)
  - ADR-0001 Summarizer vs. MAF Compaction
  - ADR-0003 Turn pipeline compensation
  - ADR-0005 Mud.AI family extraction
- Process docs: [`documents/单元测试体系过度测试分析与解决方案.md`](documents/单元测试体系过度测试分析与解决方案.md)

## License

MIT, see `LICENSE-MIT`.
