using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class ConnectionEditorWindow : ReactiveWindow<ConnectionEditorViewModel>
    {
        public ConnectionEditorWindow()
        {
            InitializeComponent();

            this.WhenActivated(disposables =>
            {
                this.ViewModel!.SaveCommand.Subscribe(_ => this.Close(true)).DisposeWith(disposables);
                this.ViewModel!.CancelCommand.Subscribe(_ => this.Close(false)).DisposeWith(disposables);
            });

            this.Opened += (_, _) => this.NameBox.Focus();
        }
    }
}
