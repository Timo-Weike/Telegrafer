using System.Globalization;
using System.Net;

namespace Telegrafer.Utils
{
    /// <summary>
    /// Parses and formats addresses like "192.168.1.10:5000", "plc-01:5000" or "[fe80::1]:5000".
    /// </summary>
    public static class HostAndPort
    {
        public static bool TryParse(string? text, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var value = text.Trim();
            string hostPart;
            string portPart;

            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                var end = value.IndexOf("]:", StringComparison.Ordinal);

                if (end < 0)
                {
                    return false;
                }

                hostPart = value.Substring(1, end - 1);
                portPart = value.Substring(end + 2);
            }
            else
            {
                var separator = value.LastIndexOf(':');

                if (separator < 0)
                {
                    return false;
                }

                hostPart = value.Substring(0, separator);
                portPart = value.Substring(separator + 1);

                // An IPv6 address must be written in brackets, otherwise the port cannot be identified.
                if (hostPart.Contains(':'))
                {
                    return false;
                }
            }

            if (!IsValidHost(hostPart) || !TryParsePort(portPart, out port))
            {
                return false;
            }

            host = hostPart;
            return true;
        }

        public static bool TryParsePort(string? text, out int port)
        {
            return int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && port >= 1
                && port <= IPEndPoint.MaxPort;
        }

        public static bool IsValidHost(string? host)
        {
            return !string.IsNullOrWhiteSpace(host) && !host.Any(char.IsWhiteSpace);
        }

        public static string Format(string host, int port)
        {
            return host.Contains(':')
                ? $"[{host}]:{port.ToString(CultureInfo.InvariantCulture)}"
                : $"{host}:{port.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
