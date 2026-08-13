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
      Installer   نفس بناء Folder (مستقل، .NET جوّه الحزمة) بس **من غير**
                  portable.marker، وبعدين بيبني منه ملف تثبيت واحد بـ Inno
                  Setup. ده وضع التسليم للعميل.

    أمثلة:
        .\publish.ps1
        .\publish.ps1 -Mode SingleFile
        .\publish.ps1 -Version 1.1.0 -NoZip
        .\publish.ps1 -Mode Installer -SeedDatabase "C:\...\workforce_2026-08-11.db"

    ملاحظة: أوضاع Folder/SingleFile/Light بتطلع **فاضية من البيانات** — أول
    تشغيل بيعمل قاعدة بيانات جديدة ويزرعها.

    وضع Installer مختلف: بياخد -SeedDatabase (اختياري) ويحط قاعدة البيانات دي
    جوّه ملف التثبيت، فالعميل أول ما يفتح البرنامج يلاقي بياناته جاهزة من غير
    خطوة استرجاع. الملف ده **لازم** يكون ناتج زرار "نسخة احتياطية الآن" في
    شاشة الإعدادات — الزرار بيعمل VACUUM INTO فبيطلّع لقطة سليمة كاملة
    الجداول. نسخ الـ .db يدوي والبرنامج مفتوح بيطلّع قاعدة ناقصة (وضع WAL).
#>

