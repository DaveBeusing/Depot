$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:DEPOT_TEST_DATABASE_SERVICE)) { throw 'DEPOT_TEST_DATABASE_SERVICE is not set.' }
if ([string]::IsNullOrWhiteSpace($env:DEPOT_TEST_DATABASE_PORT)) { throw 'DEPOT_TEST_DATABASE_PORT is not set.' }

Restart-Service -Name $env:DEPOT_TEST_DATABASE_SERVICE -Force
$port = [int]$env:DEPOT_TEST_DATABASE_PORT
$deadline = [DateTime]::UtcNow.AddMinutes(3)
do {
    if (Test-NetConnection -ComputerName '127.0.0.1' -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) {
        Write-Host "Database service '$($env:DEPOT_TEST_DATABASE_SERVICE)' recovered on port $port."
        exit 0
    }
    Start-Sleep -Seconds 2
} while ([DateTime]::UtcNow -lt $deadline)

throw "Database service '$($env:DEPOT_TEST_DATABASE_SERVICE)' did not recover on port $port."
