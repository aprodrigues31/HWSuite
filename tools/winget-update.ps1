<#
.SYNOPSIS
    Submits a new HWSuite version to the Windows Package Manager community repository (microsoft/winget-pkgs).

.DESCRIPTION
    Used by the Release workflow after each release, and by the "Winget (update)" workflow by hand.
    It first asks winget-pkgs whether aprodrigues31.HWSuite is already published:
      - not yet (the first submission is still in review): nothing to do, says so;
      - this version already there (or already submitted): nothing to do;
      - otherwise: "wingetcreate update" opens the pull request with the new installer URL.
    Every outcome is a workflow annotation, readable on the run page; the token is never printed.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Token
)

$ErrorActionPreference = 'Stop'
$package = 'aprodrigues31.HWSuite'
$api = 'https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/a/aprodrigues31/HWSuite'
$headers = @{ 'User-Agent' = 'HWSuite-release'; Authorization = "Bearer $Token" }

try {
    $published = (Invoke-RestMethod $api -Headers $headers) | Where-Object { $_.type -eq 'dir' } | ForEach-Object { $_.name }
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        Write-Output "::notice title=Winget::$package is not in winget yet (the first submission is still being reviewed). Version $Version will go in with the next update after it is merged."
        exit 0
    }
    throw
}

Write-Host "Published versions: $($published -join ', ')"
if ($published -contains $Version) {
    Write-Output "::notice title=Winget::$package $Version is already in winget."
    exit 0
}

$url = "https://github.com/aprodrigues31/HWSuite/releases/download/v$Version/hwsuite-setup-$Version.exe"
Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile wingetcreate.exe
$output = & .\wingetcreate.exe update $package --version $Version --urls $url --submit --token $Token 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Host $output
$lines = (($output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 12) -join ' | ').Replace($Token, '***')
if ($code -ne 0) {
    Write-Output "::error title=wingetcreate failed ($code)::$lines"
    exit 1
}

Write-Output "::notice title=Winget::$package $Version submitted. $lines"
