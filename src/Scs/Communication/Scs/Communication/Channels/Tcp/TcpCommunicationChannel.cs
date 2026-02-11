using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Hik.Communication.Scs.Communication.EndPoints;
using Hik.Communication.Scs.Communication.EndPoints.Tcp;
using Hik.Communication.Scs.Communication.Messages;

namespace Hik.Communication.Scs.Communication.Channels.Tcp
{
    /// <summary>
    /// This class is used to communicate with a remote application over TCP/IP protocol.
    /// </summary>
    internal class TcpCommunicationChannel : CommunicationChannelBase
    {
        #region Public properties

        ///<summary>
        /// Gets the endpoint of remote application.
        ///</summary>
        public override ScsEndPoint RemoteEndPoint
        {
            get
            {
                return _remoteEndPoint;
            }
        }
        private readonly ScsTcpEndPoint _remoteEndPoint;

        #endregion

        #region Private fields

        /// <summary>
        /// Size of the buffer that is used to receive bytes from TCP socket.
        /// </summary>
        private const int ReceiveBufferSize = 4 * 1024; //4KB

        /// <summary>
        /// This buffer is used to receive bytes 
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        /// Socket object to send/reveice messages.
        /// </summary>
        private readonly Socket _clientSocket;

        /// <summary>
        /// A flag to control thread's running
        /// </summary>
        private volatile bool _running;

        /// <summary>
        /// This object is just used for thread synchronizing (locking).
        /// </summary>
        private readonly object _syncLock;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new TcpCommunicationChannel object.
        /// </summary>
        /// <param name="clientSocket">A connected Socket object that is
        /// used to communicate over network</param>
        /// <param name="socketOptions">TCP socket options to apply, or null for defaults</param>
        public TcpCommunicationChannel(Socket clientSocket, TcpSocketOptions socketOptions = null)
        {
            _clientSocket = clientSocket;
            var options = socketOptions ?? new TcpSocketOptions();
            ApplySocketOptions(_clientSocket, options);

            var ipEndPoint = (IPEndPoint)_clientSocket.RemoteEndPoint;
            _remoteEndPoint = new ScsTcpEndPoint(ipEndPoint.Address.ToString(), ipEndPoint.Port);

            _buffer = new byte[ReceiveBufferSize];
            _syncLock = new object();
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Disconnects from remote application and closes channel.
        /// </summary>
        public override void Disconnect()
        {
            _running = false;
            try
            {
                if (_clientSocket.Connected)
                {
                    _clientSocket.Close();
                }

                _clientSocket.Dispose();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.Write($"Disconnect: {exception}");
            }

            CommunicationState = CommunicationStates.Disconnected;
            OnDisconnected();
        }

        #endregion

        #region Protected methods

        /// <summary>
        /// Starts the thread to receive messages from socket.
        /// </summary>
        protected override void StartInternal()
        {
            _running = true;
            _clientSocket.BeginReceive(_buffer, 0, _buffer.Length, 0, new AsyncCallback(ReceiveCallback), null);
        }

        /// <summary>
        /// Sends a message to the remote application.
        /// </summary>
        /// <param name="message">Message to be sent</param>
        protected override void SendMessageInternal(IScsMessage message)
        {
            //Send message
            var totalSent = 0;
            lock (_syncLock)
            {
                //Create a byte array from message according to current protocol
                var messageBytes = WireProtocol.GetBytes(message);
                //Send all bytes to the remote application
                while (totalSent < messageBytes.Length)
                {
                    var sent = _clientSocket.Send(messageBytes, totalSent, messageBytes.Length - totalSent, SocketFlags.None);
                    if (sent <= 0)
                    {
                        throw new CommunicationException("Message could not be sent via TCP socket. Only " + totalSent + " bytes of " + messageBytes.Length + " bytes are sent.");
                    }

                    totalSent += sent;
                }

                LastSentMessageTime = DateTime.Now;
                OnMessageSent(message);
            }
        }

        #endregion

        #region Private methods

        // TCP_KEEPIDLE – seconds before first keep-alive probe (Linux)
        private const int TcpKeepIdle = 4;
        // TCP_KEEPINTVL – seconds between keep-alive probes (Linux)
        private const int TcpKeepInterval = 5;

        /// <summary>
        /// Applies the given TCP socket options to a connected socket.
        /// </summary>
        private static void ApplySocketOptions(Socket socket, TcpSocketOptions options)
        {
            socket.NoDelay = options.NoDelay;
            socket.SendTimeout = options.SendTimeout;
            socket.ReceiveTimeout = options.ReceiveTimeout;

            if (options.KeepAliveEnabled)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (options.KeepAliveTimeSeconds.HasValue)
                    {
                        try
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.Tcp,
                                (SocketOptionName)TcpKeepIdle,
                                options.KeepAliveTimeSeconds.Value);
                        }
                        catch (SocketException)
                        {
                            // Platform doesn't support this option – silently ignore
                        }
                    }

