using Telegrafer.Communication;
using Xunit;

namespace Telegrafer.Tests
{
    public class TelegramCodecTests
    {
        [Theory]
        [InlineData("P1|W1", "50 31 7C 57 31")]
        [InlineData("<STX>A<ETX>", "02 41 03")]
        [InlineData("<stx>A<etx>", "02 41 03")]
        [InlineData("<x02>A<x03>", "02 41 03")]
        [InlineData("<x2>", "02")]
        [InlineData("<x0D0A>", "0D 0A")]
        [InlineData("<XfF>", "FF")]
        [InlineData("<u2>A<u3>", "02 41 03")]
        [InlineData("a<<b", "61 3C 62")]
        [InlineData("a>b", "61 3E 62")]
        [InlineData("<CR><LF><TAB><HT><NUL><DEL><ESC><US>", "0D 0A 09 09 00 7F 1B 1F")]
        [InlineData("<n><r><t><a><b><v><f>", "0A 0D 09 07 08 0B 0C")]
        [InlineData("", "")]
        public void ParseInput_ConvertsEscapeSequencesIntoBytes(string input, string expectedHex)
        {
            var bytes = TelegramCodec.ParseInput(input, TelegramEncoding.Ascii);

            Assert.Equal(expectedHex, TelegramCodec.FormatAsHex(bytes));
        }

        [Theory]
        [InlineData("a < b")]
        [InlineData("ABC <stx")]
        [InlineData("<foo>")]
        [InlineData("<>")]
        [InlineData("<x>")]
        [InlineData("<x123>")]
        [InlineData("<x0G>")]
        [InlineData("<u55296>")]
        public void ParseInput_RejectsInvalidEscapeSequences(string input)
        {
            Assert.Throws<FormatException>(() => TelegramCodec.ParseInput(input, TelegramEncoding.Ascii));
        }

        [Fact]
        public void ParseInput_DoesNotReplaceUnencodableCharactersSilently()
        {
            var exception = Assert.Throws<FormatException>(() => TelegramCodec.ParseInput("Grüße", TelegramEncoding.Ascii));

            Assert.Contains("„ü“", exception.Message);
            Assert.Contains("Position 3", exception.Message);
        }

        [Theory]
        [InlineData(TelegramEncoding.Latin1, "ä", "E4")]
        [InlineData(TelegramEncoding.Utf8, "ä", "C3 A4")]
        [InlineData(TelegramEncoding.Utf8, "€", "E2 82 AC")]
        [InlineData(TelegramEncoding.Utf8, "<u228>", "C3 A4")]
        [InlineData(TelegramEncoding.Latin1, "<u228>", "E4")]
        [InlineData(TelegramEncoding.Ascii, "<xE4>", "E4")]
        public void ParseInput_EncodesTextWithTheSelectedEncoding(TelegramEncoding encoding, string input, string expectedHex)
        {
            Assert.Equal(expectedHex, TelegramCodec.FormatAsHex(TelegramCodec.ParseInput(input, encoding)));
        }

        [Fact]
        public void ParseInput_RejectsCharactersOutsideLatin1()
        {
            Assert.Throws<FormatException>(() => TelegramCodec.ParseInput("€", TelegramEncoding.Latin1));
        }

        [Theory]
        [InlineData("41", true, LineEnding.None, "02 41 03")]
        [InlineData("02 41 03", true, LineEnding.None, "02 41 03")]
        [InlineData("02 41", true, LineEnding.None, "02 41 03")]
        [InlineData("41 03", true, LineEnding.None, "02 41 03")]
        [InlineData("02 41 03 0D 0A", true, LineEnding.None, "02 41 03 0D 0A")]
        [InlineData("41 0D 0A", true, LineEnding.None, "02 41 0D 0A 03")]
        [InlineData("", true, LineEnding.None, "02 03")]
        [InlineData("41", true, LineEnding.CrLf, "02 41 03 0D 0A")]
        [InlineData("41", true, LineEnding.Lf, "02 41 03 0A")]
        [InlineData("41", false, LineEnding.Cr, "41 0D")]
        [InlineData("41", false, LineEnding.None, "41")]
        [InlineData("02 41 03", false, LineEnding.None, "02 41 03")]
        public void BuildTelegram_AddsOnlyTheRequestedFrameBytes(string payloadHex, bool addStxEtx, LineEnding lineEnding, string expectedHex)
        {
            var telegram = TelegramCodec.BuildTelegram(FromHex(payloadHex), addStxEtx, lineEnding);

            Assert.Equal(expectedHex, TelegramCodec.FormatAsHex(telegram));
        }

