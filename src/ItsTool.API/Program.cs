using ItsTool.Application.Constants;
using ItsTool.Application.Interfaces;
using ItsTool.Infrastructure.Services;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Data.Interceptors;
using ItsTool.Infrastructure.Security;
using ItsTool.API.HostedServices;
using ItsTool.API.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.SignalR;
using ItsTool.API.Hubs;
using System.Text;
using Microsoft.OpenApi.Models;

var currentDir = Directory.GetCurrentDirectory();
var webRootPath = Path.Combine(currentDir, "src", "ItsTool.Web", "wwwroot");
if (!Directory.Exists(webRootPath))
{
    webRootPath = Path.Combine(currentDir, "..", "ItsTool.Web", "wwwroot");
}
if (!Directory.Exists(webRootPath))
{
    webRootPath = Path.Combine(currentDir, "wwwroot");
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.GetFullPath(webRootPath)
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ITSM Tool API",
        Version = "v1",
        Description = "Enterprise IT Service Management (ITSM) RESTful Web API with Clean Architecture, EAV Dynamic Forms, Multi-Agent AI Copilot, and Real-Time SignalR Notifications.",
        Contact = new OpenApiContact
        {
            Name = "TEAM-SMS / Berkay Bayramoğlu",
            Email = "berkaybbayramoglu@gmail.com"
        },
        License = new OpenApiLicense
        {
            Name = "MIT License"
        }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();

builder.Services.AddScoped<SystemAuditInterceptor>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddDbContext<ItsToolDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    var interceptor = sp.GetRequiredService<SystemAuditInterceptor>();
    options.AddInterceptors(interceptor);
});

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPermissionCalculator, PermissionCalculator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Organization Services (Phase 4)
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Catalog & Workflow Services (Phase 5)
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IDynamicFormService, DynamicFormService>();

// Ticket Services (Phase 6)
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<ITicketService, TicketService>();
    builder.Services.AddScoped<IEmailService>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var host = config["Smtp:Host"];
        if (!string.IsNullOrEmpty(host))
        {
            var logger = sp.GetRequiredService<ILogger<SmtpEmailService>>();
            return new SmtpEmailService(config, logger);
        }
        return new StubEmailService();
    });
    builder.Services.AddScoped<IEmailIngestionService, EmailIngestionService>();
    builder.Services.AddSingleton<IEmailQueue, InMemoryEmailQueue>();
    builder.Services.AddHostedService<ItsTool.Infrastructure.BackgroundServices.EmailBackgroundService>();
    builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<ISlaEngine, SlaEngine>();
builder.Services.AddScoped<IAssignmentEngine, AssignmentEngine>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddHttpClient<ILlmService, LlmService>();
builder.Services.AddScoped<IAiAgentDispatcher, AiAgentDispatcher>();
builder.Services.AddScoped<ItsTool.Infrastructure.Agents.ResolutionCopilotAgent>();
builder.Services.AddScoped<ItsTool.Infrastructure.Agents.TicketHandoffSwarm>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISignalRPusher, ItsTool.API.Services.SignalRPusher>();
    builder.Services.AddScoped<ISlaService, SlaService>();
    builder.Services.AddHostedService<SlaCheckerService>();

    // Dashboard, Reporting & KB (Phase 8)
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IReportService, ReportService>();
    builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
    builder.Services.AddScoped<ISystemAuditService, SystemAuditService>();

// Auth Setup
var jwtSecret = JwtSecret.Resolve(builder.Configuration["Jwt:Secret"], builder.Environment.IsDevelopment());
// TokenService signs with the same value, including the random one generated for Development
builder.Configuration["Jwt:Secret"] = jwtSecret;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    // Endpoints without [Authorize] or [AllowAnonymous] still require a login
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    foreach (var perm in PermissionConstants.AllPermissions)
    {
        options.AddPolicy($"RequirePermission:{perm}", policy => 
            policy.Requirements.Add(new PermissionRequirement(perm)));
    }
    options.AddPolicy("RequireKbManage", policy => policy.RequireClaim("permission", "kb.manage"));
});

// Minimal CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("TrustedOrigins", builder =>
    {
        builder.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("TrustedOrigins");
app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "OK",
        service = "ItsTool.API",
        timestamp = DateTime.UtcNow
    });
}).AllowAnonymous();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notification");

// Seeding creates reference and sample data and, on an empty database, the configured first administrator.
// It never creates accounts with known passwords or grants roles, so it may also run outside Development.
var firstAdmin = FirstAdminSettings.FromConfiguration(builder.Configuration);
if (builder.Configuration.GetValue<bool>("AutoSeed"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ItsTool.Infrastructure.Data.ItsToolDbContext>();
    await context.Database.MigrateAsync();
    var seeder = new ItsTool.Infrastructure.Data.DataSeeder(context, firstAdmin);
    await seeder.SeedAsync();

    if (!await context.Users.AnyAsync())
    {
        app.Logger.LogWarning("No user accounts exist. Set Bootstrap:AdminUsername, Bootstrap:AdminEmail and " +
                              "Bootstrap:AdminPassword to create the first administrator.");
    }
}

// One-off cleanup for orphaned group assignments
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<ItsTool.Infrastructure.Data.ItsToolDbContext>();
    var orphanedAssignments = await ctx.TicketAssignments
        .Where(a => a.AssignedGroupId.HasValue && !a.IsDeleted)
        .ToListAsync();
    
    var deletedGroupIds = await ctx.Groups.Where(g => g.IsDeleted).Select(g => g.Id).ToListAsync();
    
    var assignmentsToDelete = orphanedAssignments.Where(a => a.AssignedGroupId.HasValue && deletedGroupIds.Contains(a.AssignedGroupId.GetValueOrDefault())).ToList();
    foreach(var a in assignmentsToDelete) 
    {
        a.IsDeleted = true;
        a.IsActive = false;
    }
    await ctx.SaveChangesAsync();
}

await app.RunAsync();

// Lets the integration tests start the API with WebApplicationFactory<Program>
public partial class Program { }
