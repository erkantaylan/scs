using Hik.Communication.Scs.Communication.Messages;
using Hik.Communication.Scs.Communication.Protocols;
using Hik.Communication.Scs.Communication.Protocols.BinarySerialization;
using Hik.Communication.ScsServices.Communication.Messages;

namespace Scs.Tests;

/// <summary>
/// A wire protocol that replaces BinaryFormatter (removed in .NET 9+) with simple binary encoding.
/// Extends BinarySerializationProtocol to reuse the framing logic (4-byte length prefix + message accumulation).
/// </summary>
internal class TestWireProtocol : BinarySerializationProtocol
{
    private const byte TypeScsMessage = 0;
    private const byte TypeTextMessage = 1;
    private const byte TypeRawDataMessage = 2;
    private const byte TypePingMessage = 3;
    private const byte TypeRemoteInvokeMessage = 4;
    private const byte TypeRemoteInvokeReturnMessage = 5;

    private const byte ObjNull = 0;
    private const byte ObjInt = 1;
    private const byte ObjString = 2;
    private const byte ObjLong = 3;
    private const byte ObjDouble = 4;
    private const byte ObjBool = 5;
    private const byte ObjByteArray = 6;

    protected override byte[] SerializeMessage(IScsMessage message)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        switch (message)
        {
            case ScsRemoteInvokeReturnMessage ret:
                bw.Write(TypeRemoteInvokeReturnMessage);
                WriteCommon(bw, ret);
                WriteObject(bw, ret.ReturnValue);
                WriteException(bw, ret.RemoteException);
                break;
            case ScsRemoteInvokeMessage invoke:
                bw.Write(TypeRemoteInvokeMessage);
                WriteCommon(bw, invoke);
                WriteNullableString(bw, invoke.ServiceClassName);
                WriteNullableString(bw, invoke.MethodName);
                WriteObjectArray(bw, invoke.Parameters);
                break;
            case ScsPingMessage ping:
                bw.Write(TypePingMessage);
                WriteCommon(bw, ping);
                break;
            case ScsRawDataMessage raw:
                bw.Write(TypeRawDataMessage);
                WriteCommon(bw, raw);
                if (raw.MessageData == null)
                {
                    bw.Write(false);
                }
                else
                {
                    bw.Write(true);
                    bw.Write(raw.MessageData.Length);
                    bw.Write(raw.MessageData);
                }
                break;
            case ScsTextMessage txt:
                bw.Write(TypeTextMessage);
                WriteCommon(bw, txt);
                WriteNullableString(bw, txt.Text);
                break;
            case ScsMessage msg:
                bw.Write(TypeScsMessage);
                WriteCommon(bw, msg);
                break;
            default:
                throw new NotSupportedException($"Unknown message type: {message.GetType()}");
        }

