param(
    [string]$TemplatePath = '.github/rulesets/MasterGovernance.json',
    [string]$Repository = 'DaveBeusing/Depot',
    [switch]$RequireActiveRuleset,
    [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedChecks = @(
    'CI Required Gate',
    'Quality Required Gate',
    'Security Required Gate',
    'Packaged E2E Required Gate',
    'Database Provider Required Gate'
)

$workflowContracts = [ordered]@{
    '.github/workflows/ci.yml' = 'CI Required Gate'
    '.github/workflows/quality-gates.yml' = 'Quality Required Gate'
    '.github/workflows/security-supply-chain.yml' = 'Security Required Gate'
    '.github/workflows/depot-manager-e2e.yml' = 'Packaged E2E Required Gate'
    '.github/workflows/database-provider-acceptance.yml' = 'Database Provider Required Gate'
}

function Get-ObjectProperty {
    param(
        [object]$InputObject,
        [string]$Name
    )

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-ExactStringSet {
    param(
        [object[]]$Actual,
        [string[]]$Expected
    )

    $actualValues = @($Actual | ForEach-Object { [string]$_ } | Sort-Object)
    $expectedValues = @($Expected | Sort-Object)
    return $actualValues.Count -eq $expectedValues.Count -and
        ($actualValues -join "`n") -eq ($expectedValues -join "`n")
}

function Get-RulesetProblems {
    param(
        [object]$Ruleset,
        [switch]$RequireTemplateName
    )

    $problems = [System.Collections.Generic.List[string]]::new()

    if ($RequireTemplateName -and [string](Get-ObjectProperty $Ruleset 'name') -ne 'Depot master governance') {
        $problems.Add("Ruleset name must be 'Depot master governance'.")
    }
    if ([string](Get-ObjectProperty $Ruleset 'target') -ne 'branch') {
        $problems.Add("Ruleset target must be 'branch'.")
    }
    if ([string](Get-ObjectProperty $Ruleset 'enforcement') -ne 'active') {
        $problems.Add("Ruleset enforcement must be 'active'.")
    }

    $bypassActors = @(Get-ObjectProperty $Ruleset 'bypass_actors')
    if ($bypassActors.Count -ne 0) {
        $problems.Add('Ruleset must not define permanent bypass actors.')
    }

    $conditions = Get-ObjectProperty $Ruleset 'conditions'
    $refName = Get-ObjectProperty $conditions 'ref_name'
    $includes = @(Get-ObjectProperty $refName 'include')
    $excludes = @(Get-ObjectProperty $refName 'exclude')
    if (-not (Test-ExactStringSet -Actual $includes -Expected @('refs/heads/master'))) {
        $problems.Add('Ruleset must target exactly refs/heads/master.')
    }
    if ($excludes.Count -ne 0) {
        $problems.Add('Ruleset must not exclude refs from the master target.')
    }

    $rules = @(Get-ObjectProperty $Ruleset 'rules')
    $deletion = @($rules | Where-Object { [string](Get-ObjectProperty $_ 'type') -eq 'deletion' })
    $nonFastForward = @($rules | Where-Object { [string](Get-ObjectProperty $_ 'type') -eq 'non_fast_forward' })
    $pullRequest = @($rules | Where-Object { [string](Get-ObjectProperty $_ 'type') -eq 'pull_request' })
    $requiredChecks = @($rules | Where-Object { [string](Get-ObjectProperty $_ 'type') -eq 'required_status_checks' })

    if ($deletion.Count -ne 1) { $problems.Add('Ruleset must contain exactly one deletion rule.') }
    if ($nonFastForward.Count -ne 1) { $problems.Add('Ruleset must contain exactly one non_fast_forward rule.') }
    if ($pullRequest.Count -ne 1) { $problems.Add('Ruleset must contain exactly one pull_request rule.') }
    if ($requiredChecks.Count -ne 1) { $problems.Add('Ruleset must contain exactly one required_status_checks rule.') }

    if ($pullRequest.Count -eq 1) {
        $parameters = Get-ObjectProperty $pullRequest[0] 'parameters'
        $mergeMethods = @(Get-ObjectProperty $parameters 'allowed_merge_methods')
        if (-not (Test-ExactStringSet -Actual $mergeMethods -Expected @('merge', 'squash', 'rebase'))) {
            $problems.Add('Pull-request rule must allow merge, squash and rebase methods.')
        }
        $approvalCount = Get-ObjectProperty $parameters 'required_approving_review_count'
        if ($null -eq $approvalCount -or [int]$approvalCount -ne 0) {
            $problems.Add('Pull-request rule must require zero approvals for the one-person project.')
        }
        if ((Get-ObjectProperty $parameters 'require_code_owner_review') -ne $false) {
            $problems.Add('Code Owner review must not be mandatory.')
        }
        if ((Get-ObjectProperty $parameters 'require_last_push_approval') -ne $false) {
            $problems.Add('Last-push approval must not be mandatory.')
        }
        if ((Get-ObjectProperty $parameters 'required_review_thread_resolution') -ne $false) {
            $problems.Add('Review-thread resolution must not be mandatory.')
        }
    }

    if ($requiredChecks.Count -eq 1) {
        $parameters = Get-ObjectProperty $requiredChecks[0] 'parameters'
        $contexts = @(
            @(Get-ObjectProperty $parameters 'required_status_checks') |
                ForEach-Object { [string](Get-ObjectProperty $_ 'context') }
        )
        if (-not (Test-ExactStringSet -Actual $contexts -Expected $expectedChecks)) {
            $problems.Add("Required status checks must be exactly: $($expectedChecks -join ', ').")
        }
        if ((Get-ObjectProperty $parameters 'strict_required_status_checks_policy') -ne $false) {
            $problems.Add('strict_required_status_checks_policy must remain false for the current delivery model.')
        }
        if ((Get-ObjectProperty $parameters 'do_not_enforce_on_create') -ne $true) {
            $problems.Add('do_not_enforce_on_create must remain true.')
        }
    }

    return @($problems)
}

if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
    throw "Ruleset template not found: $TemplatePath"
}

$template = Get-Content -LiteralPath $TemplatePath -Raw | ConvertFrom-Json -Depth 100
$templateProblems = @(Get-RulesetProblems -Ruleset $template -RequireTemplateName)

$workflowProblems = [System.Collections.Generic.List[string]]::new()
foreach ($contract in $workflowContracts.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $contract.Key -PathType Leaf)) {
        $workflowProblems.Add("Required workflow is missing: $($contract.Key)")
        continue
    }

    $workflow = Get-Content -LiteralPath $contract.Key -Raw
    $pattern = '(?m)^\s+name:\s*{0}\s*$' -f [regex]::Escape([string]$contract.Value)
    if ($workflow -notmatch $pattern) {
        $workflowProblems.Add("Workflow $($contract.Key) does not expose required aggregate job '$($contract.Value)'.")
    }
}

