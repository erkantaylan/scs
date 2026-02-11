using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Communication.Protocols.BinarySerialization;
using Hik.Communication.ScsServices.Communication.Messages;
using Xunit;

namespace Scs.Tests;

/// <summary>
/// Serialization roundtrip tests for all message types using BinarySerializationProtocol.
/// Tests the framing protocol (4-byte length prefix), protocol version header byte,
/// MessagePack serialization, and message accumulation.
/// </summary>
public class SerializationTests
{
    private readonly BinarySerializationProtocol _protocol = new();

    private IScsMessage RoundTrip(IScsMessage message)
    {
        var bytes = _protocol.GetBytes(message);
        var messages = _protocol.CreateMessages(bytes).ToList();
        Assert.Single(messages);
        return messages[0];
    }

    [Fact]
    public void ScsMessage_RoundTrip_PreservesFields()
    {
        var original = new ScsMessage();
        var result = (ScsMessage)RoundTrip(original);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Null(result.RepliedMessageId);
    }

    [Fact]
    public void ScsMessage_WithReply_PreservesRepliedMessageId()
    {
        var replyId = Guid.NewGuid().ToString();
        var original = new ScsMessage(replyId);
        var result = (ScsMessage)RoundTrip(original);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal(replyId, result.RepliedMessageId);
    }

    [Fact]
    public void ScsTextMessage_RoundTrip_PreservesText()
    {
        var original = new ScsTextMessage("Hello, SCS!");
        var result = (ScsTextMessage)RoundTrip(original);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal("Hello, SCS!", result.Text);
    }

    [Fact]
    public void ScsTextMessage_EmptyText_RoundTrips()
    {
        var original = new ScsTextMessage("");
        var result = (ScsTextMessage)RoundTrip(original);

        Assert.Equal("", result.Text);
    }

    [Fact]
    public void ScsTextMessage_NullText_RoundTrips()
    {
        var original = new ScsTextMessage();
        var result = (ScsTextMessage)RoundTrip(original);

        Assert.Null(result.Text);
    }

    [Fact]
    public void ScsTextMessage_WithReply_PreservesAll()
    {
        var replyId = Guid.NewGuid().ToString();
        var original = new ScsTextMessage("reply text", replyId);
        var result = (ScsTextMessage)RoundTrip(original);

        Assert.Equal("reply text", result.Text);
        Assert.Equal(replyId, result.RepliedMessageId);
    }