        [Fact]
        public void BuildTelegram_FramesTheExampleTelegramWithStxAndEtxOnly()
        {
            const string input = "P1|W1|IL01_OC001|PA|49|394010007385569||001||j:00894000000012275870;e:3940100073855696";

            var telegram = TelegramCodec.BuildTelegram(TelegramCodec.ParseInput(input, TelegramEncoding.Ascii), addStxEtx: true, LineEnding.None);

            Assert.Equal(input.Length + 2, telegram.Length);
            Assert.Equal(0x02, telegram[0]);
            Assert.Equal(0x03, telegram[^1]);
            Assert.Equal(input, System.Text.Encoding.ASCII.GetString(telegram, 1, input.Length));
        }

        [Theory]
        [InlineData("02 41 03", TelegramEncoding.Ascii, "<STX>A<ETX>")]
        [InlineData("41 0D 0A", TelegramEncoding.Ascii, "A<CR><LF>")]
        [InlineData("00 09 1B 7F", TelegramEncoding.Ascii, "<NUL><TAB><ESC><DEL>")]
        [InlineData("41 E4 42", TelegramEncoding.Ascii, "A<xE4>B")]
        [InlineData("41 E4 42", TelegramEncoding.Latin1, "AäB")]
        [InlineData("41 A0 AD", TelegramEncoding.Latin1, "A<xA0><xAD>")]
        [InlineData("C3 A4", TelegramEncoding.Utf8, "ä")]
        [InlineData("C3", TelegramEncoding.Utf8, "<xC3>")]
        [InlineData("E4", TelegramEncoding.Utf8, "<xE4>")]
        [InlineData("3C 53 54 58 3E", TelegramEncoding.Ascii, "<STX>")]
        public void FormatForDisplay_ShowsControlCharactersReadable(string hex, TelegramEncoding encoding, string expected)
        {
            Assert.Equal(expected, TelegramCodec.FormatForDisplay(FromHex(hex), encoding));
        }

        [Fact]
        public void FormatForDisplay_ReportsOnlyRealControlCharactersAsRanges()
        {
            // Real STX byte, then the literal text "<STX>", then a real ETX byte.
            var ranges = new List<TextRange>();
            var text = TelegramCodec.FormatForDisplay(FromHex("02 3C 53 54 58 3E 03"), TelegramEncoding.Ascii, ranges);

            Assert.Equal("<STX><STX><ETX>", text);
            Assert.Equal(2, ranges.Count);
            Assert.Equal((0, 5), (ranges[0].Start, ranges[0].Length));
            Assert.Equal((10, 5), (ranges[1].Start, ranges[1].Length));
        }

        [Theory]
        [InlineData(TelegramEncoding.Ascii)]
        [InlineData(TelegramEncoding.Latin1)]
        [InlineData(TelegramEncoding.Utf8)]
        public void FormatAsInput_IsTheInverseOfParseInput(TelegramEncoding encoding)
        {
            var random = new Random(4711);
            var samples = new List<byte[]> { Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), FromHex("3C 3C 3E 3C") };

            for (int i = 0; i < 200; i++)
            {
                var sample = new byte[random.Next(0, 40)];
                random.NextBytes(sample);
                samples.Add(sample);
            }

            samples.Add(System.Text.Encoding.UTF8.GetBytes("Grüße <STX> 100 € ✓"));

            foreach (var sample in samples)
            {
                var input = TelegramCodec.FormatAsInput(sample, encoding);

                Assert.Equal(sample, TelegramCodec.ParseInput(input, encoding));
            }
        }

        [Theory]
        [InlineData("02 41 03", "41")]
        [InlineData("02 41 03 0D 0A", "41")]
        [InlineData("41", "41")]
        [InlineData("02 03", "")]
        public void StripStxEtx_RemovesTheFrameBytes(string hex, string expectedHex)
        {
            Assert.Equal(expectedHex, TelegramCodec.FormatAsHex(TelegramCodec.StripStxEtx(FromHex(hex))));
        }

        internal static byte[] FromHex(string hex)
        {
            return hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();
        }
    }
}
