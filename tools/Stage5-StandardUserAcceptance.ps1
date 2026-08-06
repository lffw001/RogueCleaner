[CmdletBinding()]
param(
    [string]$ValidationExe,
    [string]$ReportDirectory,
    [switch]$CleanupOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ValidationExe)) {
    $exeCandidates = @(Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.exe')
    if ($exeCandidates.Count -ne 1) { throw "Expected exactly one root executable, found $($exeCandidates.Count)." }
    $ValidationExe = $exeCandidates[0].FullName
}
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) {
    $reportParents = @(Get-ChildItem -LiteralPath $projectRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'reports') })
    if ($reportParents.Count -ne 1) { throw "Expected exactly one application report directory, found $($reportParents.Count)." }
    $ReportDirectory = Join-Path $reportParents[0].FullName 'reports'
}

$userName = 'RogueCleanerStage5'
$lab = 'C:\Users\Public\Documents\RogueCleanerStage5Lab'
$allowedLab = [System.IO.Path]::GetFullPath('C:\Users\Public\Documents\RogueCleanerStage5Lab')
$resolvedLab = [System.IO.Path]::GetFullPath($lab)
if ($resolvedLab -ne $allowedLab) {
    throw "Temporary lab path validation failed: $resolvedLab"
}

function Remove-Stage5Artifacts {
    $existing = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
    $existingSid = if ($existing) { $existing.Sid.Value } else { $null }
    if ($existing) { Remove-LocalUser -Name $userName }
    if ($existingSid) {
        Get-CimInstance Win32_UserProfile -Filter "SID='$existingSid'" -ErrorAction SilentlyContinue |
            Where-Object { !$_.Loaded } |
            Remove-CimInstance -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $resolvedLab) { Remove-Item -LiteralPath $resolvedLab -Recurse -Force }
}

if ($CleanupOnly) {
    Remove-Stage5Artifacts
    [pscustomobject]@{ UserExists = [bool](Get-LocalUser -Name $userName -ErrorAction SilentlyContinue); LabExists = Test-Path -LiteralPath $resolvedLab }
    return
}

if (Get-LocalUser -Name $userName -ErrorAction SilentlyContinue) {
    throw "Temporary acceptance account already exists: $userName"
}
if (Test-Path -LiteralPath $resolvedLab) {
    throw "Temporary acceptance directory already exists: $resolvedLab"
}
if (!(Test-Path -LiteralPath $ValidationExe -PathType Leaf)) {
    throw "Validation executable does not exist: $ValidationExe"
}

$password = 'Rc5!' + ([Guid]::NewGuid().ToString('N').Substring(0, 12)) + 'aA1'
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$sid = $null
$copiedReport = $null

try {
    $account = New-LocalUser -Name $userName -Password $securePassword -Description 'RogueCleaner stage 5 temporary test' -PasswordNeverExpires
    $sid = $account.Sid.Value

    New-Item -ItemType Directory -Path $resolvedLab -Force | Out-Null
    $acl = Get-Acl -LiteralPath $resolvedLab
    $identity = "$env:COMPUTERNAME\$userName"
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $resolvedLab -AclObject $acl

    $labExe = Join-Path $resolvedLab 'RogueCleanerValidation.exe'
    Copy-Item -LiteralPath $ValidationExe -Destination $labExe -Force
    $credential = New-Object System.Management.Automation.PSCredential($identity, $securePassword)
    $process = Start-Process -FilePath $labExe -ArgumentList '--acceptance-test' -WorkingDirectory $resolvedLab -Credential $credential -LoadUserProfile -PassThru
    $deadline = (Get-Date).AddMinutes(3)
    $report = $null
    do {
        Start-Sleep -Milliseconds 500
        $report = Get-ChildItem -LiteralPath $resolvedLab -Recurse -Filter 'acceptance-*.json' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        $process.Refresh()
    } while (!$report -and !$process.HasExited -and (Get-Date) -lt $deadline)
    if ($report -and !$process.HasExited) { $null = $process.WaitForExit(10000) }

    if (!$report) {
        throw "Standard-user acceptance report was not generated; process exit code=$($process.ExitCode)"
    }

    New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
    $copiedReport = Join-Path $ReportDirectory ('stage5-standard-user-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
    Copy-Item -LiteralPath $report.FullName -Destination $copiedReport -Force
    $json = Get-Content -LiteralPath $copiedReport -Raw | ConvertFrom-Json

    [pscustomobject]@{
        Report = $copiedReport
        TaskResult = $process.ExitCode
        IsAdministrator = $json.IsAdministrator
        AllRunnableCasesPassed = $json.AllRunnableCasesPassed
        HasAdminSkippedCases = $json.HasAdminSkippedCases
        CaseSummary = @($json.Cases | Group-Object Result | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join '; '
    }

    if (!$json.AllRunnableCasesPassed) {
        exit 12
    }
}
finally {
    Remove-Stage5Artifacts
}
