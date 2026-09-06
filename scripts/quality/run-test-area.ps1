param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'Core',
        'Persistence',
        'Finance',
        'Security-Auth',
        'Sessions-Audit',
        'DepotManager',
        'Sales',
        'Purchasing',
        'Inventory-Warehouse',
        'Inventory-Operations',
        'Shell-UX'
    )]
    [string]$Area,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [switch]$Coverage,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

$classifiedPatterns = @(
    'AsyncDataAccess',
    'DatabaseManagement',
    'DatabaseProvider',
    'ProviderParameterNormalization',
    'BackupRetentionPolicy',
    'RecoveryFailure',
    'Finance',
    'Authentication',
    'Authorization',
    'Password',
    'Rbac',
    'SecurityAdministration',
    'SecurityConfiguration',
    'AdministratorBootstrap',
    'LegacyAdministratorRetirement',
    'UserEditorPasswordValidation',
    'UserRoleMigration',
    'UserSession',
    'SessionSecurity',
    'Audit',
    'SecurityEvent',
    'DataSubjectAccess',
    'BusinessRecordIntegrity',
    'Sales',
    'ElectronicInvoice',
    'DocumentIssuerSnapshot',
    'CompanyDocumentIdentity',
    'CompanyProfile',
    'Purchase',
    'GoodsReceipt',
    'SupplierReturn',
    'Procurement',
    'SupplierManagement',
    'Inventory',
    'StockTransfer',
    'MaterialIssue',
    'MaterialReturn',
    'MovementReversal',
    'Warehouse',
    'ItemCost',
    'ItemMaster',
    'ItemReference',
    'ReasonCode',
    'Shell',
    'Navigation',
    'Help',
    'Notification',
    'ApplicationInformation',
    'ApplicationStartup',
    'OperationPresentation',
    'WelcomeViewModel',
    'WorkspaceDocument',
    'DocumentationFileNaming'
)

function Join-FilterOr([string[]]$patterns) {
    return '(' + (($patterns | ForEach-Object { "FullyQualifiedName~$_" }) -join '|') + ')'
}

$project = 'tests/Depot.Tests/Depot.Tests.csproj'
$filter = switch ($Area) {
    'Core' {
        (($classifiedPatterns | ForEach-Object { "FullyQualifiedName!~$_" }) -join '&')
    }
    'Persistence' {
        Join-FilterOr @(
            'AsyncDataAccess',
            'DatabaseManagement',
            'DatabaseProvider',
            'ProviderParameterNormalization',
            'BackupRetentionPolicy',
            'RecoveryFailure'
        )
    }
    'Finance' { 'FullyQualifiedName~Finance' }
    'Security-Auth' {
        Join-FilterOr @(
            'Authentication',
            'Authorization',
            'Password',
            'Rbac',
            'SecurityAdministration',
            'SecurityConfiguration',
            'AdministratorBootstrap',
            'LegacyAdministratorRetirement',
            'UserEditorPasswordValidation',
            'UserRoleMigration'
        )
    }
    'Sessions-Audit' {
        Join-FilterOr @(
            'UserSession',
            'SessionSecurity',
            'Audit',
            'SecurityEvent',
            'DataSubjectAccess',
            'BusinessRecordIntegrity'
        )
    }
    'DepotManager' {
        $project = 'tests/DepotManager.Tests/DepotManager.Tests.csproj'
        $null
    }
    'Sales' {
        Join-FilterOr @(
            'Sales',
            'ElectronicInvoice',
            'DocumentIssuerSnapshot',
            'CompanyDocumentIdentity',
            'CompanyProfile'
        )
    }
    'Purchasing' {
        Join-FilterOr @(
            'Purchase',
            'GoodsReceipt',
            'SupplierReturn',
            'Procurement',
            'SupplierManagement'
        )
    }
    'Inventory-Warehouse' {
        '(' + (Join-FilterOr @(
            'Inventory',
            'Warehouse',
            'ItemCost',
            'ItemMaster',
            'ItemReference',
            'ReasonCode'
        )) + '&FullyQualifiedName!~Finance)'
    }
    'Inventory-Operations' {
        Join-FilterOr @(
            'StockTransfer',
            'MaterialIssue',
            'MaterialReturn',
            'MovementReversal'
        )
    }
    'Shell-UX' {
        Join-FilterOr @(
            'Shell',
            'Navigation',
            'Help',
            'Notification',
            'ApplicationInformation',
            'ApplicationStartup',
            'OperationPresentation',
            'WelcomeViewModel',
            'WorkspaceDocument',
            'DocumentationFileNaming'
        )
    }
}

if ($Area -ne 'DepotManager') {
    $filter = "($filter)&QualityGate!=Performance"
}

$arguments = @(
    'test',
    $project,
    '--configuration', $Configuration,
    '--no-restore',
    '--blame-hang',
    '--blame-hang-timeout', '8m',
    '--logger', 'console;verbosity=normal'
)

if ($NoBuild) {
    $arguments += '--no-build'
}

if ($filter) {
    $arguments += @('--filter', $filter)
}

if ($Coverage) {
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $ResultsDirectory = Join-Path 'TestResults/Coverage' $Area
    }
    $arguments += @(
        '--settings', 'tests/coverage.runsettings',
        '--collect', 'Code Coverage',
        '--results-directory', $ResultsDirectory
    )
}

Write-Host "Running regression area '$Area' from '$project'."
if ($filter) {
    Write-Host "Filter: $filter"
}
if ($Coverage) {
    Write-Host "Production coverage: $ResultsDirectory"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Regression area '$Area' failed with exit code $LASTEXITCODE."
}
