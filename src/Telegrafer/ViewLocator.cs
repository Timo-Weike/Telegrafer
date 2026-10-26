using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Telegrafer.ViewModels;

namespace Telegrafer
{
    public class ViewLocator : IDataTemplate
    {
        // The tab control creates the view of the selected tab again after every tab change. The views of tabs
        // are kept as long as their view model exists, so that the history is not rebuilt and scroll position,
        // selection and entered text are kept.
        private static readonly ConditionalWeakTable<object, IControl> TabViews = new();

        public IControl Build(object data)
        {
            if (data is TabViewModelBase && TabViews.TryGetValue(data, out var tabView))
            {
                return tabView;
            }

            var name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type != null)
            {
                var control = (Control)Activator.CreateInstance(type)!;

                if (data is TabViewModelBase)
                {
                    TabViews.Add(data, control);
                }

                return control;
            }
            else
            {
                return new TextBlock { Text = "Not Found: " + name };
            }
        }

        public bool Match(object data)
        {
            return data is ViewModelBase;
        }
    }
}
