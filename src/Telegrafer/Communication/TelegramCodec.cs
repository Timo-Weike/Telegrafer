using System.Buffers;
using System.Globalization;
using System.Text;

namespace Telegrafer.Communication
{
    /// <summary>
    /// Line ending that is appended to a telegram before it is sent.
    /// </summary>
    public enum LineEnding
    {
        None,
        Cr,
        Lf,
        CrLf,
    }

    /// <summary>
    /// Character encoding that converts between the text of a telegram and the transmitted bytes.
    /// </summary>
    public enum TelegramEncoding
    {
        Ascii,
        Latin1,
        Utf8,
    }

    /// <summary>
    /// Range of a formatted telegram text that represents a control character or a raw byte, e.g. "&lt;STX&gt;".
    /// </summary>
    public readonly struct TextRange
    {
        public TextRange(int start, int length)
        {
            this.Start = start;
            this.Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }

    /// <summary>
    /// Converts between the telegram text that is entered or displayed and the bytes on the TCP stream.
    /// </summary>
    /// <remarks>
    /// Escape sequences of the input (names are case-insensitive):
    /// <list type="bullet">
    /// <item>&lt;STX&gt;, &lt;ETX&gt;, &lt;CR&gt;, &lt;LF&gt;, … – the ASCII control characters NUL … US and DEL as one byte</item>
    /// <item>&lt;xHH&gt; – one byte with the hexadecimal value HH, &lt;xHHHH…&gt; – several bytes</item>
    /// <item>&lt;uNNN&gt; – the Unicode character with the decimal code point NNN, encoded with the selected encoding</item>
    /// <item>&lt;n&gt;, &lt;r&gt;, &lt;t&gt;, &lt;a&gt;, &lt;b&gt;, &lt;v&gt;, &lt;f&gt; – short forms of LF, CR, TAB, BEL, BS, VT and FF</item>
    /// <item>&lt;&lt; – the character "&lt;"</item>
    /// </list>
    /// All other characters are encoded with the selected encoding. Nothing else is added to the payload.
    /// </remarks>
    public static class TelegramCodec
    {
        public const byte Stx = 0x02;
        public const byte Etx = 0x03;
        public const byte Lf = 0x0A;
        public const byte Cr = 0x0D;

        private static readonly string[] ControlCharacterNames =
        {
            "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "TAB", "LF", "VT", "FF", "CR", "SO", "SI",
            "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US",
        };

        private static readonly Dictionary<string, byte> EscapeNames = CreateEscapeNames();

        private static readonly Encoding StrictAscii =
            Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private static readonly Encoding StrictLatin1 =
            Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private static readonly byte[] CrBytes = { Cr };
        private static readonly byte[] LfBytes = { Lf };
        private static readonly byte[] CrLfBytes = { Cr, Lf };

        /// <summary>
        /// Converts the entered telegram text into the payload bytes.
        /// </summary>
        /// <exception cref="FormatException">
        /// The text contains an invalid escape sequence or a character that cannot be represented with the encoding.
        /// </exception>
        public static byte[] ParseInput(string input, TelegramEncoding encoding)
        {
            var bytes = new List<byte>(input.Length + 4);
            var text = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];

                if (c != '<')
                {
                    if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                    {
                        EnsureEncodable(char.ConvertToUtf32(c, input[i + 1]), i, encoding);
                        text.Append(c).Append(input[i + 1]);
                        i++;
                    }
                    else
                    {
                        EnsureEncodable(c, i, encoding);
                        text.Append(c);
                    }

                    continue;
                }

                if (i + 1 < input.Length && input[i + 1] == '<')
                {
                    text.Append('<');
                    i++;
                    continue;
                }

                var end = input.IndexOf('>', i + 1);

                if (end < 0)
                {
                    throw new FormatException(
                        $"Das Zeichen „<“ an Position {i + 1} beginnt eine Escape-Sequenz, die nicht mit „>“ abgeschlossen wird. "
                        + "Für das Zeichen „<“ selbst bitte „<<“ eingeben.");
                }

                var name = input.Substring(i + 1, end - i - 1);

                if (EscapeNames.TryGetValue(name, out var controlByte))
                {
                    FlushText(text, bytes, encoding);
                    bytes.Add(controlByte);
                }
                else if (TryParseHexBytes(name, i, out var hexBytes))
                {
                    FlushText(text, bytes, encoding);
                    bytes.AddRange(hexBytes);
                }
                else if (TryParseCodePoint(name, out var codePoint))
                {
                    EnsureEncodable(codePoint, i, encoding);
                    text.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    throw new FormatException(
                        $"Unbekannte Escape-Sequenz „<{name}>“ an Position {i + 1}. "
                        + "Erlaubt sind z. B. <STX>, <ETX>, <CR>, <LF>, <xHH> und << für das Zeichen „<“.");
                }

                i = end;
            }

            FlushText(text, bytes, encoding);

            return bytes.ToArray();
        }

