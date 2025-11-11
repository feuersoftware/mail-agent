using Destructurama.Attributed;

namespace FeuerSoftware.MailAgent.Options
{
    public class SiteEmailSetting
    {
        public string Name { get; set; } = string.Empty;

        [LogMasked]
        public string ApiKey { get; set; } = string.Empty;

        public string EMailHost { get; set; } = string.Empty;

        public int EMailPort { get; set; } = 993;

        public string EMailUsername { get; set; } = string.Empty;

        [LogMasked]
        public string EMailPassword { get; set; } = string.Empty;

        public string EMailSubjectFilter { get; set; } = string.Empty;

        public string EMailSenderFilter { get; set; } = string.Empty;

        public AuthenticationType AuthenticationType { get; set; } = AuthenticationType.Basic;
    }
}
