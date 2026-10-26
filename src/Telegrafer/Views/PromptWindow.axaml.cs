using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class PromptWindow : ReactiveWindow<PromptViewModel>
    {
        public PromptWindow()
        {
            InitializeComponent();

            this.WhenActivated(disposables =>
            {
                this.ViewModel!.ConfirmCommand.Subscribe(_ => this.Close(true)).DisposeWith(disposables);
                this.ViewModel!.CancelCommand.Subscribe(_ => this.Close(false)).DisposeWith(disposables);
            });

            this.Opened += (_, _) =>
            {
                if (this.ViewModel?.ShowInput == true)
                {
                    this.InputBox.Focus();
                    this.InputBox.SelectAll();
                }
            };
        }
    }
}
