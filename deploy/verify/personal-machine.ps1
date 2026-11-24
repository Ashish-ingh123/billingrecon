# Prompt 10.0: everything docs/runbook.md section 8 owes, run in order on the owner's personal machine (Docker Desktop,
# the .NET 10 SDK, Node 22.22 or later), with every line written to verify-10.0.log at the repository root. Send that
# file back. It builds nothing and changes nothing in git; it does replace the compose stack's data (step 8 starts the
# demo afresh with `docker compose down -v`).
#
# From the repository root, in Windows PowerShell or PowerShell 7, with Docker Desktop running and .env in place (copy
# .env.example and set its values, as for Prompt 8.1):
#
#   powershell -ExecutionPolicy Bypass -File deploy\verify\personal-machine.ps1
#
# -SkipLoad leaves out the full-size load test (about ten minutes, at least 2 GB free for Docker's data).
param([switch]$SkipLoad)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root
$log = Join-Path $root 'verify-10.0.log'
Set-Content -Path $log -Value "verify-10.0 started $(Get-Date -Format o) on $env:COMPUTERNAME" -Encoding utf8
$results = New-Object System.Collections.Generic.List[string]

# Runs one step, writes its output to the screen and the log, and keeps its exit code for the summary at the end.
function Invoke-Step([string]$Name, [scriptblock]$Block) {
    $line = "`n===== $Name ($(Get-Date -Format 'HH:mm:ss'))"
    $line | Tee-Object -FilePath $log -Append
    $global:LASTEXITCODE = 0
    & $Block 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append
    $code = $global:LASTEXITCODE
    "===== ${Name}: exit $code" | Tee-Object -FilePath $log -Append
    $results.Add(("{0,-58} exit {1}" -f $Name, $code))
}

# Waits until an address answers 200, at most $Seconds; says how long it took.
function Wait-Ok([string]$Url, [int]$Seconds = 300) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $Seconds) {
        $status = curl.exe -sk -o NUL -w '%{http_code}' $Url
        if ($status -eq '200') { "200 from $Url after $([int]((Get-Date) - $start).TotalSeconds) s"; return }
        Start-Sleep -Seconds 5
    }
    "no 200 from $Url within $Seconds s"
    $global:LASTEXITCODE = 1
}

# The value of a key of .env, for the browser suite's demo password.
function Get-EnvValue([string]$Key) {
    $match = Select-String -Path (Join-Path $root '.env') -Pattern "^$Key=(.*)$" | Select-Object -First 1
    if ($null -eq $match) { return '' }
    return $match.Matches[0].Groups[1].Value.Trim('"')
}

# 1. What this machine is.
Invoke-Step '1. Versions' {
    dotnet --list-sdks
    node --version
    npm --version
    docker version --format '{{.Server.Version}}'
    git rev-parse HEAD
}

# 2 to 5. The backend: tools, build, format gate, every test (the Database collection on SQL Server 2022 in Docker).
Invoke-Step '2. dotnet tool restore' { dotnet tool restore }
Invoke-Step '3. dotnet build' { dotnet build --nologo }
Invoke-Step '4. dotnet format --verify-no-changes' { dotnet format --verify-no-changes }
Invoke-Step '5. dotnet test (all four projects)' { dotnet test --nologo }

# 6. Runbook section 8, D: the concurrency tests, three rounds (a race shows on some runs only).
$races = 'FullyQualifiedName~NumberSeriesOnSqlServerTests|FullyQualifiedName~IdempotencyApiTests|' +
    'FullyQualifiedName~BillsApiTests|FullyQualifiedName~AuditOnSqlServerTests|FullyQualifiedName~AuditTrailApiTests|' +
    'FullyQualifiedName~ReceivablesOnSqlServerTests|FullyQualifiedName~BillPostingApiTests|' +
    'FullyQualifiedName~BillApprovalApiTests|FullyQualifiedName~PaymentsApiTests|' +
    'FullyQualifiedName~PaymentAllocationsApiTests|FullyQualifiedName~CreditNotesApiTests|' +
    'FullyQualifiedName~AdjustmentsApiTests|FullyQualifiedName~PaymentReversalsApiTests|' +
    'FullyQualifiedName~CreditApplicationReversalsApiTests|FullyQualifiedName~RefundsApiTests|' +
    'FullyQualifiedName~EndToEndScenarioTests|FullyQualifiedName~UsersApiTests|FullyQualifiedName~SettingsApiTests|' +
    'FullyQualifiedName~DatabasePermissionsOnSqlServerTests|FullyQualifiedName~IdempotencyKeysOnSqlServerTests'
