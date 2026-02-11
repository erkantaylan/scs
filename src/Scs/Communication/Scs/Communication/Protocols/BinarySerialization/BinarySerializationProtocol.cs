using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.ScsServices.Communication.Messages;
using MessagePack;
using MessagePack.Formatters;
using IFormatterResolver = MessagePack.IFormatterResolver;

namespace Hik.Communication.Scs.Communication.Protocols.BinarySerialization
{
    /// <summary>
    /// Default communication protocol between server and clients to send and receive a message.
    /// It uses MessagePack to write and read messages.
    ///
    /// A Message format:
    /// [Message Length (4 bytes)][Protocol Version (1 byte)][MessagePack Content]
    ///
    /// If a message is serialized to byte array as N bytes, this protocol
    /// adds 4 bytes size information to head of the message bytes, so total length is (4 + 1 + N) bytes.
    ///
    /// This class can be derived to change serializer (default: MessagePack). To do this,
    /// SerializeMessage and DeserializeMessage methods must be overrided.
    /// </summary>
    public class BinarySerializationProtocol : IScsWireProtocol
    {
        #region Private fields

        /// <summary>
        /// Protocol version byte. Version 1 = MessagePack serialization.
        /// </summary>
        private const byte ProtocolVersion = 1;

        /// <summary>
        /// Maximum length of a message.
        /// </summary>
        private const int MaxMessageLength = 128 * 1024 * 1024; //128 Megabytes.

        /// <summary>
        /// This MemoryStream object is used to collect receiving bytes to build messages.
        /// </summary>
        private MemoryStream _receiveMemoryStream;

        /// <summary>
        /// MessagePack serializer options with custom resolvers for object and ScsRemoteException types.
        /// </summary>
        private static readonly MessagePackSerializerOptions SerializerOptions = MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.CompositeResolver.Create(
                new IMessagePackFormatter[] { new PrimitiveObjectFormatter(), new ScsRemoteExceptionFormatter() },
                new IFormatterResolver[] { MessagePack.Resolvers.StandardResolver.Instance }
            ));

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new instance of BinarySerializationProtocol.
        /// </summary>
        public BinarySerializationProtocol()
        {
            _receiveMemoryStream = new MemoryStream();
        }

        #endregion

        #region IScsWireProtocol implementation

        /// <summary>
        /// Serializes a message to a byte array to send to remote application.
        /// This method is synchronized. So, only one thread can call it concurrently.
        /// </summary>
        /// <param name="message">Message to be serialized</param>
        /// <exception cref="CommunicationException">Throws CommunicationException if message is bigger than maximum allowed message length.</exception>
        public byte[] GetBytes(IScsMessage message)
        {
            //Serialize the message to a byte array
            var serializedMessage = SerializeMessage(message);

            //Check for message length
            var messageLength = serializedMessage.Length;
            if (messageLength > MaxMessageLength)
            {
                throw new CommunicationException("Message is too big (" + messageLength + " bytes). Max allowed length is " + MaxMessageLength + " bytes.");
            }

            //Create a byte array including the length of the message (4 bytes) and serialized message content
            var bytes = new byte[messageLength + 4];
            WriteInt32(bytes, 0, messageLength);
            Array.Copy(serializedMessage, 0, bytes, 4, messageLength);

            //Return serialized message by this protocol
            return bytes;
        }

        /// <summary>
        /// Builds messages from a byte array that is received from remote application.
        /// The Byte array may contain just a part of a message, the protocol must
        /// cumulate bytes to build messages.
        /// This method is synchronized. So, only one thread can call it concurrently.
        /// </summary>
        /// <param name="receivedBytes">Received bytes from remote application</param>
        /// <returns>
        /// List of messages.
        /// Protocol can generate more than one message from a byte array.
        /// Also, if received bytes are not sufficient to build a message, the protocol
        /// may return an empty list (and save bytes to combine with next method call).
        /// </returns>
        public IEnumerable<IScsMessage> CreateMessages(byte[] receivedBytes)
        {
            //Write all received bytes to the _receiveMemoryStream
            _receiveMemoryStream.Write(receivedBytes, 0, receivedBytes.Length);
            //Create a list to collect messages
            var messages = new List<IScsMessage>();
            //Read all available messages and add to messages collection
            while (ReadSingleMessage(messages)) { }
            //Return message list
            return messages;
        }

        /// <summary>
        /// This method is called when connection with remote application is reset (connection is renewing or first connecting).
        /// So, wire protocol must reset itself.
        /// </summary>
        public void Reset()
        {
            if (_receiveMemoryStream.Length > 0)
            {
                _receiveMemoryStream = new MemoryStream();
            }
        }

