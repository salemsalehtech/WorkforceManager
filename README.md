# نظام إدارة إنتاجية وأجور العمال — WorkforceManager

تطبيق سطح مكتب (WPF) لإدارة العمال ومهاراتهم، والمنتجات ومراحل تصنيعها،
وتسجيل الإنتاج اليومي مع حساب الأجور تلقائيًا وتقييم الأداء.

## هيكل المشروع (Clean Architecture مبسّطة)

```
WorkforceManager/
├── WorkforceManager.Core/         # النماذج (Models) + الواجهات المجردة (Interfaces) — مفيش أي اعتماد على EF Core أو WPF
│   ├── Models/                    # Worker, Product, ProductionStage, WorkerSkill, DailyProduction
│   ├── Enums/                     # SkillLevel
│   └── Interfaces/                # IGenericRepository, IWorkerRepository, IProductRepository, IDailyProductionRepository
│
├── WorkforceManager.Data/         # طبقة الوصول لقاعدة البيانات (EF Core + SQLite)
│   ├── AppDbContext.cs            # إعداد العلاقات بين الجداول
│   ├── Repositories/              # تنفيذ الـ Interfaces
│   └── Migrations/                # هتتولد تلقائيًا بأمر dotnet ef
│
├── WorkforceManager.Business/     # منطق الأعمال (حساب الأجر، تقييم الأداء)
│   ├── Services/                  # WageCalculationService, PerformanceEvaluationService
│   └── DTOs/                      # WorkerDailySummaryDto, StageBreakdownDto
│
├── WorkforceManager.UI/           # واجهة WPF (MVVM + Material Design)
│   ├── Views/                     # الشاشات (تُبنى في الخطوة القادمة)
│   ├── ViewModels/                # منطق كل شاشة (CommunityToolkit.Mvvm)
│   ├── App.xaml(.cs)              # إعداد Dependency Injection وربط كل الطبقات
│   └── MainWindow.xaml(.cs)       # القائمة الرئيسية والتنقل بين الشاشات
│
└── WorkforceManager.sln
```

## لماذا الهيكل ده؟

- **Core** مفيهوش أي اعتماد على قاعدة بيانات أو واجهة — تقدر تغيّر SQLite لـ SQL Server مستقبلًا من غير ما تلمس النماذج أو منطق الأعمال.
- **Data** مسؤولة بس عن الحفظ والاسترجاع، مفيهاش أي قرار عمل (Business Decision).
- **Business** فيها كل القواعد الحسابية (حساب الأجر، تصنيف الأداء) في مكان واحد، قابلة للاختبار بمعزل عن الواجهة.
- **UI** بتستهلك الطبقات التانية بس عن طريق Dependency Injection، مفيهاش أي منطق حسابي مكتوب جوه كود الشاشة.

## متطلبات التشغيل على جهازك

1. **.NET 8 SDK** — تحميل من: https://dotnet.microsoft.com/download/dotnet/8.0
2. **Visual Studio 2022** (أي إصدار حتى Community المجاني) مع الحزمة Workload:
   - ".NET Desktop Development"

## خطوات التشغيل لأول مرة

```bash
# من داخل مجلد WorkforceManager
dotnet restore

# تثبيت أداة EF Core (مرة واحدة بس على جهازك)
dotnet tool install --global dotnet-ef

# إنشاء أول Migration (توليد جداول قاعدة البيانات من النماذج)
dotnet ef migrations add InitialCreate --project WorkforceManager.Data --startup-project WorkforceManager.UI

# تشغيل التطبيق (هينشئ قاعدة البيانات SQLite تلقائيًا أول مرة)
dotnet run --project WorkforceManager.UI
```

قاعدة البيانات هتتخزن في:
`C:\Users\<اسم المستخدم>\AppData\Local\WorkforceManager\workforce.db`

## الحالة الحالية

- [x] النماذج الأربعة الأساسية (Worker, Product, ProductionStage, DailyProduction) + جدول الربط WorkerSkill
- [x] طبقة قاعدة البيانات (AppDbContext + Repositories)
- [x] خدمات منطق الأعمال (حساب الأجر + تقييم الأداء مقارنة بالمتوسط)
- [x] هيكل واجهة WPF أساسي (Shell + Navigation + DI)
- [ ] بناء الشاشات الفعلية (Views): العمال، المنتجات، تسجيل الإنتاج، التقارير
- [ ] استيراد بيانات العمال والمنتجات من ملفات Excel
- [ ] نظام نسخ احتياطي تلقائي لقاعدة البيانات
- [ ] تصدير التقارير PDF

## ملاحظة مهمة

هذا الهيكل جاهز 100% للفتح مباشرة في Visual Studio والبناء عليه. الخطوة
القادمة المنطقية هي بناء الشاشات (Views) الأربعة بالترتيب: العمال ← المنتجات
← تسجيل الإنتاج ← التقارير.
