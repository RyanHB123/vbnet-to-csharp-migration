#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$apiDirectory = Join-Path $repoRoot 'src/ModernInvoices.Api/bin/Release/net8.0'
if (!(Test-Path (Join-Path $apiDirectory 'ModernInvoices.Api.dll'))) {
    throw 'Build the solution in Release configuration first.'
}

# Ask the OS for an available loopback port. Kestrel owns it for the test run.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$testDataName = 'test-orders-' + [Guid]::NewGuid().ToString('N') + '.json'
$artifactDirectory = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$startArguments = @{
    FilePath = 'dotnet'
    ArgumentList = @('ModernInvoices.Api.dll', '--urls', $baseUrl, '--DataFile', $testDataName)
    WorkingDirectory = $apiDirectory
    PassThru = $true
    RedirectStandardOutput = (Join-Path $artifactDirectory 'api.stdout.log')
    RedirectStandardError = (Join-Path $artifactDirectory 'api.stderr.log')
}
if ($IsWindows) { $startArguments.WindowStyle = 'Hidden' }
$apiProcess = Start-Process @startArguments
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($apiProcess.HasExited) { throw 'API exited during startup; see artifacts/api.stderr.log.' }
        try {
            $health = Invoke-RestMethod "$baseUrl/health" -TimeoutSec 1
            if ($health.status -eq 'healthy') { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'API did not become healthy.' }

    $sample = Get-Content (Join-Path $repoRoot 'samples/trade-invoice.json') -Raw
    $quote = Invoke-RestMethod "$baseUrl/api/invoices/quote" -Method Post -ContentType 'application/json' -Body $sample
    if ($quote.subtotal -ne 1099 -or $quote.discount -ne 109.90 -or $quote.vat -ne 197.82 -or $quote.total -ne 1186.92) {
        throw "Unexpected API quote: $($quote | ConvertTo-Json -Compress)"
    }
    Write-Output 'PASS HTTP trade invoice returns all four expected totals'

    $retail = Invoke-RestMethod "$baseUrl/api/invoices/quote" -Method Post -ContentType 'application/json' -Body ($sample.Replace('Trade', 'Retail'))
    if ($retail.total -ne 1318.80 -or $retail.discount -ne 0) { throw 'Retail calculation failed.' }
    Write-Output 'PASS HTTP retail invoice receives no trade discount'

    $invalidBodies = @(
        '{"customerType":"Unknown","lines":[{"unitPrice":10,"quantity":1}]}',
        '{"customerType":"1","lines":[{"unitPrice":10,"quantity":1}]}',
        '{"customerType":"Trade","lines":[]}',
        '{"customerType":"Trade","lines":null}',
        '{"customerType":"Trade","lines":[null]}',
        '{"customerType":"Trade","lines":[{"unitPrice":-1,"quantity":1}]}',
        '{"customerType":"Trade","lines":[{"unitPrice":10,"quantity":0}]}',
        '{"customerType":"Trade","lines":[{"unitPrice":1.001,"quantity":1}]}',
        '{"customerType":"Trade","lines":[{"quantity":1}]}',
        '{"customerType":"Trade","lines":[{"unitPrice":1}]}',
        '{}'
    )
    foreach ($body in $invalidBodies) {
        $response = Invoke-WebRequest "$baseUrl/api/invoices/quote" -Method Post -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
        if ([int]$response.StatusCode -ne 400) { throw "Expected HTTP 400 for $body" }
        $content = if ($response.Content -is [byte[]]) {
            [System.Text.Encoding]::UTF8.GetString($response.Content)
        } else { $response.Content }
        $problem = $content | ConvertFrom-Json
        if (!$problem.errors) { throw "Expected validation details for $body" }
    }
    $malformed = Invoke-WebRequest "$baseUrl/api/invoices/quote" -Method Post -ContentType 'application/json' -Body '{broken' -SkipHttpErrorCheck
    if ([int]$malformed.StatusCode -ne 400) { throw 'Malformed JSON must return HTTP 400.' }
    Write-Output "PASS HTTP validation rejects $($invalidBodies.Count) invalid requests and malformed JSON"

    $dashboard = Invoke-WebRequest $baseUrl
    if ($dashboard.Content -notmatch 'Create an order') { throw 'Dashboard was not served.' }
    $orderBody = '{"customerId":"CUST-001","lines":[{"sku":"MON-001","quantity":8},{"sku":"DOCK-002","quantity":2}]}'
    $catalogueQuote = Invoke-RestMethod "$baseUrl/api/orders/quote" -Method Post -ContentType 'application/json' -Body $orderBody
    if ($catalogueQuote.total -ne 1186.92) { throw 'Catalogue quote failed.' }
    $placedResponse = Invoke-WebRequest "$baseUrl/api/orders" -Method Post -ContentType 'application/json' -Body $orderBody
    if ([int]$placedResponse.StatusCode -ne 201) { throw 'Order creation must return HTTP 201.' }
    $placed = $placedResponse.Content | ConvertFrom-Json
    if ($placed.total -or $placed.totals.total -ne 1186.92) { throw 'Order totals are incorrect.' }
    $stored = Invoke-RestMethod "$baseUrl/api/orders/$($placed.id)"
    if ($stored.lines[0].unitPrice -ne 125) { throw 'Price snapshot is missing.' }
    $products = Invoke-RestMethod "$baseUrl/api/products"
    if (($products | Where-Object sku -eq 'MON-001').stock -ne 16) { throw 'Stock was not deducted.' }
    $report = Invoke-RestMethod "$baseUrl/api/reports/sales"
    if ($report.grossSales -ne 1186.92 -or $report.unitsSold -ne 10) { throw 'Sales report is incorrect.' }
    $conflict = Invoke-WebRequest "$baseUrl/api/orders" -Method Post -ContentType 'application/json' -Body '{"customerId":"CUST-001","lines":[{"sku":"HUB-006","quantity":4}]}' -SkipHttpErrorCheck
    if ([int]$conflict.StatusCode -ne 409) { throw 'Insufficient stock must return HTTP 409.' }
    $missing = Invoke-WebRequest "$baseUrl/api/orders/missing" -SkipHttpErrorCheck
    if ([int]$missing.StatusCode -ne 404) { throw 'Missing orders must return HTTP 404.' }
    $cancelled = Invoke-RestMethod "$baseUrl/api/orders/$($placed.id)/cancel" -Method Post
    if ($cancelled.status -ne 'Cancelled') { throw 'Cancellation failed.' }
    $repeat = Invoke-WebRequest "$baseUrl/api/orders/$($placed.id)/cancel" -Method Post -SkipHttpErrorCheck
    if ([int]$repeat.StatusCode -ne 409) { throw 'Repeated cancellation must return HTTP 409.' }
    $report = Invoke-RestMethod "$baseUrl/api/reports/sales"
    if ($report.grossSales -ne 0 -or $report.cancelledOrders -ne 1) { throw 'Cancellation report is incorrect.' }
    $csv = Invoke-WebRequest "$baseUrl/api/exports/orders.csv"
    $csvText = if ($csv.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($csv.Content) } else { $csv.Content }
    if ($csvText -notmatch 'Cancelled' -or $csvText -notmatch '1186.92') { throw 'CSV history is incomplete.' }
    Write-Output 'PASS dashboard, catalogue quotes, order creation, stock, reports, conflicts, cancellation, and CSV export'
} finally {
    if (!$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
    $apiProcess.Dispose()
    $testDataPath = Join-Path $apiDirectory $testDataName
    if (Test-Path -LiteralPath $testDataPath) { Remove-Item -LiteralPath $testDataPath }
}
