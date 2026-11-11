param([string]$OutputDirectory = "$PSScriptRoot/dist")
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet publish "$PSScriptRoot/App/BurnoutPlaylistStudio.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar portable.' }
foreach($name in @('README.md','README.pt-BR.md','LICENSE','THIRD_PARTY_NOTICES.md','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')) {
 $file=Join-Path $PSScriptRoot $name
 if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $OutputDirectory -Force}
}
if(Test-Path -LiteralPath "$PSScriptRoot/docs") { Copy-Item -LiteralPath "$PSScriptRoot/docs" -Destination $OutputDirectory -Recurse -Force }