        #endregion

        #region Protected virtual methods

        /// <summary>
        /// This method is used to serialize a IScsMessage to a byte array.
        /// This method can be overrided by derived classes to change serialization strategy.
        /// It is a couple with DeserializeMessage method and must be overrided together.
        /// </summary>
        /// <param name="message">Message to be serialized</param>
        /// <returns>
        /// Serialized message bytes.
        /// Does not include length of the message.
        /// </returns>
        protected virtual byte[] SerializeMessage(IScsMessage message)
        {
            var msgpackBytes = MessagePackSerializer.Serialize<IScsMessage>(message, SerializerOptions);
            var result = new byte[1 + msgpackBytes.Length];
            result[0] = ProtocolVersion;
            Array.Copy(msgpackBytes, 0, result, 1, msgpackBytes.Length);
            return result;
        }

        /// <summary>
        /// This method is used to deserialize a IScsMessage from it's bytes.
        /// This method can be overrided by derived classes to change deserialization strategy.
        /// It is a couple with SerializeMessage method and must be overrided together.
        /// </summary>
        /// <param name="bytes">
        /// Bytes of message to be deserialized (does not include message length. It consist
        /// of a single whole message)
        /// </param>
        /// <returns>Deserialized message</returns>
        protected virtual IScsMessage DeserializeMessage(byte[] bytes)
        {
            if (bytes.Length < 1)
            {
                throw new CommunicationException("Message is too short to contain a protocol version byte.");
            }

            var version = bytes[0];
            if (version != ProtocolVersion)
            {
                throw new CommunicationException("Unsupported protocol version: " + version + ". Expected: " + ProtocolVersion);
            }

            try
            {
                var msgpackBytes = new byte[bytes.Length - 1];
                Array.Copy(bytes, 1, msgpackBytes, 0, msgpackBytes.Length);
                return MessagePackSerializer.Deserialize<IScsMessage>(msgpackBytes, SerializerOptions);
            }
            catch (Exception exception)
            {
                Reset();
                throw new CommunicationException("Error while deserializing message", exception);
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// This method tries to read a single message and add to the messages collection.
        /// </summary>
        /// <param name="messages">Messages collection to collect messages</param>
        /// <returns>
        /// Returns a boolean value indicates that if there is a need to re-call this method.
        /// </returns>
        /// <exception cref="CommunicationException">Throws CommunicationException if message is bigger than maximum allowed message length.</exception>
        private bool ReadSingleMessage(ICollection<IScsMessage> messages)
        {
            //Go to the begining of the stream
            _receiveMemoryStream.Position = 0;

            //If stream has less than 4 bytes, that means we can not even read length of the message
            //So, return false to wait more bytes from remore application.
            if (_receiveMemoryStream.Length < 4)
            {
                return false;
            }

            //Read length of the message
            var messageLength = ReadInt32(_receiveMemoryStream);
            if (messageLength > MaxMessageLength)
            {
                throw new Exception("Message is too big (" + messageLength + " bytes). Max allowed length is " + MaxMessageLength + " bytes.");
            }

            //If message is zero-length (It must not be but good approach to check it)
            if (messageLength == 0)
            {
                //if no more bytes, return immediately
                if (_receiveMemoryStream.Length == 4)
                {
                    _receiveMemoryStream = new MemoryStream(); //Clear the stream
                    return false;
                }

                //Create a new memory stream from current except first 4-bytes.
                var bytes = _receiveMemoryStream.ToArray();
                _receiveMemoryStream = new MemoryStream();
                _receiveMemoryStream.Write(bytes, 4, bytes.Length - 4);
                return true;
            }

            //If all bytes of the message is not received yet, return to wait more bytes
            if (_receiveMemoryStream.Length < (4 + messageLength))
            {
                _receiveMemoryStream.Position = _receiveMemoryStream.Length;
                return false;
            }

            //Read bytes of serialized message and deserialize it
            var serializedMessageBytes = ReadByteArray(_receiveMemoryStream, messageLength);

            messages.Add(DeserializeMessage(serializedMessageBytes));

            //Read remaining bytes to an array
            var remainingBytes = ReadByteArray(_receiveMemoryStream, (int)(_receiveMemoryStream.Length - (4 + messageLength)));

            //Re-create the receive memory stream and write remaining bytes
            _receiveMemoryStream = new MemoryStream();
            _receiveMemoryStream.Write(remainingBytes, 0, remainingBytes.Length);

            //Return true to re-call this method to try to read next message
            return (remainingBytes.Length > 4);
        }

        /// <summary>
        /// Writes a int value to a byte array from a starting index.
        /// </summary>
        /// <param name="buffer">Byte array to write int value</param>
        /// <param name="startIndex">Start index of byte array to write</param>
        /// <param name="number">An integer value to write</param>
        private static void WriteInt32(byte[] buffer, int startIndex, int number)
        {
            buffer[startIndex] = (byte)((number >> 24) & 0xFF);
            buffer[startIndex + 1] = (byte)((number >> 16) & 0xFF);
            buffer[startIndex + 2] = (byte)((number >> 8) & 0xFF);
            buffer[startIndex + 3] = (byte)((number) & 0xFF);
        }

        /// <summary>
        /// Deserializes and returns a serialized integer.
        /// </summary>
        /// <returns>Deserialized integer</returns>
        private static int ReadInt32(Stream stream)
        {
            var buffer = ReadByteArray(stream, 4);
            return ((buffer[0] << 24) |
                    (buffer[1] << 16) |
                    (buffer[2] << 8) |
                    (buffer[3])
                   );
        }

        /// <summary>
        /// Reads a byte array with specified length.
        /// </summary>
        /// <param name="stream">Stream to read from</param>
        /// <param name="length">Length of the byte array to read</param>
        /// <returns>Read byte array</returns>
        /// <exception cref="EndOfStreamException">Throws EndOfStreamException if can not read from stream.</exception>
        private static byte[] ReadByteArray(Stream stream, int length)
        {
            var buffer = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                var read = stream.Read(buffer, totalRead, length - totalRead);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Can not read from stream! Input stream is closed.");
                }

                totalRead += read;
            }

            return buffer;
        }

