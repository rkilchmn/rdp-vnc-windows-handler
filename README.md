# rdp:// and vnc:// URL handler for Windows

A browser URL protocol handler that turns links into sessions:

| link | result |
| --- | --- |
| `rdp://127.0.0.1:3390` | starts `mstsc.exe` (Windows Remote Desktop) with a generated `.rdp` file |
| `vnc://127.0.0.1:5901` | starts the TightVNC viewer (`tvnviewer.exe`) |

Host and port always come from the link, so the same handler works against a local bridge (for
example RDP/VNC running in WSL) as well as remote hosts you allow.

No Visual Studio or .NET SDK needed: it compiles with the C# compiler that ships with Windows
(.NET Framework 4.x).

## Layout

| File | Purpose |
| --- | --- |
| `src/RdpVncUrlHandler.cs` | the handler (windowed exe, no console flash) |
| `build.ps1` | compiles `dist\RdpVncUrlHandler.exe` with `%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| `install.ps1` | registers the URL protocols, backs up any previous registration |
| `uninstall.ps1` | restores the backups / removes the keys |
| `settings.sample.json` | defaults, copied to `dist\settings.json` |

## Build, install, test

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\dist\install.ps1          # registers rdp:// and vnc://
powershell -ExecutionPolicy Bypass -File .\dist\install.ps1 -TestUrl 'vnc://127.0.0.1:5901'
```

Restart the browser once so it picks up the new associations, then click:

```html
<a href="rdp://127.0.0.1:3390">rdp</a>
<a href="vnc://127.0.0.1:5901">vnc</a>
```

Chrome, Edge and Firefox hand `rdp://` and `vnc://` links to the Windows shell on Windows, and
the shell reads `HKCU\SOFTWARE\Classes\<scheme>`. The first click shows a browser confirmation
("Open Windows Remote Desktop?") - tick "Always allow" to skip it afterwards.

Uninstall:

```powershell
powershell -ExecutionPolicy Bypass -File .\dist\uninstall.ps1              # everything installed
powershell -ExecutionPolicy Bypass -File .\dist\uninstall.ps1 -Protocol vnc
```

## What the handler does

1. Reads the url from `argv[1]` (falling back to scanning the raw command line).
2. Accepts `rdp://` and `vnc://` only. Credentials in the url (`user@host`) and setting
   overrides on `vnc://` links are rejected, so a password never reaches a command line.
3. Checks the host against an allow list. Loopback (`127.0.0.0/8`, `::1`, `localhost`) is
   allowed by default; everything else is refused unless listed in `AllowedHosts` or
   `AllowRemoteHosts` is set. Any page a user visits can trigger a protocol handler, so it must
   not become an open redirector to arbitrary hosts.
4. Ports default to `3389` for `rdp://` and `5900` for `vnc://` when the link has none.
5. `rdp://` accepts MSTSC style overrides in the query string and re-validates the address if
   they try to redirect the connection:
   `rdp://127.0.0.1:3390/?desktopwidth:i=1600&administrative session:i=1`
6. Starts the client and logs what it ran.
7. A per-user named mutex keeps a second click from opening a duplicate RDP session
   (`AllowMultipleSessions` turns that off; `vnc://` is never deduplicated).

Exit codes: `0` ok, `1` bad url, `2` host not allowed, `3` client could not be started.

## `rdp://` - Windows Remote Desktop

Writes `%TEMP%\RdpUrlHandler\rdp-<pid>-<timestamp>.rdp` as UTF-16LE, starts
`mstsc.exe "<file>"`, waits for it to exit and deletes the file. Files older than a day are swept
on the next launch. Contents come from the settings table, for example:

```
screen mode id:i:2
desktopwidth:i:1280
desktopheight:i:800
full address:s:127.0.0.1:3390
prompt for credentials:i:1
authentication level:i:0
```

## `vnc://` - TightVNC

```
C:\Program Files\TightVNC\tvnviewer.exe 127.0.0.1::5901
```

- The viewer is located automatically in `%ProgramFiles%\TightVNC\tvnviewer.exe`,
  `%ProgramFiles(x86)%\TightVNC\tvnviewer.exe` and `%LOCALAPPDATA%\TightVNC\tvnviewer.exe`.
  `VncViewerPath` overrides that.
- `VncViewerArgs` is a template; `{hostport}` becomes `host::port`, `{host}` and `{port}` the
  individual parts. The default `{hostport}` is the positional form TightVNC expects - its
  `-connect host::port` option is rejected with "Error in format of command line".
- The handler returns as soon as the viewer is up, so several VNC windows can be open.
- The server decides whether to prompt: both ports observed here use VNC authentication, so
  TightVNC asks for the password. That is the viewer talking, not a handler failure.

## Settings

`%LOCALAPPDATA%\RdpVncUrlHandler\settings.json` (per user) or `settings.json` next to the exe
(portable). The per user file is merged last and wins.