        bw.Flush();
        return ms.ToArray();
    }

    protected override IScsMessage DeserializeMessage(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        var typeId = br.ReadByte();

        return typeId switch
        {
            TypeScsMessage => ReadScsMessage(br),
            TypeTextMessage => ReadTextMessage(br),
            TypeRawDataMessage => ReadRawDataMessage(br),
            TypePingMessage => ReadPingMessage(br),
            TypeRemoteInvokeMessage => ReadRemoteInvokeMessage(br),
            TypeRemoteInvokeReturnMessage => ReadRemoteInvokeReturnMessage(br),
            _ => throw new NotSupportedException($"Unknown type id: {typeId}")
        };
    }

    private static void WriteCommon(BinaryWriter bw, IScsMessage msg)
    {
        WriteNullableString(bw, msg.MessageId);
        WriteNullableString(bw, msg.RepliedMessageId);
    }

    private static void ReadCommon(BinaryReader br, ScsMessage msg)
    {
        msg.MessageId = ReadNullableString(br);
        msg.RepliedMessageId = ReadNullableString(br);
    }

    private static void WriteNullableString(BinaryWriter bw, string? value)
    {
        if (value == null)
        {
            bw.Write(false);
        }
        else
        {
            bw.Write(true);
            bw.Write(value);
        }
    }

    private static string? ReadNullableString(BinaryReader br)
    {
        return br.ReadBoolean() ? br.ReadString() : null;
    }

    private static void WriteObject(BinaryWriter bw, object? value)
    {
        switch (value)
        {
            case null:
                bw.Write(ObjNull);
                break;
            case int i:
                bw.Write(ObjInt);
                bw.Write(i);
                break;
            case string s:
                bw.Write(ObjString);
                bw.Write(s);
                break;
            case long l:
                bw.Write(ObjLong);
                bw.Write(l);
                break;
            case double d:
                bw.Write(ObjDouble);
                bw.Write(d);
                break;
            case bool b:
                bw.Write(ObjBool);
                bw.Write(b);
                break;
            case byte[] arr:
                bw.Write(ObjByteArray);
                bw.Write(arr.Length);
                bw.Write(arr);
                break;
            default:
                throw new NotSupportedException($"Cannot serialize object of type: {value.GetType()}");
        }
    }

    private static object? ReadObject(BinaryReader br)
    {
        var tag = br.ReadByte();
        return tag switch
        {
            ObjNull => null,
            ObjInt => br.ReadInt32(),
            ObjString => br.ReadString(),
            ObjLong => br.ReadInt64(),
            ObjDouble => br.ReadDouble(),
            ObjBool => br.ReadBoolean(),
            ObjByteArray => br.ReadBytes(br.ReadInt32()),
            _ => throw new NotSupportedException($"Unknown object tag: {tag}")
        };
    }

    private static void WriteObjectArray(BinaryWriter bw, object[]? arr)
    {
        if (arr == null)
        {
            bw.Write(-1);
            return;
        }

        bw.Write(arr.Length);
        foreach (var item in arr)
            WriteObject(bw, item);
    }

    private static object[]? ReadObjectArray(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len < 0) return null;

        var arr = new object[len];
        for (int i = 0; i < len; i++)
            arr[i] = ReadObject(br)!;
        return arr;
    }

    private static void WriteException(BinaryWriter bw, ScsRemoteException? ex)
    {
        if (ex == null)
        {
            bw.Write(false);
            return;
        }

        bw.Write(true);
        bw.Write(ex.Message ?? "");
    }

    private static ScsRemoteException? ReadException(BinaryReader br)
    {
        if (!br.ReadBoolean()) return null;
        var message = br.ReadString();
        return new ScsRemoteException(message);
    }

    private static ScsMessage ReadScsMessage(BinaryReader br)
    {
        var msg = new ScsMessage();
        ReadCommon(br, msg);
        return msg;
    }

    private static ScsTextMessage ReadTextMessage(BinaryReader br)
    {
        var msg = new ScsTextMessage();
        ReadCommon(br, msg);
        msg.Text = ReadNullableString(br);
        return msg;
    }

    private static ScsRawDataMessage ReadRawDataMessage(BinaryReader br)
    {
        var msg = new ScsRawDataMessage();
        ReadCommon(br, msg);
        if (br.ReadBoolean())
        {
            var len = br.ReadInt32();
            msg.MessageData = br.ReadBytes(len);
        }
        return msg;
    }

    private static ScsPingMessage ReadPingMessage(BinaryReader br)
    {
        var msg = new ScsPingMessage();
        ReadCommon(br, msg);
        return msg;
    }

    private static ScsRemoteInvokeMessage ReadRemoteInvokeMessage(BinaryReader br)
    {
        var msg = new ScsRemoteInvokeMessage();
        ReadCommon(br, msg);
        msg.ServiceClassName = ReadNullableString(br);
        msg.MethodName = ReadNullableString(br);
        msg.Parameters = ReadObjectArray(br);
        return msg;
    }

    private static ScsRemoteInvokeReturnMessage ReadRemoteInvokeReturnMessage(BinaryReader br)
    {
        var msg = new ScsRemoteInvokeReturnMessage();
        ReadCommon(br, msg);
        msg.ReturnValue = ReadObject(br);
        msg.RemoteException = ReadException(br);
        return msg;
    }
}

/// <summary>
/// Factory for creating TestWireProtocol instances.
/// </summary>
internal class TestWireProtocolFactory : IScsWireProtocolFactory
{
    public IScsWireProtocol CreateWireProtocol() => new TestWireProtocol();
}
