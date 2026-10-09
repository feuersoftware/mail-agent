namespace FeuerSoftware.MailAgent.Services
{
    /// <summary>
    /// Decides on header data (subject, sender) whether a mail belongs to this site.
    /// Mails that do not match are silently ignored: never downloaded, marked as read, age-checked or logged.
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

        /// <summary>
        /// Case-insensitive substring match. An empty filter matches every mail.
        /// </summary>
        public static bool Matches(string? subject, string? sender, string? subjectFilter, string? senderFilter)
            => ContainsOrEmptyFilter(subject, subjectFilter) && ContainsOrEmptyFilter(sender, senderFilter);

        private static bool ContainsOrEmptyFilter(string? value, string? filter)
            => string.IsNullOrEmpty(filter)
            || (value?.Contains(filter, StringComparison.InvariantCultureIgnoreCase) ?? false);
    }
}
