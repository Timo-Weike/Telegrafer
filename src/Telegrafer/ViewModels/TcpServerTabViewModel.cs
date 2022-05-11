using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Telegrafer.Views;

namespace Telegrafer.ViewModels
{
    [DataContract]
    public class TcpServerTabViewModel : TabViewModelBase
    {
        public TcpServerTabViewModel()
        {
            this.SendCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    //await this.TrySend();
                }, this.WhenAnyValue(x => x.CanSend));
            this.ConnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    var connected = await this.TryConnect();

                    this.IsListening = connected;
                }, this.WhenAnyValue(x => x.CanConnect));
            this.DisconnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    await this.Disconnect();
                }, this.WhenAnyValue(x => x.IsListening));

            this.WhenAnyValue(x => x.Port, x => x.IsListening)
                .Subscribe(x =>
                {
                    var (port, connected) = x;

                    if (connected)
                        this.CanConnect = false;
                    else if (Regex.IsMatch(port, @"\d+"))
                        this.CanConnect = true;
                    else
                        this.CanConnect = false;
                });

            this.WhenAnyValue(x => x.IsListening, x => x.Payload)
                .Subscribe(t =>
                {
                    var (connected, payload) = t;

                    this.CanSend = connected
                        && !string.IsNullOrEmpty(payload);
                });

            this.Port = "";
            this.IsListening = false;
        }


        private string port = string.Empty;

        [DataMember]
        public string Port
        {
            get => port;
            set => this.RaiseAndSetIfChanged(ref port, value);
        }

        private bool canConnect;

        public bool CanConnect
        {
            get { return canConnect; }
            set { this.RaiseAndSetIfChanged(ref canConnect, value); }
        }

        private bool isListening;

        public bool IsListening
        {
            get { return isListening; }
            set { this.RaiseAndSetIfChanged(ref isListening, value); }
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
        public ICommand ConnectCommand { get; }
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

        private string? header = null;
        private TcpClient? tcpClient;
        private NetworkStream? networkStream;
        private StreamReader? streamReader;
        private StreamWriter? streamWriter;
        private CancellationTokenSource? cts;
        private Task? readerTask;
        private TcpListener tcpServer;
        private Task acceptTask;

        [DataMember]
        public string Header
        {
            get
            {
                if (header is null)
                {
                    return this.Id;
                }
                else { return header; }
            }

            set { this.RaiseAndSetIfChanged(ref header, value); }
        }

        private async Task<bool> TryConnect()
        {
            try
            {
                var port = int.Parse(this.Port);

                tcpServer = new TcpListener(IPAddress.Any, port);
                cts = new CancellationTokenSource();

                tcpServer.Start();

                acceptTask = Task.Run(async () =>
                {
                    while (!this.cts.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await this.tcpServer.AcceptTcpClientAsync(this.cts.Token);
                        }
                        catch (Exception ex)
                        {
                            //if (!this.cts!.IsCancellationRequested)
                            //    await HandleException(ex);
                        }
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Debug.WriteLine("Could not connect");

                return false;
            }
        }

        private async Task<bool> Disconnect()
        {
            try
            {
                this.cts!.Cancel();

                await (this.readerTask ?? Task.CompletedTask);

                this.CanConnect = false;
                this.CanSend = false;
                this.IsListening = false;

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

                return true;
            }
            catch (Exception ex)
            {
                
            }

            return false;
        }
    }

    public class TcpClientWrapper : ReactiveObject
    {
        public TcpClient TcpClient { get; }

        private string EndPointProperty;

        public string EndPoint
        {
            get { return EndPointProperty; }
            set { this.RaiseAndSetIfChanged(ref EndPointProperty, value); }
        }

        private Brush ColorProperty;

        public Brush Color
        {
            get { return ColorProperty; }
            set { this.RaiseAndSetIfChanged(ref ColorProperty, value); }
        }

    }
}
