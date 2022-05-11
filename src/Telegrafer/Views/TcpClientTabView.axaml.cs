using Avalonia.ReactiveUI;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class TcpClientTabView : ReactiveUserControl<TcpClientViewModel>
    {
        public TcpClientTabView()
        {
            InitializeComponent();
        }
    }
}
