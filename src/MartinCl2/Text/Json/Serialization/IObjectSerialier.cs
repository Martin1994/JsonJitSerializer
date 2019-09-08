using System.Text.Json;

namespace MartinCl2.Text.Json.Serialization
{
    public interface ISerialierImplementation<T>
    {
        void Reset();

        bool SerializeChunk(Utf8JsonWriter writer, T value);

        void Serialize(Utf8JsonWriter writer, T value);
    }
}
