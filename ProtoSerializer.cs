using System.Reflection;
using ProtoBuf;

namespace Evolits.NetworkProtocol;

/// <summary>
/// Serializes and deserializes protobuf classes within specified assemblies with class information deterministically.
/// </summary>
public class ProtoSerializer
{
    private List<Type> _indexedTypes = [];
    
    public ProtoSerializer(IEnumerable<Assembly> assemblies)
    {
        //Search for proto types in assemblies.
        foreach (Type type in assemblies.SelectMany(asm => asm.GetTypes()))
        {
            if (type.GetCustomAttributes(typeof(ProtoContractAttribute), true).Length <= 0) continue;
            _indexedTypes.Add(type);
            if (_indexedTypes.Count > short.MaxValue) throw new Exception("ProtoSerializer exceeded type limit.");
        }
        
        //Order types deterministically.
        _indexedTypes = _indexedTypes.OrderBy(type => type.AssemblyQualifiedName).ToList();
    }

    /// <summary>
    /// Serializes protoObject to buffer.
    /// Returns false if protoObject is not a ProtoContract type or not in an indexed assembly.
    /// </summary>
    /// <param name="protoObject"></param>
    /// <param name="objectType"></param>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public bool Serialize(object protoObject, Type objectType, out Memory<byte> buffer)
    {
        /*
         * Formatting from byte zero, serialized object is an unknown number of bytes large:
         * [index byte 1][index byte 2][:][serialized object][>][\]
         * : is expected at the third byte of every serialized object, >\ represents end of object in this protocol.
         */
        
        buffer = Memory<byte>.Empty;
        int index = _indexedTypes.FindIndex(type => type == objectType);
        if (index < 0) return false;
        FromShort((short)index, out byte byte0, out byte byte1);
        List<byte> headerBytes =
        [
            byte0,
            byte1,
            0x3a // :
        ];
        var stream = new MemoryStream();
        stream.Write(headerBytes.ToArray(), 0, 3);
        Serializer.Serialize(stream, protoObject);
        List<byte> endBytes =
        [
            0x3e, // >
            0x5c // \
        ];
        stream.Write(endBytes.ToArray(), 0, 2);
        buffer = stream.GetBuffer().AsMemory();
        return true;
    }
    
    /// <summary>
    /// Serializes protoObject to buffer.
    /// Returns false if protoObject is not a ProtoContract type or not in an indexed assembly.
    /// </summary>
    /// <param name="protoObject"></param>
    /// <param name="buffer"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public bool Serialize<T>(T protoObject, out Memory<byte> buffer)
    {
        return Serialize(protoObject!, typeof(T), out buffer);
    }

    /// <summary>
    /// Deserializes bytes into a dictionary of objects and their types.
    /// Returns false if the formatting is bad or if the index type could not be found.
    /// </summary>
    /// <returns></returns>
    public bool Deserialize(ReadOnlyMemory<byte> buffer, out IDictionary<object, Type> objects)
    {
        objects = new Dictionary<object, Type>();
        Stream stream = new MemoryStream(buffer.ToArray());
        while (true)
        {
            //Iterate through serialized objects.
            
            //Verify header.
            byte[] headerBytes = [];
            //Break if header incomplete / no more objects available.
            if (stream.Read(headerBytes, 0, 3) < 3) break;
            //Check : character is in the right location.
            if (headerBytes[2] != 0x3a) return false;

            //Get type from index.
            int index = ToShort(headerBytes[0], headerBytes[1]);
            //Index unknown.
            if (index >= _indexedTypes.Count) return false;
            Type type = _indexedTypes[index];

            if (!IterateBytesUntilEndOfObject(stream, out List<byte> objectBytes)) return false;

            object? deserialized = Serializer.Deserialize(type, new MemoryStream(objectBytes.ToArray()));

            if (deserialized != null)
            {
                objects.Add(deserialized, type);
            }
        }

        return true;
    }

    private static bool IterateBytesUntilEndOfObject(Stream stream, out List<byte> objectBytes)
    {
        objectBytes = [];
        while (true)
        {
            byte[] nextByte = [];
            if (stream.Read(nextByte, 0, 1) > 0)
            {
                //Check for end of object characters in sequence.
                if (nextByte[0] == 0x5c && objectBytes.Last() == 0x3e)
                {
                    objectBytes.RemoveAt(objectBytes.Count - 1);
                    break;
                }
                objectBytes.Add(nextByte[0]);
            }
            else
            {
                //Invalid format.
                return false;
            }
        }

        return true;
    }

    private static short ToShort(byte byte1, byte byte2)
    {
        return (short)((byte2 << 8) + byte1);
    }

    private static void FromShort(short number, out byte byte1, out byte byte2)
    {
        byte2 = (byte)(number >> 8);
        byte1 = (byte)(number & 0xFF);
    }
}