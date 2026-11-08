param(
    [string]$WebView2Package,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '外卖商家券')
)
$ErrorActionPreference = 'Stop'

$version = '1.0.3912.50'
$arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw '需要 Windows .NET Framework 4.8 编译器。'
}

if (!$WebView2Package) {
    $WebView2Package = Join-Path $env:TEMP ('waimai-webview2-' + $version)
    $packageDll = Join-Path $WebView2Package 'lib\net462\Microsoft.Web.WebView2.Core.dll'
    if (!(Test-Path -LiteralPath $packageDll)) {
        New-Item -ItemType Directory -Path $WebView2Package -Force | Out-Null
        $zip = Join-Path $WebView2Package 'webview2.zip'
        $url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg"
        curl.exe -fsSL --max-time 90 -o $zip -- $url
        if ($LASTEXITCODE -ne 0) { throw '下载微软 WebView2 开发组件失败。' }
        Expand-Archive -LiteralPath $zip -DestinationPath $WebView2Package -Force
    }
}

$src = Join-Path $PSScriptRoot 'src'
$dist = $OutputDirectory
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$managed = Join-Path $WebView2Package 'lib\net462'
$core = Join-Path $managed 'Microsoft.Web.WebView2.Core.dll'
$forms = Join-Path $managed 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $WebView2Package "runtimes\win-$arch\native\WebView2Loader.dll"
$exe = Join-Path $dist '外卖商家券.exe'
$resource = Join-Path $src 'read-coupon.js'
$pinyin = Join-Path $src 'pinyin-initials.bin'
$manifest = Join-Path $src 'App.manifest'
$icon = Join-Path $src 'app.ico'
$compilerArgs = @(
    '/nologo', '/optimize+', '/codepage:65001', '/target:winexe', "/platform:$arch", "/out:$exe",
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Net.Http.dll',
    '/reference:System.Web.dll', '/reference:System.Web.Extensions.dll', "/reference:$core", "/reference:$forms",
    "/win32manifest:$manifest", "/win32icon:$icon", "/resource:$resource,read-coupon.js", "/resource:$pinyin,pinyin-initials.bin",
    (Join-Path $src 'MainForm.cs'), (Join-Path $src 'StoreLookup.cs'), (Join-Path $src 'AdvancedSettingsForm.cs'),
    (Join-Path $src 'StoreNameOrder.cs'), (Join-Path $src 'AppTheme.cs')
)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }

Copy-Item -LiteralPath $core,$forms,$loader -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $src 'App.config') -Destination "$exe.config" -Force
Copy-Item -LiteralPath (Join-Path $src 'Unicode-LICENSE.txt') -Destination $dist -Force
$license = Join-Path $WebView2Package 'LICENSE.txt'
if (Test-Path -LiteralPath $license) {
    Copy-Item -LiteralPath $license -Destination (Join-Path $dist 'WebView2-LICENSE.txt') -Force
}
Write-Output $exe
