# Deploying to staging (staging.fnrc.gov.ae/itsm)

The service desk runs on IIS as a sub-application of the staging site, at **https://staging.fnrc.gov.ae/itsm/**.
It has its own application pool, its own folder and its own SQL Server database.

- **First installation:** steps 1 to 7, about an hour once the prerequisites are in place.
- **Each new release:** five short steps, see [Each new release](#each-new-release).

Rehearsed on 7 Nov 2026 with IIS Express on a development PC: sub-application under a parent site, database built
only from the package's scripts, app running on the limited database account, first sign-in, a ticket, live
notifications and a release update. Two things could not be rehearsed there and are Microsoft's documented IIS
setup: settings kept on the application pool, and (optionally) the pool running as a service account.

## What you need

| | Who provides it | Notes |
|---|---|---|
| Windows Server 2016 or later with IIS 10, admin rights | Server team | The server that hosts staging.fnrc.gov.ae |
| **ASP.NET Core 8.0 Hosting Bundle**, latest 8.0.x | Server team | [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0), "Hosting Bundle" under ASP.NET Core Runtime |
| IIS features **WebSocket Protocol** and **Application Initialization** | Server team | Recommended. Without WebSockets, live notifications fall back to slower polling; without Application Initialization, the app starts on the first visit after IIS recycles it |
| SQL Server, and a login that may create a database | DBA | SQL Server Management Studio (SSMS) to run the scripts. The server must allow SQL logins ("SQL Server and Windows Authentication mode"): the app signs in with one |
| Two file shares, for attachments and for security evidence | File server team | Set-up and permissions: [deployment-file-storage.md](deployment-file-storage.md) |
| HTTPS on staging.fnrc.gov.ae | Server team | The sub-application uses the site's certificate |

In the commands below, replace these examples with the real names:

| Example | Meaning |
|---|---|
| `staging.fnrc.gov.ae` | The parent site's name in IIS Manager |
| `SQLSTG01` | The SQL Server |
| `FnrcItsm` | The database |
| `FNRC\WEBSTG01$` | The web server's computer account, which reaches the file shares for the app |
| `D:\Sites\itsm` | The app's folder: outside the parent site's folder |
| `\\fileserver\ITSM-Files`, `\\fileserver\ITSM-Evidence` | The two shares |

## Do I need Visual Studio?

No. One command on your PC builds the package (step 1): the same site files as Visual Studio's *Publish*, plus the
database update script, which Visual Studio does not make. Nothing needs to be installed on the server apart from
the Hosting Bundle.

## 1. Build the release package (your PC)

In the repository folder:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

It needs the .NET SDK and the EF Core tool (`dotnet tool install --global dotnet-ef --version 8.*`), both on the
development PC already. It makes `artifacts\fnrc-itsm-<date>-<commit>.zip`:

| In the package | What it is |
|---|---|
| `app\` | The site |
| `sql\1-create-database.sql` | First installation only |
| `sql\2-database-update.sql` | Every release: brings the database up to this release. Safe to run again |
| `sql\3-app-account.sql` | First installation only: gives the app's account its limited rights |
| `update-site.ps1`, `app_offline.htm` | Copies the site into place, showing a "being updated" page meanwhile |

Copy the zip to the web server, for example to `D:\Releases\`, and unzip it there.

## 2. Create the database (DBA, first installation)

In SSMS, as a SQL Server administrator, open and run the scripts in this order:

1. `sql\1-create-database.sql`: creates `FnrcItsm`. Add it to the backup plan, together with the two shares.
2. `sql\2-database-update.sql`, with `FnrcItsm` selected: creates the tables and the `itsm_app` role.
3. `sql\3-app-account.sql`, with `FnrcItsm` selected. It creates the SQL login `itsm_app_login` and shows its
   password **once**, in the results. Copy it straight into step 5's connection string; do not keep it anywhere else.
   (If the domain team provides a service account for the app pool, set `@WindowsAccount` at the top first: the
   app then signs in as that account, with no password.)

The app's account can read and change data, but not tables, and it cannot change or delete audit and history
records, in the app or directly in SQL. Database updates are therefore always run by the DBA's login.

## 3. Install the Hosting Bundle (server, first installation)

Run the Hosting Bundle installer, then `iisreset` in an administrator prompt. **This briefly restarts every site on
the server.** In IIS Manager, the server's *Modules* list now shows `AspNetCoreModuleV2`.

## 4. Create the application pool and the application (server, first installation)

In PowerShell as an administrator:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
$pool   = 'FNRC-ITSM'
$site   = 'staging.fnrc.gov.ae'
$folder = 'D:\Sites\itsm'

# No .NET Framework; keep it running so SLA checks and reminders do not stop when nobody is using the desk
& $appcmd add apppool /name:$pool /managedRuntimeVersion:"" /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00
& $appcmd add app /site.name:$site /path:/itsm /physicalPath:$folder /applicationPool:$pool /preloadEnabled:true

New-Item -ItemType Directory -Force $folder | Out-Null
icacls $folder /grant "IIS AppPool\${pool}:(OI)(CI)RX"
```

The pool keeps IIS's own identity (`IIS AppPool\FNRC-ITSM`). On the file shares it appears as the web server's
computer account, `FNRC\WEBSTG01$`: grant that account the rights in
[deployment-file-storage.md](deployment-file-storage.md).

Only with a service account from the domain team: set it in IIS Manager (*Application Pools* > *FNRC-ITSM* >
*Advanced Settings* > *Identity* > *Custom account*; for a group managed service account, leave both password boxes
empty), and give it the folder and share rights instead.

The app must have **its own pool**: an ASP.NET Core app cannot share one (error 500.35).

## 5. Settings (server, first installation)

Settings are kept on the application pool, not in the site folder, so a release never overwrites them and the
passwords in them are readable by administrators only. In the same PowerShell window:

```powershell
function Set-ItsmSetting([string]$Name, [string]$Value) {
    # Replaces the setting if it is already there
    & $appcmd set config /section:system.applicationHost/applicationPools "/-[name='$pool'].environmentVariables.[name='$Name']" /commit:apphost *> $null
    & $appcmd set config /section:system.applicationHost/applicationPools "/+[name='$pool'].environmentVariables.[name='$Name',value='$Value']" /commit:apphost
}
$key = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($key)

Set-ItsmSetting 'ConnectionStrings__DefaultConnection' 'Server=SQLSTG01;Database=FnrcItsm;User ID=itsm_app_login;Password=<the password from step 2>;Encrypt=True'
Set-ItsmSetting 'Jwt__Secret' ([Convert]::ToBase64String($key))
Set-ItsmSetting 'AppBaseUrl' 'https://staging.fnrc.gov.ae/itsm'
Set-ItsmSetting 'FileStorage__BasePath' '\\fileserver\ITSM-Files\uploads'
Set-ItsmSetting 'FileStorage__EvidencePath' '\\fileserver\ITSM-Evidence\evidence'
Set-ItsmSetting 'AutoSeed' 'true'

# The first administrator, for the first start only (step 7 removes these)
Set-ItsmSetting 'Bootstrap__AdminUsername' '<your username>'
Set-ItsmSetting 'Bootstrap__AdminEmail' '<your work e-mail>'
Set-ItsmSetting 'Bootstrap__AdminPassword' '<a temporary password>'
```

| Setting | What it does |
|---|---|
| `ConnectionStrings__DefaultConnection` | The database, and the SQL login from step 2 with its password. With a service account instead: `Integrated Security=True` in place of `User ID` and `Password` |
| `Jwt__Secret` | Signs sign-in sessions. Generated above; changing it signs everyone out |
| `AppBaseUrl` | The address used in links in e-mails |
| `FileStorage__*` | Where attachments and evidence are kept (UNC paths, never a mapped drive) |
| `AutoSeed` | At each start: adds the roles, permissions and reference data a new release brings. In an empty database it also creates the starting data (statuses, priorities, sample departments, groups and knowledge articles) and the first administrator |
| `Bootstrap__*` | The first administrator. The password must be at least 8 characters with an upper-case letter, a lower-case letter, a digit and a symbol. It has to be changed at the first sign-in |

Optional, for e-mail notifications: `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`. Without
`Smtp__Host` the app sends no e-mail.

A value must not contain a single quote (`'`). If SQL Server's certificate is not trusted by the web server, the
connection fails with "The certificate chain was issued by an authority that is not trusted": ask the DBA for a
proper certificate, or on staging only add `TrustServerCertificate=True` to the connection string.

**IIS older than 10** has no pool settings. There, put the same settings in `D:\Sites\itsm\appsettings.Production.json`
(`{"ConnectionStrings": {"DefaultConnection": "..."}, "Jwt": {"Secret": "..."}, ...}`) and allow only
administrators and the pool's account to read that file. Releases leave it in place.

## 6. Copy the site (server)

From the unzipped package, in PowerShell as an administrator:

```powershell
powershell -ExecutionPolicy Bypass -File .\update-site.ps1 -SiteFolder D:\Sites\itsm
```

## 7. First sign-in

1. Open **https://staging.fnrc.gov.ae/itsm/**. The first visit takes a few seconds while the app starts.
2. Sign in with the `Bootstrap__` username and temporary password, and choose your own password.
3. Remove the temporary administrator settings (in a new PowerShell window, first set `$appcmd` and `$pool` as in step 4):

   ```powershell
   foreach ($name in 'Bootstrap__AdminUsername', 'Bootstrap__AdminEmail', 'Bootstrap__AdminPassword') {
       & $appcmd set config /section:system.applicationHost/applicationPools "/-[name='$pool'].environmentVariables.[name='$name']" /commit:apphost
   }
   ```

4. `https://staging.fnrc.gov.ae/itsm/api/health` answers `"status":"OK"`.

## Each new release

1. **Your PC:** `scripts\publish.ps1`, then copy the zip to the server and unzip it.
2. **DBA:** back up the database.
3. **DBA:** run the package's `sql\2-database-update.sql` on `FnrcItsm`.
4. **Server:** `.\update-site.ps1 -SiteFolder D:\Sites\itsm` from the package. Users see the "being updated" page for
   a few seconds.
5. Open the site and sign in.

Settings, the database account and the pool stay as they are. If step 3 is forgotten, the app does not start
(see 500.30 below) rather than running against an old database.

**To undo a release:** restore the backup from step 2, then run `update-site.ps1` from the previous package.

## When something goes wrong

The app writes its errors to **Event Viewer > Windows Logs > Application**, sources *IIS AspNetCore Module V2* and
*.NET Runtime*.

| What you see | Cause and fix |
|---|---|
| HTTP 500.19 | The Hosting Bundle is missing: install it and run `iisreset`. Or the parent site's `web.config` locks a section the app sets: ask the parent site's owner |
| HTTP 500.30, "app failed to start" | Read the event log. *Connection string 'DefaultConnection' is not configured* or *Jwt:Secret is not configured*: step 5. *Login failed for user 'itsm_app_login'*: the password in the connection string is not the one step 2 showed, or `3-app-account.sql` was not run. A lost password is replaced by the DBA (`ALTER LOGIN [itsm_app_login] WITH PASSWORD = N'<new password>';`, at least 12 characters with upper and lower case letters and digits), then put in the connection string (step 5). *CREATE TABLE permission denied* or *Invalid object name*: this release's `2-database-update.sql` was not run |
| HTTP 500.35 | Another app shares the pool: give `/itsm` its own (step 4) |
| HTTP 401 or a Windows sign-in prompt before the sign-in page | Windows Authentication is inherited from the parent site: in IIS Manager, select `itsm` > *Authentication*, enable *Anonymous*, disable *Windows* |
| The parent site's page or 404 instead of the service desk | The parent site's URL Rewrite rules apply to `/itsm` too: ask its owner to exclude `itsm/` from them |
| Reminders and SLA alerts arrive late after quiet periods | The pool idled or recycled: check step 4's settings and install Application Initialization |

## Security notes

- **Shared address:** under `staging.fnrc.gov.ae/itsm` the app shares an origin with every other application on
  that site. Browsers keep sign-ins per origin, so a flaw in any of those applications could reach service desk
  sessions. That is acceptable for staging; production should have its own host name, such as `itsm.fnrc.gov.ae`.
- Passwords and keys (the database password, the signing key) are only in the pool's settings: never in the
  repository, the package or the site folder.
- The app's database account cannot change tables, audit records or histories.
- Remove the `Bootstrap__` settings after the first sign-in (step 7).
