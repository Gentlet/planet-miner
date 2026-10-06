#requires -Version 7.0

<#
.SYNOPSIS
PlanetMiner의 연결된 Unity Editor에서 재컴파일과 선택한 EditMode 테스트를 실행하고 결과를 요약한다.
.DESCRIPTION
입력은 프로젝트/CLI 경로와 CompileOnly 또는 TestFilter 또는 명시적인 AllEditModeTests다.
CLI 접수 성공을 완료로 간주하지 않고 Editor 준비, 컴파일 종료, 어셈블리 시각과 실제 테스트 건수를 확인한다.
실패/경고는 요약 결과에도 남긴다. Play Mode·입력·렌더·성능의 동작 증거는 제공하지 않는다.
외부 프로세스·poll timeout·선택 테스트 결과는 이 실행 범위가 소유하며 게임 소스/장면/설정은 수정하지 않는다.
#>

[CmdletBinding()]
param(
    [string[]]$TestFilter,
    [switch]$CompileOnly,
    [switch]$AllEditModeTests,
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$UnityCliPath = 'C:\Users\cyc07\AppData\Local\Unity\bin\unity.exe',
    [ValidateRange(1, 30)]
    [int]$InitialPollSeconds = 2,
    [ValidateRange(1, 60)]
    [int]$MaximumPollSeconds = 10,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 300,
    [ValidateRange(1, 100)]
    [int]$MaximumFailureStackLines = 12,
    [ValidateSet('Text', 'Json')]
    [string]$OutputFormat = 'Text'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:UnityWarnings = [System.Collections.Generic.List[string]]::new()

# 오류 항목을 제한된 개수로 요약하되 오류 코드/메시지를 남겨 성공 출력으로 오해하지 않게 한다.
function Get-CompactErrorText {
    param([object[]]$Errors)

    $messages = foreach ($errorItem in @($Errors) | Select-Object -First 3) {
        if ($null -eq $errorItem) {
            continue
        }

        $code = if ($errorItem.PSObject.Properties['code']) { [string]$errorItem.code } else { '' }
        $message = if ($errorItem.PSObject.Properties['message']) { [string]$errorItem.message } else { [string]$errorItem }
        if ($code) { "$code`: $message" } else { $message }
    }

    return ($messages -join '; ')
}

# stdout/stderr를 따로 모으되 JSON/CLI 종료 오류를 함께 판정한다. 접수 성공과 Editor 작업 완료를 구분한다.
function Invoke-UnityJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $UnityCliPath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    foreach ($argument in $Arguments) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    foreach ($globalArgument in @('--format', 'json', '--no-banner', '--no-pager', '--non-interactive')) {
        $null = $startInfo.ArgumentList.Add($globalArgument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unity CLI 프로세스를 시작하지 못했습니다: $UnityCliPath"
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()

    if ([string]::IsNullOrWhiteSpace($stdout)) {
        $detail = if ($stderr) { $stderr.Trim() } else { 'stdout이 비어 있습니다.' }
        throw "Unity CLI가 JSON 응답을 반환하지 않았습니다(exit $exitCode): $detail"
    }

    try {
        $response = $stdout | ConvertFrom-Json -Depth 100
    }
    catch {
        $preview = $stdout.Substring(0, [Math]::Min(500, $stdout.Length)).Trim()
        throw "Unity CLI JSON을 파싱하지 못했습니다(exit $exitCode): $preview"
    }

    $warningItems = @($response.warnings)
    if ($response.PSObject.Properties['data'] -and $response.data -and $response.data.PSObject.Properties['warnings']) {
        $warningItems += @($response.data.warnings)
    }
    foreach ($warningItem in $warningItems) {
        if ($null -eq $warningItem) { continue }
        $warningText = if ($warningItem.PSObject.Properties['message']) {
            [string]$warningItem.message
        }
        else {
            [string]$warningItem
        }
        if ($warningText) { $script:UnityWarnings.Add($warningText.Trim()) }
    }

    $outerSuccess = $response.PSObject.Properties['success'] -and [bool]$response.success
    $innerSuccess = -not $response.PSObject.Properties['data'] -or
        $null -eq $response.data -or
        -not $response.data.PSObject.Properties['success'] -or
        [bool]$response.data.success

    if ($exitCode -ne 0 -or -not $outerSuccess -or -not $innerSuccess) {
        $detail = Get-CompactErrorText -Errors @($response.errors)
        if (-not $detail -and $response.PSObject.Properties['data'] -and $response.data) {
            $detail = Get-CompactErrorText -Errors @($response.data.errors)
        }
        if (-not $detail) { $detail = "Unity CLI 명령 실패(exit $exitCode)" }
        throw $detail
    }

    return $response
}

# Pipeline 버전에 따라 result가 객체 또는 JSON 문자열일 수 있다. 외부 응답 포장과 실제 작업 결과를 분리해 해석한다.
function Get-UnityCommandResult {
    param([Parameter(Mandatory)]$Response)

    if (-not $Response.PSObject.Properties['data'] -or $null -eq $Response.data) {
        throw 'Unity CLI 응답에 data가 없습니다.'
    }

    $result = $Response.data.result
    if ($result -is [string]) {
        try {
            return $result | ConvertFrom-Json -Depth 100
        }
        catch {
            throw "Unity command result JSON을 파싱하지 못했습니다: $result"
        }
    }

    if ($null -eq $result) {
        throw 'Unity CLI 응답에 command result가 없습니다.'
    }

    return $result
}

# 비동기 작업은 다시 제출하지 않고 상태만 조회한다. poll 간격을 늘리되 timeout/실패를 성공으로 승격하지 않는다.
function Wait-UnityOperation {
    param(
        [Parameter(Mandatory)][ValidateSet('recompile_status', 'test_status')][string]$StatusCommand,
        [Parameter(Mandatory)][string[]]$CompletedStatuses
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $pollSeconds = $InitialPollSeconds
    $lastStatus = $null

    while ([DateTime]::UtcNow -lt $deadline) {
        $response = Invoke-UnityJson -Arguments @(
            'command', $StatusCommand,
            '--project-path', $ProjectPath
        )
        $result = Get-UnityCommandResult -Response $response
        $status = [string]$result.status

        if ($status -ne $lastStatus) {
            Write-Verbose "$StatusCommand 상태: $status"
            $lastStatus = $status
        }

        if ($CompletedStatuses -contains $status) {
            return $result
        }

        if ($result.PSObject.Properties['failed'] -and [bool]$result.failed) {
            return $result
        }

        Start-Sleep -Seconds $pollSeconds
        $pollSeconds = [Math]::Min($MaximumPollSeconds, [Math]::Ceiling($pollSeconds * 1.5))
    }

    throw "$StatusCommand 완료를 ${TimeoutSeconds}초 안에 확인하지 못했습니다. 작업을 다시 제출하지 말고 Editor 연결과 상태를 확인하세요."
}

# 편집기 연결이 있더라도 컴파일/도메인 재로딩이 진행 중이면 다음 검증을 시작하지 않는다.
function Wait-EditorReady {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $pollSeconds = $InitialPollSeconds
    $lastState = $null
    $normalizedProject = [System.IO.Path]::GetFullPath($ProjectPath).TrimEnd('\', '/')

    while ([DateTime]::UtcNow -lt $deadline) {
        $response = Invoke-UnityJson -Arguments @('status', '--project-path', $ProjectPath)
        $instances = @($response.data.instances)
        $instance = $instances | Where-Object {
            [System.IO.Path]::GetFullPath([string]$_.project).TrimEnd('\', '/') -eq $normalizedProject
        } | Select-Object -First 1

        if ($null -eq $instance) {
            throw "연결된 Planet Miner Editor를 찾지 못했습니다: $ProjectPath"
        }

        $state = [string]$instance.state
        if ($state -ne $lastState) {
            Write-Verbose "Editor 상태: $state"
            $lastState = $state
        }

        if ($state -eq 'ready') {
            return
        }

        Start-Sleep -Seconds $pollSeconds
        $pollSeconds = [Math]::Min($MaximumPollSeconds, [Math]::Ceiling($pollSeconds * 1.5))
    }

    throw "Editor가 ${TimeoutSeconds}초 안에 ready 상태가 되지 않았습니다."
}

# up_to_date 응답만으로 최신 소스 반영을 보장하지 않는다. 런타임/선택 테스트 어셈블리 시각을 각 소스 집합과 비교한다.
function Assert-AssemblyFreshness {
    param([switch]$IncludeEditorAssembly)

    $assetsPath = Join-Path $ProjectPath 'Assets'
    $assemblyPath = Join-Path $ProjectPath 'Library\ScriptAssemblies\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "컴파일 어셈블리를 찾지 못했습니다: $assemblyPath"
    }

    $runtimeSources = Get-ChildItem -LiteralPath $assetsPath -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/]Editor[\\/]' }
    $latestRuntimeSource = $runtimeSources | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $runtimeAssembly = Get-Item -LiteralPath $assemblyPath
    if ($latestRuntimeSource -and $runtimeAssembly.LastWriteTimeUtc -lt $latestRuntimeSource.LastWriteTimeUtc) {
        throw "Assembly-CSharp.dll이 최신 C# 소스보다 오래되었습니다: $($latestRuntimeSource.FullName)"
    }

    if ($IncludeEditorAssembly) {
        $editorAssemblyPath = Join-Path $ProjectPath 'Library\ScriptAssemblies\Assembly-CSharp-Editor.dll'
        if (-not (Test-Path -LiteralPath $editorAssemblyPath -PathType Leaf)) {
            throw "EditMode 테스트 어셈블리를 찾지 못했습니다: $editorAssemblyPath"
        }

        $editorSources = Get-ChildItem -LiteralPath $assetsPath -Filter '*.cs' -File -Recurse |
            Where-Object { $_.FullName -match '[\\/]Editor[\\/]' }
        $latestEditorSource = $editorSources | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        $editorAssembly = Get-Item -LiteralPath $editorAssemblyPath
        if ($latestEditorSource -and $editorAssembly.LastWriteTimeUtc -lt $latestEditorSource.LastWriteTimeUtc) {
            throw "Assembly-CSharp-Editor.dll이 최신 Editor C# 소스보다 오래되었습니다: $($latestEditorSource.FullName)"
        }
    }
}

# 전체 테스트 로그 대신 실패 이름/메시지/제한된 스택만 보관한다. 실패 건수/실패 존재 자체는 숨기지 않는다.
function Get-FailedTestDetails {
    param([Parameter(Mandatory)]$TestResult)

    $failures = foreach ($test in @($TestResult.results) | Where-Object { $_.Status -eq 'Failed' }) {
        $stackLines = @()
        if ($test.StackTrace) {
            $stackLines = @([string]$test.StackTrace -split "`r?`n" | Select-Object -First $MaximumFailureStackLines)
        }

        [ordered]@{
            name = [string]$test.FullName
            message = ([string]$test.Message).Trim()
            stack = $stackLines
        }
    }

    return @($failures)
}

# 같은 검증 결과를 기계용 JSON 또는 사람용 요약으로 출력한다. 출력 형식 선택이 실행/검증 범위를 바꾸지는 않는다.
function Write-ValidationResult {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][int]$ExitCode
    )

    if ($OutputFormat -eq 'Json') {
        $Result | ConvertTo-Json -Depth 8 -Compress
        return
    }

    $overall = if ($Result.success) { 'PASS' } else { 'FAIL' }
    Write-Output "Unity validation: $overall"
    Write-Output "Compile: $($Result.compile.status) | Assembly freshness: $($Result.compile.assemblyFreshness)"
    if ($Result.compile.testAssemblyFreshness -ne 'not_checked') {
        Write-Output "Test assembly freshness: $($Result.compile.testAssemblyFreshness)"
    }

    foreach ($test in @($Result.tests)) {
        Write-Output ("Tests: {0} | Total: {1} | Passed: {2} | Failed: {3} | Skipped: {4} | Inconclusive: {5} | Duration: {6}s" -f
            $test.filter, $test.total, $test.passed, $test.failed, $test.skipped, $test.inconclusive, $test.duration)
        foreach ($failure in @($test.failures)) {
            Write-Output "Failed: $($failure.name)"
            if ($failure.message) { Write-Output "Message: $($failure.message)" }
            if ($failure.stack.Count -gt 0) { Write-Output "Stack:`n$($failure.stack -join "`n")" }
        }
    }

    if ($Result.error) {
        Write-Output "Error: $($Result.error)"
    }

    foreach ($warning in @($Result.warnings)) {
        Write-Output "Warning: $warning"
    }

    if ($ExitCode -ne 0) {
        Write-Output "Exit code: $ExitCode"
    }
}

$validationResult = [ordered]@{
    success = $false
    project = $null
    compile = [ordered]@{
        status = 'not_run'
        assemblyFreshness = 'not_checked'
        testAssemblyFreshness = 'not_checked'
    }
    tests = @()
    error = $null
    warnings = @()
}

try {
    if (-not (Test-Path -LiteralPath $UnityCliPath -PathType Leaf)) {
        throw "Unity CLI를 찾지 못했습니다: $UnityCliPath"
    }

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        throw "프로젝트 경로를 찾지 못했습니다: $ProjectPath"
    }

    $ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    $validationResult.project = $ProjectPath

    # 모드는 하나만 허용해 실수로 전체 테스트를 실행하지 않게 한다.
    # TODO: null TestFilter도 @($TestFilter).Count가 양수로 계산되는 기존 조건 때문에 CompileOnly가 거부될 수 있다.
    # 옵션 오검출은 실제 호출에서 확인한 별도 결함이며 이번 주석 작업에서는 동작을 변경하지 않는다.
    $selectedModes = @(@(
            [bool]$CompileOnly,
            [bool]$AllEditModeTests,
            [bool](@($TestFilter).Count -gt 0)
        ) | Where-Object { $_ })
    if ($selectedModes.Count -ne 1) {
        throw 'CompileOnly, TestFilter, AllEditModeTests 중 정확히 하나를 지정하세요. 전체 테스트는 AllEditModeTests를 명시한 경우에만 실행됩니다.'
    }

    Wait-EditorReady

    $assetsNeedRefresh = $false
    try {
        Assert-AssemblyFreshness -IncludeEditorAssembly:(-not $CompileOnly)
    }
    catch {
        $assetsNeedRefresh = $true
        Write-Verbose "오래된 어셈블리를 감지해 Unity AssetDatabase를 새로고칩니다: $($_.Exception.Message)"
    }

    if ($assetsNeedRefresh) {
        $null = Invoke-UnityJson -Arguments @(
            'command', 'eval',
            '--code', 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);',
            '--timeout', '30000',
            '--project-path', $ProjectPath
        )
        Wait-EditorReady
    }

    $null = Invoke-UnityJson -Arguments @(
        'command', 'recompile', '--focus', 'false',
        '--project-path', $ProjectPath
    )
    $compileResult = Wait-UnityOperation -StatusCommand 'recompile_status' -CompletedStatuses @('completed', 'up_to_date')
    $compileErrors = @($compileResult.errors)
    if (($compileResult.PSObject.Properties['failed'] -and [bool]$compileResult.failed) -or $compileErrors.Count -gt 0) {
        $detail = Get-CompactErrorText -Errors $compileErrors
        if (-not $detail) { $detail = 'Unity 재컴파일이 실패했습니다.' }
        throw $detail
    }

    Wait-EditorReady
    $validationResult.compile.status = [string]$compileResult.status
    Assert-AssemblyFreshness
    $validationResult.compile.assemblyFreshness = 'fresh'
    if (-not $CompileOnly) {
        $validationResult.compile.testAssemblyFreshness = if ($assetsNeedRefresh) {
            'asset_refresh_then_recompile'
        }
        else {
            'fresh_before_test'
        }
    }

    if (-not $CompileOnly) {
        $filters = if ($AllEditModeTests) { @($null) } else { @($TestFilter) }
        foreach ($filter in $filters) {
            $testArguments = @(
                'command', 'run_tests', '--mode', 'editor',
                '--async_tests', 'true', '--project-path', $ProjectPath
            )
            if ($null -ne $filter) {
                $testArguments += @('--filter', $filter)
            }

            $null = Invoke-UnityJson -Arguments $testArguments
            $testResult = Wait-UnityOperation -StatusCommand 'test_status' -CompletedStatuses @('completed')
            $summary = $testResult.summary
            if ($null -eq $summary) {
                throw '완료된 테스트 응답에 summary가 없습니다.'
            }

            $failures = Get-FailedTestDetails -TestResult $testResult
            $testSummary = [ordered]@{
                filter = if ($null -eq $filter) { '<all EditMode tests>' } else { $filter }
                total = [int]$summary.total
                passed = [int]$summary.passed
                failed = [int]$summary.failed
                skipped = [int]$summary.skipped
                inconclusive = [int]$summary.inconclusive
                duration = [double]$testResult.duration
                failures = $failures
            }
            $validationResult.tests += $testSummary
        }

    }

    $validationResult.success = @($validationResult.tests | Where-Object { $_.failed -gt 0 }).Count -eq 0
    $exitCode = if ($validationResult.success) { 0 } else { 1 }
}
catch {
    $validationResult.error = $_.Exception.Message
    $exitCode = 3
}

$validationResult.warnings = @($script:UnityWarnings | Select-Object -Unique)
Write-ValidationResult -Result $validationResult -ExitCode $exitCode
exit $exitCode
