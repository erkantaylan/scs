using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Hik.Communication.Scs.Communication;
using Hik.Communication.Scs.Communication.Channels;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Communication.Protocols;
using Hik.Threading;

namespace Hik.Communication.Scs.Client
{
    /// <summary>
    /// This class provides base functionality for client classes.
    /// </summary>
    internal abstract class ScsClientBase : IScsClient
    {
        #region Public events

        /// <summary>
        /// This event is raised when a new message is received.
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageReceived;

        /// <summary>
        /// This event is raised when a new message is sent without any error.
        /// It does not guaranties that message is properly handled and processed by remote application.
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageSent;

        /// <summary>
        /// This event is raised when communication channel closed.
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// This event is raised when client disconnected from server.
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// This event is raised when a ping round-trip completes.
        /// </summary>
        public event EventHandler<PingCompletedEventArgs> PingCompleted;

        #endregion

        #region Public properties

        /// <summary>
        /// Timeout for connecting to a server (as milliseconds).
        /// Default value: 15 seconds (15000 ms).
        /// </summary>
        public int ConnectTimeout { get; set; }

        /// <summary>
        /// Gets/sets wire protocol that is used while reading and writing messages.
        /// </summary>
        public IScsWireProtocol WireProtocol
        {
            get { return _wireProtocol; }
            set
            {
                if (CommunicationState == CommunicationStates.Connected)
                {
                    throw new ApplicationException("Wire protocol can not be changed while connected to server.");
                }

                _wireProtocol = value;
            }
        }
        private IScsWireProtocol _wireProtocol;

        /// <summary>
        /// Gets the communication state of the Client.
        /// </summary>
        public CommunicationStates CommunicationState
        {
            get
            {
                return _communicationChannel != null
                           ? _communicationChannel.CommunicationState
                           : CommunicationStates.Disconnected;
            }
        }

        /// <summary>
        /// Gets the time of the last succesfully received message.
        /// </summary>
        public DateTime LastReceivedMessageTime
        {
            get
            {
                return _communicationChannel != null
                           ? _communicationChannel.LastReceivedMessageTime
                           : DateTime.MinValue;
            }
        }

        /// <summary>
        /// Gets the time of the last succesfully received message.
        /// </summary>
        public DateTime LastSentMessageTime
        {
            get
            {
                return _communicationChannel != null
                           ? _communicationChannel.LastSentMessageTime
                           : DateTime.MinValue;
            }
        }

        /// <summary>
        /// Gets/sets the interval between ping messages in milliseconds.
        /// Default value: 30000 (30 seconds).
        /// </summary>
        public int PingInterval
        {
            get { return _pingTimer.Period; }
            set { _pingTimer.Period = value; }
        }

        /// <summary>
        /// Gets the round-trip time of the last completed ping in milliseconds,
        /// or null if no ping has completed yet.
        /// </summary>
        public long? LastPingRtt { get; private set; }

        /// <summary>
        /// Gets the average round-trip time of recent pings in milliseconds,
        /// or null if no ping has completed yet.
        /// </summary>
        public long? AveragePingRtt
        {
            get
            {
                if (_rttCount == 0)
                {
                    return null;
                }

                return _rttSum / _rttCount;
            }
        }

        #endregion

        #region Private fields

        /// <summary>
        /// Default timeout value for connecting a server.
        /// </summary>
        private const int DefaultConnectionAttemptTimeout = 15000; //15 seconds.

        /// <summary>
        /// Default ping interval in milliseconds.
        /// </summary>
        private const int DefaultPingInterval = 30000; //30 seconds.

        /// <summary>
        /// Maximum number of RTT samples to keep for averaging.
        /// </summary>
        private const int MaxRttSamples = 10;

        /// <summary>
        /// The communication channel that is used by client to send and receive messages.
        /// </summary>
        private ICommunicationChannel _communicationChannel;

