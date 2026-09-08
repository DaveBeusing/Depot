param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SqlServer', 'MariaDB', 'MySQL')]
    [string]$Provider
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($name in @('DEPOT_TEST_DATABASE_NAME', 'DEPOT_TEST_DATABASE_PASSWORD', 'DEPOT_TEST_DATABASE_CLIENT', 'DEPOT_TEST_DATABASE_DUMP_CLIENT')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "$name is not set." }
}

$database = $env:DEPOT_TEST_DATABASE_NAME
$password = $env:DEPOT_TEST_DATABASE_PASSWORD
$client = $env:DEPOT_TEST_DATABASE_CLIENT
$dumpClient = $env:DEPOT_TEST_DATABASE_DUMP_CLIENT

function Invoke-Tool([string]$FilePath, [string[]]$Arguments, [string]$ErrorMessage, [string]$InputFile = '') {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if (-not [string]::IsNullOrWhiteSpace($InputFile)) { $start.RedirectStandardInput = $true }
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "Could not start $FilePath." }
    if (-not [string]::IsNullOrWhiteSpace($InputFile)) {
        $reader = [System.IO.StreamReader]::new($InputFile)
        try { $process.StandardInput.Write($reader.ReadToEnd()) }
        finally {
            $reader.Dispose()
            $process.StandardInput.Close()
        }
    }
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "$ErrorMessage ExitCode=$($process.ExitCode). $stderr" }
    return $stdout
}

switch ($Provider) {
    'SqlServer' {
        $instanceId = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -Name 'SQLEXPRESS').'SQLEXPRESS'
        $backupDirectory = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer" -Name 'BackupDirectory').BackupDirectory
        if ([string]::IsNullOrWhiteSpace($backupDirectory)) { throw 'SQL Server backup directory could not be resolved.' }
        $backupPath = Join-Path $backupDirectory 'depot-provider-acceptance.bak'
        $escapedBackup = $backupPath.Replace("'", "''")
        $env:SQLCMDPASSWORD = $password
        try {
            $setupSql = "USE [$database]; IF OBJECT_ID(N'ProviderAcceptanceRecovery',N'U') IS NULL CREATE TABLE ProviderAcceptanceRecovery (Id int NOT NULL PRIMARY KEY, Marker nvarchar(100) NOT NULL); DELETE FROM ProviderAcceptanceRecovery; INSERT INTO ProviderAcceptanceRecovery(Id,Marker) VALUES(1,N'provider-restore-ok'); BACKUP DATABASE [$database] TO DISK=N'$escapedBackup' WITH INIT,COPY_ONLY,CHECKSUM; DELETE FROM ProviderAcceptanceRecovery;"
            [void](Invoke-Tool -FilePath $client -Arguments @('-S','127.0.0.1,1433','-U','sa','-b','-Q',$setupSql) -ErrorMessage 'SQL Server backup preparation failed.')
            $restoreSql = "USE master; ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$database] FROM DISK=N'$escapedBackup' WITH REPLACE; ALTER DATABASE [$database] SET MULTI_USER; USE [$database]; IF NOT EXISTS (SELECT 1 FROM ProviderAcceptanceRecovery WHERE Id=1 AND Marker=N'provider-restore-ok') THROW 51000,'Restore marker missing',1;"
            [void](Invoke-Tool -FilePath $client -Arguments @('-S','127.0.0.1,1433','-U','sa','-b','-Q',$restoreSql) -ErrorMessage 'SQL Server restore failed.')
        }
        finally {
            Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
            Remove-Item $backupPath -Force -ErrorAction SilentlyContinue
        }
    }
    default {
        $dumpPath = Join-Path $env:RUNNER_TEMP ("{0}-provider-acceptance.sql" -f $Provider.ToLowerInvariant())
        $env:MYSQL_PWD = $password
        try {
            $common = @('--protocol=TCP','--host=127.0.0.1','--port=3306','--user=depot')
            [void](Invoke-Tool -FilePath $client -Arguments ($common + @('--database',$database,'--execute',"CREATE TABLE IF NOT EXISTS ProviderAcceptanceRecovery (Id INT PRIMARY KEY, Marker VARCHAR(100) NOT NULL); DELETE FROM ProviderAcceptanceRecovery; INSERT INTO ProviderAcceptanceRecovery(Id,Marker) VALUES(1,'provider-restore-ok');")) -ErrorMessage "$Provider recovery marker creation failed.")

            $dumpStart = [System.Diagnostics.ProcessStartInfo]::new()
            $dumpStart.FileName = $dumpClient
            $dumpStart.UseShellExecute = $false
            $dumpStart.RedirectStandardOutput = $true
            $dumpStart.RedirectStandardError = $true
            foreach ($argument in ($common + @('--single-transaction','--routines','--triggers','--hex-blob',$database))) { [void]$dumpStart.ArgumentList.Add($argument) }
            $dump = [System.Diagnostics.Process]::Start($dumpStart)
            if ($null -eq $dump) { throw "Could not start $dumpClient." }
            $writer = [System.IO.StreamWriter]::new($dumpPath, $false, [System.Text.UTF8Encoding]::new($false))
            try { $writer.Write($dump.StandardOutput.ReadToEnd()) } finally { $writer.Dispose() }
            $dumpError = $dump.StandardError.ReadToEnd()
            $dump.WaitForExit()
            if ($dump.ExitCode -ne 0) { throw "$Provider dump failed. ExitCode=$($dump.ExitCode). $dumpError" }

            [void](Invoke-Tool -FilePath $client -Arguments ($common + @('--database',$database,'--execute','DELETE FROM ProviderAcceptanceRecovery;')) -ErrorMessage "$Provider recovery marker mutation failed.")
            [void](Invoke-Tool -FilePath $client -Arguments ($common + @('--execute',"DROP DATABASE IF EXISTS ``$database``; CREATE DATABASE ``$database`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")) -ErrorMessage "$Provider database recreation failed.")
            [void](Invoke-Tool -FilePath $client -Arguments ($common + @('--database',$database)) -ErrorMessage "$Provider restore failed." -InputFile $dumpPath)
            $marker = Invoke-Tool -FilePath $client -Arguments ($common + @('--batch','--skip-column-names','--database',$database,'--execute',"SELECT COUNT(*) FROM ProviderAcceptanceRecovery WHERE Id=1 AND Marker='provider-restore-ok';")) -ErrorMessage "$Provider restore marker validation failed."
            if ($marker.Trim() -ne '1') { throw "$Provider restore completed but the recovery marker was not restored." }
        }
        finally {
            Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
            Remove-Item $dumpPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "$Provider native backup/restore boundary restored database '$database' and preserved the acceptance marker."
