using System.Runtime.Serialization;

namespace Telegrafer.ViewModels
{
    [DataContract]
    [KnownType(typeof(TcpClientViewModel))]
    public abstract class TabViewModelBase : ViewModelBase
    {
        public string Id { get; } = Guid.NewGuid().ToString();
    }
}
