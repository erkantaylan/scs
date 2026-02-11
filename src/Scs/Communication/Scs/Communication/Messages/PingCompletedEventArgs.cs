using System;

namespace Hik.Communication.Scs.Communication.Messages
{
    /// <summary>
    /// Stores ping round-trip time information for the PingCompleted event.
    /// </summary>
    public class PingCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Round-trip time of the completed ping in milliseconds.
        /// </summary>
        public long RoundTripTimeMs { get; }

        /// <summary>
        /// Creates a new PingCompletedEventArgs object.
        /// </summary>
        /// <param name="roundTripTimeMs">Round-trip time in milliseconds</param>
        public PingCompletedEventArgs(long roundTripTimeMs)
        {
            RoundTripTimeMs = roundTripTimeMs;
        }
    }
}
