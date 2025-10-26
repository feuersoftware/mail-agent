namespace FeuerSoftware.MailAgent.Options
{
    public class ConnectPatternOptions
    {
        public const string SectionName = nameof(ConnectPatternOptions);

        public string StartPattern { get; set; } = string.Empty;

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

        public string NumberPattern { get; set; } = string.Empty;

        public List<PatternField> AdditionalProperties { get; set; } = new List<PatternField>();
    }
}
