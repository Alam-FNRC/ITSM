<#
.SYNOPSIS
Runs the service desk on this PC at http://localhost:5246 with its own SQL Server LocalDB database.

.DESCRIPTION
The first run creates the database, with sample categories, teams and projects, and asks for the first
administrator. That password is temporary: the app asks for a new one at the first login.
The browser opens on the login page when the app is ready. Ctrl+C stops the app.

Run it from the repository folder:
    powershell -ExecutionPolicy Bypass -File scripts\run-local.ps1
    powershell -ExecutionPolicy Bypass -File scripts\run-local.ps1 -Database FnrcItsm_Test2

.PARAMETER Database
The LocalDB database to use; a new name starts with an empty service desk.

.PARAMETER Port
The port the app listens on.

.PARAMETER NoBrowser
Do not open the browser.
#>
param(
    [string]$Database = 'FnrcItsm_Demo',
    [int]$Port = 5246,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$server = '(localdb)\MSSQLLocalDB'
$url = "http://localhost:$Port"

if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw 'Use only letters, digits and _ in the database name.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is missing. Install the .NET 9 SDK and the .NET 8 runtime from https://dotnet.microsoft.com/download'
}
if (-not (Get-Command sqllocaldb -ErrorAction SilentlyContinue)) {
    throw 'SQL Server LocalDB is missing. Install SQL Server Express LocalDB.'
}
sqllocaldb start MSSQLLocalDB | Out-Null

# A new database needs a first administrator; an existing one keeps its accounts
$isNew = $true
if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
    $found = sqlcmd -S $server -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database'"
    $isNew = "$($found | Select-Object -First 1)".Trim() -ne '1'
}

if ($isNew -and -not $env:Bootstrap__AdminPassword) {
    Write-Host "Database $Database is new. Choose the first administrator (the password is replaced at the first login)."
    $env:Bootstrap__AdminUsername = Read-Host 'Username'
    $env:Bootstrap__AdminEmail = Read-Host 'Email'
    do {
        $secure = Read-Host 'Temporary password (8 or more characters: upper case, lower case, digit, symbol)' -AsSecureString
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        if ($plain.Length -lt 8) { Write-Host 'The password must be at least 8 characters.' }
    } while ($plain.Length -lt 8)
    $env:Bootstrap__AdminPassword = $plain
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = "Server=$server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"

# Opens the login page once the app answers
$opener = $null
if (-not $NoBrowser) {
    $opener = Start-Job -ArgumentList $url -ScriptBlock {
        param($url)
        for ($i = 0; $i -lt 180; $i++) {
            try {
                Invoke-WebRequest "$url/api/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
                Start-Process "$url/login.html"
                return
            } catch { Start-Sleep -Seconds 1 }
        }
    }
}

Write-Host "Starting the service desk on $url (database $Database). Press Ctrl+C to stop."
try {
    dotnet run --project "$root\src\ItsTool.API" --no-launch-profile --urls $url
}
finally {
    if ($opener) { Remove-Job $opener -Force -ErrorAction SilentlyContinue }
    # The administrator password stays out of the console session
    Remove-Item Env:Bootstrap__AdminPassword, Env:Bootstrap__AdminUsername, Env:Bootstrap__AdminEmail -ErrorAction SilentlyContinue
}