        /// <summary>
        /// Builds the bytes that are written to the TCP stream: [STX] payload [ETX] [line ending].
        /// </summary>
        /// <remarks>
        /// STX is not added if the payload already starts with STX. ETX is not added if the payload already ends
        /// with ETX; CR and LF bytes behind that ETX are ignored for this check. No other bytes are added.
        /// </remarks>
        public static byte[] BuildTelegram(ReadOnlySpan<byte> payload, bool addStxEtx, LineEnding lineEnding)
        {
            var prependStx = addStxEtx && !(payload.Length > 0 && payload[0] == Stx);
            var appendEtx = addStxEtx && !EndsWithEtx(payload);
            var ending = GetLineEndingBytes(lineEnding);

            var result = new byte[(prependStx ? 1 : 0) + payload.Length + (appendEtx ? 1 : 0) + ending.Length];
            var position = 0;

            if (prependStx)
            {
                result[position++] = Stx;
            }

            payload.CopyTo(result.AsSpan(position));
            position += payload.Length;

            if (appendEtx)
            {
                result[position++] = Etx;
            }

            ending.CopyTo(result.AsSpan(position));

            return result;
        }

        /// <summary>
        /// Formats bytes for the display. Control characters are shown as names like "&lt;STX&gt;" and bytes that
        /// cannot be shown as a character as "&lt;xHH&gt;". The bytes themselves are not changed.
        /// </summary>
        /// <param name="controlRanges">Receives the text ranges of all control characters and raw bytes.</param>
        public static string FormatForDisplay(ReadOnlySpan<byte> data, TelegramEncoding encoding, List<TextRange>? controlRanges = null)
        {
            return Format(data, encoding, forInput: false, controlRanges);
        }

        /// <summary>
        /// Formats bytes as input text that <see cref="ParseInput"/> converts back into exactly the same bytes.
        /// </summary>
        public static string FormatAsInput(ReadOnlySpan<byte> data, TelegramEncoding encoding)
        {
            return Format(data, encoding, forInput: true, controlRanges: null);
        }

