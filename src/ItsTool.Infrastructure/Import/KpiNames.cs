using System.Text.RegularExpressions;

namespace ItsTool.Infrastructure.Import;

/// <summary>
/// English names for the capabilities, KPIs, units and scopes of FNRC's IT KPIs sheet, which is written in Arabic
/// only. An administrator can change them after the import; an unknown Arabic name is kept as it is.
/// </summary>
public static class KpiNames
{
    private static readonly Dictionary<string, string> English = new()
    {
        // Capabilities
        ["استراتيجية الذكاء الاصطناعي والإبتكار الرقمي"] = "AI strategy and digital innovation",
        ["خطة التحول الرقمي"] = "Digital transformation plan",
        ["إدارة القنوات الرقمية"] = "Digital channel management",
        ["نظام إدارة أمن المعلومات وفق الأيزو 27001"] = "Information security management system (ISO 27001)",
        ["المنظومة الموحدة للمرونة الرقمية"] = "Unified digital resilience system",
        ["إطار الشراكات والتكامل المؤسسي"] = "Partnerships and institutional integration framework",
        ["بوابة الخدمات الذاتية"] = "Self-service portal",
        // KPIs
        ["عدد اللوحات التحليلية والتنبؤية"] = "Analytical and predictive dashboards",
        ["عدد الحلول المدعومة بالذكاء الاصطناعي المطبقة"] = "AI-supported solutions in use",
        ["نسبة الخدمات الخارجية المرقمنة"] = "Digitised external services",
        ["نسبة إتاحة الموقع الإلكتروني والتطبيق الذكي"] = "Website and smart app availability",
        ["زمن دورة تطوير الموقع الإلكتروني"] = "Website development cycle time",
        ["متوسط زمن حل الأعطال"] = "Average fault resolution time",
        ["عدد الخوادم ومراكز البيانات ومواقع التعافي من الكوارث الموزعة"] = "Servers, data centres and disaster recovery sites",
        ["نسبة إتاحة الأنظمة الحرجة"] = "Critical systems availability",
        ["نسبة السياسات المراجعة في موعدها"] = "Policies reviewed on time",
        ["عدد الجهات التي يتم تبادل البيانات معها"] = "Entities with active data exchange",
        ["رضا الموظفين عن الأنظمة المتكاملة"] = "Staff satisfaction with the integrated systems",
        ["نسبة الخدمات المفعل فيها الدخول بالهوية الرقمية"] = "Services with digital identity sign-in",
        ["نسبة إتاحة البوابة"] = "Portal availability",
        ["عدد الخدمات والوظائف الرقمية في البوابة"] = "Digital services and functions in the portal",
        ["عدد قنوات خدمات الموظفين"] = "Staff service channels",
        // Units
        ["لوحة"] = "dashboard",
        ["حل"] = "solution",
        ["خدمة"] = "service",
        ["وحدة مئوية"] = "%",
        ["سنة"] = "year",
        ["ساعة"] = "hour",
        ["رقم"] = "count",
        ["جهة"] = "entity",
        ["قناة"] = "channel",
        // Scopes
        ["استراتيجي"] = "Strategic",
        ["تشغيلي"] = "Operational",
        ["الموقع الإلكتروني"] = "Website"
    };

    /// <summary>The English name, or null when the Arabic text is not known.</summary>
    public static string? Of(string? arabic) =>
        arabic != null && English.TryGetValue(Normalize(arabic), out var english) ? english : null;

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
