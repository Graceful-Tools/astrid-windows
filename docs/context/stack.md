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

## The Windows test device

One physical machine owns the Windows app, the way the Mac owns iOS (astrid-web's
`docs/AGENT_FLEET.md`, step 7). Cloud `windows-latest` keeps doing the builds and the unit tests;
the device runs what needs a real desktop — the UI smoke suite, a packaged MSIX, an actual window.

| | |
|---|---|
| Host | `JONSYOGA` — Lenovo 83ED, Qualcomm ARMv8 (64-bit) ~3.4 GHz, 16 GB RAM |
| Architecture | **ARM64** — `aarch64-pc-windows-msvc`, confirmed by `cargo -vV`, not by the shell |
| OS | Windows 11 **Home**, 10.0.26200 (build 26200) |
| Virtualization | On and in use: VBS running, hypervisor present, HVCI and Secure Launch enforced |
| Unattended agent | `scripts/fixall-loop.ps1` under Task Scheduler — see [AUTOMATION.md](../AUTOMATION.md) |

**Do not read the architecture from the shell.** Git Bash and PowerShell 5.1 both run as emulated
x64 here, so `PROCESSOR_ARCHITECTURE` and `uname -m` say x64 and mean nothing.
`scripts/predeploy.ps1` reads the registry for this reason; `cargo -vV`'s `host:` line is the cheap
check by hand. A build that trusts the shell builds the wrong architecture and the shell then
refuses to load the core (ASTRID001).

**Hyper-V is not available on this device, so Android is not.** The SKU is Home, which does not
ship client Hyper-V or the Windows Hypervisor Platform, and an Android emulator needs one of them.
The virtualization extensions *are* enabled — VBS is running on them — but a Home SKU gives no way
to reach them. Android work needs a different machine or a different SKU; it is not a question of
turning something on here.

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
