# JSON Serializer JIT Compiler for .NET Core 3.0

__Note: This package is currently only for experiment purpose. APIs may be changed at any time.__

Emit CIL (MSIL) code at runtime for serializing JSON object, using the `System.Test.Json` namespace provided by .Net Core 3.0.

# How to play with it

Modify `Program.cs`.

# Example usage

```C#
static async Task<string> CompileAndSerialize<T>(T obj)
{
    JsonJitSerializer<T> serializer = JsonJitSerializer.Compile<T>(new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    return await serializer.SerializeAsync(obj);
}
```

# High level design

The JIT compiler generates a dynamic class inside a dynamic assembly when `JsonJitSerializer.Compile<T>` is called. This dynamic class is a value type and implements `ISerialierImplementation<T>`. That interface defines two methods: `Serialize` and `SerializeChunk`. `Serialize` serializes an object in one shot. `SerializeChunk` is re-enterable which can be called with the same parameter until it returns false, so it can be used for asynchronized serialization. Both methods will be implemented by generated IL codes. Other than that, the dynamic class contains static fields used as constants, such as converters, names and serialization options.

The dynamic class contains a list of `Object` fields used as serialization stack. The type `Object` is actually used as `void*` (_Note: value type will be boxed_). However there is no runtime casting involved. Therefore the runtime memory consumption is linear to the depth of the payload object. Since the dynamic class is a value type, the serialization stack will be allocated on the stack at runtime.

# Supported feature

* Just-in-time compiled JSON serialization with `System.Text.Json.JsonSerializerOptions`.
* Custom `JsonConverter` attribute.
* Both synchronized and asynchronized (re-enterable) serialization.
* `struct` serialization.
* Correctly deal with ref getter.
* `IEnumerable<T>` serialization.
* O(depth of structure) runtime memory consumption.

# Roadmap (randomly ordered)

* Support `IDictionary<string, T>`.
* Support element converter for `IEnumerable<T>`. _(Not supported in the official `JsonSerializer` yet)_
* Benchmark.
* Unit tests.
* Optionally skip null. _(Looks like [the official implementation always skips null](https://github.com/dotnet/corefx/issues/38492). However the current implementation of this library always keep null)_
* Pre-cached UTF-8 property name.
* Avoid boxing for struct?
* Pointer getter?
