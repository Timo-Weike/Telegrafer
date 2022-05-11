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
using Telegrafer.Views;

namespace Telegrafer.ViewModels
{
    [DataContract]
    public class TcpClientTabViewModel : TabViewModelBase
    {
        public TcpClientTabViewModel()
        {
            this.ConnectCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    var connected = await this.TryConnect();
                }, this.WhenAnyValue(x => x.CanConnect));

            this.WhenAnyValue(x => x.IpAndPort, x => x.TcpClient.Connected)
                .Subscribe(x =>
                {
                    var (ipAndHost, connected) = x;

                    if (connected)
                        this.CanConnect = false;
                    else if (Regex.IsMatch(ipAndHost, @"[^:]:\d+"))
                        this.CanConnect = true;
                    else
                        this.CanConnect = false;
                });

            this.IpAndPort = "";

            this.TcpClient = new();
        }

        private string ipAndPort = string.Empty;

        [DataMember]
        public string IpAndPort
        {
            get => ipAndPort;
            set
            {
                this.RaiseAndSetIfChanged(ref ipAndPort, value);
                this.RaisePropertyChanged(nameof(Header));
            }
        }

        private bool canConnect;

        public bool CanConnect
        {
            get { return canConnect; }
            set { this.RaiseAndSetIfChanged(ref canConnect, value); }
        }

        public ICommand ConnectCommand { get; }

        private TcpClientViewModel TcpClientProperty;

        public TcpClientViewModel TcpClient
        {
            get { return TcpClientProperty; }
            set { this.RaiseAndSetIfChanged(ref TcpClientProperty, value); }
        }

        private async Task<bool> TryConnect()
        {
            var split = this.ipAndPort.Split(':');

            if (split.Length != 2)
            {
                Debug.WriteLine($"{this.ipAndPort} is not valid");
                return false;
            }

            if (!int.TryParse(split[1], out var port))
            {
                Debug.WriteLine($"{split[1]} is not valid port");
                return false;
            }

            try
            {
                await this.TcpClient.AddInfoTextAsync("*** Connecting... ***");

                var tcpClient = new TcpClient();

                await tcpClient.ConnectAsync(split[0], port);

                await this.TcpClient.AddInfoTextAsync("*** Connected ***");

                this.TcpClient.Use(tcpClient);

                return true;
            }
            catch (Exception ex)
            {
                await this.TcpClient.HandleException(ex);

                return false;
            }
        }

        public string Header
        {
            get
            {
                if (string.IsNullOrEmpty(this.IpAndPort))
                {
                    return "Untiteld";
                }
                else
                {
                    return this.IpAndPort;
                }
            }
        }
    }
}