                    if (options.KeepAliveIntervalSeconds.HasValue)
                    {
                        try
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.Tcp,
                                (SocketOptionName)TcpKeepInterval,
                                options.KeepAliveIntervalSeconds.Value);
                        }
                        catch (SocketException)
                        {
                            // Platform doesn't support this option – silently ignore
                        }
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // On Windows, use IOControl with tcp_keepalive structure
                    var keepAliveTime = options.KeepAliveTimeSeconds.HasValue
                        ? (uint)(options.KeepAliveTimeSeconds.Value * 1000)
                        : 0u;
                    var keepAliveInterval = options.KeepAliveIntervalSeconds.HasValue
                        ? (uint)(options.KeepAliveIntervalSeconds.Value * 1000)
                        : 0u;

                    if (keepAliveTime > 0 || keepAliveInterval > 0)
                    {
                        try
                        {
                            // tcp_keepalive struct: onoff (4 bytes), keepalivetime (4 bytes), keepaliveinterval (4 bytes)
                            var inValue = new byte[12];
                            BitConverter.GetBytes(1u).CopyTo(inValue, 0); // onoff = 1
                            BitConverter.GetBytes(keepAliveTime > 0 ? keepAliveTime : 7200000u).CopyTo(inValue, 4);
                            BitConverter.GetBytes(keepAliveInterval > 0 ? keepAliveInterval : 1000u).CopyTo(inValue, 8);
                            // SIO_KEEPALIVE_VALS = 0x98000004 (Windows IOControl code)
                            socket.IOControl(unchecked((int)0x98000004), inValue, null);
                        }
                        catch (SocketException)
                        {
                            // Platform doesn't support this option – silently ignore
                        }
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS uses TCP_KEEPALIVE (0x10) for idle time
                    if (options.KeepAliveTimeSeconds.HasValue)
                    {
                        try
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.Tcp,
                                (SocketOptionName)0x10,
                                options.KeepAliveTimeSeconds.Value);
                        }
                        catch (SocketException)
                        {
                            // Platform doesn't support this option – silently ignore
                        }
                    }

                    if (options.KeepAliveIntervalSeconds.HasValue)
                    {
                        try
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.Tcp,
                                (SocketOptionName)0x101,
                                options.KeepAliveIntervalSeconds.Value);
                        }
                        catch (SocketException)
                        {
                            // Platform doesn't support this option – silently ignore
                        }
                    }
                }
            }
            else
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, false);
            }
        }

        /// <summary>
        /// This method is used as callback method in _clientSocket's BeginReceive method.
        /// It reveives bytes from socker.
        /// </summary>
        /// <param name="ar">Asyncronous call result</param>
        private void ReceiveCallback(IAsyncResult ar)
        {
            if (!_running)
            {
                return;
            }

            try
            {
                //Get received bytes count
                var bytesRead = _clientSocket.EndReceive(ar);
                if (bytesRead > 0)
                {
                    LastReceivedMessageTime = DateTime.Now;

                    //Copy received bytes to a new byte array
                    var receivedBytes = new byte[bytesRead];
                    Array.Copy(_buffer, 0, receivedBytes, 0, bytesRead);

                    try
                    {
                        //Read messages according to current wire protocol
                        var messages = WireProtocol.CreateMessages(receivedBytes);
                        //Raise MessageReceived event for all received messages
                        foreach (var message in messages)
                        {
                            OnMessageReceived(message);
                        }
                    }
                    catch (SerializationException ex)
                    {
                        System.Diagnostics.Trace.Write($"Error while deserializing message: {ex}");
                    }
                }
                else
                {
                    throw new CommunicationException("Tcp socket is closed");
                }

                //Read more bytes if still running
                if (_running)
                {
                    _clientSocket.BeginReceive(_buffer, 0, _buffer.Length, 0, new AsyncCallback(ReceiveCallback), null);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.Write($"ReceiveCallback: {exception}");
                Disconnect();
            }
        }

        #endregion
    }
}
