using MessagePack;
using Hik.Communication.ScsServices.Communication.Messages;

namespace Hik.Communication.Scs.Communication.Messages
{
    /// <summary>
    /// Represents a message that is sent and received by server and client.
    /// </summary>
    [Union(0, typeof(ScsMessage))]
    [Union(1, typeof(ScsPingMessage))]
    [Union(2, typeof(ScsTextMessage))]
    [Union(3, typeof(ScsRawDataMessage))]
    [Union(4, typeof(ScsRemoteInvokeMessage))]
    [Union(5, typeof(ScsRemoteInvokeReturnMessage))]
    public interface IScsMessage
    {
        /// <summary>
        /// Unique identified for this message.
        /// </summary>
        string MessageId { get; }

        /// <summary>
        /// Unique identified for this message.
        /// </summary>
        string RepliedMessageId { get; set; }
    }
}