foreach ($round in 1..3) {
    Invoke-Step "6. The concurrency tests, round $round of 3" {
        dotnet test tests/BillingRecon.IntegrationTests --nologo --filter $races
    }
}

# 7. The frontend's gates, as CI runs them.
Push-Location frontend
Invoke-Step '7a. npm ci' { npm ci }
Invoke-Step '7b. npm audit' { npm audit }
Invoke-Step '7c. format' { npm run format:check }
Invoke-Step '7d. lint' { npm run lint }
Invoke-Step '7e. types and build' { npm run build }
Invoke-Step '7f. the budgets of the build' { npm run budgets }
Invoke-Step '7g. component tests' { npm test }
Invoke-Step '7h. the load test arithmetic' { node --test ../deploy/load/stats.test.mjs }
Pop-Location

# 8. The one-command run, afresh, with the number series' timings on for the load test.
Invoke-Step '8. docker compose down -v, then up --build' {
    docker compose down -v
    $env:SERVER_TIMING = 'true'
    docker compose up -d --build
    Wait-Ok 'https://localhost/health/ready' 600
    docker compose ps
}

# 9. Section 2 and row 8.1: health, headers, the redirect, readiness following SQL Server, the correlation id.
Invoke-Step '9a. Health and the headers of the page' {
    curl.exe -sk -i https://localhost/health/live
    curl.exe -sk -i https://localhost/health/ready
    curl.exe -sk -I https://localhost/
    curl.exe -s -I http://localhost/
}
Invoke-Step '9b. Readiness follows SQL Server' {
    docker compose stop sql
    Start-Sleep -Seconds 10
    curl.exe -sk -i https://localhost/health/ready
    curl.exe -sk -i https://localhost/health/live
    docker compose start sql
    Wait-Ok 'https://localhost/health/ready' 300
}
Invoke-Step '9c. A request with a correlation id (find it in Seq afterwards)' {
    curl.exe -sk -i -H 'X-Correlation-Id: 3f2504e0-4f89-11d3-9a0c-0305e82c3301' https://localhost/api/v1/system/info
}

# 10. Row 8.2: the load test at full size (10,000 bills), then the timings off again.
if (-not $SkipLoad) {
    Invoke-Step '10. The load test (about ten minutes)' { docker compose --profile load run --rm load }
}
Invoke-Step '10b. Timings off again' {
    Remove-Item Env:SERVER_TIMING -ErrorAction SilentlyContinue
    docker compose up -d
    Wait-Ok 'https://localhost/health/ready' 300
}

# 11. Rows 8.2 and 9.1 to 9.4b: every browser suite through nginx at https://localhost (the journey in the demo hospital).
Push-Location frontend
Invoke-Step '11a. The browser for Playwright' { npx playwright install chromium }
Invoke-Step '11b. Every browser suite through nginx' {
    $env:E2E_PASSWORD = Get-EnvValue 'DEMO_PASSWORD'
    $env:E2E_BASE_URL = 'https://localhost'
    npx playwright test --reporter=line
    Remove-Item Env:E2E_PASSWORD
}
Pop-Location

# 12. Row 8.3: the backup in the container, its check, and the recovery model.
Invoke-Step '12. Backup, restore check and recovery model' {
    docker compose cp deploy/backup/backup.sql sql:/tmp/backup.sql
    docker compose exec -T sql bash -c 'mkdir -p /var/opt/mssql/backup && SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /tmp/backup.sql -v Database=BillingRecon BackupFile=/var/opt/mssql/backup/BillingRecon.bak'
    docker compose cp sql:/var/opt/mssql/backup/BillingRecon.bak ./BillingRecon.bak
    docker compose cp deploy/backup/restore-check.sql sql:/tmp/restore-check.sql
    docker compose exec -T sql bash -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -W -i /tmp/restore-check.sql -v Database=BillingRecon BackupFile=/var/opt/mssql/backup/BillingRecon.bak Target=BillingRecon_Restored DataDir=/var/opt/mssql/data'
    docker compose exec -T sql bash -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -W -Q "SELECT name, recovery_model_desc, is_read_committed_snapshot_on FROM sys.databases WHERE name LIKE N''BillingRecon%''; DROP DATABASE IF EXISTS [BillingRecon_Restored];"'
}

"`n===== Summary" | Tee-Object -FilePath $log -Append
$results | Tee-Object -FilePath $log -Append
"`nDone $(Get-Date -Format o). Send verify-10.0.log back; the stack is left running for the checks by hand." |
    Tee-Object -FilePath $log -Append
