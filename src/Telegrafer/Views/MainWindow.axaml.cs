using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Telegrafer.ViewModels;
using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;

namespace Telegrafer.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
            this.WhenActivated(d => { });

            //this.WhenAnyValue(c => c.DataContext)
            //    .Subscribe(d =>
            //    {
            //        if (d is MainWindowViewModel vm)
            //        {
            //            vm!.MeunItems = new(new MenuItemViewModel[]
            //            {
            //                new()
            //                {
            //                    Header = "_File",
            //                    Items = new()
            //                    {
            //                        new()
            //                        {
            //                            Header = "_New",
            //                            Items = new()
            //                            {
            //                                new()
            //                                {
            //                                    Header = "_Client Connection Tab",
            //                                    Command = vm.NewClientTab,
            //                                    HotKey = KeyGesture.Parse("ctrl+n"),
            //                                },
            //                                new()
            //                                {
            //                                    Header = "_Server Tab",
            //                                    Command = vm.NewServerTab,
            //                                },
            //                            },
            //                        },
            //                    },
                                
            //                },
            //                new()
            //                {
            //                    Header = "_Help"
            //                }
            //            });
            //        }
            //    });
        }

        private void DTapped(object sender, RoutedEventArgs e)
        {
            if (sender is StyledElement styledElement)
            {
                if (styledElement.DataContext is Connection connection)
                {
                    this.ViewModel?.OpenTabForConnection?.Execute(connection);
                }
            }
        }

    }

    
}