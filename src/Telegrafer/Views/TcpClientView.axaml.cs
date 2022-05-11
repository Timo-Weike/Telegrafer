using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Telegrafer.Views
{
    public partial class TcpClientView : UserControl
    {
        public TcpClientView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
