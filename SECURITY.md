# Security

## Trust boundary

IPTray runs as a normal, unelevated user process. It crosses a privilege boundary in exactly one
place: changing DNS servers requires administrator rights, so the app launches a second, elevated
copy of itself (`IPTray.exe --apply-dns ...`) behind a UAC prompt. Everything else — looking up the
public IP, downloading flags, reading DNS configuration, writing the log — happens without
elevation.

The interesting attacker in this model is **another process already running as the same user**.
It cannot read the elevated process's memory, but it can write to every per-user location IPTray
touches, so nothing in those locations may be trusted by the privileged half.

## Fixed in 1.0.1

| | Issue | Fix |
| --- | --- | --- |
| 1 | **Helper executables were launched by name.** The elevated helper ran `netsh` and `ipconfig` without a path, so Windows searched the application directory and the working directory first. Placing a `netsh.exe` in either — both writable by the user in a per-user install — meant it ran with administrator rights. | Both are now launched by absolute path from `%SystemRoot%\System32`, with the working directory pinned to the system directory. |
| 2 | **The request was passed through a file in `%TEMP%`.** Another process running as the user could rewrite it between the moment the UAC prompt appeared and the moment the elevated copy read it, so the user could approve "set Cloudflare" and get the attacker's resolvers instead. | The request is now passed as command-line arguments, which are fixed when the process is created and cannot be modified afterwards. |
| 3 | **The elevated helper wrote a result file next to the request.** An unprivileged process that redirected that path (junction or symlink) turned it into an arbitrary file overwrite running as administrator. | The helper writes no files at all. The outcome is reported through the process exit code, which the unelevated half turns into a message. |
| 4 | **The privileged half trusted its input.** Addresses and the adapter were validated only in the window that built the request. | The elevated half now validates everything itself: the adapter identifier must be an alphanumeric/brace/dash token, addresses must be canonical dotted-quad IPv4, and the adapter must actually exist. The adapter is identified by its Windows GUID rather than by its display name, so no free-form text reaches the command line. |
| 5 | **CSV formula injection.** The ISP and country strings come from a remote lookup service and are written to `ip-log.csv`, which IPTray writes with a byte-order mark specifically so it opens nicely in Excel. A value beginning with `=`, `+`, `-`, `@` or a tab would have been evaluated as a formula. | Those leading characters are neutralised with an apostrophe on write and stripped again on read, so the stored value still round-trips. |
| 6 | **Unbounded response bodies.** A hostile or compromised lookup endpoint could stream an endless body into memory. | Responses are capped at 2 MB. |
| 7 | **Country code was only length-checked** before being used to build a URL and a cache file name. | It must now be two ASCII letters. |
| 8 | **`DOTNET_STARTUP_HOOKS` was honoured.** The runtime loads any assembly named by that variable before `Main` runs. On a split-token administrator account the elevated copy is created from the same per-user environment, so a process running as the user could have had its own code executed with administrator rights. Verified reproducible against the 1.0.0 build. | `StartupHookSupport` is set to `false`, which makes the runtime ignore the variable. Verified: the same test now exits with the argument-validation code instead of loading the assembly. |
| 9 | **`BinaryFormatter` was enabled.** Windows Forms leaves the unsafe deserialiser switched on for legacy clipboard and drag-drop payloads. IPTray never needs it. | `EnableUnsafeBinaryFormatterSerialization` is set to `false`. Copying plain text still works and is covered by a test. |

## Known residual risks

**A per-user install can be tampered with by the account that owns it.**
When you choose "install for me only", IPTray lands in `%LOCALAPPDATA%\Programs\IPTray`, which the
user account can write to. Because the app elevates itself for DNS changes, malware already running
as that user could replace `IPTray.exe` and have its own code run with administrator rights the next
time you approve a DNS change. This is inherent to every self-elevating per-user application, and it
cannot be fixed from inside the app.

*If this matters to you, choose "install for all users" in the installer.* IPTray then lives in
`Program Files`, which only administrators can modify, and the elevated copy is guaranteed to be the
one you installed.

**A .NET process can still be instrumented through the environment.** Setting
`CORECLR_ENABLE_PROFILING` and `CORECLR_PROFILER_PATH` makes the runtime load a native DLL into any
.NET process. There is no runtime switch to refuse that, so a process running as you, on an account
where elevation does not change user (the default split-token administrator setup), could get its
code into the elevated helper if it can get you to approve a DNS change. Installing to `Program
Files` does not help here, because the problem is the environment rather than the files.

What does help is elevating as a *different* account — a standard user account that prompts for
separate administrator credentials. The elevated helper is then built from the administrator's
environment, which an attacker confined to your account cannot write to.

**The binaries are not code-signed.** SmartScreen will warn on first run, and the publisher shows as
unknown in the UAC prompt. Verify the SHA-256 checksum published with each release before running the
installer.

**The IP history is stored in the clear.** `%APPDATA%\IPTray\ip-log.csv` records every public IP your
machine has had, with timestamps. Anyone with access to your user profile can read it. Use *Clear log*
in the IP history window, or let the uninstaller delete the data folder, if that is not acceptable.

**Flag images are decoded from a remote source.** They arrive over HTTPS from `flagcdn.com` or
`flagsapi.com` and are decoded by the Windows imaging stack. A vulnerability in that decoder would be
reachable this way. Decoding happens unelevated and the result is validated before it is cached.

## What IPTray does not do

It opens no listening ports, installs no service or driver, hooks nothing, requires no account, sends
no telemetry, and transmits nothing about your machine. The only outbound requests are to the IP
lookup providers and the flag CDNs listed in the README.

## Reporting a vulnerability

Open a [security advisory](https://github.com/memues/IPTray/security/advisories/new) or a regular
issue if it is not sensitive.
