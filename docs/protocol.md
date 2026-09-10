# jrdp stdio protocol

Reference for `jrdp --stdio` (helper mode). This is the contract an embedding
host programs against; it is unchanged from the original yterminal
`RdpActivexProxyClient` helper, so hosts written against that work as-is.

## Framing

One JSON object per line, UTF-8, `\n`-terminated, in both directions.
stdout is line-buffered and auto-flushed, so events arrive as they happen.

- **Commands** go to jrdp's stdin.
- **Events** come from jrdp's stdout.
- Unparseable input produces an `error` event and is skipped — it never kills
  the process.
- When stdin closes, jrdp exits. You don't need to send `quit` if you're
  tearing down the pipe anyway.

## Lifecycle

```
spawn jrdp --stdio
  <- {"type":"hwnd","hwnd":332010}      // always first
  <- {"type":"ready","version":"1.0.0"}
  -> {"type":"setOwner","ownerHwnd":"65784"}
  -> {"type":"setBounds","x":0,"y":0,"width":1280,"height":720}
  -> {"type":"setVisible","visible":true}
  -> {"type":"connect","host":"srv01",...}
  <- {"type":"connecting"}
  <- {"type":"connected"}
  ...
  -> {"type":"disconnect"}
  <- {"type":"disconnected"}
  -> {"type":"quit"}
  <- {"type":"exiting"}
```

The window exists before you connect, so reparent and position it first: the
session then renders straight into its final size and skips a renegotiation
round-trip.

## Commands

### `connect`

Opens a session. Every field except `host` is optional; defaults shown.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `host` | string | — | Server name or IP (required) |
| `port` | int | `3389` | TCP port |
| `username` | string | `""` | User name |
| `domain` | string | `""` | Domain |
| `password` | string | `""` | Cleartext password; omitted → server prompts |
| `width` | int | `1280` | Requested desktop width |
| `height` | int | `720` | Requested desktop height |
| `smartSizing` | bool | `true` | Scale the session to the window |
| `redirectDrives` | bool | `true` | Local drives |
| `redirectPrinters` | bool | `true` | Printers |
| `redirectClipboard` | bool | `true` | Clipboard |
| `redirectSmartCards` | bool | `false` | Smart cards |
| `redirectPorts` | bool | `false` | COM / LPT ports |
| `redirectWebauthn` | bool | `false` | WebAuthn / FIDO |
| `audioMode` | int | `0` | `0` play locally, `1` play remotely, `2` disabled |
| `audioCapture` | bool | `false` | Redirect the microphone |
| `enableNla` | bool | `true` | Network Level Authentication (CredSSP) |
| `ignoreCertErrors` | bool | `false` | Accept any server certificate (**insecure**) |

Options unsupported by the installed control version are skipped with a
`warning` rather than failing the connection.

### `disconnect`

```json
{"type":"disconnect"}
```

Ends the session but keeps the process alive, so you can `connect` again
without respawning.

### `setBounds`

```json
{"type":"setBounds","x":0,"y":0,"width":1280,"height":720}
```

Coordinates are **physical pixels**, not DIPs — jrdp is Per-Monitor-V2 DPI
aware. From Electron, convert first with `screen.dipToScreenRect()`.

Moving and repainting happens immediately; the expensive part (renegotiating
the remote desktop size with the server) is debounced 200 ms, so streaming
`setBounds` during a drag or animation is safe and cheap.

Calls with a width or height below 10 px are ignored with a `warning` — those
indicate a host that hasn't laid out yet, and forwarding them would renegotiate
the remote desktop to a near-zero size.

### `setVisible`

```json
{"type":"setVisible","visible":true}
```

Show or hide the window without disconnecting — use it when your tab goes
background.

### `setOwner`

```json
{"type":"setOwner","ownerHwnd":"65784"}
```

Sets the owner window (`GWLP_HWNDPARENT`), which is how the session becomes
part of your window. `"0"` or omitted detaches.

`ownerHwnd` is a **decimal string, not a number**: an HWND on 64-bit Windows
can exceed JavaScript's `Number.MAX_SAFE_INTEGER`.

### `screenshot`

```json
{"type":"screenshot","format":"jpeg","quality":75}
```

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `format` | string | `"jpeg"` | `"jpeg"` or `"png"` |
| `quality` | int | `75` | JPEG quality 1–100; ignored for PNG |

Answers with a `screenshot` event. Capture uses `PrintWindow` with
`PW_RENDERFULLCONTENT`, which works on the DirectX-composited RDP surface —
so it succeeds even when the window is occluded or hidden.

### `quit`

```json
{"type":"quit"}
```

Exits cleanly, emitting `exiting` last.

## Events

| Event | Payload | When |
| --- | --- | --- |
| `hwnd` | `hwnd` (int64) | Once at startup, before `ready` |
| `ready` | `version` (string) | jrdp is accepting commands |
| `connecting` | — | Negotiation started |
| `connected` | — | Session is live |
| `disconnected` | — | A live or connecting session ended |
| `screenshot` | `format` + `data` (base64), or `error` | Reply to `screenshot` |
| `key-esc` | — | Esc was pressed (see below) |
| `warning` | `message` | Non-fatal: unsupported option, failed Win32 call |
| `error` | `message`, sometimes `stack` | A command failed, or an unhandled exception |
| `exiting` | — | Last line before exit |

Connection state is derived by polling the control's `Connected` property
every 200 ms. `disconnected` fires only on a transition **out of** a
connecting/connected state, so the initial disconnected reading is never
reported as a drop.

### `key-esc`

The RDP control takes full keyboard capture, which means your host's normal
key handling — including whatever you use for "let me out of this session" —
never sees a keystroke while the session has focus.

jrdp watches for Esc with both a thread-level message filter and a
`WH_KEYBOARD_LL` hook (the control installs its own low-level hook, so the
filter alone misses Esc during fullscreen capture) and emits `key-esc` for
each press. Keystrokes are **never consumed** — Esc still reaches the remote
session.

Hosts typically use this for a gesture like "Esc three times quickly releases
the embed". Implement the timing on your side; jrdp only reports presses.

## Error handling

- Malformed JSON → `error`, line skipped.
- Unknown command → `error`, ignored.
- A command that throws → `error` naming the command; the process survives.
- An unsupported COM property → `warning`; the connection continues.
- Unhandled exception → `error` with a `stack`, then the process dies. If you
  see one, it's a bug — please report it.

Treat `error` as diagnostic, not necessarily fatal: the only reliable
"session is gone" signals are `disconnected` and process exit.
