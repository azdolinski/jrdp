# jrdp

An embeddable Remote Desktop client for Windows. One small `.exe` that runs
either as a standalone RDP client or as a headless helper another application
drives over stdin/stdout.

It wraps Microsoft's RDP ActiveX control (`MsTscAx` — the same engine as the
built-in `mstsc.exe`), so sessions behave exactly like the Windows client:
same protocol support, same redirection, same performance.

## Why

Embedding RDP in your own app normally means shelling out to `mstsc.exe` and
losing control of the window, or reimplementing the RDP protocol. jrdp takes
the third route: it hosts the real Microsoft control in a borderless window
whose HWND it hands to you, so you can reparent it into your own UI and drive
it with typed commands.

Extracted from [yterminal](https://github.com/azdolinski/yterminal), where it
backs the embedded RDP tabs.

## Install

Grab `jrdp-win-x64.exe` (or `-arm64`) from the
[latest release](../../releases/latest). It's self-contained and single-file:
it bundles the .NET 8 runtime, so **no .NET installation is required**.

Verify the download against the attached `.sha256`:

```powershell
(Get-FileHash jrdp-win-x64.exe -Algorithm SHA256).Hash.ToLower()
Get-Content jrdp-win-x64.exe.sha256
```

## Usage

### Standalone client

```
jrdp                                    # open the connect dialog
jrdp --host srv01                       # connect immediately
jrdp --host srv01 --user CORP\me        # with credentials
jrdp --host srv01 --no-clipboard        # tune redirection
jrdp --help                             # all options
```

Passing `--password` puts the password in the process list where any local
user can read it. Prefer the dialog, or leave it out and let the server
prompt.

### Helper mode (embedding)

```
jrdp --stdio
```

The window is created borderless, off-screen and without a taskbar entry, and
its HWND is reported on stdout. Your process reparents it, positions it, and
controls the session with newline-delimited JSON:

```jsonc
> {"type":"hwnd","hwnd":332010}          // we tell you the window
> {"type":"ready","version":"1.0.0"}
< {"type":"setOwner","ownerHwnd":"65784"} // you adopt it
< {"type":"setBounds","x":0,"y":0,"width":1280,"height":720}
< {"type":"connect","host":"srv01","username":"me","password":"..."}
> {"type":"connecting"}
> {"type":"connected"}
```

See **[docs/protocol.md](docs/protocol.md)** for the full command and event
reference.

## Requirements

- Windows 10 or 11 (x64 or arm64)
- The RDP ActiveX control, registered — present by default on every Windows
  install. If jrdp reports that it can't find it, re-register with:
  `regsvr32 C:\Windows\System32\mstscax.dll`

## Build from source

Prerequisite: [.NET 8 SDK](https://dotnet.microsoft.com/download). Nothing
else — no Windows SDK, no Visual Studio Build Tools, no `aximp` (the COM
control is called through late-bound `dynamic` IDispatch, so there's no typed
interop to generate).

```powershell
./build.ps1                      # host arch -> dist/jrdp-win-<arch>.exe
./build.ps1 -Arch arm64
./build.ps1 -Version 1.2.3       # stamp a version
```

## Releasing

Releases are built by GitHub Actions. Push a tag and both arches are published
automatically:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The [release workflow](.github/workflows/release.yml) builds each arch on a
native runner (`windows-latest`, `windows-11-arm`), smoke-tests the artifact,
computes SHA-256 checksums, and creates the GitHub release with all four files
attached. You can also trigger it manually from the Actions tab for a tag that
doesn't exist yet.

Version numbers come from the tag (`v1.2.3` → assembly version `1.2.3`), so
there is no version constant to bump by hand except
[`Program.VERSION`](src/Program.cs), which is reported in the stdio `ready`
event.

## Layout

| File | Role |
| --- | --- |
| [src/Program.cs](src/Program.cs) | Entry point; picks GUI or stdio mode |
| [src/CommandLineOptions.cs](src/CommandLineOptions.cs) | Argument parsing and `--help` |
| [src/GuiMode.cs](src/GuiMode.cs) | Standalone client |
| [src/ConnectDialog.cs](src/ConnectDialog.cs) | The connect dialog |
| [src/StdioMode.cs](src/StdioMode.cs) | JSON protocol loop for embedding hosts |
| [src/RdpSessionHost.cs](src/RdpSessionHost.cs) | The window hosting the session |
| [src/RdpAxControl.cs](src/RdpAxControl.cs) | `AxHost` subclass wrapping `MsTscAx` |
| [src/RdpConnectionSettings.cs](src/RdpConnectionSettings.cs) | Connection options |
| [src/EscKeyWatcher.cs](src/EscKeyWatcher.cs) | Esc sniffers for keyboard-capture escape gestures |

## License

MIT — see [LICENSE](LICENSE).
