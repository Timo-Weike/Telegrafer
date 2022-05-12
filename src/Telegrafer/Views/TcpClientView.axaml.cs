using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FuzzySharp;
using FuzzySharp.PreProcess;

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
        }
    }
}
