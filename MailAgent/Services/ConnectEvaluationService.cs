using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Processors;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FeuerSoftware.MailAgent.Services
{
    public class ConnectEvaluationService : IConnectEvaluationService
    {
        private readonly ILogger<ConnectPlainProcessor> _log;
        private readonly ConnectPatternOptions _options;
        private static readonly TimeSpan _timeoutTimeSpan = TimeSpan.FromSeconds(2);

        public ConnectEvaluationService(
            [NotNull] ILogger<ConnectPlainProcessor> log,
            [NotNull] IOptions<ConnectPatternOptions> options)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public OperationModel Evaluate(string text)
        {
            _log.LogDebug("Evaluating connect operation with regex...");

            var keywordRegex = new Regex(_options.KeywordPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var factsRegex = new Regex(_options.FactsPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var streetRegex = new Regex(_options.StreetPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var houeseNumberRegex = new Regex(_options.HouseNumberPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var cityRegex = new Regex(_options.CityPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var districtRegex = new Regex(_options.DistrictPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var zipCodeRegex = new Regex(_options.ZipCodePattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var ricRegex = new Regex(_options.RicPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var longitudeRegex = new Regex(_options.LongitudePattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var latitudeRegex = new Regex(_options.LatitudePattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var reporterNameRegex = new Regex(_options.ReporterNamePattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var reporterPhoneRegex = new Regex(_options.ReporterPhonePattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var numberRegex = new Regex(_options.NumberPattern, RegexOptions.Compiled, _timeoutTimeSpan);
            var startRegex = new Regex(_options.StartPattern, RegexOptions.Compiled, _timeoutTimeSpan);

            var keyword = keywordRegex.Match(text).Groups[1].Value.Trim();
            var facts = factsRegex.Match(text).Groups[1].Value.Trim();
            var street = streetRegex.Match(text).Groups[1].Value.Trim();
            var houseNumber = houeseNumberRegex.Match(text).Groups[1].Value.Trim();
            var city = cityRegex.Match(text).Groups[1].Value.Trim();
            var district = districtRegex.Match(text).Groups[1].Value.Trim();
            var zipCode = zipCodeRegex.Match(text).Groups[1].Value.Trim();
            var ric = string.Join("; ", ricRegex.Matches(text).Select(match => match.Groups[1].Value?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)));
            var longitude = longitudeRegex.Match(text).Groups[1].Value.Trim();
            var latitude = latitudeRegex.Match(text).Groups[1].Value.Trim();
            var reporterName = reporterNameRegex.Match(text).Groups[1].Value.Trim();
            var reporterPhone = reporterPhoneRegex.Match(text).Groups[1].Value.Trim();
            var number = numberRegex.Match(text).Groups[1].Value.Trim();
            var start = startRegex.Match(text).Groups[1].Value.Trim();

            // Get the number separator for this culture and replace any others with it
            var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            // Replace any periods or commas with the current culture separator and parse
            longitude = Regex.Replace(longitude, "[.,]", separator);
            latitude = Regex.Replace(latitude, "[.,]", separator);

            _log.LogDebug($"Raw text to evaluate is: '{text}'.");

            var additionalProperties = new List<OperationPropertyModel>();

            foreach (var additionalProperty in _options.AdditionalProperties)
            {
                var regex = new Regex(additionalProperty.Pattern);

                var value = regex.Match(text).Groups[1].Value;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                additionalProperties.Add(new OperationPropertyModel()
                {
                    Key = additionalProperty.Name,
                    Value = value.Trim(),
                });
            }

            var hasValidPosition = !string.IsNullOrWhiteSpace(longitude)
                && !string.IsNullOrWhiteSpace(latitude)
                && double.TryParse(longitude, out var _)
                && double.TryParse(latitude, out var _);

            var hasReporter = !string.IsNullOrWhiteSpace(reporterName) || !string.IsNullOrWhiteSpace(reporterPhone);
            var hasStart = DateTime.TryParse(start, out var startParsed);

            var operation = new OperationModel()
            {
                Number = number,
                Address = new AddressModel()
                {
                    City = city,
                    District = district,
                    Street = street,
                    HouseNumber = houseNumber,
                    ZipCode = zipCode,
                },
                Position = hasValidPosition ? new PositionModel()
                {
                    Longitude = Convert.ToDouble(longitude),
                    Latitude = Convert.ToDouble(latitude),
                } : null,
                Reporter = hasReporter ? new ReporterModel()
                {
                    Name = reporterName,
                    Phone = reporterPhone,
                } : null,
                Keyword = keyword,
                Facts = facts,
                Ric = ric,
                Start = hasStart ? startParsed : DateTime.Now,
                Properties = additionalProperties,
            };

            _log.LogDebug("Evaluated operation {@Operation} from text.", operation);

            return operation;
        }
    }
}
