﻿using Destructurama.Attributed;

namespace FeuerSoftware.MailAgent.Options
{
    public class MailAgentOptions
    {
        public const string SectionName = nameof(MailAgentOptions);

        public List<SiteEmailSetting> EmailSettings { get; set; } = new List<SiteEmailSetting>();

        public int EMailPollingIntervalSeconds { get; set; } = 5;

        [LogMasked]
        public string SecretKeyPassphrase { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;

        public ProcessMode ProcessMode { get; set; } = ProcessMode.Text;

        public EMailMode EMailMode { get; set; } = EMailMode.Imap;

        public bool IgnoreCertificateErrors { get; set; } = false;

        public TimeSpan? HeartbeatInterval { get; set; } = null;

        public string HeartbeatUrl { get; set; } = string.Empty;
    }
}
