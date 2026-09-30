# IPTray privacy policy

Effective date: September 30, 2026. Applies to IPTray 1.1.0 and later, including the GitHub desktop edition and Microsoft Store edition.

IPTray is an open-source desktop utility maintained at [memues/IPTray](https://github.com/memues/IPTray). It has no user accounts, advertising, analytics or telemetry. The maintainers do not operate a server receiving your IP history.

## Online lookups and third parties

Before the first lookup, IPTray asks whether you allow online lookups. If you decline, no IP lookup or flag download is made. DNS viewing and existing local history remain available. Change this choice at any time with **Allow online IP lookups** in the tray menu; turning it off cancels current lookup requests and stops further requests. Data already received by a service cannot be recalled.

When enabled, IPTray uses HTTPS to contact these services, trying alternatives when necessary:

- [ipwho.is](https://ipwho.is/) and [ipapi.co](https://ipapi.co/): return your public IP, approximate country and ISP/organization.
- [ipify](https://www.ipify.org/): returns your public IP.
- [country.is](https://country.is/): receives the public IP returned by ipify in its request URL and returns the country.
- [FlagCDN](https://flagcdn.com/) and [FlagsAPI](https://flagsapi.com/): receive a country code to download a flag image.

As with other internet connections, each contacted service can see your public IP address and ordinary connection information. IPTray sends an application User-Agent, but no unique device identifier, credentials, complete IP history, adapter names or settings. The country is inferred from the public IP; IPTray does not access GPS or the Windows Location Service. These independent services have their own data practices, retention and policies; IPTray cannot control their server logs. Opening the privacy policy or project links contacts GitHub through your browser under GitHub's own privacy practices.

## Local data and retention

IPTray stores the timestamp, IP address, country and ISP/organization when the observed IP changes. The CSV history, JSON settings, cached flags and diagnostic error log stay on your PC. They are not uploaded by IPTray. History rotates at approximately 2 MiB, keeping the current file and one previous file. Error logs may contain exception details and local paths.

The GitHub edition stores data in `%APPDATA%\IPTray`. The Store edition uses its package's local application data folder, independently of the GitHub edition. The exact folder is shown by **About IPTray**. Local files are protected by your Windows account permissions, but are not separately encrypted by IPTray. Other software with access to your account may read them; exported CSV files should be treated as personal information.

You can inspect and export the current history in **IP history...**, clear the current history there, or exit IPTray and delete files in the data folder (including `ip-log.previous.csv` for the archived history). Turning off lookups does not automatically delete existing data. The GitHub uninstaller offers optional removal of local data during an interactive uninstall. Windows manages removal of Store package data.

## DNS and startup

IPTray reads the DNS server addresses and identifiers of connected network adapters locally. This information is not sent to lookup services. In the GitHub edition, applying a DNS change launches a validated helper after a Windows administrator approval prompt. The Store edition only displays DNS configuration and opens Windows network settings for edits; it does not elevate or change DNS itself.

Startup is optional. The GitHub edition uses a per-user Run registry entry. The Store edition uses a Windows package startup task, which you can also control in Windows Settings or Task Manager.

## Contact and updates

For questions or requests, [open an issue](https://github.com/memues/IPTray/issues) without posting your IP history or other private data. Changes to IPTray's data handling will be reflected in this policy and the release notes.
