#Requires -Version 7.0
<#
.SYNOPSIS
    End-to-end demo: fresh database -> start API -> initial submission, duplicate, correction, stale and
    conflicting re-sends -> snapshots (corrected and point-in-time) -> expected-total checks -> structured logs.

.EXAMPLE
    ./scripts/demo.ps1                      # drop + recreate the LocalDB database, start the API, run, stop
.EXAMPLE
    ./scripts/demo.ps1 -UseRunningApi       # run the calls against an API you already started (no reset)
#>
param(
    [string] $BaseUrl = 'http://localhost:5080',
    [switch] $UseRunningApi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$apiProject = Join-Path $root 'src/TradeSnapshot.Api'
$infraProject = Join-Path $root 'src/TradeSnapshot.Infrastructure'
$logFile = Join-Path $root 'artifacts/demo-api.log'
$apiProcess = $null

function Section([string] $title) { Write-Host "`n=== $title ===" -ForegroundColor Cyan }

function Submit-Trade([string] $label, [hashtable] $trade) {
    $response = Invoke-WebRequest -Uri "$BaseUrl/api/v1/trades" -Method Post -ContentType 'application/json' `
        -Body ($trade | ConvertTo-Json -Depth 5) -Headers @{ 'X-Correlation-ID' = "demo-$label" } -SkipHttpErrorCheck
    $body = $response.Content | ConvertFrom-Json
    $colour = if ([int]$response.StatusCode -ge 400) { 'Yellow' } else { 'Green' }
    Write-Host ('{0,-26} HTTP {1}  outcome={2,-10} version={3,-2} current={4,-2} reason={5}' -f `
        $label, [int]$response.StatusCode, $body.outcome, ($body.version ?? '-'), ($body.current_version ?? '-'), $body.reason) -ForegroundColor $colour
}

function Show-Snapshot([string] $title, [string] $query) {
    Section $title
    $s = Invoke-RestMethod "$BaseUrl/api/v1/accounts/ACC-001/snapshot?$query"
    $s.positions | Select-Object symbol, currency, quantity, unit_cost, price, @{ n = 'fx'; e = { $_.fx.rate } },
        market_value_local, market_value_usd, cost_basis_usd, unrealized_pnl_usd, valuation_status | Format-Table -AutoSize | Out-Host
    $s.totals | Format-List | Out-Host
    return $s
}

function Assert-Equal([string] $what, $expected, $actual) {
    if ([decimal]$expected -eq [decimal]$actual) { Write-Host "PASS  $what = $actual" -ForegroundColor Green }
    else { Write-Host "FAIL  $what expected $expected but was $actual" -ForegroundColor Red; $script:failed = $true }
}

function New-Trade([string] $ref, [string] $isin, [decimal] $qty, [decimal] $price, [string] $asOf) {
    @{ external_ref = $ref; account_id = 'ACC-001'; instrument = @{ isin = $isin }; side = 'BUY'
       quantity = $qty; price = $price; trade_date = '2025-03-01'; as_of = $asOf }
}

try {
    if (-not $UseRunningApi) {
        Section 'Resetting database and starting API'
        dotnet tool restore | Out-Null
        dotnet ef database drop --force --project $infraProject --startup-project $infraProject | Out-Null
        dotnet build $apiProject -v q -nologo | Out-Null
        New-Item -ItemType Directory -Force (Split-Path $logFile) | Out-Null
        $apiProcess = Start-Process dotnet -ArgumentList @('run', '--no-build', '--project', $apiProject, '--launch-profile', 'http') `
            -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -NoNewWindow
        $deadline = (Get-Date).AddSeconds(90)
        do {
            Start-Sleep -Milliseconds 500
            try { $ready = (Invoke-WebRequest "$BaseUrl/health/ready" -SkipHttpErrorCheck).StatusCode -eq 200 } catch { $ready = $false }
        } until ($ready -or (Get-Date) -gt $deadline)
        if (-not $ready) { throw "API did not become ready; see $logFile" }
        Write-Host "API ready at $BaseUrl (migrations applied, reference data seeded)"
    }

    Section 'Ingestion'
    $original = New-Trade 'TRX-1001' 'US0378331005' 120 185.40 '2025-03-01T10:15:00Z'
    Submit-Trade '1-initial'           $original
    Submit-Trade '2-duplicate-resend'  $original
    Submit-Trade '3-correction'        (New-Trade 'TRX-1001' 'US0378331005' 100 185.20 '2025-03-01T14:00:00Z')
    Submit-Trade '4-late-resend-of-v1' $original
    Submit-Trade '5-out-of-order'      (New-Trade 'TRX-1001' 'US0378331005' 110 185.30 '2025-03-01T12:30:00Z')
    Submit-Trade '6-conflict'          (New-Trade 'TRX-1001' 'US0378331005' 999 185.20 '2025-03-01T14:00:00Z')
    Submit-Trade '7-zar-trade'         (New-Trade 'TRX-1002' 'ZAE000015889' 50 4450 '2025-03-01T10:30:00Z')

    $latest = Show-Snapshot 'Snapshot 2025-03-01 (latest corrected view)' 'date=2025-03-01'
    $noon = Show-Snapshot 'Snapshot 2025-03-01 as known at 12:00Z (before the correction)' 'date=2025-03-01&known_at=2025-03-01T12:00:00Z'

    Section 'Expected totals'
    $failed = $false
    Assert-Equal 'AAPL quantity (corrected)'         100       ($latest.positions | Where-Object symbol -eq 'AAPL').quantity
    Assert-Equal 'Total market value USD (corrected)' 30619.23 $latest.totals.market_value_usd
    Assert-Equal 'Total unrealised P/L USD'           213.55   $latest.totals.unrealized_pnl_usd
    Assert-Equal 'AAPL quantity (as known at noon)'   120      ($noon.positions | Where-Object symbol -eq 'AAPL').quantity
    Assert-Equal 'Total value USD (as known at noon)' 34339.23 $noon.totals.total_value_usd

    Section 'Audit trail for TRX-1001'
    $trade = Invoke-RestMethod "$BaseUrl/api/v1/trades/TRX-1001"
    $trade.versions | Format-Table version, quantity, price, as_of_utc, received_at_utc, is_current -AutoSize | Out-Host
    $trade.ingestion_events | Format-Table received_at_utc, as_of_utc, outcome, reason, version, correlation_id -AutoSize | Out-Host

    if (-not $UseRunningApi) {
        Section 'Structured log lines (EventId 1001/1002, JSON)'
        Get-Content $logFile | Where-Object { $_ -match '"EventId":100[12]' } | Select-Object -First 7 | ForEach-Object { Write-Host $_ }
    }

    if ($failed) { exit 1 }
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        $apiProcess.Kill($true)   # kill the whole tree (dotnet run -> TradeSnapshot.Api)
        Write-Host "`nAPI stopped. Full log: $logFile"
    }
}
