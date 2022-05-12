using Avalonia.Controls;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Sytem;
using Telegrafer.Utils;
using Telegrafer.Views;

namespace Telegrafer.ViewModels
{
    [DataContract]
    public class TcpClientViewModel : TabViewModelBase
    {
        public TcpClientViewModel()
        {
            this.SendCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.TrySend();
                }, this.WhenAnyValue(x => x.CanSend));
            this.DisconnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.Disconnect(true);
                }, this.WhenAnyValue(x => x.Connected));

            this.WhenAnyValue(x => x.Connected, x => x.Payload)
                .Subscribe(t =>
                {
                    var (connected, payload) = t;

                    this.CanSend = connected
                        && !string.IsNullOrEmpty(payload);
                });

            this.Connected = false;
        }

        private bool connected;

        public bool Connected
        {
            get { return connected; }
            set { this.RaiseAndSetIfChanged(ref connected, value); }
        }

        private bool canSend;

        public bool CanSend
        {
            get { return canSend; }
            set { this.RaiseAndSetIfChanged(ref canSend, value); }
        }

        private ObservableCollection<MyTextRun> TextRunsProperty = new();

        [DataMember]
        public ObservableCollection<MyTextRun> TextRuns
        {
            get { return TextRunsProperty; }
            set { this.RaiseAndSetIfChanged(ref TextRunsProperty, value); }
        }

        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }

        private string payload;

        [DataMember]
        public string Payload
        {
            get { return payload; }
            set
            {
                this.RaiseAndSetIfChanged(ref payload, value);
            }
        }

        private bool WithLinefeedProperty;

        [DataMember]
        public bool WithLinefeed
        {
            get { return WithLinefeedProperty; }
            set { this.RaiseAndSetIfChanged(ref WithLinefeedProperty, value); }
        }

        public ObservableCollection<string> OldPayloads => OldInputs.OldPayloads;

        private TcpClient? tcpClient;
        private NetworkStream? networkStream;
        private StreamReader? streamReader;
        private StreamWriter? streamWriter;
        private CancellationTokenSource? cts;

        public async Task AddInfoTextAsync(string v)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.TextRuns.Add(MyTextRun.Info(v));
            });
        }

        private Task? readerTask;

        public async Task HandleException(Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var text =
                    Environment.NewLine
                    + "---------------"
                    + Environment.NewLine
                    + ex.ToString()
                    + Environment.NewLine
                    + "---------------"
                    + Environment.NewLine;

                this.TextRuns.Add(MyTextRun.Error(text));
            });
        }

        public void Use(TcpClient tcpClient)
        {
            try
            {
                this.tcpClient = tcpClient;

                networkStream = tcpClient.GetStream();

                streamReader = new StreamReader(networkStream, Encoding.ASCII, leaveOpen: true);
                streamWriter = new StreamWriter(networkStream, Encoding.ASCII, leaveOpen: true);

                if (this.cts is not null) this.cts.Dispose();
                this.cts = new CancellationTokenSource();

                readerTask = Task.Run(async () =>
                {
                    var buffer = new char[1024];

                    while (!this.cts!.IsCancellationRequested)
                    {
                        try
                        {
                            var readCount = await streamReader.ReadAsync(buffer.AsMemory(), cts.Token);

                            if (readCount <= 0)
                            {
                                readCount = await streamReader.ReadAsync(buffer.AsMemory(), cts.Token);

                                if (readCount <= 0)
                                {
                                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                                     {
                                         await Disconnect(false);
                                     });
                                    return;
                                }
                            }

                            var readStr = new string(buffer.AsSpan(0, readCount));

                            await AddRemoteTextAsync(readStr);
                        }
                        catch (Exception ex)
                        {
                            if (!this.cts!.IsCancellationRequested)
                                await HandleException(ex);
                        }
                    }
                });

                this.Connected = true;
            }
            catch (Exception)
            {
                this.Connected = false;
                throw;
            }
        }

        private async Task<bool> TrySend()
        {
            if (this.streamWriter is not null && !string.IsNullOrWhiteSpace(this.Payload))
            {
                try
                {
                    var payload = StringParser.Parse(this.Payload);

                    if (WithLinefeed)
                    {
                        payload += Environment.NewLine;
                    }

                    await streamWriter.WriteAsync(payload);
                    streamWriter.Flush();
                    await AddLocalTextAsync(payload);

                    OldInputs.AddNewPayload(this.Payload);

                    return true;
                }
                catch (Exception ex)
                {
                    await HandleException(ex);
                }
            }

            return false;
        }

        private async Task AddRemoteTextAsync(string readStr)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.TextRuns.Add(MyTextRun.Remote(readStr));
            });
        }

        private async Task AddLocalTextAsync(string payload)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.TextRuns.Add(MyTextRun.Local(payload));
            });
        }

        public async Task<bool> Disconnect(bool userRequested)
        {
            try
            {
                await this.AddInfoTextAsync(
                    userRequested
                    ? "*** Disconnecting ***"
                    : "*** Remote host closed the connection ***");

                this.cts!.Cancel();

                await (this.readerTask ?? Task.CompletedTask);

                this.CanSend = false;
                this.Connected = false;

                using (this.tcpClient)
                using (this.networkStream)
                using (this.streamReader)
                using (this.streamWriter)
                {
                    this.tcpClient!.Close();
                }

                this.tcpClient = null;
                this.streamWriter = null;
                this.streamReader = null;
                this.networkStream = null;

                if (userRequested)
                {
                    await this.AddInfoTextAsync("*** Disconnected ***");
                }

                return true;
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }

            return false;
        }
    }
}
