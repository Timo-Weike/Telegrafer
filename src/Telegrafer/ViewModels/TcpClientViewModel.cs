using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Telegrafer.Communication;
using Telegrafer.Utils;

namespace Telegrafer.ViewModels
{
    /// <summary>
    /// Connection, telegram history and send options of a client tab.
    /// </summary>
    [DataContract]
    public class TcpClientViewModel : TabViewModelBase
    {
        private readonly ConcurrentQueue<TelegramReceivedEventArgs> receivedQueue = new();
        private TelegramConnection? connection;
        private CancellationTokenSource? connectCancellation;
        private Task? connectionDisposal;
        private int drainScheduled;
        private DateTime? lastSentAt;

        public TcpClientViewModel()
        {
            this.SendCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.SendAsync();
                }, this.WhenAnyValue(x => x.CanSend));
            this.DisconnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.Disconnect(true);
                }, this.WhenAnyValue(x => x.Connected));
            this.CancelConnectCommand = ReactiveCommand.Create(
                () =>
                {
                    this.connectCancellation?.Cancel();
                }, this.WhenAnyValue(x => x.IsConnecting));
            this.ClearCommand = ReactiveCommand.Create(
                () =>
                {
                    this.Log.Clear();
                });
            this.ExportCommand = ReactiveCommand.CreateFromTask(this.ExportAsync);
            this.TakeOverCommand = ReactiveCommand.Create<TelegramLogEntry>(this.TakeOver);

            this.WhenAnyValue(x => x.State, x => x.Payload)
                .Subscribe(t =>
                {
                    var (state, payload) = t;

                    this.CanSend = state == ConnectionState.Connected
                        && !string.IsNullOrEmpty(payload);
                });
        }

        private ConnectionState state;

        public ConnectionState State
        {
            get { return state; }
            private set
            {
                this.RaiseAndSetIfChanged(ref state, value);
                this.RaisePropertyChanged(nameof(Connected));
                this.RaisePropertyChanged(nameof(IsConnecting));
            }
        }

        public bool Connected => this.State == ConnectionState.Connected;

        public bool IsConnecting => this.State == ConnectionState.Connecting;

        private string? stateDetail;

        /// <summary>
        /// Gets the reason of the last connection error.
        /// </summary>
        public string? StateDetail
        {
            get { return stateDetail; }
            private set { this.RaiseAndSetIfChanged(ref stateDetail, value); }
        }

        private bool canSend;

        public bool CanSend
        {
            get { return canSend; }
            set { this.RaiseAndSetIfChanged(ref canSend, value); }
        }

        public TelegramLog Log { get; } = new();

        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand CancelConnectCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand TakeOverCommand { get; }

        private string payload = string.Empty;

        [DataMember]
        public string Payload
        {
            get { return payload; }
            set
            {
                this.RaiseAndSetIfChanged(ref payload, value ?? string.Empty);
            }
        }

        private bool addStxEtx = true;

        /// <summary>
        /// Gets or sets a value indicating whether STX (0x02) and ETX (0x03) are added to every sent telegram.
        /// </summary>
        public bool AddStxEtx
        {
            get { return addStxEtx; }
            set { this.RaiseAndSetIfChanged(ref addStxEtx, value); }
        }

        private LineEnding lineEnding;

        public LineEnding LineEnding
        {
            get { return lineEnding; }
            set
            {
                this.RaiseAndSetIfChanged(ref lineEnding, value);
                this.RaisePropertyChanged(nameof(SelectedLineEnding));
            }
        }

        public Option<LineEnding> SelectedLineEnding
        {
            get => ProtocolOptions.Find(ProtocolOptions.LineEndings, this.LineEnding);
            set
            {
                if (value is not null)
                {
                    this.LineEnding = value.Value;
                }
            }
        }

        private TelegramEncoding encoding;

        public TelegramEncoding Encoding
        {
            get { return encoding; }
            set
            {
                this.RaiseAndSetIfChanged(ref encoding, value);
                this.RaisePropertyChanged(nameof(SelectedEncoding));
            }
        }

        public Option<TelegramEncoding> SelectedEncoding
        {
            get => ProtocolOptions.Find(ProtocolOptions.Encodings, this.Encoding);
            set
            {
                if (value is not null)
                {
                    this.Encoding = value.Value;
                }
            }
        }

        public IReadOnlyList<Option<LineEnding>> LineEndings => ProtocolOptions.LineEndings;

        public IReadOnlyList<Option<TelegramEncoding>> Encodings => ProtocolOptions.Encodings;

        private bool autoScroll = true;

        public bool AutoScroll
        {
            get { return autoScroll; }
            set { this.RaiseAndSetIfChanged(ref autoScroll, value); }
        }

        private bool wordWrap = true;

        /// <summary>
        /// Gets or sets a value indicating whether long telegrams are wrapped in the history.
        /// </summary>
        public bool WordWrap
        {
            get { return wordWrap; }
            set { this.RaiseAndSetIfChanged(ref wordWrap, value); }
        }

        private int maxLogEntries = 10_000;

        public int MaxLogEntries
        {
            get { return maxLogEntries; }
            set
            {
                this.RaiseAndSetIfChanged(ref maxLogEntries, Math.Max(1, value));
                this.TrimLog();
            }
        }

        /// <summary>
        /// Gets or sets the name of the connection that is written to the history.
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the address of the connection that is written to the history.
        /// </summary>
        public string Address { get; set; } = string.Empty;

        public ObservableCollection<string> OldPayloads => OldInputs.OldPayloads;

        /// <summary>
        /// Establishes a new connection. Progress and errors are written to the history.
        /// </summary>
        public async Task<bool> ConnectAsync(string host, int port)
        {
            if (this.connection is not null || this.State == ConnectionState.Connecting)
            {
                return false;
            }

            if (this.connectionDisposal is not null)
            {
                // The previous connection is released completely before a new one is established.
                await this.connectionDisposal;
                this.connectionDisposal = null;
            }

            var newConnection = new TelegramConnection(host, port);
            newConnection.TelegramReceived += this.OnTelegramReceived;
            newConnection.Closed += this.OnConnectionClosed;

            this.connection = newConnection;
            this.connectCancellation = new CancellationTokenSource();
            this.lastSentAt = null;
            this.StateDetail = null;
            this.State = ConnectionState.Connecting;
            this.AddMessage(TelegramLogKind.Info, $"Verbindung zu {HostAndPort.Format(host, port)} wird hergestellt …");

            try
            {
                await newConnection.ConnectAsync(TelegramConnection.DefaultConnectTimeout, this.connectCancellation.Token);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(this.connection, newConnection))
                {
                    this.connection = null;
                }

                await this.DisposeConnectionAsync(newConnection);

                if (ex is OperationCanceledException)
                {
                    this.State = ConnectionState.Disconnected;
                    this.AddMessage(TelegramLogKind.Info, "Verbindungsaufbau abgebrochen.");
                }
                else
                {
                    this.StateDetail = DescribeError(ex);
                    this.State = ConnectionState.Failed;
                    this.AddMessage(TelegramLogKind.Error, "Verbindung fehlgeschlagen: " + this.StateDetail);
                }

                return false;
            }
            finally
            {
                this.connectCancellation?.Dispose();
                this.connectCancellation = null;
            }

            if (!ReferenceEquals(this.connection, newConnection))
            {
                // The connection was closed before this continuation ran.
                return false;
            }

            this.State = ConnectionState.Connected;

            // For a host name the resolved IP address is shown as well.
            var remoteAddress = newConnection.RemoteEndPoint?.Address;

            if (remoteAddress is not null && remoteAddress.IsIPv4MappedToIPv6)
            {
                remoteAddress = remoteAddress.MapToIPv4();
            }

            this.AddMessage(
                TelegramLogKind.Info,
                remoteAddress is not null && !IPAddress.TryParse(host, out _)
                    ? $"Verbunden mit {HostAndPort.Format(host, port)} ({HostAndPort.Format(remoteAddress.ToString(), port)})."
                    : $"Verbunden mit {HostAndPort.Format(host, port)}.");

            return true;
        }

        /// <summary>
        /// Closes the connection.
        /// </summary>
        /// <returns><see langword="true"/> if a connection was closed.</returns>
        public async Task<bool> Disconnect(bool userRequested)
        {
            if (this.State == ConnectionState.Connecting)
            {
                this.connectCancellation?.Cancel();
                return true;
            }

            var activeConnection = this.connection;

            if (activeConnection is null)
            {
                return false;
            }

            this.connection = null;
            await this.DisposeConnectionAsync(activeConnection);

            this.DrainReceived();
            this.State = ConnectionState.Disconnected;
            this.AddMessage(TelegramLogKind.Info, userRequested ? "Verbindung getrennt." : "Verbindung geschlossen.");

            return true;
        }

        /// <summary>
        /// Writes an error message to the history.
        /// </summary>
        public void ReportError(string message)
        {
            this.AddMessage(TelegramLogKind.Error, message);
        }

        private async Task SendAsync()
        {
            var activeConnection = this.connection;
            var input = this.Payload;

            if (activeConnection is null || this.State != ConnectionState.Connected || string.IsNullOrEmpty(input))
            {
                return;
            }

            byte[] telegram;

            try
            {
                telegram = TelegramCodec.BuildTelegram(TelegramCodec.ParseInput(input, this.Encoding), this.AddStxEtx, this.LineEnding);
            }
            catch (FormatException ex)
            {
                this.AddMessage(TelegramLogKind.Error, "Nicht gesendet: " + ex.Message);
                return;
            }

            // The entry is added before the bytes are written, so that a fast answer is always shown after it.
            var timestamp = DateTime.Now;
            this.lastSentAt = timestamp;
            this.AddEntries(new[] { TelegramLogEntry.Sent(timestamp, telegram, this.Encoding, this.ConnectionName, this.Address) });

            try
            {
                await activeConnection.SendAsync(telegram, CancellationToken.None);
                OldInputs.AddNewPayload(input);
            }
            catch (Exception ex) when (ex is IOException
                || ex is SocketException
                || ex is TimeoutException
                || ex is ObjectDisposedException
                || ex is InvalidOperationException
                || ex is OperationCanceledException)
            {
                this.lastSentAt = null;
                this.AddMessage(TelegramLogKind.Error, "Senden fehlgeschlagen: " + DescribeError(ex));
            }
        }

        private void TakeOver(TelegramLogEntry entry)
        {
            if (entry.Data is null)
            {
                return;
            }

            ReadOnlySpan<byte> bytes = entry.Data;
            var ending = TelegramCodec.GetLineEndingBytes(this.LineEnding);

            if (ending.Length > 0 && bytes.EndsWith(ending))
            {
                bytes = bytes.Slice(0, bytes.Length - ending.Length);
            }

            if (this.AddStxEtx)
            {
                bytes = TelegramCodec.StripStxEtx(bytes);
            }

            this.Payload = TelegramCodec.FormatAsInput(bytes, this.Encoding);
        }

        private async Task ExportAsync()
        {
            if (this.Log.Count == 0)
            {
                return;
            }

            var fileName = $"Telegramme_{FileNames.Sanitize(this.ConnectionName)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var path = await Interactions.SaveFile.Handle(new FileDialogRequest("Verlauf exportieren", fileName, FileDialogRequest.LogFilters));

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var entries = this.Log.ToList();
            var asText = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

            try
            {
                await Task.Run(() => File.WriteAllText(
                    path,
                    asText ? TelegramLogFormatter.FormatText(entries) : TelegramLogFormatter.FormatCsv(entries),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

                this.AddMessage(TelegramLogKind.Info, $"{entries.Count} Einträge exportiert nach „{path}“.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.AddMessage(TelegramLogKind.Error, "Export fehlgeschlagen: " + ex.Message);
            }
        }

        private void OnTelegramReceived(object? sender, TelegramReceivedEventArgs e)
        {
            // Raised on a thread pool thread. The telegrams are collected and added to the history in one step.
            this.receivedQueue.Enqueue(e);

            if (Interlocked.Exchange(ref this.drainScheduled, 1) == 0)
            {
                Dispatcher.UIThread.Post(this.DrainReceived);
            }
        }

        private void DrainReceived()
        {
            Volatile.Write(ref this.drainScheduled, 0);

            List<TelegramLogEntry>? entries = null;

            while (this.receivedQueue.TryDequeue(out var received))
            {
                TimeSpan? responseTime = null;

                if (this.lastSentAt is DateTime sentAt && received.Timestamp >= sentAt)
                {
                    responseTime = received.Timestamp - sentAt;
                    this.lastSentAt = null;
                }

                entries ??= new List<TelegramLogEntry>();
                entries.Add(TelegramLogEntry.Received(
                    received.Timestamp,
                    received.Chunk.Data,
                    this.Encoding,
                    this.ConnectionName,
                    this.Address,
                    responseTime,
                    received.Chunk.Kind == ReceivedChunkKind.IncompleteFrame));
            }

            if (entries is not null)
            {
                this.AddEntries(entries);
            }
        }

        private void OnConnectionClosed(object? sender, ConnectionClosedEventArgs e)
        {
            if (sender is TelegramConnection closedConnection)
            {
                Dispatcher.UIThread.Post(() => this.HandleConnectionClosed(closedConnection, e));
            }
        }

        private void HandleConnectionClosed(TelegramConnection closedConnection, ConnectionClosedEventArgs e)
        {
            if (!ReferenceEquals(closedConnection, this.connection))
            {
                // Closed by Disconnect, which updates the state itself.
                return;
            }

            this.DrainReceived();
            this.connection = null;
            this.connectionDisposal = this.DisposeConnectionAsync(closedConnection);

            switch (e.Reason)
            {
                case ConnectionCloseReason.RemoteClosed:
                    this.StateDetail = "Die Gegenstelle hat die Verbindung beendet.";
                    this.State = ConnectionState.Lost;
                    this.AddMessage(TelegramLogKind.Error, "Verbindung unerwartet beendet: " + this.StateDetail);
                    break;
                case ConnectionCloseReason.ConnectionLost:
                    this.StateDetail = DescribeError(e.Error);
                    this.State = ConnectionState.Lost;
                    this.AddMessage(TelegramLogKind.Error, "Verbindung unterbrochen: " + this.StateDetail);
                    break;
                default:
                    this.State = ConnectionState.Disconnected;
                    this.AddMessage(TelegramLogKind.Info, "Verbindung getrennt.");
                    break;
            }
        }

        private async Task DisposeConnectionAsync(TelegramConnection connectionToDispose)
        {
            connectionToDispose.TelegramReceived -= this.OnTelegramReceived;
            connectionToDispose.Closed -= this.OnConnectionClosed;

            try
            {
                await connectionToDispose.DisposeAsync();
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                this.AddMessage(TelegramLogKind.Error, "Fehler beim Schließen der Verbindung: " + ex.Message);
            }
        }

        private void AddMessage(TelegramLogKind kind, string message)
        {
            var entry = kind == TelegramLogKind.Error
                ? TelegramLogEntry.Error(DateTime.Now, message, this.ConnectionName, this.Address)
                : TelegramLogEntry.Info(DateTime.Now, message, this.ConnectionName, this.Address);

            this.AddEntries(new[] { entry });
        }

        private void AddEntries(IReadOnlyList<TelegramLogEntry> entries)
        {
            this.Log.AddRange(entries);
            this.TrimLog();
        }

        private void TrimLog()
        {
            var excess = this.Log.Count - this.MaxLogEntries;

            if (excess > 0)
            {
                // Removes a few more entries than necessary, so that not every new telegram triggers a removal.
                this.Log.RemoveOldest(Math.Min(this.Log.Count, excess + this.MaxLogEntries / 20));
            }
        }

        internal static string DescribeError(Exception? exception)
        {
            switch (exception)
            {
                case null:
                    return "Unbekannte Ursache.";
                case SocketException socketException:
                    return DescribeSocketError(socketException);
                case IOException ioException when ioException.InnerException is SocketException innerSocketException:
                    return DescribeSocketError(innerSocketException);
                default:
                    return exception.Message;
            }
        }

        private static string DescribeSocketError(SocketException exception)
        {
            switch (exception.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    return "Verbindung abgelehnt – auf diesem Port wartet kein Dienst.";
                case SocketError.HostNotFound:
                case SocketError.NoData:
                    return "Der Host wurde nicht gefunden.";
                case SocketError.TryAgain:
                    return "Der Hostname konnte nicht aufgelöst werden.";
                case SocketError.TimedOut:
                    return "Zeitüberschreitung – die Gegenstelle antwortet nicht.";
                case SocketError.HostUnreachable:
                    return "Der Host ist nicht erreichbar.";
                case SocketError.NetworkUnreachable:
                case SocketError.NetworkDown:
                    return "Das Netzwerk ist nicht erreichbar.";
                case SocketError.ConnectionReset:
                    return "Die Verbindung wurde von der Gegenstelle zurückgesetzt.";
                case SocketError.ConnectionAborted:
                    return "Die Verbindung wurde abgebrochen.";
                default:
                    return $"{exception.Message} (Socket-Fehler {exception.SocketErrorCode})";
            }
        }
    }
}
