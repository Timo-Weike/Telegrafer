using Avalonia.ReactiveUI;
using Telegrafer.ViewModels;

namespace Telegrafer.Views
{
    public partial class TcpServerTabView : ReactiveUserControl<TcpClientViewModel>
    {
        public TcpServerTabView()
        {
            InitializeComponent();
        }
    }
}