    [Fact]
    public void ScsRawDataMessage_RoundTrip_PreservesData()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
        var original = new ScsRawDataMessage(data);
        var result = (ScsRawDataMessage)RoundTrip(original);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal(data, result.MessageData);
    }

    [Fact]
    public void ScsRawDataMessage_EmptyData_RoundTrips()
    {
        var original = new ScsRawDataMessage(Array.Empty<byte>());
        var result = (ScsRawDataMessage)RoundTrip(original);

        Assert.Empty(result.MessageData);
    }

    [Fact]
    public void ScsRawDataMessage_NullData_RoundTrips()
    {
        var original = new ScsRawDataMessage();
        var result = (ScsRawDataMessage)RoundTrip(original);

        Assert.Null(result.MessageData);
    }

    [Fact]
    public void ScsRawDataMessage_LargePayload_RoundTrips()
    {
        var data = new byte[64 * 1024]; // 64 KB
        new Random(42).NextBytes(data);
        var original = new ScsRawDataMessage(data);
        var result = (ScsRawDataMessage)RoundTrip(original);

        Assert.Equal(data, result.MessageData);
    }

    [Fact]
    public void ScsPingMessage_RoundTrip_PreservesId()
    {
        var original = new ScsPingMessage();
        var result = (ScsPingMessage)RoundTrip(original);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Null(result.RepliedMessageId);
    }

    [Fact]
    public void ScsPingMessage_WithReply_PreservesRepliedId()
    {
        var replyId = Guid.NewGuid().ToString();
        var original = new ScsPingMessage(replyId);
        var result = (ScsPingMessage)RoundTrip(original);

        Assert.Equal(replyId, result.RepliedMessageId);
    }

    [Fact]
    public void ScsRemoteInvokeMessage_RoundTrip_PreservesAll()
    {
        var original = new ScsRemoteInvokeMessage
        {
            ServiceClassName = "ICalculator",
            MethodName = "Add",
            Parameters = new object[] { 1, 2 }
        };
        var result = (ScsRemoteInvokeMessage)RoundTrip(original);

        Assert.Equal("ICalculator", result.ServiceClassName);
        Assert.Equal("Add", result.MethodName);
        Assert.Equal(2, result.Parameters.Length);
        Assert.Equal(1, result.Parameters[0]);
        Assert.Equal(2, result.Parameters[1]);
    }

    [Fact]
    public void ScsRemoteInvokeReturnMessage_WithReturnValue_RoundTrips()
    {
        var original = new ScsRemoteInvokeReturnMessage
        {
            ReturnValue = 42,
            RepliedMessageId = "test-id"
        };
        var result = (ScsRemoteInvokeReturnMessage)RoundTrip(original);

        Assert.Equal(42, result.ReturnValue);
        Assert.Null(result.RemoteException);
        Assert.Equal("test-id", result.RepliedMessageId);
    }

    [Fact]
    public void ScsRemoteInvokeReturnMessage_WithException_RoundTrips()
    {
        var original = new ScsRemoteInvokeReturnMessage
        {
            RemoteException = new ScsRemoteException("Something failed"),
            RepliedMessageId = "test-id"
        };
        var result = (ScsRemoteInvokeReturnMessage)RoundTrip(original);

        Assert.Null(result.ReturnValue);
        Assert.NotNull(result.RemoteException);
        Assert.Contains("Something failed", result.RemoteException.Message);
    }

    [Fact]
    public void MultipleMessages_InSingleByteStream_AllDeserialized()
    {
        var protocol = new BinarySerializationProtocol();

        var msg1 = new ScsTextMessage("first");
        var msg2 = new ScsTextMessage("second");
        var msg3 = new ScsRawDataMessage(new byte[] { 1, 2, 3 });

        var bytes1 = protocol.GetBytes(msg1);
        var bytes2 = protocol.GetBytes(msg2);
        var bytes3 = protocol.GetBytes(msg3);

        // Combine all bytes into one stream
        var combined = new byte[bytes1.Length + bytes2.Length + bytes3.Length];
        Array.Copy(bytes1, 0, combined, 0, bytes1.Length);
        Array.Copy(bytes2, 0, combined, bytes1.Length, bytes2.Length);
        Array.Copy(bytes3, 0, combined, bytes1.Length + bytes2.Length, bytes3.Length);

        var messages = protocol.CreateMessages(combined).ToList();

        Assert.Equal(3, messages.Count);
        Assert.Equal("first", ((ScsTextMessage)messages[0]).Text);
        Assert.Equal("second", ((ScsTextMessage)messages[1]).Text);
        Assert.Equal(new byte[] { 1, 2, 3 }, ((ScsRawDataMessage)messages[2]).MessageData);
    }

    [Fact]
    public void PartialMessage_CompletedInSecondCall_Deserializes()
    {
        var protocol = new BinarySerializationProtocol();
        var original = new ScsTextMessage("split message");
        var bytes = protocol.GetBytes(original);

        // Split in the middle
        var half = bytes.Length / 2;
        var part1 = new byte[half];
        var part2 = new byte[bytes.Length - half];
        Array.Copy(bytes, 0, part1, 0, half);
        Array.Copy(bytes, half, part2, 0, bytes.Length - half);

        var messages1 = protocol.CreateMessages(part1).ToList();
        Assert.Empty(messages1);

        var messages2 = protocol.CreateMessages(part2).ToList();
        Assert.Single(messages2);
        Assert.Equal("split message", ((ScsTextMessage)messages2[0]).Text);
    }

    [Fact]
    public void ProtocolVersionByte_IsIncludedInSerializedOutput()
    {
        var protocol = new BinarySerializationProtocol();
        var message = new ScsTextMessage("test");
        var bytes = protocol.GetBytes(message);

        // After the 4-byte length prefix, the first byte should be the protocol version (1)
        Assert.Equal(1, bytes[4]);
    }
}
