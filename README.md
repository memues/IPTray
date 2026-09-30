<h1 align="center">
  <img src="docs/icon.png" width="96" alt=""><br>
  IPTray
</h1>

<p align="center">
  A minimal Windows tray app that shows the public IP address you are currently seen as,
  using the flag of its country as the tray icon.
</p>

---

## What it does

- **Country flag as the tray icon.** The notification-area icon is the flag of the country your
  public IP belongs to. Flags are downloaded once and cached; the app falls back to a globe when
  the country is unknown, and adds a red badge when there is no connection.
- **Public IP at a glance.** Hover for a tooltip, or right-click for the address, the country and
  the ISP, with one click to copy the address.
- **Timestamped history.** Every change is appended to a CSV log. `IP history...` opens a window
  listing each address with the time it appeared, the country and the ISP.
- **DNS servers.** `DNS servers...` shows the IPv4 and IPv6 resolvers each connected adapter is
  using and whether they come from DHCP or are set statically, and lets you switch to a preset
  (Cloudflare, Google, Quad9, AdGuard, OpenDNS), enter your own, or go back to automatic.
- **Sensible refreshing.** Checks every 30 seconds to 15 minutes, your choice, and re-checks a few
  seconds after Windows reports a network change.
- Optional balloon notification on change, optional start with Windows, no account, no telemetry.
- Online lookups are opt-in on first use and can be turned off in the tray menu. See the
  [privacy policy](PRIVACY.md) for the services contacted and local data handling.

<p align="center">
  <img src="docs/window-logs.png" width="49%" alt="IP history window">
  <img src="docs/window-dns.png" width="49%" alt="DNS servers window">
</p>

## Install

Download `IPTray-1.1.0-setup.exe` from the
[latest release](https://github.com/memues/IPTray/releases/latest) and run it.

The installer offers a per-user install (no administrator rights needed) or an all-users install.
Nothing else is required: the .NET runtime is included in the package. If you care about the
tamper-resistance of a self-elevating app, prefer the all-users install — see
[SECURITY.md](SECURITY.md).

To remove it, use **Settings → Apps → Installed apps → IPTray**, or *Programs and Features*.
The uninstaller asks whether the settings and the IP history should be deleted too.

The release also includes `IPTray-1.1.0-win-x64-portable.zip`: extract the entire archive and run
`IPTray.exe`. Keep the bundled runtime files alongside the executable; no separate .NET install
is needed. The portable edition uses the same per-user data folder as the installer edition.

## Microsoft Store edition

The Microsoft Store product is named **IPTray Orbit**, published by **omni.apps**. Its
Store ID is `9MSXJD9BWBD7`. Availability depends on Microsoft certification;
creating the product or building its package does not mean it has been approved.

The Store edition keeps IP monitoring, flags, history, optional notifications, refresh intervals
and optional startup. It displays DNS configuration and opens Windows network settings for
DNS edits. It contains no elevated helper and does not apply DNS changes itself. Direct DNS
switching remains available in the GitHub installer and portable editions.

Store data is isolated in the package's local application data folder, displayed by **About
IPTray**. Store startup uses a Windows startup task instead of a Run registry entry. Install
only one edition for everyday use; the editions share the single-instance guard.

> [!NOTE]
> **Smart App Control blocks unsigned programs.** IPTray is not code-signed, so on a Windows 11
> machine with Smart App Control switched on, the installer or the uninstaller may be refused with
> "An Application Control policy has blocked this file". This has been observed against the
> uninstaller in particular. If it happens, see
> [SECURITY.md](SECURITY.md#unsigned-binaries-and-smart-app-control) for how to remove IPTray by
> hand — it is four things in four places, nothing hidden.

## Where things are stored

In the GitHub edition, everything lives under `%APPDATA%\IPTray`:

| File | Contents |
| --- | --- |
| `ip-log.csv` | The IP history, UTF-8 CSV, newest row last |
| `settings.json` | Check interval and the notification preference |
| `flags\` | Cached flag images |
| `error.log` | Only written if something goes wrong |

The "start with Windows" option is a single value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, removed when you uninstall.

## How the public IP is resolved

Three providers are tried in order, so one being down, rate limited or blocked does not break the
app: [ipwho.is](https://ipwho.is), [ipapi.co](https://ipapi.co), then
[ipify](https://www.ipify.org) combined with [country.is](https://country.is). Country names come
from the .NET region database, so they read the same whichever provider answered.

## Changing DNS servers

In the GitHub edition, IPTray itself runs without administrator rights. Applying a DNS change needs them, so the app
launches one elevated copy of itself that runs the `netsh interface ipv4 set dnsservers` commands
and reports the result back. You will see a single UAC prompt per change, and nothing happens if
you decline it. Reading the current configuration never needs elevation.

That elevated copy is the only place IPTray crosses a privilege boundary, so it is deliberately
narrow: it takes its instructions from its command line rather than from a file, validates every
argument itself, launches `netsh` by absolute path, and writes nothing to disk.
[SECURITY.md](SECURITY.md) explains the reasoning and lists the residual risks.

## Building from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and
[Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install --id JRSoftware.InnoSetup`).

```cmd
build.cmd
```

This publishes a self-contained win-x64 build into `build\publish` and compiles the installer into
`build\installer`. To run it without packaging:

```cmd
dotnet run --project src\IPTray\IPTray.csproj
```

For the Store package, install the Windows SDK (MakeAppx) and use the values in
[`packaging/store-identity.json`](packaging/store-identity.json):

```powershell
$identity = Get-Content packaging/store-identity.json -Raw | ConvertFrom-Json
.\build-store.ps1 -IdentityName $identity.IdentityName -Publisher $identity.Publisher -PublisherDisplayName $identity.PublisherDisplayName -DisplayName $identity.DisplayName -PackageVersion $identity.PackageVersion
```

Pass `-MakeAppxPath` if the tool is installed outside the Windows SDK directory. The script
publishes a separate Store build, validates the manifest, creates fresh staging files and
outputs `build/store/IPTray-1.1.0-x64.msix`. Submit this unsigned MSIX to Partner Center;
Microsoft signs accepted Store packages. It is not a directly installable GitHub download.
Use the EXE installer or portable ZIP for GitHub distribution.

Distribution and privacy regression checks (no DNS changes or elevation):

```powershell
dotnet build tests/IPTray.Checks -c Release
dotnet tests/IPTray.Checks/bin/Release/net8.0-windows10.0.19041.0/win-x64/IPTray.Checks.dll
dotnet build tests/IPTray.Checks -c Release -p:StoreBuild=true
dotnet tests/IPTray.Checks/bin/Release/net8.0-windows10.0.19041.0/win-x64/IPTray.Checks.dll
```

## Licence

[MIT](LICENSE)
