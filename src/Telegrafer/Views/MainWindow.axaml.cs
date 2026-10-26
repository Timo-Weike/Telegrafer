using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();

            // The view models request dialogs through interactions; the handlers live as long as the window.
            this.WhenActivated(disposables =>
            {
                Interactions.EditConnection.RegisterHandler(this.ShowConnectionEditorAsync).DisposeWith(disposables);
                Interactions.Prompt.RegisterHandler(this.ShowPromptAsync).DisposeWith(disposables);
                Interactions.EditSettings.RegisterHandler(this.ShowSettingsAsync).DisposeWith(disposables);
                Interactions.SaveFile.RegisterHandler(this.ShowSaveFileDialogAsync).DisposeWith(disposables);
                Interactions.OpenFile.RegisterHandler(this.ShowOpenFileDialogAsync).DisposeWith(disposables);
            });

            this.AddHandler(KeyDownEvent, this.OnWindowKeyDown, RoutingStrategies.Bubble);
        }

        private void DTapped(object sender, RoutedEventArgs e)
        {
            if (sender is StyledElement styledElement)
            {
                if (styledElement.DataContext is ConnectionNode connection)
                {
                    this.ViewModel?.Library?.OpenCommand.Execute(connection);
                }
            }
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+F searches the saved connections, unless the telegram history handled it for its own search.
            if (!e.Handled && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                this.SearchBox.Focus();
                this.SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private async Task ShowConnectionEditorAsync(InteractionContext<ConnectionEditorViewModel, bool> context)
        {
            var dialog = new ConnectionEditorWindow { DataContext = context.Input };
            context.SetOutput(await dialog.ShowDialog<bool>(this));
        }

        private async Task ShowPromptAsync(InteractionContext<PromptViewModel, bool> context)
        {
            var dialog = new PromptWindow { DataContext = context.Input };
            context.SetOutput(await dialog.ShowDialog<bool>(this));
        }

        private async Task ShowSettingsAsync(InteractionContext<SettingsViewModel, bool> context)
        {
            var dialog = new SettingsWindow { DataContext = context.Input };
            context.SetOutput(await dialog.ShowDialog<bool>(this));
        }

        private async Task ShowSaveFileDialogAsync(InteractionContext<FileDialogRequest, string?> context)
        {
            var dialog = new SaveFileDialog
            {
                Title = context.Input.Title,
                InitialFileName = context.Input.FileName,
                DefaultExtension = context.Input.Filters.FirstOrDefault()?.Extensions.FirstOrDefault(),
                Filters = CreateFilters(context.Input),
            };

            context.SetOutput(await dialog.ShowAsync(this));
        }

        private async Task ShowOpenFileDialogAsync(InteractionContext<FileDialogRequest, string?> context)
        {
            var dialog = new OpenFileDialog
            {
                Title = context.Input.Title,
                AllowMultiple = false,
                Filters = CreateFilters(context.Input),
            };

            var paths = await dialog.ShowAsync(this);
            context.SetOutput(paths?.FirstOrDefault());
        }

        private static List<FileDialogFilter> CreateFilters(FileDialogRequest request)
        {
            return request.Filters
                .Select(f => new FileDialogFilter { Name = f.Name, Extensions = f.Extensions.ToList() })
                .ToList();
        }
    }
}
