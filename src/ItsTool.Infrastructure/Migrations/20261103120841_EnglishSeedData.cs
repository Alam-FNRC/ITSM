using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <summary>
    /// The seed data used to be Turkish; the seeder now writes English. This renames what the old seed created.
    /// Only text that is still exactly as seeded changes, a name is left alone when the English one is already
    /// taken, and every change is written to the audit log. History (audit log, ticket history) keeps what it recorded.
    /// </summary>
    public partial class EnglishSeedData : Migration
    {
        private static readonly Change[] Changes =
        {
            new("Statuses", "Status", "Name", "Name", true, new[]
            {
                ("Açık", "Open"), ("Devam Ediyor", "In Progress"), ("Beklemede", "Pending"), ("Çözüldü", "Resolved"), ("Kapatıldı", "Closed")
            }),
            new("Priorities", "Priority", "Name", "Name", true, new[]
            {
                ("Kritik", "Critical"), ("Yüksek", "High"), ("Orta", "Medium"), ("Düşük", "Low")
            }),
            new("Departments", "Department", "Name", "Name", true, new[]
            {
                ("Bilgi Teknolojileri", "Information Technology"), ("İnsan Kaynakları", "Human Resources")
            }),
            new("Groups", "Group", "Name", "Name", true, new[]
            {
                ("Helpdesk Ekibi", "Helpdesk Team"), ("Sistem & Network Ekibi", "Systems & Network Team"),
                ("Yazılım Destek Ekibi", "Software Support Team"), ("Güvenlik Ekibi", "Security Team")
            }),
            new("Projects", "Project", "Name", "Name", true, new[]
            {
                ("IT Destek", "IT Support"), ("İnsan Kaynakları", "Human Resources"), ("Yazılım Geliştirme", "Software Development"),
                ("DevOps & Altyapı", "DevOps & Infrastructure"), ("Güvenlik Operasyonları", "Security Operations")
            }),
            new("Categories", "Category", "Name", "Name", true, new[]
            {
                ("Donanım Arızası", "Hardware Failure"), ("Yazılım Talebi", "Software Request"), ("Ağ & İnternet", "Network & Internet")
            }),
            new("KnowledgeCategories", "KnowledgeCategory", "Name", "Name", true, new[]
            {
                ("Rehberler", "Guides"), ("Prosedürler", "Procedures"), ("Sistem & Altyapı", "Systems & Infrastructure")
            }),
            new("FieldDefinitions", "FieldDefinition", "Key", "Label", true, new[]
            {
                ("sunucu_adi", "server_name"), ("etkilenen_kullanicilar", "affected_users")
            }),
            new("FieldDefinitions", "FieldDefinition", "Label", "Label", false, new[]
            {
                ("Sunucu Adı", "Server Name"), ("Etkilenen Kullanıcı Sayısı", "Number of Affected Users")
            }),
            new("WorkflowTransitions", "WorkflowTransition", "TransitionName", "TransitionName", false, new[]
            {
                ("Kapatıldı", "Close")
            }),
            new("Workflows", "Workflow", "Description", "Name", false, new[]
            {
                ("Sistem varsayılan iş akışı", "System default workflow")
            }),
            new("SlaPolicies", "SlaPolicy", "Description", "Name", false, new[]
            {
                ("Varsayılan hizmet seviyesi sözleşmesi", "Default service level agreement")
            }),
            new("KnowledgeArticles", "KnowledgeArticle", "Title", "Title", false, new[]
            {
                ("VPN Bağlantı Rehberi", "VPN Connection Guide"),
                ("Parola Sıfırlama Prosedürü", "Password Reset Procedure"),
                ("Sunucu Bakım Kontrol Listesi", "Server Maintenance Checklist"),
                ("Taslak: Yeni Personel Onboarding", "Draft: New Staff Onboarding")
            }),
            new("KnowledgeArticles", "KnowledgeArticle", "Content", "Title", false, new[]
            {
                ("Şirket ağına uzaktan erişim sağlamak için VPN bağlantısının nasıl kurulacağını adım adım anlatan rehber.\n\n1. Cisco AnyConnect uygulamasını açın.\n2. Sunucu adresi olarak vpn.sirket.com girin.\n3. Kullanıcı adı ve şifrenizle giriş yapın.\n4. MFA (Çok Faktörlü Doğrulama) kodunu girin.",
                 "A step-by-step guide to setting up the VPN connection for remote access to the company network.\n\n1. Open the Cisco AnyConnect app.\n2. Enter vpn.example.com as the server address.\n3. Sign in with your username and password.\n4. Enter your MFA (multi-factor authentication) code."),
                ("Hesap parolanızı unuttuysanız veya süresi dolduysa izlemeniz gereken adımlar.\n\nEğer bilgisayarınızda oturum açabiliyorsanız, Ctrl+Alt+Del yaparak 'Parola Değiştir' seçeneğini kullanın. Oturum açamıyorsanız Self-Service Portal üzerinden telefon numaranıza gelecek SMS ile sıfırlama yapabilirsiniz.",
                 "The steps to follow if you have forgotten your account password or it has expired.\n\nIf you can sign in to your computer, press Ctrl+Alt+Del and choose 'Change a password'. If you cannot sign in, you can reset it through the Self-Service Portal with an SMS sent to your phone number."),
                ("DİKKAT: Bu doküman sadece IT personeli içindir.\n\nAylık sunucu bakımlarında kontrol edilmesi gereken metrikler:\n- CPU ve RAM kullanım trendleri\n- Disk doluluk oranları (>%80 ise uyarı)\n- Güvenlik yamalarının (patch) durumu\n- Backup operasyonlarının son durumu (Success/Failure logları)",
                 "NOTE: This document is for IT staff only.\n\nMetrics to check during monthly server maintenance:\n- CPU and RAM usage trends\n- Disk usage (warn above 80%)\n- Security patch status\n- Latest status of backup jobs (success/failure logs)"),
                ("Yeni personellerin ilk günlerinde IT tarafından sağlanacak donanım ve yazılım erişimlerinin taslağıdır. Henüz tamamlanmamıştır.",
                 "A draft of the hardware and software access IT provides to new staff in their first days. Not finished yet.")
            })
        };

        // Matches text with a letter only Turkish uses here
        private const string TurkishLetters = "N'%[ıİşŞğĞçÇöÖüÜ]%'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var change in Changes) migrationBuilder.Sql(change.Sql(reverse: false));

            // Turkish permission descriptions are cleared; the seeder writes the English ones when the app next starts
            migrationBuilder.Sql($@"
INSERT INTO [SystemAuditLogs] ([EntityType], [EntityName], [EntityId], [Action], [FieldName], [OldValue], [NewValue], [CreatedAt], [CreatedBy], [IsActive], [IsDeleted])
SELECT N'Permission', p.[Key], CAST(p.[Id] AS nvarchar(20)), N'Updated', N'Description', p.[Description], N'', SYSUTCDATETIME(), N'system', 1, 0
FROM [Permissions] p WHERE p.[Description] COLLATE Latin1_General_BIN2 LIKE {TurkishLetters};
UPDATE [Permissions] SET [Description] = N'', [UpdatedAt] = SYSUTCDATETIME(), [UpdatedBy] = N'system'
WHERE [Description] COLLATE Latin1_General_BIN2 LIKE {TurkishLetters};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The names go back; permission descriptions stay English
            foreach (var change in Enumerable.Reverse(Changes)) migrationBuilder.Sql(change.Sql(reverse: true));
        }

        /// <summary>Text changes in one column. Unique: skip a row when another row already has the new value.</summary>
        private sealed record Change(string Table, string EntityType, string Column, string NameColumn, bool Unique, (string From, string To)[] Values)
        {
            public string Sql(bool reverse)
            {
                // Line breaks go in as NCHAR(10): EF rewrites the SQL line by line, which would turn a raw one into CR LF
                static string Literal(string s) => "N'" + s.Replace("'", "''").Replace("\n", "' + NCHAR(10) + N'") + "'";
                var rows = string.Join(", ", Values.Select(v => reverse
                    ? $"({Literal(v.To)}, {Literal(v.From)})"
                    : $"({Literal(v.From)}, {Literal(v.To)})"));
                var match = $"FROM [{Table}] t JOIN @map m ON t.[{Column}] COLLATE Latin1_General_BIN2 = m.[OldValue] COLLATE Latin1_General_BIN2"
                    + (Unique ? $" WHERE NOT EXISTS (SELECT 1 FROM [{Table}] x WHERE x.[{Column}] = m.[NewValue])" : "");
                return $@"
DECLARE @map TABLE ([OldValue] nvarchar(max) COLLATE DATABASE_DEFAULT, [NewValue] nvarchar(max) COLLATE DATABASE_DEFAULT);
INSERT INTO @map ([OldValue], [NewValue]) VALUES {rows};
INSERT INTO [SystemAuditLogs] ([EntityType], [EntityName], [EntityId], [Action], [FieldName], [OldValue], [NewValue], [CreatedAt], [CreatedBy], [IsActive], [IsDeleted])
SELECT N'{EntityType}', t.[{NameColumn}], CAST(t.[Id] AS nvarchar(20)), N'Updated', N'{Column}', m.[OldValue], m.[NewValue], SYSUTCDATETIME(), N'system', 1, 0
{match};
UPDATE t SET t.[{Column}] = m.[NewValue], t.[UpdatedAt] = SYSUTCDATETIME(), t.[UpdatedBy] = N'system'
{match};";
            }
        }
    }
}