[CmdletBinding()]
param(
    [ValidateSet('Folder', 'SingleFile', 'Light', 'Installer')]
    [string]$Mode = 'Folder',
    [string]$Version,
    [switch]$NoZip,
    [string]$SeedDatabase
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$sln     = Join-Path $root 'WorkforceManager'
$proj    = Join-Path $sln  'WorkforceManager.UI\WorkforceManager.UI.csproj'
$dist    = Join-Path $root 'dist'
$assets  = Join-Path $root 'publish-assets'
$issFile = Join-Path $root 'installer\WorkforceManager.iss'

# وضع التثبيت بيبني في مرحلة وسيطة مخفية (_stage) لا العميل ولا أي حد
# تاني المفروض يلمسها: لو دخل مجلد dist ولقى مجلد فيه WorkforceManager.exe
# جنب السيتب، هيشغّله على طول ويتخطى التثبيت بالكامل — يعني من غير
# ProgramData بصلاحياته الصحيحة، من غير أيقونة، من غير أي حاجة السيتب
# المفروض يعملها. آخر الملف بتتمسح المرحلة الوسيطة دي، فمجلد dist في وضع
# Installer يفضل فيه ملف واحد بس: السيتب النهائي.
$stageRoot = Join-Path $dist '_stage'
$outDir    = if ($Mode -eq 'Installer') { Join-Path $stageRoot 'app' } else { Join-Path $dist 'WorkforceManager' }

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

# فحوصات وضع التثبيت **قبل** البناء: البناء بياخد دقايق، ومالوش لازمة لو
# الأداة ناقصة أو ملف الداتا مش موجود
$iscc = $null
if ($Mode -eq 'Installer') {
    if (-not (Test-Path $issFile)) { throw "مش لاقي سكربت التثبيت: $issFile" }

    if ($SeedDatabase) {
        if (-not (Test-Path -LiteralPath $SeedDatabase -PathType Leaf)) {
            throw "مش لاقي ملف قاعدة البيانات: $SeedDatabase"
        }
        $SeedDatabase = (Resolve-Path -LiteralPath $SeedDatabase).Path
    }

    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
    if (-not $iscc) {
        $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($found) { $iscc = $found.Source }
    }
    if (-not $iscc) {
        throw "مش لاقي ISCC.exe — نصّب Inno Setup 6 من jrsoftware.org الأول."
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
    'Installer' {
        # نفس Folder بالظبط — الفرق كله في اللي بعد النشر (من غير
        # portable.marker، ومن غير ضغط، وبعدها بناء ملف التثبيت)
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
#
# portable.marker بيتشال في وضع التثبيت عن قصد: وجوده بيخلي AppPaths يحط
# قاعدة البيانات جوّه مجلد البرنامج — وده مع التثبيت بيبقى Program Files،
# اللي المستخدم العادي ممنوع يكتب فيه، وأي ترقية أو إلغاء تثبيت بيمسح
# محتواه. من غيره البيانات بتروح %ProgramData% وتفضل عايشة عبر التحديثات.
if (Test-Path $assets) {
    Get-ChildItem $assets -File | ForEach-Object {
        if ($Mode -eq 'Installer' -and $_.Name -eq 'portable.marker') {
            Write-Host "  - portable.marker (متشال في وضع التثبيت)" -ForegroundColor DarkGray
            return
        }
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
# وضع التثبيت مبيتضغطش: الناتج المسلَّم هو ملف السيتب نفسه، والمجلد ده
# مجرد مرحلة وسيطة
$zipMB = 0
$zipped = (-not $NoZip) -and ($Mode -ne 'Installer')
if ($zipped) {
    $zipPath = Join-Path $dist ("WorkforceManager-Portable-v{0}.zip" -f $Version)
    Write-Host "جاري الضغط ..." -ForegroundColor Cyan
    Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal -Force
    $zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
}

# ---- 6) بناء ملف التثبيت ----
$setupPath = $null
if ($Mode -eq 'Installer') {
    $isccArgs = @("/DAppVersion=$Version", "/DAppDir=$outDir", "/DOutDir=$dist")

    if ($SeedDatabase) {
        $seedDir  = Join-Path $stageRoot 'seed'
        New-Item -ItemType Directory -Force -Path $seedDir | Out-Null
        $seedDest = Join-Path $seedDir 'workforce.db'
        Copy-Item -LiteralPath $SeedDatabase -Destination $seedDest -Force
        $seedMB = [math]::Round((Get-Item $seedDest).Length / 1MB, 2)
        Write-Host ("  + قاعدة بيانات جاهزة ({0:N2} MB)" -f $seedMB) -ForegroundColor DarkGray
        $isccArgs += "/DSeedDb=$seedDest"
    } else {
        Write-Host "  ! من غير -SeedDatabase: العميل هيفتح البرنامج على قاعدة جديدة مزروعة" -ForegroundColor Yellow
    }

    $isccArgs += $issFile

    Write-Host "جاري بناء ملف التثبيت ..." -ForegroundColor Cyan
    & $iscc @isccArgs
    if ($LASTEXITCODE -ne 0) { throw "فشل بناء ملف التثبيت (exit $LASTEXITCODE)" }

    $setupPath = Join-Path $dist ("WorkforceManager-Setup-v{0}.exe" -f $Version)
    if (-not (Test-Path $setupPath)) { throw "السيتب اتبنى بس مش لاقيه في: $setupPath" }

    # ISCC.exe خلاص قرا وضغط كل الملفات جوّه السيتب — المرحلة الوسيطة
    # خلصت دورها. مسحها هو اللي بيضمن إن dist مفيهاش غير التسليم النهائي.
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

# ---- 7) التقرير ----
Write-Host ""
Write-Host ("=" * 60)
Write-Host "تم بنجاح" -ForegroundColor Green
if ($setupPath) {
    $setupMB = [math]::Round((Get-Item $setupPath).Length / 1MB, 2)
    Write-Host ("  الحجم الخام قبل التغليف: {0,8:N2} MB  ({1} ملف)" -f $folderMB, $fileCount) -ForegroundColor DarkGray
    Write-Host "  - اتمسحت ملفات البناء الوسيطة (dist\_stage) — مفيش غير السيتب" -ForegroundColor DarkGray
    Write-Host ("  السيتب   : {0,8:N2} MB" -f $setupMB)
    Write-Host ("  للتسليم  : {0}" -f $setupPath) -ForegroundColor Green
} else {
    Write-Host ("  المجلد   : {0,8:N2} MB  ({1} ملف)" -f $folderMB, $fileCount)
    if ($zipped) {
        Write-Host ("  المضغوط  : {0,8:N2} MB" -f $zipMB)
    }
    Write-Host ("  المسار   : {0}" -f $outDir)
}
Write-Host ""
