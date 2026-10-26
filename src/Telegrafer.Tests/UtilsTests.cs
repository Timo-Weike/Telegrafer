using Telegrafer.Utils;
using Xunit;

namespace Telegrafer.Tests
{
    public class UtilsTests
    {
        [Theory]
        [InlineData("192.168.1.10:5000", "192.168.1.10", 5000)]
        [InlineData("  plc-01:4000 ", "plc-01", 4000)]
        [InlineData("localhost:1", "localhost", 1)]
        [InlineData("[fe80::1]:65535", "fe80::1", 65535)]
        public void HostAndPort_ParsesValidAddresses(string text, string expectedHost, int expectedPort)
        {
            Assert.True(HostAndPort.TryParse(text, out var host, out var port));
            Assert.Equal(expectedHost, host);
            Assert.Equal(expectedPort, port);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("192.168.1.10")]
        [InlineData("192.168.1.10:")]
        [InlineData(":5000")]
        [InlineData("host:0")]
        [InlineData("host:65536")]
        [InlineData("host:-1")]
        [InlineData("host:50a")]
        [InlineData("fe80::1:5000")]
        [InlineData("my host:5000")]
        public void HostAndPort_RejectsInvalidAddresses(string? text)
        {
            Assert.False(HostAndPort.TryParse(text, out _, out _));
        }

        [Theory]
        [InlineData("192.168.1.10", 5000, "192.168.1.10:5000")]
        [InlineData("fe80::1", 5000, "[fe80::1]:5000")]
        public void HostAndPort_FormatsAddresses(string host, int port, string expected)
        {
            Assert.Equal(expected, HostAndPort.Format(host, port));
        }

        [Fact]
        public void NaturalStringComparer_SortsNumbersByValue()
        {
            var names = new[] { "Steuerung 10", "steuerung 2", "Lift", "Steuerung 1", "Shuttle 02", "Shuttle 1" };

            var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();

            Assert.Equal(new[] { "Lift", "Shuttle 1", "Shuttle 02", "Steuerung 1", "steuerung 2", "Steuerung 10" }, sorted);
        }
    }
}
