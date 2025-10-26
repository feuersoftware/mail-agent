namespace FeuerSoftware.MailAgent.Models
{
    public class OperationModel
    {
        public DateTimeOffset Start { get; set; }

        public string? Keyword { get; set; }

        public AddressModel? Address { get; set; }

        public PositionModel? Position { get; set; }

        public ReporterModel? Reporter { get; set; }

        public string? Facts { get; set; }

        public string? Ric { get; set; }

        public string? Number { get; set; }

        public string? Source { get; set; } = "MailAgent";

        public List<OperationPropertyModel> Properties { get; set; } = new();
    }
}
