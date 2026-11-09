using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Domain.Entities.SLA;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using ItsTool.Application.Constants;
namespace ItsTool.Infrastructure.Data;

public static class StatusConstants
{
    public const string Open = "Open";
    public const string InProgress = "In Progress";
    public const string OnHold = "Pending";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";
}

public static class TransitionConstants
{
    public const string StartProgress = "Start Progress";
    public const string Close = "Close";
    public const string PutOnHold = "Put on Hold";
    public const string Resolve = "Resolve";
    public const string MoveToOpen = "Move to Open";
    public const string ResumeProgress = "Resume Progress";
    public const string ReopenToOpen = "Reopen to Open";
    public const string ReopenToProgress = "Reopen to Progress";
    public const string ReopenToPending = "Reopen to Pending";
    public const string ReopenToResolved = "Reopen to Resolved";
}


public class DataSeeder
{
    private const string RoleSuperAdmin = "SuperAdmin";
    private const string RoleManager = "Manager";
    private const string RoleAgent = "Agent";
    private const string RoleEndUser = "EndUser";
    private const string RoleSecurityTeam = "Security Team";
    private const string ItDepartment = "Information Technology";
    private const string HardwareCategory = "Hardware Failure";

    private const string DefaultPermissionDescription = "Added by the system.";

    private static readonly Dictionary<string, string> PermissionDescriptions = new()
    {
        { PermissionConstants.TicketCreate, "Create new tickets." },
        { PermissionConstants.TicketView, "See tickets: all of them, or those the user is entitled to." },
        { PermissionConstants.TicketEdit, "Edit ticket details." },
        { PermissionConstants.TicketAssign, "Assign tickets to people or teams." },
        { PermissionConstants.TicketTransfer, "Move tickets to another project or department." },
        { PermissionConstants.TicketResolve, "Mark tickets as resolved." },
        { PermissionConstants.TicketClose, "Close resolved tickets for good." },
        { PermissionConstants.TicketReopen, "Reopen closed tickets." },
        { PermissionConstants.TicketComment, "Add comments to tickets." },
        { PermissionConstants.TicketCommentInternal, "Add internal notes that only staff can see." },
        { PermissionConstants.TicketCommentEdit, "Edit comments on tickets." },
        { PermissionConstants.TicketCommentReply, "Reply to comments on tickets." },
        { PermissionConstants.TicketCommentDelete, "Delete comments on tickets." },
        { PermissionConstants.ReportView, "See reports and statistics." },
        { PermissionConstants.AdminManage, "Full administrator rights across the system (SuperAdmin)." },
        { PermissionConstants.ConfigManage, "Manage system settings, rules and webhooks." },
        { PermissionConstants.SlaManage, "Manage SLA (service level) policies." },
        { PermissionConstants.AuditView, "See the system audit log." },
        { PermissionConstants.KbManage, "Write, edit and publish knowledge base articles." },
        { PermissionConstants.KbView, "Read knowledge base articles." },
        { PermissionConstants.SurveySubmit, "Fill in satisfaction surveys." },
        { PermissionConstants.SecurityIncidentView, "See all security incidents and federal reports." },
        { PermissionConstants.SecurityIncidentManage, "Triage, work on and close security incidents." },
        { PermissionConstants.RiskManage, "See and keep the risk register." },
        { PermissionConstants.RiskApprove, "See every risk and approve treatment plans and risk acceptance (the IT manager or a deputy)." },
        { PermissionConstants.KpiManage, "Define the IT KPIs, their owners, cycles and targets; enter any value; see the year-end report." }
    };

    private const string TicketView = PermissionConstants.TicketView;
    private static readonly string[] ManagerPermissions = new[] { "report.view", "audit.view", TicketView, "ticket.assign", "ticket.transfer", "kb.manage" };
    private static readonly string[] AgentPermissions = new[] { TicketView, PermissionConstants.TicketEdit, PermissionConstants.TicketResolve, "ticket.comment", "ticket.assign", "ticket.transfer", "kb.view" };
    private static readonly string[] EndUserPermissions = new[] { "ticket.create", TicketView, "survey.submit", "kb.view" };

