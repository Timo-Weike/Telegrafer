using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Telegrafer.Communication;

namespace Telegrafer.Views
{
    /// <summary>
    /// Symbol and color of a connection state. Every state has its own shape, so that it can be recognized
    /// without colors.
    /// </summary>
    public static class ConnectionStateConverters
    {
        // Filled circle.
        private static readonly Geometry ConnectedGeometry = StreamGeometry.Parse("M12,3 A9,9 0 1 1 11.99,3 Z");

        // Ring.
        private static readonly Geometry DisconnectedGeometry = StreamGeometry.Parse("M12,3 A9,9 0 1 1 11.99,3 Z M12,6.5 A5.5,5.5 0 1 0 12.01,6.5 Z");

        // Three quarters of a ring.
        private static readonly Geometry ConnectingGeometry = StreamGeometry.Parse("M12,3 A9,9 0 1 1 3,12 L6.5,12 A5.5,5.5 0 1 0 12,6.5 Z");

        // Cross.
        private static readonly Geometry FailedGeometry = StreamGeometry.Parse("M4.9,7.1 L7.1,4.9 L12,9.8 L16.9,4.9 L19.1,7.1 L14.2,12 L19.1,16.9 L16.9,19.1 L12,14.2 L7.1,19.1 L4.9,16.9 L9.8,12 Z");

        // Warning triangle with exclamation mark.
        private static readonly Geometry LostGeometry = StreamGeometry.Parse("M12,2 L23,21 L1,21 Z M10.8,8.5 L13.2,8.5 L13.2,14.5 L10.8,14.5 Z M10.8,16.2 L13.2,16.2 L13.2,18.6 L10.8,18.6 Z");

        private static readonly IBrush ConnectedBrush = new ImmutableSolidColorBrush(Color.Parse("#3FB950"));
        private static readonly IBrush ConnectingBrush = new ImmutableSolidColorBrush(Color.Parse("#D29922"));
        private static readonly IBrush DisconnectedBrush = new ImmutableSolidColorBrush(Color.Parse("#8B949E"));
        private static readonly IBrush FailedBrush = new ImmutableSolidColorBrush(Color.Parse("#F85149"));
        private static readonly IBrush LostBrush = new ImmutableSolidColorBrush(Color.Parse("#E3833A"));

        /// <summary>
        /// Converts a state into the geometry of its symbol.
        /// </summary>
        public static readonly IValueConverter Symbol =
            new FuncValueConverter<ConnectionState?, Geometry?>(state => GetGeometry(state));

        /// <summary>
        /// Converts a state into the brush of its symbol.
        /// </summary>
        public static readonly IValueConverter Foreground =
            new FuncValueConverter<ConnectionState?, IBrush?>(state => GetBrush(state));

        private static Geometry? GetGeometry(ConnectionState? state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return ConnectedGeometry;
                case ConnectionState.Connecting:
                    return ConnectingGeometry;
                case ConnectionState.Failed:
                    return FailedGeometry;
                case ConnectionState.Lost:
                    return LostGeometry;
                case ConnectionState.Disconnected:
                    return DisconnectedGeometry;
                default:
                    // No open tab: no symbol.
                    return null;
            }
        }

        private static IBrush? GetBrush(ConnectionState? state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return ConnectedBrush;
                case ConnectionState.Connecting:
                    return ConnectingBrush;
                case ConnectionState.Failed:
                    return FailedBrush;
                case ConnectionState.Lost:
                    return LostBrush;
                default:
                    return DisconnectedBrush;
            }
        }
    }
}
