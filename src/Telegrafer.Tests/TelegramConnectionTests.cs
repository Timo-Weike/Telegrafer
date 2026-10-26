using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Telegrafer.Communication;
using Xunit;
using static Telegrafer.Tests.TelegramCodecTests;

namespace Telegrafer.Tests
{
    public sealed class TelegramConnectionTests : IDisposable
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

        private readonly TcpListener listener;

        public TelegramConnectionTests()
        {
            this.listener = new TcpListener(IPAddress.Loopback, 0);
            this.listener.Start();
        }

        private int Port => ((IPEndPoint)this.listener.LocalEndpoint).Port;

        public void Dispose()
        {
            this.listener.Stop();
        }

        [Fact]
        public async Task SendAsync_WritesExactlyTheGivenBytes()
        {
            await using var connection = new TelegramConnection("127.0.0.1", this.Port);
            await connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None);
            using var server = await this.listener.AcceptTcpClientAsync();

            var telegram = TelegramCodec.BuildTelegram(TelegramCodec.ParseInput("P1|W1|PA|49", TelegramEncoding.Ascii), true, LineEnding.None);
            await connection.SendAsync(telegram, CancellationToken.None);
            await connection.DisconnectAsync();

            using var received = new MemoryStream();
            await server.GetStream().CopyToAsync(received);

            Assert.Equal("02 50 31 7C 57 31 7C 50 41 7C 34 39 03", TelegramCodec.FormatAsHex(received.ToArray()));
        }

        [Fact]
        public async Task Receive_SplitsTheStreamIntoTelegrams()
        {
            var received = new BlockingCollection<ReceivedChunk>();
            await using var connection = new TelegramConnection("127.0.0.1", this.Port);
            connection.TelegramReceived += (_, e) => received.Add(e.Chunk);
            await connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None);
            using var server = await this.listener.AcceptTcpClientAsync();
            server.NoDelay = true;
            var stream = server.GetStream();

            await stream.WriteAsync(FromHex("02 41 03 02 42 03 02 43"));
            await Task.Delay(50);
            await stream.WriteAsync(FromHex("44 03"));

            Assert.Equal("02 41 03", Take(received));
            Assert.Equal("02 42 03", Take(received));
            Assert.Equal("02 43 44 03", Take(received));
        }

        [Fact]
        public async Task Receive_EmitsUnframedDataAfterAShortPause()
        {
            var received = new BlockingCollection<ReceivedChunk>();
            await using var connection = new TelegramConnection("127.0.0.1", this.Port);
            connection.TelegramReceived += (_, e) => received.Add(e.Chunk);
            await connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None);
            using var server = await this.listener.AcceptTcpClientAsync();

            await server.GetStream().WriteAsync(FromHex("06"));

            Assert.True(received.TryTake(out var chunk, WaitTimeout));
            Assert.Equal(ReceivedChunkKind.Data, chunk!.Kind);
            Assert.Equal("06", TelegramCodec.FormatAsHex(chunk.Data));
        }

        [Fact]
        public async Task Closed_ReportsThatThePeerClosedTheConnection()
        {
            var closed = new TaskCompletionSource<ConnectionClosedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var connection = new TelegramConnection("127.0.0.1", this.Port);
            connection.Closed += (_, e) => closed.TrySetResult(e);
            await connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None);

            using (var server = await this.listener.AcceptTcpClientAsync())
            {
                server.Close();
            }

            var args = await closed.Task.WaitAsync(WaitTimeout);
            Assert.NotEqual(ConnectionCloseReason.Disconnected, args.Reason);
        }

        [Fact]
        public async Task Closed_ReportsALocalDisconnect()
        {
            var closed = new TaskCompletionSource<ConnectionClosedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var connection = new TelegramConnection("127.0.0.1", this.Port);
            connection.Closed += (_, e) => closed.TrySetResult(e);
            await connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None);
            using var server = await this.listener.AcceptTcpClientAsync();

            await connection.DisconnectAsync();

            var args = await closed.Task.WaitAsync(WaitTimeout);
            Assert.Equal(ConnectionCloseReason.Disconnected, args.Reason);
            Assert.Null(args.Error);
        }

        [Fact]
        public async Task ConnectAsync_FailsWhenNobodyListens()
        {
            var port = this.Port;
            this.listener.Stop();

            await using var connection = new TelegramConnection("127.0.0.1", port);

            var exception = await Assert.ThrowsAsync<SocketException>(
                () => connection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, CancellationToken.None));
            Assert.Equal(SocketError.ConnectionRefused, exception.SocketErrorCode);
        }

        [Fact]
        public async Task ConnectAsync_CanBeCanceled()
        {
            // 192.0.2.1 is reserved for documentation (RFC 5737) and is normally not reachable.
            await using var connection = new TelegramConnection("192.0.2.1", 5000);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var exception = await Record.ExceptionAsync(
                () => connection.ConnectAsync(TimeSpan.FromSeconds(30), cancellation.Token));

            // Depending on the network the attempt is canceled or rejected right away; it must not hang.
            Assert.True(exception is OperationCanceledException || exception is SocketException, exception?.ToString());
        }

        private static string Take(BlockingCollection<ReceivedChunk> received)
        {
            Assert.True(received.TryTake(out var chunk, WaitTimeout), "No telegram received.");
            return TelegramCodec.FormatAsHex(chunk!.Data);
        }
    }
}
