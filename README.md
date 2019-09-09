# JSON Serializer JIT Compiler for .NET Core 3.0

__Note: This package is currently only for experiment purpose. APIs may be changed at any time.__

Emit CIL (MSIL) code at runtime for serializing JSON object, utilizing the `System.Test.Json` namespace provided since .NET Core 3.0.

# Example usage

```C#
static string CompileAndSerialize<T>(T obj)
{
    JsonJitSerializer<T> serializer = JsonJitSerializer<T>.Compile(new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    return serializer.Serialize(obj);
}

static async Task CompileAndSerializeAsync<T>(Stream stream, T obj)
{
    JsonJitSerializer<T> serializer = JsonJitSerializer<T>.Compile(new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
    {
        await serializer.SerializeAsync(writer, obj);
    }
}
```

# High level design

The JIT compiler generates a dynamic class inside a dynamic assembly when `JsonJitSerializer.Compile<T>` is called. This dynamic class is a value type and implements `ISerializerImplementation<T>`. That interface defines two methods: `Serialize` and `SerializeChunk`. `Serialize` serializes an object in one shot. `SerializeChunk` is re-enterable which can be called with the same parameter until it returns false, so it can be used for asynchronized serialization. Both methods will be implemented by generated IL codes. Other than that, the dynamic class contains static fields used as constants, such as converters, names and serialization options.

The dynamic class contains a list of `Object` fields used as serialization stack. The type `Object` is actually used as `void*` (_Note: value type will be boxed_). However there is no runtime casting involved. Therefore the runtime memory consumption is linear to the depth of the payload object. One exception is `IDictionary<string, T>`, because a local variable is needed to store a `KeyValuePair<string, T>` for every dictionary. Since the dynamic class is a value type, the serialization stack will be allocated on the stack at runtime.

# Supported feature

* Just-in-time compiled JSON serialization with `System.Text.Json.JsonSerializerOptions`.
* Custom `JsonConverter` attribute.
* Custom `JsonPropertyName` attribute.
* Both synchronized and asynchronized (re-enterable) serialization.
* `struct` serialization.
* Correctly deal with ref getter.
* `IEnumerable<T>` and `IDictionary<string, T>` serialization.
* O(`depth of structure` + `number of dictionary`) runtime memory consumption.

# Roadmap (randomly ordered)

