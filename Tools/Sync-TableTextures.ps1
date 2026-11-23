#requires -Version 7.0

<#
.SYNOPSIS
TableBuilder로 설정을 생성하고 열린 Planet Miner Unity Editor에 건물과 아이템 텍스처를 적용한다.
.DESCRIPTION
동일 프로젝트의 Unity 검증 래퍼와 실행을 직렬화한다. 생성이 성공한 뒤 Editor가 동기 임포트 및 머티리얼 저장 확인을 마치면 저장 건수를 출력한다.
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$TableBuilderRoot,
    [string]$UnityCliPath = 'C:\Users\cyc07\AppData\Local\Unity\bin\unity.exe',
    [ValidateRange(1, 300)]
    [int]$ApplyTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-UnityCommand {
    param([string[]]$Command)

    $output = & $UnityCliPath command @Command --project-path $ProjectPath --caller plugin --skill unity-cli --format json
    $exitCode = $LASTEXITCODE
    $responseText = $output -join [Environment]::NewLine
    $response = try { ConvertFrom-Json -InputObject $responseText -AsHashtable } catch { $null }
    $commandName = $Command[0..1] -join ' '
    if ($response -isnot [System.Collections.IDictionary]) {
        throw "Unity '$commandName'의 결과를 확인할 수 없습니다 (exit $exitCode). $responseText"
    }

    if ($exitCode -ne 0 -or $response['success'] -eq $false) {
        $message = if ($response['errorDetails']) {
            ($response['errorDetails'] -split '\r?\n', 2)[0]
        } elseif ($response['errors']) {
            ($response['errors'] | ForEach-Object { $_['message'] }) -join ' / '
        } elseif ($response['error']) {
            $response['error']
        } else {
            $responseText
        }
        throw "Unity '$commandName' 실패 (exit $exitCode). $message"
    }

    return $response
}

function Assert-EditorReady {
    $editor = Invoke-UnityCommand -Command @('editor', 'status')
    if ($editor['projectPath'] -ne $ProjectPath) {
        throw "Unity Editor 프로젝트가 일치하지 않습니다. 요청 '$ProjectPath', 응답 '$($editor['projectPath'])'."
    }
    if ($editor['status'] -ne 'ready' -or $editor['playMode'] -ne 'stopped' -or
        $editor['compiling'] -or $editor['domainReloadInProgress']) {
        throw "Unity Editor가 Edit Mode에서 준비된 뒤 다시 실행하세요. 상태 $($editor['status']), Play Mode $($editor['playMode'])."
    }

    $compile = Invoke-UnityCommand -Command @('script', 'recompile_status')
    $tests = Invoke-UnityCommand -Command @('test', 'status')
    $activeStates = @('queued', 'pending', 'compiling', 'running', 'in_progress', 'executing')
    if (!$compile['status'] -or !$tests['status'] -or $compile['status'] -in $activeStates -or
        $tests['status'] -in $activeStates -or $compile['failed'] -or $compile['compilationFailed']) {
        throw "Unity 컴파일과 테스트의 준비 상태를 확인하세요. 컴파일 $($compile['status']), 테스트 $($tests['status'])."
    }

    $build = Invoke-UnityCommand -Command @('script', 'eval', '--code', 'return UnityEditor.BuildPipeline.isBuildingPlayer;')
    if ($build['success'] -ne $true -or $build['result'] -isnot [bool] -or $build['result']) {
        throw 'Unity Player 빌드가 끝난 뒤 다시 실행하세요.'
    }
}

$stage = '환경 확인'
$projectMutex = $null
$ownsMutex = $false
try {
    $ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\', '/')
    if (!$TableBuilderRoot) {
        $TableBuilderRoot = Join-Path (Split-Path -Parent $ProjectPath) 'TableBuilder'
    }
    $TableBuilderRoot = (Resolve-Path -LiteralPath $TableBuilderRoot).Path
    $builderProject = Join-Path $TableBuilderRoot 'TableBuilder.csproj'
    $tablesPath = Join-Path $TableBuilderRoot 'Tables'
    $outputPath = Join-Path $ProjectPath 'Assets/Resources/Config'
    if (!(Test-Path -LiteralPath $builderProject -PathType Leaf) -or
        !(Test-Path -LiteralPath $tablesPath -PathType Container)) {
        throw "TableBuilder 프로젝트와 Tables 폴더를 찾을 수 없습니다. '$TableBuilderRoot'."
    }
    if (!(Test-Path -LiteralPath $outputPath -PathType Container)) {
        throw "프로젝트의 Config 출력 폴더를 찾을 수 없습니다. '$outputPath'."
    }

    $null = Get-Command dotnet -CommandType Application
    $UnityCliPath = (Resolve-Path -LiteralPath $UnityCliPath).Path

    # Verify-Unity.ps1과 같은 프로젝트 키를 사용해 컴파일/테스트 도중 에셋 저장을 막는다.
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($ProjectPath.ToUpperInvariant())
        $mutexKey = [BitConverter]::ToString($sha256.ComputeHash($pathBytes)).Replace('-', '')
    } finally {
        $sha256.Dispose()
    }
    $projectMutex = [System.Threading.Mutex]::new($false, "PlanetMiner.UnityValidation.$mutexKey")
    try { $ownsMutex = $projectMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (!$ownsMutex) {
        throw '같은 프로젝트의 Unity 검증 또는 텍스처 적용이 진행 중입니다.'
    }

    $stage = 'Unity Editor 준비 확인'
    Assert-EditorReady

    $stage = 'TableBuilder 설정 생성'
    $builderOutput = & dotnet run --project $builderProject -- --all $tablesPath $outputPath 2>&1
    $builderExit = $LASTEXITCODE
    if ($builderExit -ne 0) {
        throw "TableBuilder 실패 (exit $builderExit). $($builderOutput -join [Environment]::NewLine)"
    }
    Write-Verbose ($builderOutput -join [Environment]::NewLine)

    $stage = 'Unity 임포트 및 텍스처 적용 (JSON 생성 완료)'
    Assert-EditorReady
    $result = Invoke-UnityCommand -Command @(
        'script', 'eval',
        '--code', 'return TableTextureSynchronizer.ApplyGeneratedTextures();',
        '--timeout', $ApplyTimeoutSeconds
    )
    $saved = $result['result']
    if ($result['success'] -ne $true -or ($saved -isnot [int] -and $saved -isnot [long]) -or $saved -lt 0) {
        throw 'Unity가 머티리얼 저장 건수를 반환하지 않았습니다. Editor에서 현재 작업 상태를 확인하세요.'
    }
    Write-Output "Table 텍스처 적용 완료. 저장 확인: $saved 개."
} catch {
    Write-Error "[$stage] $($_.Exception.Message)" -ErrorAction Continue
    exit 1
} finally {
    if ($ownsMutex) { $projectMutex.ReleaseMutex() }
    if ($null -ne $projectMutex) { $projectMutex.Dispose() }
}
