<#
.SYNOPSIS
    Copies a release of the FNRC Service Desk into its IIS site folder.

.DESCRIPTION
    Run on the web server, as an administrator, from the unzipped release package, after sql\2-database-update.sql
    has been run. While it copies, IIS shows app_offline.htm ("being updated"); the app starts again on the first
    request after. Files in the site folder that the release no longer has are removed, except the logs folder and
    appsettings.Production.json.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\update-site.ps1 -SiteFolder D:\Sites\itsm
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SiteFolder
)

$ErrorActionPreference = 'Stop'
$release = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $release 'ItsTool.API.dll'))) {
    throw "No release in $release. Run this script from the unzipped release package."
}
New-Item -ItemType Directory -Force $SiteFolder | Out-Null

$offline = Join-Path $SiteFolder 'app_offline.htm'
Copy-Item (Join-Path $PSScriptRoot 'app_offline.htm') $offline
try {
    # IIS needs a moment to stop the app and release its files
    Start-Sleep -Seconds 5
    # /MIR: the folder ends up exactly like the release, apart from the server's own settings file and the logs;
    # /R /W: retry a file still in use
    robocopy $release $SiteFolder /MIR /XF app_offline.htm appsettings.Production.json /XD logs /R:10 /W:3 /NP /NDL /NFL
    # Robocopy exit codes below 8 mean success
    if ($LASTEXITCODE -ge 8) { throw "Copying failed (robocopy exit code $LASTEXITCODE). The site still shows the update page." }
}
catch {
    Write-Warning 'The update page stays up until the copy succeeds. Fix the problem and run this script again.'
    throw
}
Remove-Item $offline
Write-Host "Updated $SiteFolder. Open the site to start it." -ForegroundColor Green
