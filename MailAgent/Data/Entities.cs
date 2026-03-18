namespace FeuerSoftware.MailAgent.Data
{
    // Singleton entity (Id always = 1)
    public class MailAgentSettingsEntity
    {
        public int Id { get; set; } = 1;
        public int EMailPollingIntervalSeconds { get; set; } = 5;
        public string SecretKeyPassphrase { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string ProcessMode { get; set; } = "ConnectPlain";
        public string EMailMode { get; set; } = "Imap";
        public bool IgnoreCertificateErrors { get; set; } = false;
        public string? HeartbeatInterval { get; set; }
        public string HeartbeatUrl { get; set; } = string.Empty;
        public bool DisableEmailAgeThreshold { get; set; } = false;
    }

    // Collection entity for email accounts
    public class EmailAccountEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string EMailHost { get; set; } = string.Empty;
        public int EMailPort { get; set; } = 993;
        public string EMailUsername { get; set; } = string.Empty;
        public string EMailPassword { get; set; } = string.Empty;
        public string EMailSubjectFilter { get; set; } = string.Empty;
        public string EMailSenderFilter { get; set; } = string.Empty;
        public string AuthenticationType { get; set; } = "Basic";
    }

    // Singleton entity (Id always = 1)
    public class PatternSettingsEntity
    {
        public int Id { get; set; } = 1;
        public string StartPattern { get; set; } = string.Empty;
        public string NumberPattern { get; set; } = string.Empty;
        public string KeywordPattern { get; set; } = string.Empty;
        public string FactsPattern { get; set; } = string.Empty;
        public string StreetPattern { get; set; } = string.Empty;
        public string HouseNumberPattern { get; set; } = string.Empty;
        public string CityPattern { get; set; } = string.Empty;
        public string DistrictPattern { get; set; } = string.Empty;
        public string ZipCodePattern { get; set; } = string.Empty;
        public string RicPattern { get; set; } = string.Empty;
        public string LongitudePattern { get; set; } = string.Empty;
        public string LatitudePattern { get; set; } = string.Empty;
        public string ReporterNamePattern { get; set; } = string.Empty;
        public string ReporterPhonePattern { get; set; } = string.Empty;
        public List<AdditionalPatternEntity> AdditionalProperties { get; set; } = [];
    }

    public class AdditionalPatternEntity
    {
        public int Id { get; set; }
        public int PatternSettingsId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
    }
}
