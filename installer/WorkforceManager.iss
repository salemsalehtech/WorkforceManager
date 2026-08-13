; ══════════════════════════════════════════════════════════════════════
;  ملف تثبيت — نظام إدارة إنتاجية وأجور العمال
;
;  مبيتبنيش بإيده: publish.ps1 -Mode Installer هو اللي بينشر البرنامج
;  الأول وبعدين بينادي ISCC.exe ويمرّر له AppVersion و AppDir و OutDir
;  (و SeedDb لو اتحدد). القيم الافتراضية تحت عشان الملف يفضل يفتح في
;  محرر Inno من غير أخطاء.
;
;  ثلاث قواعد هنا هي اللي بتحمي بيانات العميل — متتغيّرش:
;
;  1) AppId ثابت للأبد. ده اللي بيخلي السيتب التاني *ترقية* للبرنامج
;     المتنصّب مش تثبيت تاني جنبه. لو اتغيّر، العميل هيبقى عنده نسختين.
;
;  2) قاعدة البيانات بتتحط في {commonappdata} مش في {app}. مجلد البرنامج
;     بيتمسح ويتكتب من أول وجديد مع كل ترقية، والمستخدم العادي ممنوع
;     يكتب فيه أصلاً.
;
;  3) onlyifdoesntexist + uninsneveruninstall على ملف الداتا. الأول
;     بيمنع الترقية إنها تدوس على شغل العميل، والتاني بيمنع إلغاء
;     التثبيت إنه يمسحه. من غير التاني، Inno بيمسح كل ملف نصّبه.
; ══════════════════════════════════════════════════════════════════════

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppDir
  #define AppDir "..\dist\WorkforceManager"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

#define AppName "نظام إدارة إنتاجية وأجور العمال"
#define AppExeName "WorkforceManager.exe"
; غيّر السطر ده لاسمك أو اسم شركتك — بيظهر في "إضافة أو إزالة البرامج"
#define AppPublisher "WorkforceManager"
#define DataDirName "WorkforceManager"

[Setup]
AppId={{1A235F53-6901-486F-AB17-1061FE47A564}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName}

DefaultDirName={autopf}\WorkforceManager
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
DisableWelcomePage=no

; البرنامج بيتنصّب لكل مستخدمي الجهاز، وبيظبط صلاحيات مجلد البيانات —
; الاتنين محتاجين صلاحية مدير
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#OutDir}
OutputBaseFilename=WorkforceManager-Setup-v{#AppVersion}
SetupIconFile=..\WorkforceManager\WorkforceManager.UI\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; الترقية والبرنامج مفتوح: Restart Manager بيلاقي الملفات المقفولة،
; والـ AppMutex بيمسك الحالة اللي RM بيفوّتها. من غيرهم الترقية بتفشل
; وسط الطريق وتسيب مجلد نُص ونُص.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
AppMutex=Local\WorkforceManager_SingleInstance

[LangOptions]
RightToLeft=yes

[Tasks]
Name: "desktopicon"; Description: "إنشاء أيقونة على سطح المكتب"; GroupDescription: "أيقونات إضافية:"

[Dirs]
; مجلد البيانات لازم يبقى قابل للكتابة لكل المستخدمين — SQLite بيكتب
; ملفات -wal و -shm في المجلد نفسه، مش في ملف القاعدة بس، فصلاحية على
; الملف لوحده مش كفاية.
Name: "{commonappdata}\{#DataDirName}";          Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\{#DataDirName}\Backups";  Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifdef SeedDb
; بيانات العميل الجاهزة — بتتحط مرة واحدة بس وعمرها ما تتمسح.
; شوف القاعدتين 2 و 3 فوق.
Source: "{#SeedDb}"; DestDir: "{commonappdata}\{#DataDirName}"; DestName: "workforce.db"; \
    Flags: onlyifdoesntexist uninsneveruninstall
#endif

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExeName}"
Name: "{group}\إلغاء تثبيت البرنامج";     Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; runasoriginaluser: البرنامج يفتح بصلاحية المستخدم العادي مش المدير —
; وده كمان بيجرّب صلاحيات مجلد البيانات على طول
Filename: "{app}\{#AppExeName}"; Description: "تشغيل البرنامج دلوقتي"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[Messages]
; Inno مبيجيش معاه ترجمة عربية رسمية، فبنترجم الرسايل اللي المستخدم
; بيقراها فعلاً في رحلة نكست-نكست. الباقي بيفضل إنجليزي وده مبيظهرش
; في التثبيت العادي.
SetupAppTitle=تثبيت
SetupWindowTitle=تثبيت — %1
UninstallAppTitle=إلغاء تثبيت
UninstallAppFullTitle=إلغاء تثبيت %1

ButtonBack=< السابق
ButtonNext=التالي >
ButtonInstall=تثبيت
ButtonCancel=إلغاء
ButtonFinish=إنهاء
ButtonBrowse=استعراض...
ButtonYes=نعم
ButtonNo=لا
ButtonOK=موافق

WizardSelectDir=اختيار مكان التثبيت
SelectDirDesc=فين تحب البرنامج يتنصّب؟
SelectDirLabel3=هيتنصّب في المجلد ده. لو عايز مكان تاني اضغط استعراض.
SelectDirBrowseLabel=اضغط التالي للاستمرار، أو استعراض لاختيار مجلد تاني.
DiskSpaceGBLabel=محتاج على الأقل [gb] جيجا مساحة فاضية.
DiskSpaceMBLabel=محتاج على الأقل [mb] ميجا مساحة فاضية.

WizardSelectTasks=مهام إضافية
SelectTasksDesc=تحب نعمل إيه كمان؟
SelectTasksLabel2=اختار المهام الإضافية وبعدين اضغط التالي.

WizardReady=جاهز للتثبيت
ReadyLabel1=كل حاجة جاهزة. اضغط تثبيت.
ReadyLabel2a=اضغط تثبيت للبدء، أو السابق لو عايز تغيّر حاجة.
ReadyMemoDir=مكان التثبيت:
ReadyMemoTasks=مهام إضافية:

WizardPreparing=جاري التجهيز
PreparingDesc=استنى شوية...
WizardInstalling=جاري التثبيت
InstallingLabel=استنى لحد ما التثبيت يخلص...
StatusExtractFiles=جاري نسخ الملفات...
StatusCreateIcons=جاري إنشاء الأيقونات...
StatusUninstalling=جاري إلغاء التثبيت...

FinishedHeadingLabel=تم التثبيت بنجاح
FinishedLabel=البرنامج اتنصّب على الجهاز. اضغط إنهاء للخروج.
FinishedLabelNoIcons=البرنامج اتنصّب على الجهاز.
ClickFinish=اضغط إنهاء للخروج.
RunEntryExec=تشغيل %1

ExitSetupTitle=إنهاء التثبيت
ExitSetupMessage=التثبيت لسه مخلصش. لو خرجت دلوقتي البرنامج مش هيتنصّب.%n%nمتأكد إنك عايز تخرج؟
ConfirmUninstall=متأكد إنك عايز تشيل %1 من الجهاز؟%n%nبيانات البرنامج (العمال والإنتاج والأجور) هتفضل موجودة زي ما هي.
UninstalledAll=%1 اتشال من الجهاز. البيانات لسه موجودة.
UninstalledMost=%1 اتشال، بس فيه حاجات مقدرناش نشيلها.
