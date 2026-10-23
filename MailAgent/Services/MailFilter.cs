using MimeKit;

namespace FeuerSoftware.MailAgent.Services
{
    /// <summary>
    /// Decides on the mail headers (subject, sender) whether a mail belongs to this site.
    /// </summary>
    public class MailFilter
    {
        private readonly string? _subjectFilter;
        private readonly string? _senderFilter;

        public MailFilter(string? subjectFilter, string? senderFilter)
        {
            _subjectFilter = subjectFilter;
            _senderFilter = senderFilter;
        }

        public bool Matches(string? subject, string? sender)
            => Matches(subject, sender, _subjectFilter, _senderFilter);

        public override string ToString()
            => $"subject filter '{_subjectFilter}' / sender filter '{_senderFilter}'";

        /// <summary>
        /// Case-insensitive substring match. An empty filter matches every mail.
        /// </summary>
        public static bool Matches(string? subject, string? sender, string? subjectFilter, string? senderFilter)
            => ContainsOrEmptyFilter(subject, subjectFilter) && ContainsOrEmptyFilter(sender, senderFilter);

        /// <summary>
        /// Reads subject and first sender from raw MIME headers, the same way <see cref="MimeMessage.Subject"/>
        /// and <see cref="MimeMessage.From"/> do.
        /// </summary>
        public static (string? subject, string? sender) GetSubjectAndSender(HeaderList headers)
        {
            var fromHeader = headers.FirstOrDefault(h => h.Id == HeaderId.From);
            InternetAddressList? from = null;

            if (fromHeader is not null)
            {
                InternetAddressList.TryParse(ParserOptions.Default, fromHeader.RawValue, out from);
            }

            return (headers[HeaderId.Subject], from?.FirstOrDefault()?.ToString());
        }

        private static bool ContainsOrEmptyFilter(string? value, string? filter)
            => string.IsNullOrEmpty(filter)
            || (value?.Contains(filter, StringComparison.InvariantCultureIgnoreCase) ?? false);
    }
}
