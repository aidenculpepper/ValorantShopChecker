param([string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part version in version.json.' }
$assembly = @"
using System.Reflection;
[assembly: AssemblyTitle("Valorant Shop Checker")]
[assembly: AssemblyProduct("Nightshift - Valorant Shop Checker")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@
$assembly | Set-Content (Join-Path $PSScriptRoot 'AssemblyInfo.cs') -Encoding UTF8
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Shop.cs','Brand.cs','ShopUI.cs','ClientLifecycle.cs','LifecycleTests.cs','Preferences.cs','Updates.cs','UpdateTests.cs','SettingsUI.cs','AssemblyInfo.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /optimize+ "/out:$PSScriptRoot\ValorantShopChecker.exe" "/win32icon:$PSScriptRoot\Nightshift.ico" "/resource:$PSScriptRoot\Nightshift.ico,Nightshift.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
if (-not $InnoCompiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $InnoCompiler=$command.Source }
    else { $InnoCompiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Pass the Inno Setup 6.7+ compiler path using -InnoCompiler.' }
& $InnoCompiler /Q "/DAppVersion=$version" "$PSScriptRoot\ValorantShopChecker.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$hash=(Get-FileHash (Join-Path $PSScriptRoot 'ValorantShopCheckerSetup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
@{ version=$version; sha256=$hash } | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'release.json') -Encoding UTF8
Write-Output "Built ValorantShopChecker $version"
