namespace FeuerSoftware.MailAgent.Services
{
    /// <summary>
    /// Decides on header data (subject, sender) whether a mail belongs to this site.
    /// Mails that do not match are never downloaded, marked as read or age-checked.
    /// </summary>
    public class MailFilter
    {
        private readonly string? _subjectFilter;
        private readonly string? _senderFilter;
        private readonly ILogger _log;

        // ponytail: grows by one entry per ignored mail over the process lifetime; prune if that ever matters.
        private readonly HashSet<string> _loggedIgnoredIds = new();

        public MailFilter(string? subjectFilter, string? senderFilter, ILogger log)
        {
            _subjectFilter = subjectFilter;
            _senderFilter = senderFilter;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool ShouldProcess(string id, string? subject, string? sender)
        {
            if (Matches(subject, sender, _subjectFilter, _senderFilter))
            {
                return true;
            }

            if (_loggedIgnoredIds.Add(id))
            {
                _log.LogDebug($"Mail '{id}' with subject '{subject}' and sender '{sender}' does not match filters. Ignoring.");
            }

            return false;
        }

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