        /// <summary>
        /// This timer is used to send PingMessage messages to server periodically.
        /// </summary>
        private readonly Timer _pingTimer;

        /// <summary>
        /// Tracks pending ping messages by their MessageId to the Stopwatch started when sent.
        /// </summary>
        private readonly ConcurrentDictionary<string, Stopwatch> _pendingPings = new ConcurrentDictionary<string, Stopwatch>();

        /// <summary>
        /// Circular buffer of recent RTT samples for computing average.
        /// </summary>
        private readonly long[] _rttSamples = new long[MaxRttSamples];

        /// <summary>
        /// Number of RTT samples collected (capped at MaxRttSamples).
        /// </summary>
        private int _rttCount;

        /// <summary>
        /// Sum of all samples in the RTT buffer for fast average computation.
        /// </summary>
        private long _rttSum;

        /// <summary>
        /// Index of the next position to write in the circular buffer.
        /// </summary>
        private int _rttIndex;

        /// <summary>
        /// Lock object for RTT sample buffer updates.
        /// </summary>
        private readonly object _rttLock = new object();

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor.
        /// </summary>
        protected ScsClientBase()
        {
            _pingTimer = new Timer(DefaultPingInterval);
            _pingTimer.Elapsed += PingTimer_Elapsed;
            ConnectTimeout = DefaultConnectionAttemptTimeout;
            WireProtocol = WireProtocolManager.GetDefaultWireProtocol();
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Connects to server.
        /// </summary>
        public void Connect()
        {
            WireProtocol.Reset();
            _communicationChannel = CreateCommunicationChannel();
            _communicationChannel.WireProtocol = WireProtocol;
            _communicationChannel.Disconnected += CommunicationChannel_Disconnected;
            _communicationChannel.MessageReceived += CommunicationChannel_MessageReceived;
            _communicationChannel.MessageSent += CommunicationChannel_MessageSent;
            _communicationChannel.Start();
            _pingTimer.Start();
            OnConnected();
        }

        /// <summary>
        /// Disconnects from server.
        /// Does nothing if already disconnected.
        /// </summary>
        public void Disconnect()
        {
            if (CommunicationState != CommunicationStates.Connected)
            {
                return;
            }

            _communicationChannel.Disconnect();
        }

        /// <summary>
        /// Disposes this object and closes underlying connection.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="message">Message to be sent</param>
        /// <exception cref="CommunicationStateException">Throws a CommunicationStateException if client is not connected to the server.</exception>
        public void SendMessage(IScsMessage message)
        {
            if (CommunicationState != CommunicationStates.Connected)
            {
                throw new CommunicationStateException("Client is not connected to the server.");
            }

            _communicationChannel.SendMessage(message);
        }

        #endregion

        #region Abstract methods

        /// <summary>
        /// This method is implemented by derived classes to create appropriate communication channel.
        /// </summary>
        /// <returns>Ready communication channel to communicate</returns>
        protected abstract ICommunicationChannel CreateCommunicationChannel();

        #endregion

        #region Private methods

        /// <summary>
        /// Handles MessageReceived event of _communicationChannel object.
        /// </summary>
        /// <param name="sender">Source of event</param>
        /// <param name="e">Event arguments</param>
        private void CommunicationChannel_MessageReceived(object sender, MessageEventArgs e)
        {
            var pingMessage = e.Message as ScsPingMessage;
            if (pingMessage != null)
            {
                HandlePingReply(pingMessage);
                return;
            }

            OnMessageReceived(e.Message);
        }

        /// <summary>
        /// Handles a received ping reply by computing RTT from the matching pending ping.
        /// </summary>
        /// <param name="pingMessage">The received ping reply message</param>
        private void HandlePingReply(ScsPingMessage pingMessage)
        {
            if (string.IsNullOrEmpty(pingMessage.RepliedMessageId))
            {
                return;
            }

            Stopwatch sw;
            if (!_pendingPings.TryRemove(pingMessage.RepliedMessageId, out sw))
            {
                return;
            }

            sw.Stop();
            var rttMs = sw.ElapsedMilliseconds;

            LastPingRtt = rttMs;
            RecordRttSample(rttMs);
            OnPingCompleted(rttMs);
        }

        /// <summary>
        /// Records an RTT sample into the circular buffer and updates the running sum.
        /// </summary>
        /// <param name="rttMs">Round-trip time in milliseconds</param>
        private void RecordRttSample(long rttMs)
        {
            lock (_rttLock)
            {
                if (_rttCount < MaxRttSamples)
                {
                    _rttSamples[_rttIndex] = rttMs;
                    _rttSum += rttMs;
                    _rttCount++;
                }
                else
                {
                    _rttSum -= _rttSamples[_rttIndex];
                    _rttSamples[_rttIndex] = rttMs;
                    _rttSum += rttMs;
                }

                _rttIndex = (_rttIndex + 1) % MaxRttSamples;
            }
        }

        /// <summary>
        /// Handles MessageSent event of _communicationChannel object.
        /// Starts RTT tracking for outgoing ping messages.
        /// </summary>
        /// <param name="sender">Source of event</param>
        /// <param name="e">Event arguments</param>
        private void CommunicationChannel_MessageSent(object sender, MessageEventArgs e)
        {
            if (e.Message is ScsPingMessage && string.IsNullOrEmpty(e.Message.RepliedMessageId))
            {
                _pendingPings[e.Message.MessageId] = Stopwatch.StartNew();
            }

            OnMessageSent(e.Message);
        }

        /// <summary>
        /// Handles Disconnected event of _communicationChannel object.
        /// </summary>
        /// <param name="sender">Source of event</param>
        /// <param name="e">Event arguments</param>
        private void CommunicationChannel_Disconnected(object sender, EventArgs e)
        {
            _pingTimer.Stop();
            _pendingPings.Clear();
            OnDisconnected();
        }

        /// <summary>
        /// Handles Elapsed event of _pingTimer to send PingMessage messages to server.
        /// </summary>
        /// <param name="sender">Source of event</param>
        /// <param name="e">Event arguments</param>
        private void PingTimer_Elapsed(object sender, EventArgs e)
        {
            if (CommunicationState != CommunicationStates.Connected)
            {
                return;
            }

            try
            {
                var lastMinute = DateTime.Now.AddMinutes(-1);
                if (_communicationChannel.LastReceivedMessageTime > lastMinute || _communicationChannel.LastSentMessageTime > lastMinute)
                {
                    return;
                }

                _communicationChannel.SendMessage(new ScsPingMessage());
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.Write($"PingTimer_Elapsed: {exception}");
            }
        }

        #endregion

        #region Event raising methods

        /// <summary>
        /// Raises Connected event.
        /// </summary>
        protected virtual void OnConnected()
        {
            var handler = Connected;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raises Disconnected event.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            var handler = Disconnected;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raises MessageReceived event.
        /// </summary>
        /// <param name="message">Received message</param>
        protected virtual void OnMessageReceived(IScsMessage message)
        {
            var handler = MessageReceived;
            if (handler != null)
            {
                handler(this, new MessageEventArgs(message));
            }
        }

        /// <summary>
        /// Raises MessageSent event.
        /// </summary>
        /// <param name="message">Received message</param>
        protected virtual void OnMessageSent(IScsMessage message)
        {
            var handler = MessageSent;
            if (handler != null)
            {
                handler(this, new MessageEventArgs(message));
            }
        }

        /// <summary>
        /// Raises PingCompleted event.
        /// </summary>
        /// <param name="rttMs">Round-trip time in milliseconds</param>
        protected virtual void OnPingCompleted(long rttMs)
        {
            var handler = PingCompleted;
            if (handler != null)
            {
                handler(this, new PingCompletedEventArgs(rttMs));
            }
        }

        #endregion
    }
}
