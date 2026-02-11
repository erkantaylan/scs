using System;

namespace Hik.Communication.Scs.Communication.Channels.Tcp
{
    /// <summary>
    /// Configurable TCP socket options applied to connections.
    /// All values use <see cref="System.Net.Sockets.Socket.SetSocketOption"/> compatible with netstandard2.0.
    /// </summary>
    public sealed class TcpSocketOptions
    {
        /// <summary>
        /// Gets or sets whether TCP_NODELAY (Nagle algorithm disabled) is enabled.
        /// Default: true.
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// Gets or sets whether SO_KEEPALIVE is enabled.
        /// Default: false.
        /// </summary>
        public bool KeepAliveEnabled { get; set; }

        /// <summary>
        /// Gets or sets the keep-alive idle time in seconds before the first probe is sent.
        /// Only applied when <see cref="KeepAliveEnabled"/> is true.
        /// Null means use the OS default.
        /// On platforms that don't support this option, it is silently ignored.
        /// </summary>
        public int? KeepAliveTimeSeconds { get; set; }

        /// <summary>
        /// Gets or sets the interval in seconds between keep-alive probes.
        /// Only applied when <see cref="KeepAliveEnabled"/> is true.
        /// Null means use the OS default.
        /// On platforms that don't support this option, it is silently ignored.
        /// </summary>
        public int? KeepAliveIntervalSeconds { get; set; }

        /// <summary>
        /// Gets or sets the send timeout in milliseconds.
        /// 0 means infinite (no timeout).
        /// Default: 5000 (5 seconds).
        /// </summary>
        public int SendTimeout { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the receive timeout in milliseconds.
        /// 0 means infinite (no timeout).
        /// Default: 0 (infinite).
        /// </summary>
        public int ReceiveTimeout { get; set; }

        /// <summary>
        /// Creates a new TcpSocketOptions with default values.
        /// </summary>
        public TcpSocketOptions()
        {
        }

        /// <summary>
        /// Creates a copy of the given options.
        /// </summary>
        internal TcpSocketOptions(TcpSocketOptions other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            NoDelay = other.NoDelay;
            KeepAliveEnabled = other.KeepAliveEnabled;
            KeepAliveTimeSeconds = other.KeepAliveTimeSeconds;
            KeepAliveIntervalSeconds = other.KeepAliveIntervalSeconds;
            SendTimeout = other.SendTimeout;
            ReceiveTimeout = other.ReceiveTimeout;
        }
    }
}