        #endregion

        #region Custom MessagePack formatters

        /// <summary>
        /// Custom MessagePack formatter for <see cref="object"/> that handles common primitive types.
        /// Each value is serialized as a 2-element array [type_tag, value], or a 1-element array [0] for null.
        /// This ensures the wire format is identical across all .NET runtimes.
        /// </summary>
        internal sealed class PrimitiveObjectFormatter : IMessagePackFormatter<object>
        {
            private const byte TagNull = 0;
            private const byte TagInt32 = 1;
            private const byte TagString = 2;
            private const byte TagInt64 = 3;
            private const byte TagDouble = 4;
            private const byte TagBool = 5;
            private const byte TagByteArray = 6;

            public void Serialize(ref MessagePackWriter writer, object value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteArrayHeader(1);
                    writer.Write(TagNull);
                    return;
                }

                writer.WriteArrayHeader(2);
                switch (value)
                {
                    case int i:
                        writer.Write(TagInt32);
                        writer.Write(i);
                        break;
                    case string s:
                        writer.Write(TagString);
                        writer.Write(s);
                        break;
                    case long l:
                        writer.Write(TagInt64);
                        writer.Write(l);
                        break;
                    case double d:
                        writer.Write(TagDouble);
                        writer.Write(d);
                        break;
                    case bool b:
                        writer.Write(TagBool);
                        writer.Write(b);
                        break;
                    case byte[] arr:
                        writer.Write(TagByteArray);
                        writer.Write(arr);
                        break;
                    default:
                        throw new NotSupportedException("Cannot serialize object of type: " + value.GetType());
                }
            }

            public object Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                var count = reader.ReadArrayHeader();
                var tag = reader.ReadByte();

                if (tag == TagNull)
                {
                    return null;
                }

                switch (tag)
                {
                    case TagInt32: return reader.ReadInt32();
                    case TagString: return reader.ReadString();
                    case TagInt64: return reader.ReadInt64();
                    case TagDouble: return reader.ReadDouble();
                    case TagBool: return reader.ReadBoolean();
                    case TagByteArray:
                        var seq = reader.ReadBytes();
                        if (!seq.HasValue) return null;
                        return seq.Value.ToArray();
                    default:
                        throw new NotSupportedException("Unknown object tag: " + tag);
                }
            }
        }

        /// <summary>
        /// Custom MessagePack formatter for <see cref="ScsRemoteException"/>.
        /// Serializes only the exception message string since Exception subclasses cannot be
        /// directly serialized by MessagePack.
        /// </summary>
        internal sealed class ScsRemoteExceptionFormatter : IMessagePackFormatter<ScsRemoteException>
        {
            public void Serialize(ref MessagePackWriter writer, ScsRemoteException value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.Write(value.Message ?? "");
            }

            public ScsRemoteException Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                {
                    return null;
                }

                var message = reader.ReadString();
                return new ScsRemoteException(message);
            }
        }

        #endregion
    }
}
