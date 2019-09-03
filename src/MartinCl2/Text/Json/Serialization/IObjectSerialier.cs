using System;
using System.Text.Json;

namespace MartinCl2.Text.Json.Serialization
{
    public interface IObjectSerialier<T>
    {
        bool SerializeChunk(Utf8JsonWriter writer, T value);

        void Serialize(Utf8JsonWriter writer, T value);
    }
}