    private readonly ItsToolDbContext _context;
    private readonly FirstAdminSettings? _firstAdmin;

    public DataSeeder(ItsToolDbContext context, FirstAdminSettings? firstAdmin = null)
    {
        _context = context;
        _firstAdmin = firstAdmin;
    }

    public async Task SeedAsync()
    {        await SeedTicketTypesAsync();
        await SeedStatusesAsync();
        await SeedPrioritiesAsync();
        await SeedDepartmentsAndGroupsAsync();
        await SeedRolesAndPermissionsAsync();
        await SeedFirstAdminAsync();
        await SeedProjectsAndCategoriesAsync();
        await SeedDynamicFieldsAsync();
        await SeedSlaPoliciesAsync();
        await SeedWorkflowTransitionsAsync();
        await SeedKnowledgeBaseAsync();

    }

    private async Task SeedTicketTypesAsync()
    {
        // Ticket Types
        if (!await _context.TicketTypes.AnyAsync())
        {
            _context.TicketTypes.AddRange(
                new TicketType { Name = "Incident" },
                new TicketType { Name = "Service Request" }
            );
        }
    }

    private async Task SeedStatusesAsync()
    {
        // Statuses
        if (!await _context.Statuses.AnyAsync())
        {
            _context.Statuses.AddRange(
                new Status { Name = StatusConstants.Open, SortOrder = 1, IsSystemDefault = true },
                new Status { Name = StatusConstants.InProgress, SortOrder = 2 },
                new Status { Name = StatusConstants.OnHold, SortOrder = 3, PausesSla = true },
                new Status { Name = StatusConstants.Resolved, SortOrder = 4, IsClosedStatus = true },
                new Status { Name = StatusConstants.Closed, SortOrder = 5, IsClosedStatus = true, IsFinal = true }
            );
        }
    }

    private async Task SeedPrioritiesAsync()
    {
        // Priorities
        if (!await _context.Priorities.AnyAsync())
        {
            _context.Priorities.AddRange(
                new Priority { Name = "Critical", Weight = 100, SeverityLevel = 1 },
                new Priority { Name = "High", Weight = 75, SeverityLevel = 2 },
                new Priority { Name = "Medium", Weight = 50, SeverityLevel = 3 },
                new Priority { Name = "Low", Weight = 25, SeverityLevel = 4 }
            );
        }
    }