| Setting | Default | Notes |
| --- | --- | --- |
| `MstscPath` | `""` | empty means `%SystemRoot%\System32\mstsc.exe` |
| `VncViewerPath` | `""` | empty means auto-detect TightVNC; a wrong path is reported as an error, not silently ignored |
| `VncViewerArgs` | `{hostport}` | positional TightVNC form; `{host}`, `{port}`, `{hostport}` are substituted |
| `VncDefaultPort` | `5900` | used when a `vnc://` link has no port |
| `DefaultPort` | `3389` | used when an `rdp://` link has no port |
| `ScreenModeId` | `2` | `2` windowed, `1` full screen |
| `DesktopWidth` / `DesktopHeight` | `1280` / `800` | windowed size |
| `UseMultiMon` | `0` | |
| `SessionBpp` | `32` | |
| `PromptForCredentials` | `1` | `0` reuses the credentials Windows saved for that host |
| `AuthenticationLevel` | `0` | `0` ignore certificate errors (self signed test servers), `1` warn, `2` require |
| `RedirectClipboard` | `1` | |
| `AllowedHosts` | `["127.0.0.1","localhost","::1"]` | exact names, `*.suffix` wildcards allowed |
| `AllowRemoteHosts` | `false` | `true` accepts any host - only do this if you trust every page you visit |
| `AllowMultipleSessions` | `false` | |
| `DeleteRdpFile` | `true` | keep the `.rdp` for debugging |
| `LogRetentionDays` | `14` | |
| `LogVerbose` | `false` | |

Example override:

```json
{
  "VncViewerArgs": "{hostport} -viewonly",
  "ScreenModeId": 1,
  "AllowedHosts": ["127.0.0.1", "localhost", "*.internal.example.com"]
}
```

## Diagnostics

```powershell
dist\RdpVncUrlHandler.exe 'rdp://127.0.0.1:3390' /dryrun   # print what would run, start nothing
dist\RdpVncUrlHandler.exe 'vnc://127.0.0.1:5901' /dryrun
dist\RdpVncUrlHandler.exe 'rdp://127.0.0.1:3390' /nowait /verbose
Get-Content "$env:LOCALAPPDATA\RdpVncUrlHandler\logs\handler-$(Get-Date -Format yyyyMMdd).log" -Tail 40
```

## Ports observed on this machine

| Port | What it is |
| --- | --- |
| 3390 / 3391 | the local RDP bridge; served from WSL with a shared loopback, so `Get-NetTCPConnection` shows no listener but `netstat` shows the sessions |
| 5900 / 5901 | VNC servers - full `RFB 003.008` handshake, single security type `2` (VNC authentication, so a password is required) |

A port that reports "address already in use" when you bind it locally while
`Get-NetTCPConnection` shows no listener usually means a WSL service holds it.

## Why not MobaXterm

MobaXterm was evaluated and dropped. Its RDP engine does launch from the command line, but its
VNC client can only be opened for a session that already exists in its saved session list:
`-exec "livesession …"`, `-protocolHandler` and its `MobaVNC.exe -connect host::port` all either
do nothing or fail, and a running MobaXterm ignores changes to its session file. Since VNC has
to work for any host and port from a link, TightVNC - which takes the address on the command
line - is the reliable choice.

## Security notes

- `vnc://` never accepts credentials in the url, so a password cannot leak through the command
  line or browser history; the viewer prompts instead.
- Hosts are allow-listed, and a `full address` override in an `rdp://` link is re-validated, so
  a hostile page cannot turn the handler into a launcher for arbitrary internal hosts.
- Installing `rdp://` per user overrides the built-in `mstsc` association for that user only.
  `-Machine` writes `HKLM\SOFTWARE\Classes` instead and needs an elevated shell.
  `install.ps1` exports any pre-existing key to
  `%LOCALAPPDATA%\RdpVncUrlHandler\<protocol>-backup-<timestamp>.reg`, and `uninstall.ps1`
  re-imports it.
- Some corporate policies or older Firefox profiles keep their own protocol handler list; in
  that case the link is opened by the built-in client and this handler is bypassed. Firefox on
  Windows normally uses the shell, so this is rare.

## Troubleshooting

- **Click does nothing**: check the browser's "always allow" state and confirm the key exists
  (`HKCU\SOFTWARE\Classes\rdp\shell\open\command`). Restart the browser after installing.
- **TightVNC shows "Error in format of command line"**: `VncViewerArgs` must not use
  `-connect`; use `{hostport}` or `-connect {host}::{port}` only with a viewer that accepts it.
- **TightVNC asks for a password**: both local VNC servers use VNC authentication.
- **No viewer found**: `winget install --id GlavSoft.TightVNC -e`, or set `VncViewerPath`.
- **`build.ps1` fails with "process cannot access the file"**: a handler process is still waiting
  for an open mstsc session, which keeps `RdpVncUrlHandler.exe` locked. Close the session (or use
  `/nowait` when starting one) and build again.
- **Ports 3390/3391 look empty**: they are served from WSL, check with `netstat` rather than
  `Get-NetTCPConnection`.