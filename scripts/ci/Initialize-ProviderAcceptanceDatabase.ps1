param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SqlServer', 'MariaDB', 'MySQL')]
    [string]$Provider
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-CiEnvironment([string]$Name, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
        Set-Item -Path "Env:$Name" -Value $Value
        return
    }
    Add-Content -Path $env:GITHUB_ENV -Value "$Name=$Value" -Encoding utf8
}

function Wait-TcpPort([int]$Port) {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        if (Test-NetConnection -ComputerName '127.0.0.1' -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) { return }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Database server did not open TCP port $Port within the startup window."
}

function Find-Executable([string[]]$Roots, [string[]]$Names) {
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) { continue }
        foreach ($name in $Names) {
            $candidate = Get-ChildItem -Path $root -Filter $name -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $candidate) { return $candidate.FullName }
        }
    }
    throw "Could not locate database client executable: $($Names -join ', ')."
}

$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString() }
$attempt = if ($env:GITHUB_RUN_ATTEMPT) { $env:GITHUB_RUN_ATTEMPT } else { '1' }
$databaseName = ("depot_test_{0}_{1}_{2}" -f $Provider.ToLowerInvariant(), $runId, $attempt) -replace '[^a-zA-Z0-9_]', '_'
$password = "D3pot!$([Guid]::NewGuid().ToString('N'))aA1"
$port = 3306
$serviceName = $null
$client = $null
$dumpClient = $null
$connectionVariable = $null
$connectionString = $null

switch ($Provider) {
    'SqlServer' {
        $port = 1433
        $serviceName = 'MSSQL$SQLEXPRESS'
        $configurationPath = Join-Path $env:RUNNER_TEMP 'sqlserver-provider-acceptance.ini'
        $adminAccount = "$env:USERDOMAIN\$env:USERNAME"
        @"
[OPTIONS]
ACTION="Install"
FEATURES=SQLENGINE
INSTANCENAME="SQLEXPRESS"
INSTANCEID="SQLEXPRESS"
SQLSVCSTARTUPTYPE="Automatic"
SQLSYSADMINACCOUNTS="$adminAccount"
SECURITYMODE="SQL"
SAPWD="$password"
TCPENABLED="1"
NPENABLED="0"
UPDATEENABLED="False"
"@ | Set-Content -Path $configurationPath -Encoding ascii

        try {
            & choco install sql-server-express --version 2022.16.0.20260305 -y --no-progress --params="'/ConfigurationFile:$configurationPath'"
            if ($LASTEXITCODE -notin @(0, 3010, 1641)) { throw "SQL Server Express installation failed with exit code $LASTEXITCODE." }
        }
        finally {
            Remove-Item $configurationPath -Force -ErrorAction SilentlyContinue
        }

        $instanceNamesPath = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL'
        $instanceId = (Get-ItemProperty -Path $instanceNamesPath -Name 'SQLEXPRESS').'SQLEXPRESS'
        if ([string]::IsNullOrWhiteSpace($instanceId)) { throw 'SQL Server SQLEXPRESS instance registration was not found.' }
        $ipAll = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer\SuperSocketNetLib\Tcp\IPAll"
        Set-ItemProperty -Path $ipAll -Name TcpDynamicPorts -Value ''
        Set-ItemProperty -Path $ipAll -Name TcpPort -Value '1433'
        Restart-Service -Name $serviceName -Force
        Wait-TcpPort -Port $port

        & choco install sqlserver-cmdlineutils --version 15.0.4298.100 -y --no-progress
        if ($LASTEXITCODE -notin @(0, 3010, 1641)) { throw "sqlcmd installation failed with exit code $LASTEXITCODE." }
        $client = Find-Executable -Roots @("$env:ProgramFiles\Microsoft SQL Server", "${env:ProgramFiles(x86)}\Microsoft SQL Server") -Names @('SQLCMD.EXE', 'sqlcmd.exe')
        $dumpClient = $client
        $connectionVariable = 'DEPOT_TEST_SQLSERVER_CONNECTION_STRING'
        $connectionString = "Server=127.0.0.1,1433;Database=$databaseName;User ID=sa;Password=$password;Encrypt=False;TrustServerCertificate=True"
    }
    'MySQL' {
        $serviceName = 'DepotMySQL'
        $dataLocation = Join-Path $env:RUNNER_TEMP 'mysql-provider-data'
        & choco install mysql --version 8.4.6 -y --no-progress --params="'/port:3306 /serviceName:$serviceName /dataLocation:$dataLocation'"
        if ($LASTEXITCODE -notin @(0, 3010, 1641)) { throw "MySQL installation failed with exit code $LASTEXITCODE." }
        Wait-TcpPort -Port $port
        $client = Find-Executable -Roots @('C:\tools', $env:ProgramFiles) -Names @('mysql.exe')
        $dumpClient = Find-Executable -Roots @('C:\tools', $env:ProgramFiles) -Names @('mysqldump.exe')
        $sql = "ALTER USER 'root'@'localhost' IDENTIFIED BY '$password'; CREATE USER 'depot'@'%' IDENTIFIED BY '$password'; GRANT ALL PRIVILEGES ON *.* TO 'depot'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;"
        & $client --protocol=TCP --host=127.0.0.1 --port=3306 --user=root --execute=$sql
        if ($LASTEXITCODE -ne 0) { throw "MySQL test-account provisioning failed with exit code $LASTEXITCODE." }
        $connectionVariable = 'DEPOT_TEST_MYSQL_CONNECTION_STRING'
        $connectionString = "Server=127.0.0.1;Port=3306;Database=$databaseName;User ID=depot;Password=$password;SslMode=Disabled"
    }
    'MariaDB' {
        $serviceName = 'MySQL'
        & choco install mariadb.install --version 11.8.9 -y --no-progress
        if ($LASTEXITCODE -notin @(0, 3010, 1641)) { throw "MariaDB installation failed with exit code $LASTEXITCODE." }
        Wait-TcpPort -Port $port
        $client = Find-Executable -Roots @($env:ProgramFiles, 'C:\tools') -Names @('mariadb.exe', 'mysql.exe')
        $dumpClient = Find-Executable -Roots @($env:ProgramFiles, 'C:\tools') -Names @('mariadb-dump.exe', 'mysqldump.exe')
        $sql = "ALTER USER 'root'@'localhost' IDENTIFIED BY '$password'; CREATE USER 'depot'@'%' IDENTIFIED BY '$password'; GRANT ALL PRIVILEGES ON *.* TO 'depot'@'%' WITH GRANT OPTION; FLUSH PRIVILEGES;"
        & $client --protocol=TCP --host=127.0.0.1 --port=3306 --user=root --execute=$sql
        if ($LASTEXITCODE -ne 0) { throw "MariaDB test-account provisioning failed with exit code $LASTEXITCODE." }
        $connectionVariable = 'DEPOT_TEST_MARIADB_CONNECTION_STRING'
        $connectionString = "Server=127.0.0.1;Port=3306;Database=$databaseName;User ID=depot;Password=$password;SslMode=Disabled"
    }
}

Write-CiEnvironment -Name $connectionVariable -Value $connectionString
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_PROVIDER' -Value $Provider
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_NAME' -Value $databaseName
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_PASSWORD' -Value $password
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_SERVICE' -Value $serviceName
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_PORT' -Value $port.ToString()
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_CLIENT' -Value $client
Write-CiEnvironment -Name 'DEPOT_TEST_DATABASE_DUMP_CLIENT' -Value $dumpClient

Write-Host "Prepared isolated $Provider acceptance server on 127.0.0.1:$port with database '$databaseName'. Credentials were written only to the runner environment."