    private async Task SeedDepartmentsAndGroupsAsync()
    {
        // Departments
        if (!await _context.Departments.AnyAsync())
        {
            _context.Departments.AddRange(
                new Department { Name = ItDepartment },
                new Department { Name = "Human Resources" }
            );
        }
        await _context.SaveChangesAsync();

        var itDept = await _context.Departments.FirstOrDefaultAsync(d => d.Name == ItDepartment);
        // Groups
        if (!await _context.Groups.AnyAsync() && itDept != null)
        {
            _context.Groups.AddRange(
                new Group { Name = "Helpdesk Team", DepartmentId = itDept.Id },
                new Group { Name = "Systems & Network Team", DepartmentId = itDept.Id },
                new Group { Name = "Software Support Team", DepartmentId = itDept.Id },
                new Group { Name = "Security Team", DepartmentId = itDept.Id }
            );
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedRolesAndPermissionsAsync()
    {
        // Roles & Permissions Setup
        var allPermissions = ItsTool.Application.Constants.PermissionConstants.AllPermissions;
        var existingPermissions = await _context.Permissions.ToListAsync();
        
        var missingPermissions = allPermissions.Where(pKey => !existingPermissions.Any(p => p.Key == pKey)).Select(pKey => new Permission { Name = pKey, Key = pKey, Description = PermissionDescriptions.GetValueOrDefault(pKey, DefaultPermissionDescription) }).ToList();
        if (missingPermissions.Count > 0)
        {
            _context.Permissions.AddRange(missingPermissions);
            existingPermissions.AddRange(missingPermissions);
        }
        await _context.SaveChangesAsync();
        
        // Fill in descriptions that are missing
        var permissionsToUpdate = existingPermissions.Where(ep => string.IsNullOrEmpty(ep.Description)).ToList();
        foreach (var ep in permissionsToUpdate)
        {
            if (PermissionDescriptions.TryGetValue(ep.Key, out var desc))
            {
                ep.Description = desc;
            }
        }
        await _context.SaveChangesAsync();

        var rolesToSeed = new Dictionary<string, string[]>
        {
            // SuperAdmin gets every permission except the explicit-only ones; those go to the security team,
            // apart from approving risks, which an administrator grants to the IT manager
            { RoleSuperAdmin, allPermissions.Except(PermissionConstants.ExplicitOnly).ToArray() },
            { RoleSecurityTeam, PermissionConstants.ExplicitOnly.Except(new[] { PermissionConstants.RiskApprove }).ToArray() },
            { RoleManager, ManagerPermissions },
            { RoleAgent, AgentPermissions },
            { RoleEndUser, EndUserPermissions }
        };

        var existingRoles = await _context.Roles.Include(r => r.RolePermissions).ToListAsync();

        foreach (var kvp in rolesToSeed)
        {
            var role = existingRoles.FirstOrDefault(r => r.Name == kvp.Key);
            if (role == null)
            {
                role = new Role { Name = kvp.Key };
                _context.Roles.Add(role);
                existingRoles.Add(role);
                await _context.SaveChangesAsync(); // save to get Id
            }

            foreach (var permKey in kvp.Value)
            {
                var perm = existingPermissions.FirstOrDefault(p => p.Key == permKey);
                if (perm != null && !role.RolePermissions.Any(rp => rp.PermissionId == perm.Id))
                {
                    _context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
                    _context.SystemAuditLogs.Add(SystemAudit("Role", role.Id, role.Name, "PermissionAdded", "Permissions", permKey));
                }
            }
        }
        await _context.SaveChangesAsync();
    }

    // No account is ever seeded with a known password, and seeding never grants roles to existing users or groups.
    // The first administrator comes from the Bootstrap:Admin* settings and is created only while there are no users.
    private async Task SeedFirstAdminAsync()
    {
        if (_firstAdmin == null || await _context.Users.AnyAsync()) return;

        var superAdminRole = await _context.Roles.FirstAsync(r => r.Name == RoleSuperAdmin);
        var itDept = await _context.Departments.FirstOrDefaultAsync(d => d.Name == ItDepartment);
        var admin = new ItsTool.Domain.Entities.Organization.User
        {
            Username = _firstAdmin.Username,
            Email = _firstAdmin.Email,
            FirstName = "System",
            LastName = "Administrator",
            DepartmentId = itDept?.Id,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(_firstAdmin.Password),
            // The bootstrap password sits in configuration, so it is replaced at first login
            MustChangePassword = true
        };
        _context.Users.Add(admin);
        _context.UserRoles.Add(new ItsTool.Domain.Entities.Auth.UserRole { User = admin, RoleId = superAdminRole.Id });
        await _context.SaveChangesAsync();
        _context.SystemAuditLogs.Add(SystemAudit("User", admin.Id, admin.Username, "RoleAssigned", "Roles", RoleSuperAdmin));
        await _context.SaveChangesAsync();
    }

    // Role assignments and role permissions are kept out of the automatic audit, so the grants made here are recorded explicitly
    private static ItsTool.Domain.Entities.SystemAuditLog SystemAudit(string entityType, int entityId, string entityName, string action, string fieldName, string newValue) => new()
    {
        EntityType = entityType,
        EntityId = entityId.ToString(),
        EntityName = entityName,
        Action = action,
        FieldName = fieldName,
        OldValue = "-",
        NewValue = newValue,
        CreatedBy = "system"
    };

    private async Task SeedProjectsAndCategoriesAsync()
    {
        // Five default projects
        if (!await _context.Projects.AnyAsync())
        {
            _context.Projects.AddRange(
                new ItsTool.Domain.Entities.Project.Project { Name = "IT Support", ProjectKey = "ITS" },
                new ItsTool.Domain.Entities.Project.Project { Name = "Human Resources", ProjectKey = "HR" },
                new ItsTool.Domain.Entities.Project.Project { Name = "Software Development", ProjectKey = "DEV" },
                new ItsTool.Domain.Entities.Project.Project { Name = "DevOps & Infrastructure", ProjectKey = "OPS" },
                new ItsTool.Domain.Entities.Project.Project { Name = "Security Operations", ProjectKey = "SEC" }
            );
            await _context.SaveChangesAsync();
        }
        // Default Categories for ITS project
        var itsProject = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectKey == "ITS");
        if (!await _context.Categories.AnyAsync() && itsProject != null)
        {
            _context.Categories.AddRange(
                new ItsTool.Domain.Entities.Ticket.Category { Name = HardwareCategory, ProjectId = itsProject.Id },
                new ItsTool.Domain.Entities.Ticket.Category { Name = "Software Request", ProjectId = itsProject.Id },
                new ItsTool.Domain.Entities.Ticket.Category { Name = "Network & Internet", ProjectId = itsProject.Id }
            );
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedDynamicFieldsAsync()
    {
        // Dynamic Fields Demo
        if (!await _context.FieldDefinitions.AnyAsync())
        {
            var serverNameField = new ItsTool.Domain.Entities.Config.FieldDefinition { Key = "server_name", Label = "Server Name", FieldType = ItsTool.Domain.Entities.Config.FieldType.Text };
            var impactedUsersField = new ItsTool.Domain.Entities.Config.FieldDefinition { Key = "affected_users", Label = "Number of Affected Users", FieldType = ItsTool.Domain.Entities.Config.FieldType.Number };
            _context.FieldDefinitions.AddRange(serverNameField, impactedUsersField);
            await _context.SaveChangesAsync();

            var hwCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Name == HardwareCategory);
            if (hwCategory != null)
            {
                _context.FormFieldPlacements.AddRange(
                    new ItsTool.Domain.Entities.Config.FormFieldPlacement { CategoryId = hwCategory.Id, FieldDefinitionId = serverNameField.Id, SortOrder = 1, IsRequired = true },
                    new ItsTool.Domain.Entities.Config.FormFieldPlacement { CategoryId = hwCategory.Id, FieldDefinitionId = impactedUsersField.Id, SortOrder = 2, IsRequired = false }
                );
                await _context.SaveChangesAsync();
            }
        }
    }

    private async Task SeedSlaPoliciesAsync()
    {
        // SLA Seed Data
        if (!await _context.SlaPolicies.AnyAsync())
        {
            var policy = new SlaPolicy { Name = "Default SLA Policy", Description = "Default service level agreement" };
            _context.SlaPolicies.Add(policy);
            await _context.SaveChangesAsync();

            var critical = await _context.Priorities.FirstOrDefaultAsync(p => p.Name == "Critical");
            var high = await _context.Priorities.FirstOrDefaultAsync(p => p.Name == "High");
            var medium = await _context.Priorities.FirstOrDefaultAsync(p => p.Name == "Medium");
            var low = await _context.Priorities.FirstOrDefaultAsync(p => p.Name == "Low");

            if (critical != null && high != null && medium != null && low != null)
            {
                _context.SlaTargets.AddRange(
                    new SlaTarget { SlaPolicyId = policy.Id, PriorityId = critical.Id, FirstResponseMinutes = 30, ResolutionMinutes = 240 },
                    new SlaTarget { SlaPolicyId = policy.Id, PriorityId = high.Id, FirstResponseMinutes = 120, ResolutionMinutes = 1440 },
                    new SlaTarget { SlaPolicyId = policy.Id, PriorityId = medium.Id, FirstResponseMinutes = 480, ResolutionMinutes = 4320 },
                    new SlaTarget { SlaPolicyId = policy.Id, PriorityId = low.Id, FirstResponseMinutes = 1440, ResolutionMinutes = 7200 }
                );
            }

            for (int i = 1; i <= 5; i++)
            {
                _context.BusinessHours.Add(new BusinessHour
                {
                    DayOfWeek = (DayOfWeek)i,
                    StartTime = new TimeSpan(9, 0, 0),
                    EndTime = new TimeSpan(18, 0, 0),
                    IsWorkingDay = true
                });
            }
            _context.BusinessHours.Add(new BusinessHour { DayOfWeek = DayOfWeek.Saturday, IsWorkingDay = false });
            _context.BusinessHours.Add(new BusinessHour { DayOfWeek = DayOfWeek.Sunday, IsWorkingDay = false });

            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedWorkflowTransitionsAsync()
    {
        var defaultWorkflow = await GetOrCreateDefaultWorkflowAsync();
        
        var openStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == StatusConstants.Open);
        var inProgressStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == StatusConstants.InProgress);
        var onHoldStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == StatusConstants.OnHold);
        var resolvedStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == StatusConstants.Resolved);
        var closedStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == StatusConstants.Closed);

