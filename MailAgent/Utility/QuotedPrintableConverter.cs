using System.Text;

namespace FeuerSoftware.MailAgent.Utility
{
    public static class QuotedPrintableConverter
    {
        public static string Decode(string input, Encoding encoding)
        {
            var i = 0;
            var output = new List<byte>();
            while (i < input.Length)
            {
                if (input[i] == '=' && input[i + 1] == '\r' && input[i + 2] == '\n')
                {
                    // Skip
                    i += 3;
                }
                else if (input[i] == '=')
                {
                    var sHex = input;
                    sHex = sHex.Substring(i + 1, 2);
                    var hex = Convert.ToInt32(sHex, 16);
                    var b = Convert.ToByte(hex);
                    output.Add(b);
                    i += 3;
                }
                else
                {
                    output.Add((byte)input[i]);
                    i++;
                }
            }

            return encoding.GetString(output.ToArray());
        }
    }
}
