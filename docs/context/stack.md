# Stack versions

The exact versions this repo is built and tested against. Bump deliberately, in a change of its own.

| Component | Version | Pinned by |
|---|---|---|
| Rust | 1.98.1 (stable channel) | `rust-toolchain.toml` |
| Rust targets | `x86_64-pc-windows-msvc`, `aarch64-pc-windows-msvc` | `rust-toolchain.toml` |
| .NET SDK | 9.0.317 | installed via winget; CI uses `actions/setup-dotnet` |
| Visual Studio Build Tools | 2022 (17.14), MSVC 14.44 with x64 + ARM64 | developer machine setup |
| Windows SDK | 10.0.26100 | Build Tools install |
| Node.js | 24.x | contract fixture export only |
| Windows App SDK | *pinned in M2, when the shell lands* | `app/Astrid.App/Astrid.App.csproj` |
| UniFFI + uniffi-bindgen-cs | *pinned as a pair in M0/M1* | `crates/astrid-core/Cargo.toml` |

## Minimum supported Windows

Windows 10 1809 (build 17763) is the floor the Windows App SDK sets. Development and release
verification happen on Windows 11, x64 and ARM64.

## Developer machine setup

```powershell
winget install Rustlang.Rustup
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.VisualStudio.2022.BuildTools --override `
  "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools ^
   --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 ^
   --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 ^
   --add Microsoft.VisualStudio.Component.Windows11SDK.26100 ^
   --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools"
```

The ARM64 MSVC component is not optional: `npm run predeploy` cross-builds for ARM64 on every run.