        /// <summary>
        /// Formats bytes as hexadecimal values separated by spaces, e.g. "02 41 03".
        /// </summary>
        public static string FormatAsHex(ReadOnlySpan<byte> data)
        {
            var builder = new StringBuilder(data.Length * 3);

            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Removes a leading STX and a trailing ETX (ignoring CR and LF behind it) from the bytes.
        /// </summary>
        public static ReadOnlySpan<byte> StripStxEtx(ReadOnlySpan<byte> data)
        {
            if (data.Length > 0 && data[0] == Stx)
            {
                data = data.Slice(1);
            }

            var end = data.Length;

            while (end > 0 && (data[end - 1] == Cr || data[end - 1] == Lf))
            {
                end--;
            }

            if (end > 0 && data[end - 1] == Etx)
            {
                data = data.Slice(0, end - 1);
            }

            return data;
        }

        public static ReadOnlySpan<byte> GetLineEndingBytes(LineEnding lineEnding)
        {
            switch (lineEnding)
            {
                case LineEnding.Cr:
                    return CrBytes;
                case LineEnding.Lf:
                    return LfBytes;
                case LineEnding.CrLf:
                    return CrLfBytes;
                default:
                    return ReadOnlySpan<byte>.Empty;
            }
        }

        public static string GetDisplayName(TelegramEncoding encoding)
        {
            switch (encoding)
            {
                case TelegramEncoding.Latin1:
                    return "ISO-8859-1";
                case TelegramEncoding.Utf8:
                    return "UTF-8";
                default:
                    return "ASCII";
            }
        }

        private static Encoding GetEncoding(TelegramEncoding encoding)
        {
            switch (encoding)
            {
                case TelegramEncoding.Latin1:
                    return StrictLatin1;
                case TelegramEncoding.Utf8:
                    return StrictUtf8;
                default:
                    return StrictAscii;
            }
        }

        private static string Format(ReadOnlySpan<byte> data, TelegramEncoding encoding, bool forInput, List<TextRange>? controlRanges)
        {
            var builder = new StringBuilder(data.Length + 16);
            var i = 0;

            while (i < data.Length)
            {
                var b = data[i];

                if (b < 0x20)
                {
                    AppendToken(builder, ControlCharacterNames[b], controlRanges);
                    i++;
                }
                else if (b == 0x7F)
                {
                    AppendToken(builder, "DEL", controlRanges);
                    i++;
                }
                else if (b < 0x80)
                {
                    if (forInput && b == '<')
                    {
                        builder.Append("<<");
                    }
                    else
                    {
                        builder.Append((char)b);
                    }

                    i++;
                }
                else if (encoding == TelegramEncoding.Latin1 && IsVisible(b))
                {
                    builder.Append((char)b);
                    i++;
                }
                else if (encoding == TelegramEncoding.Utf8
                    && Rune.DecodeFromUtf8(data.Slice(i), out var rune, out var consumed) == OperationStatus.Done
                    && IsVisible(rune.Value))
                {
                    builder.Append(rune.ToString());
                    i += consumed;
                }
                else
                {
                    AppendToken(builder, "x" + b.ToString("X2", CultureInfo.InvariantCulture), controlRanges);
                    i++;
                }
            }

            return builder.ToString();
        }

        private static void AppendToken(StringBuilder builder, string name, List<TextRange>? controlRanges)
        {
            controlRanges?.Add(new TextRange(builder.Length, name.Length + 2));
            builder.Append('<').Append(name).Append('>');
        }

        private static bool IsVisible(int codePoint)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return false;
                default:
                    return true;
            }
        }

        private static bool EndsWithEtx(ReadOnlySpan<byte> payload)
        {
            var end = payload.Length;

            while (end > 0 && (payload[end - 1] == Cr || payload[end - 1] == Lf))
            {
                end--;
            }

            return end > 0 && payload[end - 1] == Etx;
        }

        private static void FlushText(StringBuilder text, List<byte> bytes, TelegramEncoding encoding)
        {
            if (text.Length == 0)
            {
                return;
            }

            bytes.AddRange(GetEncoding(encoding).GetBytes(text.ToString()));
            text.Clear();
        }

        private static void EnsureEncodable(int codePoint, int index, TelegramEncoding encoding)
        {
            bool encodable;

            switch (encoding)
            {
                case TelegramEncoding.Ascii:
                    encodable = codePoint <= 0x7F;
                    break;
                case TelegramEncoding.Latin1:
                    encodable = codePoint <= 0xFF;
                    break;
                default:
                    encodable = !(codePoint >= 0xD800 && codePoint <= 0xDFFF);
                    break;
            }

            if (!encodable)
            {
                var character = codePoint >= 0xD800 && codePoint <= 0xDFFF
                    ? $"U+{codePoint:X4}"
                    : char.ConvertFromUtf32(codePoint);

                throw new FormatException(
                    $"Das Zeichen „{character}“ an Position {index + 1} kann mit der Zeichenkodierung {GetDisplayName(encoding)} "
                    + "nicht übertragen werden. Bitte eine andere Zeichenkodierung wählen oder das Byte als <xHH> eingeben.");
            }
        }

        private static bool TryParseHexBytes(string name, int position, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (name.Length < 2 || (name[0] != 'x' && name[0] != 'X'))
            {
                return false;
            }

            var digits = name.Substring(1);

            foreach (var digit in digits)
            {
                if (!Uri.IsHexDigit(digit))
                {
                    return false;
                }
            }

            if (digits.Length > 2 && digits.Length % 2 != 0)
            {
                throw new FormatException(
                    $"Die Escape-Sequenz „<{name}>“ an Position {position + 1} enthält eine ungerade Anzahl Hex-Ziffern. "
                    + "Mehrere Bytes werden mit je zwei Ziffern angegeben, z. B. <x0D0A>.");
            }

            if (digits.Length <= 2)
            {
                bytes = new[] { byte.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                return true;
            }

            bytes = new byte[digits.Length / 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(digits.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return true;
        }

        private static bool TryParseCodePoint(string name, out int codePoint)
        {
            codePoint = 0;

            if (name.Length < 2 || name.Length > 8 || (name[0] != 'u' && name[0] != 'U'))
            {
                return false;
            }

            if (!int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out codePoint))
            {
                return false;
            }

            return codePoint <= 0x10FFFF && !(codePoint >= 0xD800 && codePoint <= 0xDFFF);
        }

        private static Dictionary<string, byte> CreateEscapeNames()
        {
            var names = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < ControlCharacterNames.Length; i++)
            {
                names.Add(ControlCharacterNames[i], (byte)i);
            }

            names.Add("DEL", 0x7F);
            names.Add("HT", 0x09);

            // Short forms that were supported by the first versions of the payload input.
            names.Add("n", Lf);
            names.Add("r", Cr);
            names.Add("t", 0x09);
            names.Add("a", 0x07);
            names.Add("b", 0x08);
            names.Add("v", 0x0B);
            names.Add("f", 0x0C);

            return names;
        }
    }
}
