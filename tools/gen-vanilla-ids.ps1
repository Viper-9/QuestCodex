<#
.SYNOPSIS
  SPT_Data/database/templates/quests.json 의 키를 정렬해 server/Data/vanilla-quest-ids.json 으로 기록한다.
.EXAMPLE
  .\tools\gen-vanilla-ids.ps1 -SptVersion 4.1.5
  .\tools\gen-vanilla-ids.ps1 -SptVersion 4.1.5 -SptDataDir "D:\SPT\SPT_Runtime\SPT_Data\database"
#>
param(
    [Parameter(Mandatory = $true)][string]$SptVersion,
    [string]$SptDataDir = $(if ($env:QUESTCODEX_SPT_DATA) { $env:QUESTCODEX_SPT_DATA } else { "F:\SPT4.1.2\SPT_Runtime\SPT_Data\database" })
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $PSScriptRoot
$QuestsPath = Join-Path $SptDataDir "templates\quests.json"
$OutPath = Join-Path $RootDir "server\Data\vanilla-quest-ids.json"

if (-not (Test-Path $QuestsPath)) { throw "quests.json not found: $QuestsPath" }

$json = Get-Content $QuestsPath -Raw -Encoding utf8 | ConvertFrom-Json
$ids = @($json.PSObject.Properties.Name)
[Array]::Sort($ids, [System.StringComparer]::Ordinal)

New-Item -ItemType Directory -Force (Split-Path -Parent $OutPath) | Out-Null
$payload = [ordered]@{ sptVersion = $SptVersion; questIds = $ids }
$payload | ConvertTo-Json -Depth 3 -Compress:$false | Out-File $OutPath -Encoding utf8
Write-Host "wrote $($ids.Count) ids to $OutPath"
