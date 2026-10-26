using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Telegrafer.Models;
using Telegrafer.Utils;
using Telegrafer.ViewModels;
using Telegrafer.Views;

namespace Telegrafer
{
    public partial class App : Application
    {
        public static readonly string AppDataDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegrafer");

        static App()
        {
            Directory.CreateDirectory(AppDataDirPath);
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        //public override void OnFrameworkInitializationCompleted()
        //{

        //    base.OnFrameworkInitializationCompleted();
        //}

        public override void OnFrameworkInitializationCompleted()
        {
            // Create the AutoSuspendHelper.
            var filePath = Path.Combine(AppDataDirPath, "appstate.json");
            var suspension = new AutoSuspendHelper(ApplicationLifetime);
            RxApp.SuspensionHost.CreateNewAppState = () => new MainWindowViewModel();
            RxApp.SuspensionHost.SetupDefaultSuspendResume(new NewtonsoftJsonSuspensionDriver(filePath));
            suspension.OnFrameworkInitializationCompleted();

            // Load the saved view model state.
            var state = RxApp.SuspensionHost.GetAppState<MainWindowViewModel>();

            // The saved connections are loaded in the background while the window is shown. Initialize handles
            // all errors itself and shows them in the window, so the task does not need to be awaited.
            var connectionStore = new ConnectionStore(Path.Combine(AppDataDirPath, ConnectionStore.DefaultFileName));
            _ = state.Initialize(new ConnectionLibraryViewModel(connectionStore));

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow()
                {
                    DataContext = state,
                };
                desktop.ShutdownRequested += Desktop_ShutdownRequested;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void Desktop_ShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            OldInputs.Save();
            RxApp.SuspensionHost.GetAppState<MainWindowViewModel>().Library?.SaveNow();
        }
    }
}