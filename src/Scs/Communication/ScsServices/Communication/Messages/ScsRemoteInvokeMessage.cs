using System;
using Hik.Communication.Scs.Communication.Messages;
using MessagePack;

namespace Hik.Communication.ScsServices.Communication.Messages
{
    /// <summary>
    /// This message is sent to invoke a method of a remote application.
    /// </summary>
    [Serializable]
    [MessagePackObject]
    public class ScsRemoteInvokeMessage : ScsMessage
    {
        /// <summary>
        /// Name of the remove service class.
        /// </summary>
        [Key(2)]
        public string ServiceClassName { get; set; }

        /// <summary>
        /// Method of remote application to invoke.
        /// </summary>
        [Key(3)]
        public string MethodName { get; set; }

        /// <summary>
        /// Parameters of method.
        /// </summary>
        [Key(4)]
        public object[] Parameters { get; set; }

        /// <summary>
        /// Represents this object as string.
        /// </summary>
        /// <returns>String representation of this object</returns>
        public override string ToString()
        {
            return string.Format("ScsRemoteInvokeMessage: {0}.{1}(...)", ServiceClassName, MethodName);
        }
    }
}
