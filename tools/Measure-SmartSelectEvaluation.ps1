[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputCsv,

    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredColumns = @(
    'CandidateId', 'Split', 'Category', 'ExpectedJunk', 'ExpectedSafe', 'ExpectedHarm',
    'PredictedTier', 'JunkScore', 'DeletionRisk', 'Confidence', 'SafetyCoverage', 'SelectionSafetyComplete'
)
$rows = @(Import-Csv -LiteralPath $InputCsv)
if ($rows.Count -eq 0) {
    throw 'The evaluation CSV has no candidate rows.'
}

$actualColumns = @($rows[0].PSObject.Properties.Name)
$missingColumns = @($requiredColumns | Where-Object { $_ -notin $actualColumns })
if ($missingColumns.Count -gt 0) {
    throw "The evaluation CSV is missing columns: $($missingColumns -join ', ')"
}

function ConvertTo-NullableBoolean([string] $Value, [string] $Column, [string] $CandidateId) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $parsed = $false
    if (-not [bool]::TryParse($Value, [ref] $parsed)) {
        throw "Invalid $Column value for candidate '$CandidateId': '$Value'"
    }
    return $parsed
}

function ConvertTo-NullableScore([string] $Value, [string] $Column, [string] $CandidateId) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $parsed = 0.0
    if (-not [double]::TryParse($Value, [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed) -or $parsed -lt 0 -or $parsed -gt 1) {
        throw "Invalid $Column value for candidate '$CandidateId': '$Value' (expected 0 through 1)"
    }
    return $parsed
}

$normalized = foreach ($row in $rows) {
    $tier = $row.PredictedTier.Trim()
    if ($tier -notin @('Safe', 'Likely', 'Review', 'Keep', 'Protected')) {
        throw "Invalid PredictedTier for candidate '$($row.CandidateId)': '$tier'"
    }
    [pscustomobject]@{
        CandidateId = $row.CandidateId.Trim()
        Split = $row.Split.Trim().ToLowerInvariant()
        Category = if ([string]::IsNullOrWhiteSpace($row.Category)) { 'Uncategorized' } else { $row.Category.Trim() }
        ExpectedJunk = ConvertTo-NullableBoolean $row.ExpectedJunk 'ExpectedJunk' $row.CandidateId
        ExpectedSafe = ConvertTo-NullableBoolean $row.ExpectedSafe 'ExpectedSafe' $row.CandidateId
        ExpectedHarm = ConvertTo-NullableBoolean $row.ExpectedHarm 'ExpectedHarm' $row.CandidateId
        PredictedTier = $tier
        JunkScore = ConvertTo-NullableScore $row.JunkScore 'JunkScore' $row.CandidateId
        DeletionRisk = ConvertTo-NullableScore $row.DeletionRisk 'DeletionRisk' $row.CandidateId
        Confidence = ConvertTo-NullableScore $row.Confidence 'Confidence' $row.CandidateId
        SafetyCoverage = ConvertTo-NullableScore $row.SafetyCoverage 'SafetyCoverage' $row.CandidateId
        SelectionSafetyComplete = ConvertTo-NullableBoolean $row.SelectionSafetyComplete 'SelectionSafetyComplete' $row.CandidateId
    }
}

$holdout = @($normalized | Where-Object Split -eq 'holdout')
if ($holdout.Count -eq 0) {
    throw "The evaluation CSV has no rows with Split='holdout'."
}

function Format-Rate([int] $Numerator, [int] $Denominator) {
    if ($Denominator -eq 0) { return 'n/a (0 labeled candidates)' }
    return '{0:P1} ({1}/{2})' -f ($Numerator / [double] $Denominator), $Numerator, $Denominator
}