* Reduce the number of memory allocations.
* Support element converter for `IEnumerable<T>` and `IDictionary<string, T>`. _(Not supported in the official `JsonSerializer` yet)_
* Optionally skip null. _(Looks like [the official implementation always keeps null](https://github.com/dotnet/corefx/issues/38492).)_
* Avoid boxing for struct?
* Pointer type serialization?
* Pointer getter?

# How to: Run unit tests and generate code coverage

`dotnet test ./tests/JsonJitSerializerTests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat="lcov" /p:CoverletOutput=../coverage/`

# Benchmark

Framework: [dotnet/performance/micro](https://github.com/dotnet/performance/tree/master/src/benchmarks/micro)

Command: `dotnet run -c Release -f netcoreapp3.0 --filter *Json_ToString*`

## LoginViewModel
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-PXEDDA : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                            Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|---------------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                               Jil | 342.0 ns | 6.6738 ns | 7.1409 ns | 341.6 ns | 333.7 ns | 359.7 ns |      0.1163 |           - |           - |               736 B |
|                          JSON.NET | 592.8 ns | 2.3734 ns | 2.2201 ns | 593.3 ns | 588.8 ns | 596.3 ns |      0.2378 |           - |           - |              1504 B |
|                          Utf8Json | 149.4 ns | 0.7122 ns | 0.6662 ns | 149.3 ns | 148.7 ns | 150.5 ns |      0.0304 |           - |           - |               192 B |
|                  System.Text.Json | 571.9 ns | 1.7938 ns | 1.6779 ns | 572.4 ns | 569.2 ns | 574.2 ns |      0.0780 |           - |           - |               496 B |
| MartinCl2.Text.Json.Serialization | 297.1 ns | 5.6411 ns | 5.2767 ns | 298.2 ns | 290.8 ns | 304.4 ns |      0.0993 |           - |           - |               624 B |

## Location
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-PXEDDA : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                            Method |       Mean |    Error |   StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|---------------------------------- |-----------:|---------:|---------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                               Jil |   743.8 ns | 5.919 ns | 5.537 ns |   741.6 ns |   737.3 ns |   754.4 ns |      0.2210 |           - |           - |              1392 B |
|                          JSON.NET | 1,310.2 ns | 9.554 ns | 7.459 ns | 1,309.2 ns | 1,298.6 ns | 1,323.4 ns |      0.2753 |           - |           - |              1736 B |
|                          Utf8Json |   324.1 ns | 6.143 ns | 6.308 ns |   328.1 ns |   315.9 ns |   332.0 ns |      0.0670 |           - |           - |               424 B |
|                  System.Text.Json | 1,355.8 ns | 2.452 ns | 2.294 ns | 1,355.4 ns | 1,351.2 ns | 1,361.1 ns |      0.1462 |           - |           - |               928 B |
| MartinCl2.Text.Json.Serialization |   670.3 ns | 1.338 ns | 1.251 ns |   670.5 ns |   668.4 ns |   672.3 ns |      0.1365 |           - |           - |               864 B |

## IndexViewModel
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-PXEDDA : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                            Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|---------------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                               Jil | 35.34 us | 0.1724 us | 0.1528 us | 35.31 us | 35.18 us | 35.68 us |      9.1743 |      1.4114 |           - |            56.65 KB |
|                          JSON.NET | 35.32 us | 0.0942 us | 0.0786 us | 35.34 us | 35.14 us | 35.43 us |      9.6509 |      1.5612 |           - |            59.33 KB |
|                          Utf8Json | 23.31 us | 0.0842 us | 0.0787 us | 23.28 us | 23.24 us | 23.47 us |      3.9135 |      0.2795 |           - |            24.55 KB |
|                  System.Text.Json | 34.63 us | 0.1288 us | 0.1142 us | 34.63 us | 34.42 us | 34.84 us |      5.1162 |      0.4148 |           - |            32.15 KB |
| MartinCl2.Text.Json.Serialization | 22.59 us | 0.2867 us | 0.2541 us | 22.59 us | 22.28 us | 23.09 us |      8.8480 |      1.0725 |           - |            54.83 KB |

## MyEventsListerViewModel
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-PXEDDA : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                            Method |     Mean |    Error |   StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|---------------------------------- |---------:|---------:|---------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                               Jil | 473.5 us | 1.147 us | 1.073 us | 473.3 us | 472.3 us | 475.4 us |     89.9796 |     44.9898 |     44.9898 |           490.25 KB |
|                          JSON.NET | 711.8 us | 1.603 us | 1.339 us | 712.5 us | 709.6 us | 713.2 us |     91.4286 |     45.7143 |     45.7143 |           535.49 KB |
|                          Utf8Json | 525.9 us | 3.072 us | 2.874 us | 524.6 us | 522.6 us | 530.9 us |     86.2069 |     86.2069 |     86.2069 |           348.17 KB |
|                  System.Text.Json | 632.6 us | 2.408 us | 2.135 us | 632.5 us | 629.7 us | 637.0 us |     47.5000 |     47.5000 |     47.5000 |           438.86 KB |
| MartinCl2.Text.Json.Serialization | 570.7 us | 3.750 us | 3.131 us | 570.5 us | 566.8 us | 578.4 us |     99.3228 |     99.3228 |     99.3228 |           530.48 KB |

## CollectionsOfPrimitives
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-PXEDDA : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                            Method |       Mean |     Error |    StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|---------------------------------- |-----------:|----------:|----------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                               Jil |   330.2 us | 1.4568 us | 1.3626 us |   330.0 us |   328.1 us |   333.1 us |     31.6623 |     31.6623 |     31.6623 |            216832 B |
|                          JSON.NET |   388.7 us | 1.0493 us | 0.9815 us |   388.4 us |   387.3 us |   390.7 us |     58.5516 |     29.2758 |     29.2758 |            318264 B |
|                          Utf8Json |   229.5 us | 0.7748 us | 0.7247 us |   229.8 us |   228.2 us |   230.5 us |     29.3848 |     29.3848 |     29.3848 |            100360 B |
|                  System.Text.Json |         NA |        NA |        NA |         NA |         NA |         NA |           - |           - |           - |                   - |
| MartinCl2.Text.Json.Serialization | 1,703.8 us | 7.6429 us | 6.3821 us | 1,702.7 us | 1,696.5 us | 1,721.2 us |     75.3425 |     34.2466 |     34.2466 |            540406 B |

Benchmarks with issues:
  Json_ToString<CollectionsOfPrimitives>.System.Text.Json: Job-PXEDDA(IterationTime=250.0000 ms, MaxIterationCount=20, MinIterationCount=15, WarmupCount=1)
