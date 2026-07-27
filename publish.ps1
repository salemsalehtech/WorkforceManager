<#
    بناء نسخة التوزيع
    -------------------
    بيطلّع نسخة **واحدة** نضيفة في مجلد dist، وبيمسح اللي قبلها الأول عشان
    مايحصلش تراكم. (المشكلة اللي كانت موجودة: كل نشر يدوي كان بيسيب نسخة ورا،
    فوصل المجلد 741 ميجا من 3 نسخ + مخلفات بناء.)

    الأوضاع (-Mode):
      Folder      (الافتراضي) مجلد كامل مستقل، مش محتاج تنصيب .NET على الجهاز.
                  ~172 ميجا مفكوك / ~65 ميجا مضغوط.
      SingleFile  ملف exe واحد مضغوط، برضه مستقل. ~70 ميجا.
                  أنضف في التوزيع، بس أول تشغيل بيبقى أبطأ شوية (بيفك نفسه
                  في مجلد مؤقت)، وبعض برامج الحماية بتتحسس من ملفات single-file.
      Light       يعتمد على .NET 8 Desktop Runtime المنصّب على الجهاز. ~15 ميجا.
                  أصغر بكتير، بس لازم الرنتايم يكون متنصّب على كل جهاز في المصنع.

    أمثلة:
        .\publish.ps1
        .\publish.ps1 -Mode SingleFile
        .\publish.ps1 -Version 1.1.0 -NoZip

    ملاحظة: النسخة بتطلع **فاضية من البيانات** — أول تشغيل بيعمل قاعدة بيانات
    جديدة ويزرعها. لو عايز تسلّم النسخة وبياناتها جواها، احط مجلد Data يدوي
    بعد النشر (أو استخدم النسخ الاحتياطي/الاستعادة من شاشة الإعدادات).
#>

[CmdletBinding()]
param(
    [ValidateSet('Folder', 'SingleFile', 'Light')]
    [string]$Mode = 'Folder',
    [string]$Version,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$sln     = Join-Path $root 'WorkforceManager'
$proj    = Join-Path $sln  'WorkforceManager.UI\WorkforceManager.UI.csproj'
$dist    = Join-Path $root 'dist'
$outDir  = Join-Path $dist 'WorkforceManager'
$assets  = Join-Path $root 'publish-assets'

# .NET ممكن مايكونش في الـ PATH في نافذة أوامر جديدة
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH', 'User')
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "مش لاقي dotnet. نصّب .NET 8 SDK الأول."
}

# رقم الإصدار: مصدره الوحيد Directory.Build.props إلا لو اتمرر يدوي
if (-not $Version) {
    $props = Get-Content (Join-Path $sln 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') {
        $Version = $Matches[1]
    } else {
        $Version = '1.0.0'
    }
}

function Get-FolderMB {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $sum = (Get-ChildItem $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    if (-not $sum) { return 0 }
    return [math]::Round($sum / 1MB, 2)
}

Write-Host ""
Write-Host "بناء نسخة التوزيع — الوضع: $Mode — الإصدار: $Version" -ForegroundColor Cyan
Write-Host ("=" * 60)

# ---- 1) مسح النشر القديم (ده اللي بيمنع التراكم) ----
if (Test-Path $dist) {
    Write-Host "مسح نسخة النشر القديمة ..." -ForegroundColor Yellow
    Remove-Item -LiteralPath $dist -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ---- 2) النشر ----
$publishArgs = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $outDir,
    '--nologo',
    '-v', 'quiet'
)

switch ($Mode) {
    'Folder' {
        $publishArgs += @('--self-contained', 'true')
    }
    'SingleFile' {
        $publishArgs += @(
            '--self-contained', 'true',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true'
        )
    }
    'Light' {
        $publishArgs += @('--self-contained', 'false')
    }
}

Write-Host "جاري النشر ..." -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "فشل النشر (exit $LASTEXITCODE)" }

# ---- 3) ملفات التوزيع الإضافية (اقرأني + علامة الوضع المحمول) ----
if (Test-Path $assets) {
    Get-ChildItem $assets -File | ForEach-Object {
        Copy-Item $_.FullName -Destination $outDir -Force
        Write-Host ("  + {0}" -f $_.Name) -ForegroundColor DarkGray
    }
} else {
    Write-Host "  تحذير: مجلد publish-assets مش موجود — النسخة هتطلع من غير اقرأني.txt ولا portable.marker" -ForegroundColor Yellow
}

# ---- 4) تأمين: النسخة تطلع من غير بيانات ----
# (لو اتشغل البرنامج من مجلد النشر بالغلط، بيعمل Data جنبه — مالوش لازمة في التوزيع)
$strayData = Join-Path $outDir 'Data'
if (Test-Path $strayData) {
    Remove-Item -LiteralPath $strayData -Recurse -Force
    Write-Host "  - اتشال مجلد Data (البيانات مش بتتشحن مع النسخة)" -ForegroundColor DarkGray
}

$folderMB = Get-FolderMB $outDir
$fileCount = (Get-ChildItem $outDir -Recurse -File).Count

# ---- 5) الضغط ----
$zipMB = 0
if (-not $NoZip) {
    $zipPath = Join-Path $dist ("WorkforceManager-Portable-v{0}.zip" -f $Version)
    Write-Host "جاري الضغط ..." -ForegroundColor Cyan
    Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal -Force
    $zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
}

# ---- 6) التقرير ----
Write-Host ""
Write-Host ("=" * 60)
Write-Host "تم بنجاح" -ForegroundColor Green
Write-Host ("  المجلد   : {0,8:N2} MB  ({1} ملف)" -f $folderMB, $fileCount)
if (-not $NoZip) {
    Write-Host ("  المضغوط  : {0,8:N2} MB" -f $zipMB)
}
Write-Host ("  المسار   : {0}" -f $outDir)
Write-Host ""