function Format-Mean([object[]] $Items, [string] $Property, [string] $LabelProperty = '') {
    $selected = @($Items | Where-Object {
        $null -ne $_.$Property -and ([string]::IsNullOrEmpty($LabelProperty) -or $null -ne $_.$LabelProperty)
    })
    if ($selected.Count -eq 0) { return 'n/a' }
    $values = foreach ($item in $selected) {
        if ([string]::IsNullOrEmpty($LabelProperty)) { [double] $item.$Property }
        else { [math]::Pow(([double] $item.$Property) - [int] [bool] $item.$LabelProperty, 2) }
    }
    return '{0:F3} (n={1})' -f (($values | Measure-Object -Average).Average), $selected.Count
}

function New-ReportRow([string] $Name, [object[]] $Items) {
    $safe = @($Items | Where-Object PredictedTier -eq 'Safe')
    $labeledSafe = @($safe | Where-Object { $null -ne $_.ExpectedSafe })
    $correctSafe = @($labeledSafe | Where-Object ExpectedSafe)
    $knownUnsafe = @($Items | Where-Object { $null -ne $_.ExpectedSafe -and -not $_.ExpectedSafe })
    $falseSafe = @($knownUnsafe | Where-Object PredictedTier -eq 'Safe')
    $safeFailures = @($safe | Where-Object { $_.ExpectedSafe -eq $false -or $_.ExpectedHarm -eq $true })
    $safeReviewed = @($safe | Where-Object { $null -ne $_.ExpectedSafe -or $null -ne $_.ExpectedHarm })
    $complete = @($Items | Where-Object SelectionSafetyComplete)
    $unresolved = @($Items | Where-Object {
        -not $_.SelectionSafetyComplete -or $_.PredictedTier -in @('Review', 'Keep', 'Protected')
    })

    [pscustomobject]@{
        Category = $Name
        Candidates = $Items.Count
        SafePrecision = Format-Rate $correctSafe.Count $labeledSafe.Count
        FalseSafeRate = Format-Rate $falseSafe.Count $knownUnsafe.Count
        SafeFailures = $safeFailures.Count
        SafeCategoryGate = if ($safeFailures.Count -gt 0) { 'BLOCKED' } elseif ($safeReviewed.Count -eq 0) { 'UNASSESSED' } else { 'No known failures' }
        JunkBrier = Format-Mean $Items 'JunkScore' 'ExpectedJunk'
        RiskBrier = Format-Mean $Items 'DeletionRisk' 'ExpectedHarm'
        MeanInspectionCoverage = Format-Mean $Items 'SafetyCoverage'
        CompleteSafetyScanShare = Format-Rate $complete.Count $Items.Count
        UnresolvedShare = Format-Rate $unresolved.Count $Items.Count
    }
}

$reportRows = [System.Collections.Generic.List[object]]::new()
$reportRows.Add((New-ReportRow 'All categories' $holdout))
foreach ($group in ($holdout | Group-Object Category | Sort-Object Name)) {
    $reportRows.Add((New-ReportRow $group.Name @($group.Group)))
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Smart Select held-out evaluation')
$lines.Add('')
$lines.Add("Candidates: $($holdout.Count)")
$lines.Add('')
$lines.Add('| Category | Candidates | Safe precision | False-safe rate | Safe failures | Safe gate | Junk Brier* | Risk Brier* | Mean inspection coverage | Complete safety scan | Unresolved |')
$lines.Add('|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|')
foreach ($item in $reportRows) {
    $lines.Add("| $($item.Category) | $($item.Candidates) | $($item.SafePrecision) | $($item.FalseSafeRate) | $($item.SafeFailures) | $($item.SafeCategoryGate) | $($item.JunkBrier) | $($item.RiskBrier) | $($item.MeanInspectionCoverage) | $($item.CompleteSafetyScanShare) | $($item.UnresolvedShare) |")
}
$lines.Add('')
$lines.Add('*Brier scores are descriptive diagnostics against the supplied binary review labels; they do not establish probability calibration.*')
$report = $lines -join [Environment]::NewLine

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Output $report
} else {
    Set-Content -LiteralPath $OutputPath -Value $report -Encoding utf8
    Write-Output "Wrote evaluation report to $OutputPath"
}
