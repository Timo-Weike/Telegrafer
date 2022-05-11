using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using Avalonia.Input;
using ReactiveUI;

namespace Telegrafer.ViewModels
{
    public abstract class ViewModelBase : ReactiveObject, IActivatableViewModel
    {
        public ViewModelActivator Activator => new ViewModelActivator();
    }

    public class MenuItemViewModel
    {
        public string Header { get; set; }
        public ICommand Command { get; set; }
        public object CommandParameter { get; set; }
        public ObservableCollection<MenuItemViewModel> Items { get; set; }

        public KeyGesture HotKey { get; set; }
    }
}
