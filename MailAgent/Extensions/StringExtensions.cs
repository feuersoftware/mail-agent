using System.Text;

namespace FeuerSoftware.MailAgent.Extensions
{
    public static class StringExtensions
    {
        public static Encoding GuessEncoding(this string? input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    return Encoding.UTF8;
                }

                if (input.Contains("1252") || input.Contains("8859-1"))
                {
                    return Encoding.GetEncoding(1252);
                }

                if (input.Contains("1250") || input.Contains("8859-2"))
                {
                    return Encoding.GetEncoding(1250);
                }

                return Encoding.UTF8;
            }
            catch
            {
                return Encoding.UTF8;
            }
        }
    }
}
