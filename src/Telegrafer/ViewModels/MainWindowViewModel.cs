using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Windows.Input;
using Sytem;

namespace Telegrafer.ViewModels
{
    [DataContract]
    public class MainWindowViewModel : ViewModelBase
    {
        [DataMember]
        public ObservableCollection<TabViewModelBase> Tabs { get; } = new();

        public MainWindowViewModel()
        {
            this.WhenActivated((CompositeDisposable disposables) =>
            {
            });

            this.NewClientTab = ReactiveCommand.Create(OnNewClientTabExec);
            this.NewServerTab = ReactiveCommand.Create(OnNewServerTabExec);
            this.CloseTab = ReactiveCommand.CreateFromTask<TabViewModelBase>(OnCloseTabExecAsync);
            this.OpenTabForConnection = ReactiveCommand.Create<Connection>(OnOpenTabForConnectionExec);
            this.SaveSelectedTab = ReactiveCommand.Create(OnSaveSelectedTabExec);
            this.DeleteConnection = ReactiveCommand.Create<Connection>(OnDeleteConnectionExec);
        }

        private ObservableCollection<MenuItemViewModel> MeunItemsProperty = new();

        [IgnoreDataMember]
        public ObservableCollection<MenuItemViewModel> MeunItems
        {
            get { return MeunItemsProperty; }
            set { this.RaiseAndSetIfChanged(ref MeunItemsProperty, value); }
        }

        [IgnoreDataMember]
        public ICommand NewClientTab { get; }
        private void OnNewClientTabExec()
        {
            var newTab = new TcpClientTabViewModel();
            AddTabViewModel(newTab);
        }

        private void AddTabViewModel(TabViewModelBase newTab)
        {
            Tabs.Add(newTab);
            this.SelectedIndex = Tabs.Count - 1;
        }

        [IgnoreDataMember]
        public ICommand NewServerTab { get; }
        private void OnNewServerTabExec()
        {

        }

        [IgnoreDataMember]
        public ICommand CloseTab { get; }
        private async Task OnCloseTabExecAsync(TabViewModelBase tab)
        {
            if (tab is TcpClientTabViewModel tcpClientTab)
            {
                if (tcpClientTab.TcpClient.Connected)
                {
                    await tcpClientTab.TcpClient.Disconnect(true);
                }
            }
            this.Tabs.Remove(tab);
        }

        [IgnoreDataMember]
        public ICommand OpenTabForConnection { get; }
        private void OnOpenTabForConnectionExec(Connection connection)
        {
            var newTab = new TcpClientTabViewModel { IpAndPort = connection.IpAndPort };
            this.AddTabViewModel(newTab);
        }

        [IgnoreDataMember]
        public ICommand SaveSelectedTab { get; }
        private void OnSaveSelectedTabExec()
        {
            if (this.Tabs.IsInBounds(this.SelectedIndex))
            {
                var selectedTab = this.Tabs[this.SelectedIndex];
                
                if (selectedTab is TcpClientTabViewModel tabViewModel)
                {
                    var ipAndPort = tabViewModel.IpAndPort;
                    
                    if (!string.IsNullOrWhiteSpace(ipAndPort) && !this.ClientConnections.Any(c => c.IpAndPort == ipAndPort))
                    {
                        this.ClientConnections.Add(new Connection(ipAndPort));
                    }
                }
            }
        }

        //[IgnoreDataMember]
        //public ICommand SaveSelectedTabAs { get; }
        //private void OnSaveSelectedTabAsExec(Connection connection)
        //{
        //    var newTab = new TcpClientTabViewModel { IpAndPort = connection.IpAndPort };
        //    this.AddTabViewModel(newTab);
        //}

        public ICommand DeleteConnection { get; }
        private void OnDeleteConnectionExec(Connection connection)
        {
            this.ClientConnections.Remove(connection);
        }


        private int SelectedIndexProperty;

        [DataMember]
        public int SelectedIndex
        {
            get { return SelectedIndexProperty; }
            set { this.RaiseAndSetIfChanged(ref SelectedIndexProperty, value); }
        }

        private ObservableCollection<Connection> ClientConnectionsProperty =
            new ObservableCollection<Connection>
            {
            };

        [DataMember]
        public ObservableCollection<Connection> ClientConnections
        {
            get { return ClientConnectionsProperty; }
            set { this.RaiseAndSetIfChanged(ref ClientConnectionsProperty, value); }
        }
    }

    public record Connection(string IpAndPort);
}
