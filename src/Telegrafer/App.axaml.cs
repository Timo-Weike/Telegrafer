using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;
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
        }
    }
}