$liveStatus = 'NOT_CHECKED'
$activeRulesetId = $null
$liveProblems = [System.Collections.Generic.List[string]]::new()

if ($RequireActiveRuleset) {
    $liveStatus = 'ADMIN_REQUIRED'
    try {
        $headers = @{
            Accept = 'application/vnd.github+json'
            'X-GitHub-Api-Version' = '2022-11-28'
            'User-Agent' = 'Depot-Repository-Governance-Validator'
        }
        $token = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }
        if (-not [string]::IsNullOrWhiteSpace($token)) {
            $headers.Authorization = "Bearer $token"
        }

        $listUri = "https://api.github.com/repos/$Repository/rulesets"
        $summaries = @(Invoke-RestMethod -Method Get -Uri $listUri -Headers $headers)
        $activeBranchRulesets = @($summaries | Where-Object {
            [string](Get-ObjectProperty $_ 'target') -eq 'branch' -and
            [string](Get-ObjectProperty $_ 'enforcement') -eq 'active'
        })

        if ($activeBranchRulesets.Count -eq 0) {
            $liveProblems.Add("No active branch ruleset exists for $Repository.")
        }
        else {
            $candidateProblemSets = [System.Collections.Generic.List[string]]::new()
            foreach ($summary in $activeBranchRulesets) {
                $id = [int64](Get-ObjectProperty $summary 'id')
                $detail = Invoke-RestMethod -Method Get -Uri "$listUri/$id" -Headers $headers
                $candidateProblems = @(Get-RulesetProblems -Ruleset $detail)
                if ($candidateProblems.Count -eq 0) {
                    $activeRulesetId = $id
                    $liveStatus = 'PASS'
                    break
                }
                $candidateProblemSets.Add("ruleset ${id}: $($candidateProblems -join '; ')")
            }

            if ($liveStatus -ne 'PASS') {
                $liveProblems.Add('Active branch rulesets exist, but none matches the Depot master governance contract.')
                foreach ($problemSet in $candidateProblemSets) { $liveProblems.Add($problemSet) }
            }
        }
    }
    catch {
        $liveStatus = 'FAIL'
        $liveProblems.Add("GitHub ruleset verification failed: $($_.Exception.Message)")
    }
}

$allProblems = @($templateProblems) + @($workflowProblems) + @($liveProblems)
$result = if ($allProblems.Count -eq 0) { 'PASS' } else { 'FAIL' }
$evidence = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    repository = $Repository
    templatePath = $TemplatePath
    templateValidation = if ($templateProblems.Count -eq 0) { 'PASS' } else { 'FAIL' }
    workflowContractValidation = if ($workflowProblems.Count -eq 0) { 'PASS' } else { 'FAIL' }
    liveRulesetValidation = $liveStatus
    activeRulesetId = $activeRulesetId
    requiredStatusChecks = $expectedChecks
    problems = @($allProblems)
    result = $result
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $directory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $evidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
}

if ($allProblems.Count -gt 0) {
    throw "Repository governance validation failed: $($allProblems -join ' | ')"
}

Write-Host "Repository governance validation passed. template=PASS workflows=PASS live=$liveStatus rulesetId=$activeRulesetId"
