using System;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.Messages;

namespace Hik.Communication.Scs.Client
{
    /// <summary>
    /// Represents a client for SCS servers.
    /// </summary>
    public interface IConnectableClient : IDisposable
    {
        /// <summary>
        /// This event is raised when client connected to server.
        /// </summary>
        event EventHandler Connected;

        /// <summary>
        /// This event is raised when client disconnected from server.
        /// </summary>
        event EventHandler Disconnected;

        /// <summary>
        /// This event is raised when a ping round-trip completes.
        /// </summary>
        event EventHandler<PingCompletedEventArgs> PingCompleted;

        /// <summary>
        /// Timeout for connecting to a server (as milliseconds).
        /// Default value: 15 seconds (15000 ms).
        /// </summary>
        int ConnectTimeout { get; set; }

        /// <summary>
        /// Gets/sets the interval between ping messages in milliseconds.
        /// Default value: 30000 (30 seconds).
        /// </summary>
        int PingInterval { get; set; }

        /// <summary>
        /// Gets the round-trip time of the last completed ping in milliseconds,
        /// or null if no ping has completed yet.
        /// </summary>
        long? LastPingRtt { get; }

        /// <summary>
        /// Gets the average round-trip time of recent pings in milliseconds,
        /// or null if no ping has completed yet.
        /// </summary>
        long? AveragePingRtt { get; }

        /// <summary>
        /// Gets the current communication state.
        /// </summary>
        CommunicationStates CommunicationState { get; }

        /// <summary>
        /// Connects to server.
        /// </summary>
        void Connect();

        /// <summary>
        /// Disconnects from server.
        /// Does nothing if already disconnected.
        /// </summary>
        void Disconnect();
    }
}
