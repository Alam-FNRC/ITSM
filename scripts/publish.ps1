<#
.SYNOPSIS
    Builds a release package of the FNRC Service Desk for an IIS server.

.DESCRIPTION
    Creates artifacts\fnrc-itsm-<date>-<commit>\ and a .zip of it:
      app\                       the site, to copy into the IIS site folder (update-site.ps1 does that)
      sql\1-create-database.sql  first installation only
      sql\2-database-update.sql  every release: brings the database up to this release; safe to run again
      sql\3-app-account.sql      first installation only: gives the app pool's account its limited rights
      update-site.ps1, app_offline.htm
    See docs\deployment-staging.md.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..'))

dotnet ef --version *> $null
if ($LASTEXITCODE) { throw 'The EF Core tool is missing. Install it with: dotnet tool install --global dotnet-ef --version 8.*' }

$commit = (git rev-parse --short HEAD).Trim()
if (git status --porcelain --untracked-files=no) {
    Write-Warning "There are uncommitted changes. They go into the package, but its name shows only commit $commit."
}
$name = "fnrc-itsm-$(Get-Date -Format yyyyMMdd)-$commit"
$package = Join-Path (New-Item -ItemType Directory -Force $Output).FullName $name
if (Test-Path $package) { Remove-Item $package -Recurse -Force }

Write-Host "Building $name" -ForegroundColor Cyan
dotnet publish src\ItsTool.API\ItsTool.API.csproj -c Release -o "$package\app"
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }

$migrations = Join-Path ([IO.Path]::GetTempPath()) "$name-migrations.sql"
dotnet ef migrations script --idempotent --project src\ItsTool.Infrastructure --startup-project src\ItsTool.API --configuration Release --no-build --output $migrations
if ($LASTEXITCODE) { throw 'Creating the database update script failed.' }

$sql = New-Item -ItemType Directory "$package\sql"
Copy-Item deploy\sql\1-create-database.sql, deploy\sql\3-app-account.sql $sql
# SQL Server Management Studio sets these by default, sqlcmd does not; the indexes need them
$header = @"
-- FNRC Service Desk: database update for $name.
--
-- Run before copying the new site files, in SQL Server Management Studio, with the service desk database
-- selected, as a login that may change tables (the DBA, or db_owner of this database). Take a backup first.
-- Safe to run more than once: updates already applied are skipped.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO


"@
$utf8 = New-Object System.Text.UTF8Encoding $true
$update = $header + [IO.File]::ReadAllText($migrations) + "`r`n" + [IO.File]::ReadAllText((Resolve-Path deploy\sql\app-permissions.sql))
[IO.File]::WriteAllText("$sql\2-database-update.sql", $update, $utf8)
Remove-Item $migrations

Copy-Item deploy\update-site.ps1, deploy\app_offline.htm $package
Compress-Archive -Path "$package\*" -DestinationPath "$package.zip" -Force
Write-Host "Package: $package.zip" -ForegroundColor Green
