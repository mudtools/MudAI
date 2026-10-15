# Mud.AI

`Mud.AI` is Mud Studio's neutral (product-agnostic) AI runtime suite, released independently of any
specific IM / collaboration platform. This repository was split out of the Feishu repository (former
`MudFeishu`) from its `Mud.AI.*` family as part of the "P3 upstream extraction".

## Packages

| Package | Description |
| --- | --- |
| `Mud.AI.Abstractions` | Platform-neutral ports and tool-surface contracts (session ports, channel abstractions, approval state-machine interfaces, declaration-contract family, tool-surface guidance contracts). Zero-dependency contract project. |
| `Mud.AI.Agent` | Neutral runtime: tool-execution skeleton, conversation pipeline, authorization execution-chain skeleton, RAG mechanics, channel mechanics. Native-AOT compatible on net8.0+. |
| `Mud.AI.Generator` | Tool-surface source generator (Roslyn Analyzer): compiles `[SdkToolProfile]`-driven tool interfaces into all compile-time artifacts required for model-callable tools. |

## Dependency direction (single, acyclic)

```
(consumers, e.g. Mud.Feishu.AI) ──(NuGet package)──▶ Mud.AI.Agent ──▶ Mud.AI.Abstractions
                                     Mud.AI.Generator (Analyzer, mounted by product side)
```

This repository has **zero** `Mud.Feishu*` dependencies; the product side consumes `Mud.AI.*`
**only via packages** (explicitly pinned, floating versions forbidden).

## Build & test

```pwsh
dotnet build Mud.AI.slnx -c Release
dotnet test  Tests/Mud.AI.Agent.Tests
dotnet test  Tests/Mud.AI.Generator.Tests
dotnet pack  Mud.AI.slnx -c Release
```

## Gates

```pwsh
pwsh ./scripts/verify-build.ps1            # standard
pwsh ./scripts/verify-build.ps1 -Full      # full (Timing tests + strict format)
pwsh ./scripts/comment-audit.ps1           # comment-budget ledger
```

## License

MIT, see `LICENSE-MIT`.