        if (openStatus != null && inProgressStatus != null && onHoldStatus != null && resolvedStatus != null && closedStatus != null)
        {
            var desiredTransitions = BuildDesiredTransitions(defaultWorkflow.Id, openStatus.Id, inProgressStatus.Id, onHoldStatus.Id, resolvedStatus.Id, closedStatus.Id);

            var currentTransitions = await _context.WorkflowTransitions
                .Where(wt => wt.WorkflowId == defaultWorkflow.Id)
                .ToListAsync();

            foreach (var dt in desiredTransitions)
            {
                var exists = currentTransitions.Any(wt => wt.FromStatusId == dt.FromStatusId && wt.ToStatusId == dt.ToStatusId);
                if (!exists)
                {
                    _context.WorkflowTransitions.Add(dt);
                }
            }
            await _context.SaveChangesAsync();
        }

        await UpdateLegacyTransitionsAsync(resolvedStatus?.Id, closedStatus?.Id, inProgressStatus?.Id);
    }

    private async Task UpdateLegacyTransitionsAsync(int? resolvedStatusId, int? closedStatusId, int? inProgressStatusId)
    {
        var existingTransitions = await _context.WorkflowTransitions.Where(wt => string.IsNullOrEmpty(wt.TransitionName)).ToListAsync();
        if (existingTransitions.Count == 0) return;

        foreach (var et in existingTransitions)
        {
            if ((et.FromStatusId == resolvedStatusId || et.FromStatusId == closedStatusId) && et.ToStatusId == inProgressStatusId)
            {
                et.TransitionName = "Reopen";
                et.RequiredPermissionKey = PermissionConstants.TicketReopen;
            }
            else
            {
                et.TransitionName = "Default";
            }
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedKnowledgeBaseAsync()
    {
        // Knowledge Base Categories & Articles
        if (!await _context.KnowledgeCategories.AnyAsync())
        {
            _context.KnowledgeCategories.AddRange(
                new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeCategory { Name = "Guides" },
                new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeCategory { Name = "Procedures" },
                new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeCategory { Name = "Systems & Infrastructure" }
            );
            await _context.SaveChangesAsync();
        }

        if (!await _context.KnowledgeArticles.AnyAsync())
        {
            var guidesCat = await _context.KnowledgeCategories.FirstOrDefaultAsync(c => c.Name == "Guides");
            var proceduresCat = await _context.KnowledgeCategories.FirstOrDefaultAsync(c => c.Name == "Procedures");
            var systemsCat = await _context.KnowledgeCategories.FirstOrDefaultAsync(c => c.Name == "Systems & Infrastructure");

            var adminAuthor = await _context.UserRoles
                .Where(ur => ur.Role != null && ur.Role.Name == RoleSuperAdmin && !ur.IsDeleted)
                .Select(ur => ur.User)
                .FirstOrDefaultAsync();
            
            if (adminAuthor != null && guidesCat != null && proceduresCat != null && systemsCat != null)
            {
                _context.KnowledgeArticles.AddRange(
                    new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeArticle
                    {
                        Title = "VPN Connection Guide",
                        Content = "A step-by-step guide to setting up the VPN connection for remote access to the company network.\n\n1. Open the Cisco AnyConnect app.\n2. Enter vpn.example.com as the server address.\n3. Sign in with your username and password.\n4. Enter your MFA (multi-factor authentication) code.",
                        CategoryId = guidesCat.Id,
                        AuthorUserId = adminAuthor.Id,
                        Visibility = ItsTool.Domain.Entities.KnowledgeBase.ArticleVisibility.Public,
                        Status = ItsTool.Domain.Entities.KnowledgeBase.ArticleStatus.Published,
                        ViewCount = 12
                    },
                    new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeArticle
                    {
                        Title = "Password Reset Procedure",
                        Content = "The steps to follow if you have forgotten your account password or it has expired.\n\nIf you can sign in to your computer, press Ctrl+Alt+Del and choose 'Change a password'. If you cannot sign in, you can reset it through the Self-Service Portal with an SMS sent to your phone number.",
                        CategoryId = proceduresCat.Id,
                        AuthorUserId = adminAuthor.Id,
                        Visibility = ItsTool.Domain.Entities.KnowledgeBase.ArticleVisibility.Public,
                        Status = ItsTool.Domain.Entities.KnowledgeBase.ArticleStatus.Published,
                        ViewCount = 8
                    },
                    new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeArticle
                    {
                        Title = "Server Maintenance Checklist",
                        Content = "NOTE: This document is for IT staff only.\n\nMetrics to check during monthly server maintenance:\n- CPU and RAM usage trends\n- Disk usage (warn above 80%)\n- Security patch status\n- Latest status of backup jobs (success/failure logs)",
                        CategoryId = systemsCat.Id,
                        AuthorUserId = adminAuthor.Id,
                        Visibility = ItsTool.Domain.Entities.KnowledgeBase.ArticleVisibility.Internal,
                        Status = ItsTool.Domain.Entities.KnowledgeBase.ArticleStatus.Published,
                        ViewCount = 3
                    },
                    new ItsTool.Domain.Entities.KnowledgeBase.KnowledgeArticle
                    {
                        Title = "Draft: New Staff Onboarding",
                        Content = "A draft of the hardware and software access IT provides to new staff in their first days. Not finished yet.",
                        CategoryId = guidesCat.Id,
                        AuthorUserId = adminAuthor.Id,
                        Visibility = ItsTool.Domain.Entities.KnowledgeBase.ArticleVisibility.Internal,
                        Status = ItsTool.Domain.Entities.KnowledgeBase.ArticleStatus.Draft,
                        ViewCount = 0
                    }
                );
                await _context.SaveChangesAsync();
            }
        }
    }


    private static List<ItsTool.Domain.Entities.Workflow.WorkflowTransition> BuildDesiredTransitions(int workflowId, int openId, int inProgressId, int onHoldId, int resolvedId, int closedId)
    {
        return new List<ItsTool.Domain.Entities.Workflow.WorkflowTransition>
        {
            new() { WorkflowId = workflowId, FromStatusId = openId, ToStatusId = inProgressId, IsActive = true, TransitionName = TransitionConstants.StartProgress },
            new() { WorkflowId = workflowId, FromStatusId = openId, ToStatusId = onHoldId, IsActive = true, TransitionName = TransitionConstants.PutOnHold },
            new() { WorkflowId = workflowId, FromStatusId = openId, ToStatusId = resolvedId, IsActive = true, TransitionName = TransitionConstants.Resolve, RequiredPermissionKey = PermissionConstants.TicketResolve },
            new() { WorkflowId = workflowId, FromStatusId = openId, ToStatusId = closedId, IsActive = true, TransitionName = TransitionConstants.Close, RequiredPermissionKey = PermissionConstants.TicketClose },
            new() { WorkflowId = workflowId, FromStatusId = inProgressId, ToStatusId = openId, IsActive = true, TransitionName = TransitionConstants.MoveToOpen },
            new() { WorkflowId = workflowId, FromStatusId = inProgressId, ToStatusId = onHoldId, IsActive = true, TransitionName = TransitionConstants.PutOnHold },
            new() { WorkflowId = workflowId, FromStatusId = inProgressId, ToStatusId = resolvedId, IsActive = true, TransitionName = TransitionConstants.Resolve, RequiredPermissionKey = PermissionConstants.TicketResolve },
            new() { WorkflowId = workflowId, FromStatusId = inProgressId, ToStatusId = closedId, IsActive = true, TransitionName = TransitionConstants.Close, RequiredPermissionKey = PermissionConstants.TicketClose },
            new() { WorkflowId = workflowId, FromStatusId = onHoldId, ToStatusId = openId, IsActive = true, TransitionName = TransitionConstants.MoveToOpen },
            new() { WorkflowId = workflowId, FromStatusId = onHoldId, ToStatusId = inProgressId, IsActive = true, TransitionName = TransitionConstants.ResumeProgress },
            new() { WorkflowId = workflowId, FromStatusId = onHoldId, ToStatusId = resolvedId, IsActive = true, TransitionName = TransitionConstants.Resolve, RequiredPermissionKey = PermissionConstants.TicketResolve },
            new() { WorkflowId = workflowId, FromStatusId = onHoldId, ToStatusId = closedId, IsActive = true, TransitionName = TransitionConstants.Close, RequiredPermissionKey = PermissionConstants.TicketClose },
            new() { WorkflowId = workflowId, FromStatusId = resolvedId, ToStatusId = openId, IsActive = true, TransitionName = TransitionConstants.ReopenToOpen, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = resolvedId, ToStatusId = inProgressId, IsActive = true, TransitionName = TransitionConstants.ReopenToProgress, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = resolvedId, ToStatusId = onHoldId, IsActive = true, TransitionName = TransitionConstants.ReopenToPending, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = resolvedId, ToStatusId = closedId, IsActive = true, TransitionName = TransitionConstants.Close, RequiredPermissionKey = PermissionConstants.TicketClose },
            new() { WorkflowId = workflowId, FromStatusId = closedId, ToStatusId = openId, IsActive = true, TransitionName = TransitionConstants.ReopenToOpen, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = closedId, ToStatusId = inProgressId, IsActive = true, TransitionName = TransitionConstants.ReopenToProgress, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = closedId, ToStatusId = onHoldId, IsActive = true, TransitionName = TransitionConstants.ReopenToPending, RequiredPermissionKey = PermissionConstants.TicketReopen },
            new() { WorkflowId = workflowId, FromStatusId = closedId, ToStatusId = resolvedId, IsActive = true, TransitionName = TransitionConstants.ReopenToResolved, RequiredPermissionKey = PermissionConstants.TicketReopen }
        };
    }

    private async Task<ItsTool.Domain.Entities.Workflow.Workflow> GetOrCreateDefaultWorkflowAsync()
    {
        var wf = await _context.Workflows.FirstOrDefaultAsync(w => w.Name == "Default Global Workflow");
        if (wf == null)
        {
            wf = new ItsTool.Domain.Entities.Workflow.Workflow 
            { 
                Name = "Default Global Workflow", 
                Description = "System default workflow", 
                IsActive = true 
            };
            _context.Workflows.Add(wf);
            await _context.SaveChangesAsync();
        }
        return wf;
    }
}