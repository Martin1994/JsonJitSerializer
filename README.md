# JSON Serializer JIT Compiler for .NET Core 3.0

__Note: This package is currently only for experiment purpose. APIs may be changed at any time.__

Emit CIL (MSIL) code at runtime for serializing JSON object, using the `System.Test.Json` namespace provided by .Net Core 3.0.

# How to play with it

Modify `Program.cs`.

Example:

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

For each object type, the compiler will create a dynamic class inside a dynamic assembly,
which contains a static method to serialize an object instance of that type, as well as
other hardcoded static fields including all converters needed for serialization and
`JsonSerializerOptions`.

# Supported feature

* Just-in-time compiled JSON serialization with `System.Text.Json.JsonSerializerOptions`.
* Custom `JsonConverter` attribute.
* Both synchronized and asynchronized (re-enterable) serialization.
* `struct` serialization.
* `IEnumerable<T>` serialization.
* O(depth of structure) runtime memory consumption.

# Roadmap (randomly ordered)

* Support `IDictionary<string, T>`.
* Support element converter for `IEnumerable<T>`. _(Not supported in the official `JsonSerializer` yet)_
* Benchmark.
* Unit tests.
* Optionally skip null. _(Looks like [the official implementation always skips null](https://github.com/dotnet/corefx/issues/38492). However the current implementation of this library always keep null)_
* Pre-cached UTF-8 property name.
* Support ref return.
* Avoid boxing for struct?
