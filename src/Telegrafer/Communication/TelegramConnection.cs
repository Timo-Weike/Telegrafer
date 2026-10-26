using System.Net;
using System.Net.Sockets;

namespace Telegrafer.Communication
{
    /// <summary>
    /// State of a connection as shown in the user interface.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,

        /// <summary>The connection could not be established.</summary>
        Failed,

        /// <summary>The connection was closed by the peer or by a network error.</summary>
        Lost,
    }

    public enum ConnectionCloseReason
    {
        /// <summary>The connection was closed locally.</summary>
        Disconnected,

        /// <summary>The peer closed the connection.</summary>
        RemoteClosed,

        /// <summary>The connection was interrupted by an error.</summary>
        ConnectionLost,
    }

    public sealed class TelegramReceivedEventArgs : EventArgs
    {
        public TelegramReceivedEventArgs(ReceivedChunk chunk, DateTime timestamp)
        {
            this.Chunk = chunk;
            this.Timestamp = timestamp;
        }

        public ReceivedChunk Chunk { get; }

        /// <summary>
        /// Gets the local time at which the last byte of the chunk was received.
        /// </summary>
        public DateTime Timestamp { get; }
    }

    public sealed class ConnectionClosedEventArgs : EventArgs
    {
        public ConnectionClosedEventArgs(ConnectionCloseReason reason, Exception? error)
        {
            this.Reason = reason;
            this.Error = error;
        }

        public ConnectionCloseReason Reason { get; }

        public Exception? Error { get; }
    }

    /// <summary>
    /// TCP client connection that writes telegrams unchanged to the stream and splits the received stream into
    /// telegrams with a <see cref="TelegramFramer"/>.
    /// </summary>
    /// <remarks>
    /// An instance is used for exactly one connection. The events are raised on thread pool threads in the order
    /// in which the data was received. <see cref="Closed"/> is raised exactly once after a successful
    /// <see cref="ConnectAsync"/>. Event handlers must not block.
    /// </remarks>
    public sealed class TelegramConnection : IAsyncDisposable
    {
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

        public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        // Bytes outside of an STX/ETX frame are shown after this time without new data.
        private static readonly TimeSpan DataIdleTimeout = TimeSpan.FromMilliseconds(100);

        // A frame without ETX is shown as incomplete after this time without new data.
        private static readonly TimeSpan IncompleteFrameTimeout = TimeSpan.FromSeconds(2);

        private readonly object receiveLock = new();
        private readonly TelegramFramer framer = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly CancellationTokenSource lifetime = new();
        private TcpClient? client;
        private NetworkStream? stream;
        private Timer? flushTimer;
        private Task? receiveTask;
        private Exception? abortError;
        private DateTime lastReceiveTime;
        private bool receiveCompleted;
        private volatile bool closeRequested;
        private int connectStarted;
        private int disposed;

        public TelegramConnection(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Der Host darf nicht leer sein.", nameof(host));
            }

            if (port < IPEndPoint.MinPort + 1 || port > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Der Port muss zwischen 1 und 65535 liegen.");
            }

            this.Host = host;
            this.Port = port;
        }

        public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;

        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public string Host { get; }

        public int Port { get; }

        /// <summary>
        /// Gets the resolved address of the peer after the connection has been established.
        /// </summary>
        public IPEndPoint? RemoteEndPoint { get; private set; }

        /// <exception cref="TimeoutException">The peer did not answer within <paramref name="timeout"/>.</exception>
        /// <exception cref="SocketException">The connection could not be established.</exception>
        /// <exception cref="OperationCanceledException">The connection attempt was canceled.</exception>
        public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref this.connectStarted, 1) != 0)
            {
                throw new InvalidOperationException("Eine Verbindung kann nur einmal aufgebaut werden.");
            }

            var tcpClient = new TcpClient();

            try
            {
                using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token))
                {
                    timeoutSource.CancelAfter(timeout);

                    try
                    {
                        await tcpClient.ConnectAsync(this.Host, this.Port, timeoutSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !this.lifetime.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"Keine Antwort von {this.Host}:{this.Port} innerhalb von {timeout.TotalSeconds:0} Sekunden.");
                    }
                }

                // Telegrams are usually small; they are sent immediately instead of being delayed by the Nagle algorithm.
                tcpClient.NoDelay = true;

                this.RemoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                this.client = tcpClient;
                this.stream = tcpClient.GetStream();
                this.flushTimer = new Timer(this.OnFlushTimer);
                this.receiveTask = Task.Run(this.ReceiveLoopAsync);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Writes the bytes unchanged to the TCP stream.
        /// </summary>
        /// <exception cref="TimeoutException">The bytes could not be sent in time; the connection is closed.</exception>
        /// <exception cref="IOException">The connection was interrupted; the connection is closed.</exception>
        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            var networkStream = this.stream ?? throw new InvalidOperationException("Die Verbindung ist nicht hergestellt.");

            await this.sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token))
                {
                    timeoutSource.CancelAfter(SendTimeout);
                    await networkStream.WriteAsync(data, timeoutSource.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !this.lifetime.IsCancellationRequested)
            {
                var timeoutException = new TimeoutException(
                    $"Die Daten konnten nicht innerhalb von {SendTimeout.TotalSeconds:0} Sekunden gesendet werden.");
                this.Abort(timeoutException);
                throw timeoutException;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                if (!this.closeRequested)
                {
                    this.Abort(ex);
                }

                throw;
            }
            finally
            {
                this.sendLock.Release();
            }
        }

        /// <summary>
        /// Closes the connection and waits until the receive loop has finished.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (Volatile.Read(ref this.disposed) != 0)
            {
                return;
            }

            this.closeRequested = true;
            this.lifetime.Cancel();
            this.DisposeSocket();

            var task = this.receiveTask;

            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref this.disposed) != 0)
            {
                return;
            }

            await this.DisconnectAsync().ConfigureAwait(false);

            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            // A running send releases the lock before the lock and the token source are disposed.
            await this.sendLock.WaitAsync().ConfigureAwait(false);
            this.sendLock.Dispose();
            this.lifetime.Dispose();
        }

        private async Task ReceiveLoopAsync()
        {
            var reason = ConnectionCloseReason.RemoteClosed;
            Exception? error = null;
            var buffer = new byte[16 * 1024];

            try
            {
                while (true)
                {
                    var read = await this.stream!.ReadAsync(buffer.AsMemory(), this.lifetime.Token).ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    this.ProcessReceived(buffer.AsSpan(0, read));
                }
            }
            catch (Exception ex)
            {
                reason = ConnectionCloseReason.ConnectionLost;
                error = Volatile.Read(ref this.abortError) ?? ex;
            }

            if (this.closeRequested && Volatile.Read(ref this.abortError) is null)
            {
                reason = ConnectionCloseReason.Disconnected;
                error = null;
            }

            lock (this.receiveLock)
            {
                this.receiveCompleted = true;
                this.flushTimer?.Dispose();

                var rest = this.framer.Flush();

                if (rest is not null)
                {
                    this.TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(rest, this.lastReceiveTime));
                }
            }

            this.DisposeSocket();
            this.Closed?.Invoke(this, new ConnectionClosedEventArgs(reason, error));
        }

        private void ProcessReceived(ReadOnlySpan<byte> data)
        {
            lock (this.receiveLock)
            {
                this.lastReceiveTime = DateTime.Now;

                // Raised inside the lock so that the order of the chunks is kept when the flush timer fires.
                foreach (var chunk in this.framer.Append(data))
                {
                    this.TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(chunk, this.lastReceiveTime));
                }

                if (this.framer.HasPendingData)
                {
                    var dueTime = this.framer.IsInsideFrame ? IncompleteFrameTimeout : DataIdleTimeout;
                    this.flushTimer!.Change(dueTime, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    this.flushTimer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void OnFlushTimer(object? state)
        {
            lock (this.receiveLock)
            {
                if (this.receiveCompleted)
                {
                    return;
                }

                var chunk = this.framer.Flush();

                if (chunk is not null)
                {
                    this.TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(chunk, this.lastReceiveTime));
                }
            }
        }

        private void Abort(Exception error)
        {
            Interlocked.CompareExchange(ref this.abortError, error, null);
            this.DisposeSocket();
        }

        private void DisposeSocket()
        {
            // Closing the socket ends a pending read of the receive loop.
            this.stream?.Dispose();
            this.client?.Dispose();
        }
    }
}
