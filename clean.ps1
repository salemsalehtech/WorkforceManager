<#
    تنضيف مخلفات البناء
    ----------------------
    بيمسح كل مجلدات bin و obj ومجلد dist. كلها ملفات مُولّدة — بتترجع
    تاني بأمر بناء واحد، ومحدش المفروض ينسخها أو يرفعها على git.

    السبب اللي خلّى السكريبت ده موجود: المجلد كان وصل 741 ميجا، منها
    739 مخلفات بناء متراكمة (نسخ نشر قديمة اتسابت ورا كل مرة).

    الاستخدام:
        .\clean.ps1              # يمسح bin/obj + dist
        .\clean.ps1 -KeepDist    # يمسح bin/obj بس ويسيب نسخ التوزيع
#>

[CmdletBinding()]
param(
    [switch]$KeepDist
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sln  = Join-Path $root 'WorkforceManager'

function Get-FolderMB {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $sum = (Get-ChildItem $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    if (-not $sum) { return 0 }
    return [math]::Round($sum / 1MB, 2)
}

$before = Get-FolderMB $root
$freed  = 0.0

# البرنامج لازم يكون مقفول، وإلا الملفات هتبقى مقفولة ومش هتتمسح
$running = Get-Process WorkforceManager -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "البرنامج شغّال دلوقتي — بيتقفل الأول..." -ForegroundColor Yellow
    $running.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    $running = Get-Process WorkforceManager -ErrorAction SilentlyContinue
    if ($running) { Stop-Process -Id $running.Id -Force }
}

Write-Host "`nمسح مجلدات bin / obj ..." -ForegroundColor Cyan
$targets = Get-ChildItem $sln -Recurse -Directory -Force -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
           Sort-Object { $_.FullName.Length } -Descending

foreach ($t in $targets) {
    if (-not (Test-Path $t.FullName)) { continue }
    $mb = Get-FolderMB $t.FullName
    Remove-Item -LiteralPath $t.FullName -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $t.FullName) {
        Write-Host ("  فشل   {0}" -f $t.FullName.Replace("$root\", '')) -ForegroundColor Red
    } else {
        $freed += $mb
        Write-Host ("  {0,8:N2} MB   {1}" -f $mb, $t.FullName.Replace("$root\", ''))
    }
}

if (-not $KeepDist) {
    $dist = Join-Path $root 'dist'
    if (Test-Path $dist) {
        Write-Host "`nمسح مجلد التوزيع dist ..." -ForegroundColor Cyan
        $mb = Get-FolderMB $dist
        Remove-Item -LiteralPath $dist -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $dist) {
            Write-Host "  فشل مسح dist" -ForegroundColor Red
        } else {
            $freed += $mb
            Write-Host ("  {0,8:N2} MB   dist" -f $mb)
        }
    }
}

$after = Get-FolderMB $root
Write-Host ""
Write-Host ("قبل   : {0,10:N2} MB" -f $before)
Write-Host ("اتحرر : {0,10:N2} MB" -f $freed) -ForegroundColor Green
Write-Host ("بعد   : {0,10:N2} MB" -f $after)
Write-Host "`nتم. أي بناء جديد هيرجّع اللي محتاجه بس." -ForegroundColor Green
