using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Windows.Input;
using Sytem;
using Telegrafer.Communication;
using Telegrafer.Models;
using Telegrafer.Utils;

namespace Telegrafer.ViewModels
{
    [DataContract]
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly Dictionary<TcpClientTabViewModel, IDisposable> tabSubscriptions = new();

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
            this.CloseSelectedTab = ReactiveCommand.CreateFromTask(OnCloseSelectedTabExecAsync);
            this.SaveSelectedTab = ReactiveCommand.CreateFromTask(OnSaveSelectedTabExecAsync);
            this.SaveTabAsConnection = ReactiveCommand.CreateFromTask<TcpClientTabViewModel>(OnSaveTabAsConnectionExecAsync);
            this.EditTabConnection = ReactiveCommand.CreateFromTask<TcpClientTabViewModel>(OnEditTabConnectionExecAsync);
            this.NextTab = ReactiveCommand.Create(() => this.MoveSelection(1));
            this.PreviousTab = ReactiveCommand.Create(() => this.MoveSelection(-1));
            this.ShowSettings = ReactiveCommand.CreateFromTask(OnShowSettingsExecAsync);
            this.DismissNotification = ReactiveCommand.Create(() => { this.Notification = null; });

            this.Tabs.CollectionChanged += this.OnTabsChanged;
        }

        private ObservableCollection<MenuItemViewModel> MeunItemsProperty = new();

        [IgnoreDataMember]
        public ObservableCollection<MenuItemViewModel> MeunItems
        {
            get { return MeunItemsProperty; }
            set { this.RaiseAndSetIfChanged(ref MeunItemsProperty, value); }
        }

        private ConnectionLibraryViewModel? LibraryProperty;

        /// <summary>
        /// Gets the saved connections. It is set by <see cref="Initialize"/> after the state has been restored.
        /// </summary>
        [IgnoreDataMember]
        public ConnectionLibraryViewModel? Library
        {
            get { return LibraryProperty; }
            private set { this.RaiseAndSetIfChanged(ref LibraryProperty, value); }
        }

        private string? NotificationProperty;

        /// <summary>
        /// Gets or sets a message that is shown above the tabs until the user closes it.
        /// </summary>
        [IgnoreDataMember]
        public string? Notification
        {
            get { return NotificationProperty; }
            set { this.RaiseAndSetIfChanged(ref NotificationProperty, value); }
        }

        [IgnoreDataMember]
        public bool HasTabs => this.Tabs.Count > 0;

        [IgnoreDataMember]
        public ICommand NewClientTab { get; }
        private void OnNewClientTabExec()
        {
            var newTab = new TcpClientTabViewModel();

            if (this.Library is not null)
            {
                newTab.AddStxEtx = this.Library.Settings.DefaultAddStxEtx;
                newTab.LineEnding = this.Library.Settings.DefaultLineEnding;
                newTab.Encoding = this.Library.Settings.DefaultEncoding;
                newTab.TcpClient.AutoScroll = this.Library.Settings.AutoScroll;
                newTab.Attach(null, this.Library.Settings, this.Library.GetGroupName);
            }

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
                if (tcpClientTab.TcpClient.Connected || tcpClientTab.TcpClient.IsConnecting)
                {
                    await tcpClientTab.TcpClient.Disconnect(true);
                }
            }

            // The neighbor of a closed tab is selected instead of the first tab.
            var index = this.Tabs.IndexOf(tab);
            var wasSelected = index == this.SelectedIndex;
            this.Tabs.Remove(tab);

            if (wasSelected && this.Tabs.Count > 0)
            {
                this.SelectedIndex = Math.Min(index, this.Tabs.Count - 1);
            }
        }

        [IgnoreDataMember]
        public ICommand CloseSelectedTab { get; }
        private async Task OnCloseSelectedTabExecAsync()
        {
            if (this.Tabs.IsInBounds(this.SelectedIndex))
            {
                await this.OnCloseTabExecAsync(this.Tabs[this.SelectedIndex]);
            }
        }

        /// <summary>
        /// Saves the selected tab as connection or edits its saved connection.
        /// </summary>
        [IgnoreDataMember]
        public ICommand SaveSelectedTab { get; }
        private async Task OnSaveSelectedTabExecAsync()
        {
            if (this.Tabs.IsInBounds(this.SelectedIndex) && this.Tabs[this.SelectedIndex] is TcpClientTabViewModel tabViewModel)
            {
                if (tabViewModel.IsLinked)
                {
                    await this.OnEditTabConnectionExecAsync(tabViewModel);
                }
                else
                {
                    await this.OnSaveTabAsConnectionExecAsync(tabViewModel);
                }
            }
        }

        [IgnoreDataMember]
        public ICommand SaveTabAsConnection { get; }
        private async Task OnSaveTabAsConnectionExecAsync(TcpClientTabViewModel tab)
        {
            if (this.Library is null || tab.IsLinked)
            {
                return;
            }

            var profile = this.Library.Settings.CreateConnection();
            profile.AddStxEtx = tab.AddStxEtx;
            profile.LineEnding = tab.LineEnding;
            profile.Encoding = tab.Encoding;

            if (HostAndPort.TryParse(tab.IpAndPort, out var host, out var port))
            {
                profile.Host = host;
                profile.Port = port;
            }
            else
            {
                profile.Host = tab.IpAndPort.Trim();
            }

            var created = await this.Library.CreateConnectionAsync(profile, "Verbindung speichern");

            if (created is not null)
            {
                tab.Attach(created, this.Library.Settings, this.Library.GetGroupName);
                this.UpdateConnectionStates();
            }
        }

        [IgnoreDataMember]
        public ICommand EditTabConnection { get; }
        private async Task OnEditTabConnectionExecAsync(TcpClientTabViewModel tab)
        {
            if (this.Library is not null && tab.Profile is not null)
            {
                await this.Library.EditConnectionAsync(tab.Profile);
            }
        }

        [IgnoreDataMember]
        public ICommand NextTab { get; }

        [IgnoreDataMember]
        public ICommand PreviousTab { get; }

        [IgnoreDataMember]
        public ICommand ShowSettings { get; }
        private async Task OnShowSettingsExecAsync()
        {
            if (this.Library is null)
            {
                return;
            }

            await this.Library.EditSettingsAsync();

            foreach (var tab in this.Tabs.OfType<TcpClientTabViewModel>())
            {
                tab.TcpClient.MaxLogEntries = this.Library.Settings.MaxLogEntries;
            }
        }

        [IgnoreDataMember]
        public ICommand DismissNotification { get; }

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

        /// <summary>
        /// Gets or sets the addresses that versions before the connection management saved. They are taken over
        /// into the saved connections on the first start and then removed.
        /// </summary>
        [DataMember]
        public ObservableCollection<Connection> ClientConnections
        {
            get { return ClientConnectionsProperty; }
            set { this.RaiseAndSetIfChanged(ref ClientConnectionsProperty, value); }
        }

        /// <summary>
        /// Connects the restored state with the saved connections, which are loaded in the background.
        /// </summary>
        public Task Initialize(ConnectionLibraryViewModel library)
        {
            this.Library = library;
            library.OpenRequested += this.OpenConnection;
            library.DataChanged += (_, _) => this.OnLibraryChanged();
            library.MessageRaised += message => this.Notification = message;

            foreach (var tab in this.Tabs.OfType<TcpClientTabViewModel>())
            {
                this.Track(tab);
            }

            return this.InitializeAsync(library);
        }

        /// <summary>
        /// Opens a saved connection in a tab and connects it. An open tab of the connection is reused.
        /// </summary>
        public void OpenConnection(ConnectionProfile profile, bool forceNewTab)
        {
            if (this.Library is null)
            {
                return;
            }

            var tab = forceNewTab
                ? null
                : this.Tabs.OfType<TcpClientTabViewModel>().FirstOrDefault(t => ReferenceEquals(t.Profile, profile));

            if (tab is null)
            {
                tab = new TcpClientTabViewModel();
                tab.TcpClient.AutoScroll = this.Library.Settings.AutoScroll;
                tab.Attach(profile, this.Library.Settings, this.Library.GetGroupName);
                this.AddTabViewModel(tab);
            }
            else
            {
                this.SelectedIndex = this.Tabs.IndexOf(tab);
            }

            if (tab.ConnectCommand.CanExecute(null))
            {
                tab.ConnectCommand.Execute(null);
            }
        }

        private async Task InitializeAsync(ConnectionLibraryViewModel library)
        {
            try
            {
                await this.LoadLibraryAsync(library);
            }
            catch (Exception ex)
            {
                // Last line of defense of the background initialization: the application stays usable.
                this.Notification = "Die gespeicherten Verbindungen konnten nicht geladen werden: " + ex.Message;
            }
        }

        private async Task LoadLibraryAsync(ConnectionLibraryViewModel library)
        {
            var legacyAddresses = this.ClientConnections.Select(c => c.IpAndPort).ToList();
            var message = await library.LoadAsync(legacyAddresses);

            if (legacyAddresses.Count > 0)
            {
                this.ClientConnections.Clear();
            }

            if (!library.Settings.RestoreTabs)
            {
                this.Tabs.Clear();
            }

            foreach (var tab in this.Tabs.OfType<TcpClientTabViewModel>())
            {
                var profile = tab.ConnectionId is Guid id ? library.FindConnection(id) : null;
                tab.TcpClient.AutoScroll = library.Settings.AutoScroll;
                tab.Attach(profile, library.Settings, library.GetGroupName);
            }

            this.UpdateConnectionStates();

            if (message is not null)
            {
                this.Notification = message;
            }
        }

        private void OnLibraryChanged()
        {
            if (this.Library is null || !this.Library.IsLoaded)
            {
                return;
            }

            // Tabs of deleted connections keep their address and become unsaved connections.
            foreach (var tab in this.Tabs.OfType<TcpClientTabViewModel>())
            {
                var profile = tab.Profile is null ? null : this.Library.FindConnection(tab.Profile.Id);
                tab.Attach(profile, this.Library.Settings, this.Library.GetGroupName);
            }

            this.UpdateConnectionStates();
        }

        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var tab in e.OldItems.OfType<TcpClientTabViewModel>())
                {
                    if (this.tabSubscriptions.Remove(tab, out var subscription))
                    {
                        subscription.Dispose();
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var tab in e.NewItems.OfType<TcpClientTabViewModel>())
                {
                    this.Track(tab);
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var tab in this.tabSubscriptions.Keys.Where(t => !this.Tabs.Contains(t)).ToList())
                {
                    this.tabSubscriptions[tab].Dispose();
                    this.tabSubscriptions.Remove(tab);
                }
            }

            this.RaisePropertyChanged(nameof(HasTabs));
            this.UpdateConnectionStates();
        }

        private void Track(TcpClientTabViewModel tab)
        {
            if (this.tabSubscriptions.ContainsKey(tab))
            {
                return;
            }

            this.tabSubscriptions[tab] = tab
                .WhenAnyValue(t => t.State, t => t.Profile)
                .Skip(1)
                .Subscribe(_ => this.UpdateConnectionStates());
        }

        /// <summary>
        /// Shows the state of the open tabs at the saved connections in the side bar.
        /// </summary>
        private void UpdateConnectionStates()
        {
            if (this.Library is null || !this.Library.IsLoaded)
            {
                return;
            }

            var states = new Dictionary<Guid, ConnectionState>();

            foreach (var tab in this.Tabs.OfType<TcpClientTabViewModel>())
            {
                if (tab.Profile is null)
                {
                    continue;
                }

                if (!states.TryGetValue(tab.Profile.Id, out var current) || GetPriority(tab.State) > GetPriority(current))
                {
                    states[tab.Profile.Id] = tab.State;
                }
            }

            this.Library.UpdateStates(states);
        }

        private static int GetPriority(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return 4;
                case ConnectionState.Connecting:
                    return 3;
                case ConnectionState.Lost:
                    return 2;
                case ConnectionState.Failed:
                    return 1;
                default:
                    return 0;
            }
        }

        private void MoveSelection(int offset)
        {
            if (this.Tabs.Count > 1)
            {
                this.SelectedIndex = ((this.SelectedIndex + offset) % this.Tabs.Count + this.Tabs.Count) % this.Tabs.Count;
            }
        }
    }

    /// <summary>
    /// Address that versions before the connection management saved; only read for the migration.
    /// </summary>
    public record Connection(string IpAndPort);
}
