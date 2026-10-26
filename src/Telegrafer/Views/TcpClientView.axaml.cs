using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FuzzySharp;
using FuzzySharp.PreProcess;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class TcpClientView : UserControl
    {
        public TcpClientView()
        {
            InitializeComponent();

            this.box.FilterMode = AutoCompleteFilterMode.Custom;
            this.box.TextFilter = (s, c) =>
            {
                if (c.Contains(s, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (Fuzz.Ratio(s, c) > 50)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            };

            // Enter sends the telegram. The event is handled while tunneling, because the AutoCompleteBox would
            // otherwise only close its suggestion list. A suggestion chosen with the arrow keys is already the text.
            this.box.AddHandler(KeyDownEvent, this.OnPayloadKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnPayloadKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.Handled || e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            if (this.DataContext is TcpClientViewModel viewModel && viewModel.SendCommand.CanExecute(null))
            {
                this.box.IsDropDownOpen = false;
                viewModel.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
