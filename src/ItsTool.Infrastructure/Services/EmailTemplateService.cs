using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using ItsTool.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ItsTool.Infrastructure.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly ILogger<EmailTemplateService> _logger;
    private readonly string _templatePath;

    public EmailTemplateService(ILogger<EmailTemplateService> logger)
        : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "Email"))
    {
    }

    // Tests pass their own folder so they never overwrite the shipped templates
    internal EmailTemplateService(ILogger<EmailTemplateService> logger, string templatePath)
    {
        _logger = logger;
        _templatePath = templatePath;
    }

    public string GenerateEmailBody(string eventKey, Dictionary<string, string> templateData)
    {
        string templateFileName = eventKey.StartsWith("sla.", StringComparison.OrdinalIgnoreCase) 
            ? "SlaEmailTemplate.html" 
            : "BaseTemplate.html";
        var templateFile = Path.Combine(_templatePath, templateFileName);
        
        string templateContent;
        try
        {
            if (File.Exists(templateFile))
            {
                templateContent = File.ReadAllText(templateFile);
            }
            else
            {
                var fallbackFile = Path.Combine(_templatePath, "BaseTemplate.html");
                if (File.Exists(fallbackFile))
                {
                    templateContent = File.ReadAllText(fallbackFile);
                }
                else
                {
                    _logger.LogWarning("Email template {TemplateFile} not found. Using fallback text.", templateFile);
                    templateContent = "<h2>{{EventName}}</h2><p>{{Context}}</p><p><a href=\"{{AppUrl}}\">View Ticket {{TicketNumber}}</a></p>";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read email template from {TemplateFile}", templateFile);
            templateContent = "<h2>{{EventName}}</h2><p>{{Context}}</p><p><a href=\"{{AppUrl}}\">View Ticket {{TicketNumber}}</a></p>";
        }

        // The values are plain text, and some come from users (a requester chooses the ticket title),
        // so they are encoded and cannot add markup or links to the email
        foreach (var kvp in templateData)
        {
            templateContent = templateContent.Replace($"{{{{{kvp.Key}}}}}", WebUtility.HtmlEncode(kvp.Value));
        }

        return templateContent;
    }
}
