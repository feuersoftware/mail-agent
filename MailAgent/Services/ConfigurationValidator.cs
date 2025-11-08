using FeuerSoftware.MailAgent.Options;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class ConfigurationValidator
    {
        private readonly MailAgentOptions _options;
        private readonly ILogger<ConfigurationValidator> _log;

        public ConfigurationValidator(
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<ConfigurationValidator> log)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public List<ConfigurationIssue> ValidateConfiguration()
        {
            var issues = new List<ConfigurationIssue>();

            if (!_options.EmailSettings.Any())
            {
                issues.Add(new ConfigurationIssue
                {
                    Severity = IssueSeverity.Error,
                    Category = "EmailSettings",
                    Message = "No email settings configured. Please add at least one mailbox in appsettings.json.",
                    Hint = "Add an entry to the 'EmailSettings' array with Name, EMailHost, EMailUsername, and AuthenticationType."
                });
                return issues;
            }

            for (int i = 0; i < _options.EmailSettings.Count; i++)
            {
                var setting = _options.EmailSettings[i];
                var mailboxPrefix = $"EmailSettings[{i}] ({setting.Name})";

                // Validate Name
                if (string.IsNullOrWhiteSpace(setting.Name))
                {
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Category = mailboxPrefix,
                        Message = "Mailbox has no name configured.",
                        Hint = "Set the 'Name' property to identify this mailbox in logs."
                    });
                }

                // Validate Host
                if (string.IsNullOrWhiteSpace(setting.EMailHost))
                {
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = IssueSeverity.Error,
                        Category = mailboxPrefix,
                        Message = "EMailHost is required but not configured.",
                        Hint = "Set 'EMailHost' to your mail server (e.g., 'outlook.office365.com' for O365, 'imap.gmail.com' for Gmail)."
                    });
                }

                // Validate Username
                if (string.IsNullOrWhiteSpace(setting.EMailUsername))
                {
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = IssueSeverity.Error,
                        Category = mailboxPrefix,
                        Message = "EMailUsername is required but not configured.",
                        Hint = "Set 'EMailUsername' to your email address (e.g., 'user@company.com')."
                    });
                }

                // Validate authentication-specific settings
                if (setting.AuthenticationType == AuthenticationType.Basic)
                {
                    if (string.IsNullOrWhiteSpace(setting.EMailPassword))
                    {
                        issues.Add(new ConfigurationIssue
                        {
                            Severity = IssueSeverity.Error,
                            Category = mailboxPrefix,
                            Message = "EMailPassword is required for Basic authentication but not configured.",
                            Hint = "Set 'EMailPassword' to your mailbox password, or change 'AuthenticationType' to 'O365' for modern authentication."
                        });
                    }
                }
                else if (setting.AuthenticationType == AuthenticationType.O365)
                {
                    // Check if O365ClientId is configured via environment variable
                    var clientId = Environment.GetEnvironmentVariable("O365_CLIENT_ID");
                    if (string.IsNullOrWhiteSpace(clientId))
                    {
                        issues.Add(new ConfigurationIssue
                        {
                            Severity = IssueSeverity.Warning,
                            Category = mailboxPrefix,
                            Message = "O365_CLIENT_ID environment variable not set. Using default client ID.",
                            Hint = "For production use, set the O365_CLIENT_ID environment variable with your Azure AD application client ID."
                        });
                    }

                    // Check recommended host for O365
                    if (!string.IsNullOrWhiteSpace(setting.EMailHost) && 
                        !setting.EMailHost.Contains("outlook.office365.com", StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ConfigurationIssue
                        {
                            Severity = IssueSeverity.Warning,
                            Category = mailboxPrefix,
                            Message = $"EMailHost is set to '{setting.EMailHost}' but 'outlook.office365.com' is recommended for O365.",
                            Hint = "Change 'EMailHost' to 'outlook.office365.com' for Office 365 mailboxes."
                        });
                    }
                }

                // Validate port
                if (setting.EMailPort <= 0 || setting.EMailPort > 65535)
                {
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = IssueSeverity.Error,
                        Category = mailboxPrefix,
                        Message = $"Invalid EMailPort: {setting.EMailPort}. Must be between 1 and 65535.",
                        Hint = "Set 'EMailPort' to 993 for IMAP with SSL/TLS or 443 for Exchange."
                    });
                }
            }

            return issues;
        }

        public void PrintConfigurationSummary()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine("                    CONFIGURATION SUMMARY");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine($"Process Mode:        {_options.ProcessMode}");
            Console.WriteLine($"Email Mode:          {_options.EMailMode}");
            Console.WriteLine($"Polling Interval:    {_options.EMailPollingIntervalSeconds} seconds");
            Console.WriteLine($"Output Path:         {_options.OutputPath}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Configured Mailboxes:");
            Console.ResetColor();
            Console.WriteLine("───────────────────────────────────────────────────────────────────────");

            if (!_options.EmailSettings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ✗ No mailboxes configured");
                Console.ResetColor();
            }
            else
            {
                for (int i = 0; i < _options.EmailSettings.Count; i++)
                {
                    var setting = _options.EmailSettings[i];
                    Console.WriteLine($"\n  [{i + 1}] {setting.Name}");
                    Console.WriteLine($"      Host:           {setting.EMailHost}:{setting.EMailPort}");
                    Console.WriteLine($"      Username:       {MaskEmail(setting.EMailUsername)}");
                    Console.WriteLine($"      Auth Type:      {setting.AuthenticationType}");
                    
                    if (setting.AuthenticationType == AuthenticationType.Basic)
                    {
                        var hasPassword = !string.IsNullOrWhiteSpace(setting.EMailPassword);
                        Console.ForegroundColor = hasPassword ? ConsoleColor.Green : ConsoleColor.Red;
                        Console.WriteLine($"      Password:       {(hasPassword ? "✓ Configured" : "✗ Missing")}");
                        Console.ResetColor();
                    }
                    else if (setting.AuthenticationType == AuthenticationType.O365)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"      OAuth2:         Requires authentication on first run");
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine();
        }

        public void PrintValidationResults(List<ConfigurationIssue> issues)
        {
            if (!issues.Any())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Configuration validation passed. No issues found.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("CONFIGURATION ISSUES DETECTED:");
            Console.ResetColor();
            Console.WriteLine("───────────────────────────────────────────────────────────────────────");
            Console.WriteLine();

            if (errors.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ {errors.Count} Error(s):");
                Console.ResetColor();
                foreach (var issue in errors)
                {
                    PrintIssue(issue);
                }
                Console.WriteLine();
            }

            if (warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ {warnings.Count} Warning(s):");
                Console.ResetColor();
                foreach (var issue in warnings)
                {
                    PrintIssue(issue);
                }
                Console.WriteLine();
            }

            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine();
        }

        private void PrintIssue(ConfigurationIssue issue)
        {
            var icon = issue.Severity == IssueSeverity.Error ? "✗" : "⚠";
            var color = issue.Severity == IssueSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
            
            Console.ForegroundColor = color;
            Console.WriteLine($"  {icon} [{issue.Category}]");
            Console.ResetColor();
            Console.WriteLine($"     {issue.Message}");
            
            if (!string.IsNullOrWhiteSpace(issue.Hint))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     💡 Hint: {issue.Hint}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return "(not set)";
            }

            var atIndex = email.IndexOf('@');
            if (atIndex <= 0)
            {
                return email.Length > 3 ? email.Substring(0, 3) + "***" : "***";
            }

            var localPart = email.Substring(0, atIndex);
            var domain = email.Substring(atIndex);
            
            if (localPart.Length <= 2)
            {
                return localPart[0] + "***" + domain;
            }
            
            return localPart.Substring(0, 2) + "***" + domain;
        }
    }

    public class ConfigurationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Hint { get; set; } = string.Empty;
    }

    public enum IssueSeverity
    {
        Warning,
        Error
    }
}
