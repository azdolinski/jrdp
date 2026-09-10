# Registering jrdp with Windows

How to make Windows launch `jrdp.exe` when someone opens a `.rdp` file or
clicks an `rdp://` link.

Three integrations — set up whichever you need:

| Integration | Effect | Registry alone is enough? |
| --- | --- | --- |
| [`rdp://` protocol handler](#2-rdp-protocol-handler) | `rdp://srv01` links open jrdp | **Yes** — no such scheme exists by default |
| [`.rdp` right-click verb](#add-jrdp-without-taking-over-the-association-works-registry-only) | *Open with jrdp* on any `.rdp` file | **Yes** |
| [`.rdp` default handler](#1-rdp-file-association) | Double-click opens jrdp instead of mstsc | **No** — needs a one-time *Open with → Always* |

All three were verified on Windows 11 with the values below.

Everything below uses **`HKEY_CURRENT_USER`**: no admin rights, affects only
your account, and trivially reversible. Machine-wide variants are in
[Applying it machine-wide](#applying-it-machine-wide).

> **Prerequisite:** jrdp must already accept these inputs — that's jrdp 0.6.0
> or newer. Check with `jrdp --version`. On older builds the registry entries
> "work" but jrdp rejects the argument with *unknown argument*.

First, put `jrdp.exe` somewhere permanent — the registry stores an absolute
path, so a binary in `Downloads` breaks the moment you move it. These examples
use `C:\Tools\jrdp\jrdp.exe`.

---

## 1. `.rdp` file association

Windows launches the handler as `jrdp.exe "C:\path\to\file.rdp"`. jrdp parses
the file (`full address`, `username`, `domain`, redirection settings, …) and
connects.

### Set it up

Save as `register-rdp-file.reg`, edit the path, then double-click it:

```reg
Windows Registry Editor Version 5.00

; A private ProgID for jrdp, so we never overwrite Microsoft's RDP.File.
[HKEY_CURRENT_USER\Software\Classes\jrdp.RdpFile]
@="Remote Desktop Connection (jrdp)"

[HKEY_CURRENT_USER\Software\Classes\jrdp.RdpFile\DefaultIcon]
@="C:\\Tools\\jrdp\\jrdp.exe,0"

[HKEY_CURRENT_USER\Software\Classes\jrdp.RdpFile\shell\open\command]
@="\"C:\\Tools\\jrdp\\jrdp.exe\" \"%1\""

; Point the .rdp extension at that ProgID for this user only.
[HKEY_CURRENT_USER\Software\Classes\.rdp]
@="jrdp.RdpFile"
```

Note the doubled backslashes — `.reg` files require them.

Or in PowerShell (no file needed):

```powershell
$exe = 'C:\Tools\jrdp\jrdp.exe'
$root = 'HKCU:\Software\Classes'

New-Item -Path "$root\jrdp.RdpFile\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$root\jrdp.RdpFile" -Name '(default)' -Value 'Remote Desktop Connection (jrdp)'
Set-ItemProperty -Path "$root\jrdp.RdpFile\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""

New-Item -Path "$root\jrdp.RdpFile\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$root\jrdp.RdpFile\DefaultIcon" -Name '(default)' -Value "$exe,0"

New-Item -Path "$root\.rdp" -Force | Out-Null
Set-ItemProperty -Path "$root\.rdp" -Name '(default)' -Value 'jrdp.RdpFile'
```

### The registry alone is not enough for `.rdp` — this step is required

**Verified on Windows 11:** after writing the keys above, with *no*
`UserChoice` present, the shell still resolves `.rdp` to Microsoft's handler:

```
> cmd /c assoc .rdp
.rdp=RDP.File
> cmd /c ftype RDP.File
RDP.File="%systemroot%\system32\mstsc.exe" "%1"
```

Modern Windows will not let a program silently steal an extension that already
has a system handler, and it tamper-protects `UserChoice` with a hash so the
association can't be scripted either. **You must pick jrdp once, by hand:**

- Right-click a `.rdp` file → **Open with** → **Choose another app** →
  **jrdp** → *Always use this app*, **or**
- **Settings → Apps → Default apps → Choose defaults by file type** → `.rdp`

You only do this once; it's the supported way to override an existing
association. The registry keys above are what make jrdp *appear* in that list.

### Add jrdp without taking over the association (works registry-only)

**The recommended `.rdp` route.** Unlike changing the default, this takes
effect immediately from the registry with no manual "Open with" step — it adds
a right-click entry and leaves mstsc as the default, so nothing breaks for
other tools:

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\RDP.File\shell\OpenWithJrdp]
@="Open with jrdp"
"Icon"="C:\\Tools\\jrdp\\jrdp.exe,0"

[HKEY_CURRENT_USER\Software\Classes\RDP.File\shell\OpenWithJrdp\command]
@="\"C:\\Tools\\jrdp\\jrdp.exe\" \"%1\""
```

Right-click any `.rdp` file → **Open with jrdp**.

---

## 2. `rdp://` protocol handler

Makes `rdp://srv01` clickable from a browser, chat client, intranet page or
`Win+R`. **Windows ships no `rdp:` scheme by default**, so there is nothing to
override — this is a pure addition and won't disturb mstsc.

Windows launches the handler as `jrdp.exe "rdp://srv01:3389"`.

### Set it up

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\rdp]
@="URL:Remote Desktop Protocol"
"URL Protocol"=""

[HKEY_CURRENT_USER\Software\Classes\rdp\DefaultIcon]
@="C:\\Tools\\jrdp\\jrdp.exe,0"

[HKEY_CURRENT_USER\Software\Classes\rdp\shell\open\command]
@="\"C:\\Tools\\jrdp\\jrdp.exe\" \"%1\""
```

The empty **`URL Protocol`** value is what marks the key as a URI scheme —
without it Windows ignores the whole thing.

PowerShell equivalent:

```powershell
$exe = 'C:\Tools\jrdp\jrdp.exe'
$key = 'HKCU:\Software\Classes\rdp'

New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value 'URL:Remote Desktop Protocol'
Set-ItemProperty -Path $key -Name 'URL Protocol' -Value ''
Set-ItemProperty -Path "$key\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
```

### Supported URI shapes

```
rdp://srv01                                  host only
rdp://srv01:3390                             host and port
rdp://CORP%5Cme@srv01                        DOMAIN\user (%5C is the backslash)
rdp://srv01?redirectclipboard=0&audiomode=2  options via query string
rdp://full%20address=s:srv01:3390&username=s:me   Microsoft setting form
```

Query-string names accept both the `.rdp` spellings (`redirectclipboard`,
`drivestoredirect`, `audiomode`, `enablecredsspsupport`, …) and short aliases
(`clipboard`, `drives`, `printers`, `nla`, `width`, `height`).

**A password in a URI is deliberately ignored.** URIs end up in browser
history, shell history and server logs, so `rdp://me:secret@host` connects as
`me` with no password and lets the server prompt. Use the dialog for
credentials.

---

## Verifying it works

`--print-config` resolves the input and prints what jrdp *would* connect with,
without connecting — the fastest way to confirm the registry passes what you
expect:

```powershell
jrdp "rdp://CORP%5Cme@srv01:3390?redirectclipboard=0" --print-config
```

```
host               srv01
port               3390
username           me
domain             CORP
password           (none)
size               1280x720
smartSizing        True
redirectClipboard  False
...
```

Then test the real handler paths:

```powershell
# protocol handler (goes through the registry, exactly like a clicked link)
Start-Process 'rdp://srv01'

# file association
Start-Process 'C:\path\to\some.rdp'
```

Inspect what's actually registered:

```powershell
Get-ItemProperty 'HKCU:\Software\Classes\rdp\shell\open\command'
Get-ItemProperty 'HKCU:\Software\Classes\.rdp'
```

### If nothing happens

| Symptom | Cause |
| --- | --- |
| mstsc opens instead of jrdp | Windows **UserChoice** wins — set the default via *Open with → Always* (see [above](#about-windows-default-app-prompt)) |
| "unknown argument" dialog | jrdp older than 0.6.0, or `"%1"` written without quotes |
| Nothing at all on an `rdp://` link | `URL Protocol` value missing from `HKCU\Software\Classes\rdp` |
| "Windows cannot find …" | Wrong or moved `jrdp.exe` path; the registry needs an absolute path |
| Works in a shell, not from a link | Path contains spaces and isn't quoted — `"C:\Program Files\...\jrdp.exe" "%1"` |

Registry changes apply to newly launched processes. If Explorer seems stuck on
the old handler, restart it:

```powershell
Stop-Process -Name explorer -Force   # Explorer restarts itself
```

---

## Passing extra options

`"%1"` is the file or URI. Append flags after it and they win over whatever
the file or URI said — handy for enforcing a policy:

```reg
; always disable drive redirection, whatever the .rdp file requests
@="\"C:\\Tools\\jrdp\\jrdp.exe\" \"%1\" --no-drives"
```

Flags are applied after the input source, so `--no-drives` overrides
`drivestoredirect:s:*`.

---

## Applying it machine-wide

Same keys under `HKEY_LOCAL_MACHINE\Software\Classes` (which surfaces as
`HKEY_CLASSES_ROOT`) instead of `HKEY_CURRENT_USER\Software\Classes`. This
**requires an elevated prompt and affects every user on the machine**.

Prefer the per-user setup unless you're deploying to a shared or managed
machine. Per-user keys take precedence over machine-wide ones, so a user can
still override you.

```powershell
# Run as Administrator. Protocol handler only — a pure addition, since
# Windows ships no rdp: scheme.
$exe = 'C:\Tools\jrdp\jrdp.exe'
$key = 'HKLM:\Software\Classes\rdp'

New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value 'URL:Remote Desktop Protocol'
Set-ItemProperty -Path $key -Name 'URL Protocol' -Value ''
Set-ItemProperty -Path "$key\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
```

Taking `.rdp` machine-wide means repointing `HKLM\Software\Classes\.rdp` away
from `RDP.File`, which **changes RDP for every user on the machine**. Note the
original value first so you can restore it:

```powershell
# Run as Administrator
Get-ItemProperty 'HKLM:\Software\Classes\.rdp' -Name '(default)'   # normally RDP.File
Set-ItemProperty 'HKLM:\Software\Classes\.rdp' -Name '(default)' -Value 'jrdp.RdpFile'
```

On managed fleets, prefer Group Policy — *Set a default associations
configuration file* — over writing these keys directly; it survives Windows
resetting associations after updates.

---

## Uninstalling

### Per-user

```powershell
Remove-Item 'HKCU:\Software\Classes\rdp' -Recurse -Force -EA SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\jrdp.RdpFile' -Recurse -Force -EA SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\.rdp' -Recurse -Force -EA SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\RDP.File\shell\OpenWithJrdp' -Recurse -Force -EA SilentlyContinue
```

Removing `HKCU\Software\Classes\.rdp` restores whatever the machine-wide
setting was — normally mstsc.

If you also changed the default app through *Open with → Always*, clear the
UserChoice:

```powershell
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.rdp\UserChoice' -Force -EA SilentlyContinue
```

### Machine-wide

```powershell
# Run as Administrator
Remove-Item 'HKLM:\Software\Classes\rdp' -Recurse -Force -EA SilentlyContinue
Remove-Item 'HKLM:\Software\Classes\jrdp.RdpFile' -Recurse -Force -EA SilentlyContinue
Set-ItemProperty 'HKLM:\Software\Classes\.rdp' -Name '(default)' -Value 'RDP.File'
```

---

## Reference: what Windows ships by default

Useful for comparison, and confirmed on Windows 11:

```
HKEY_CLASSES_ROOT\.rdp                          -> "RDP.File"
HKEY_CLASSES_ROOT\RDP.File                      -> "Remote Desktop Connection"
HKEY_CLASSES_ROOT\RDP.File\shell\Connect\command -> mstsc.exe "%1"
HKEY_CLASSES_ROOT\RDP.File\shell\Open\command    -> mstsc.exe "%1"
HKEY_CLASSES_ROOT\RDP.File\shell\Edit\command    -> mstsc.exe /edit "%1"
```

There is **no `rdp:`, `ms-rd:` or `ms-rdp:` URI scheme** in a stock install —
the Microsoft Store "Remote Desktop" and "Windows App" clients register their
own (`ms-rd:`, `ms-avd:`) when installed. That's why section 2 is additive and
can't collide with mstsc.
