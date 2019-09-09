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

Command: `dotnet run -c Release -f netcoreapp3.0 --filter *Json_ToStream*`

## Json_ToStream_LoginViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-HAPJHM : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |      Error |     StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|-----------:|-----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 203.9 ns |  0.2430 ns |  0.2273 ns | 203.8 ns | 203.7 ns | 204.3 ns |           - |           - |           - |                   - |
|                   JSON.NET | 515.9 ns |  0.6559 ns |  0.6135 ns | 515.8 ns | 515.3 ns | 517.6 ns |      0.0701 |           - |           - |               448 B |
|                   Utf8Json | 130.4 ns |  0.1067 ns |  0.0998 ns | 130.4 ns | 130.3 ns | 130.6 ns |           - |           - |           - |                   - |
| DataContractJsonSerializer | 958.6 ns | 15.4578 ns | 14.4592 ns | 950.5 ns | 948.3 ns | 984.5 ns |      0.1602 |           - |           - |              1008 B |
|           System.Text.Json | 582.8 ns |  0.8554 ns |  0.7583 ns | 582.7 ns | 581.7 ns | 584.2 ns |      0.0979 |           - |           - |               616 B |
|     System.Text.Json_Async | 588.2 ns |  1.1292 ns |  1.0563 ns | 588.4 ns | 585.8 ns | 589.5 ns |      0.0471 |           - |           - |               304 B |
|        MartinCl2.Text.Json | 249.2 ns |  1.4094 ns |  1.3183 ns | 249.6 ns | 247.3 ns | 251.2 ns |      0.0685 |           - |           - |               432 B |
|  MartinCl2.Text.Json_Async | 340.1 ns |  0.4841 ns |  0.4528 ns | 340.3 ns | 339.6 ns | 340.8 ns |      0.0231 |           - |           - |               152 B |

## Json_ToStream_Location_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-HAPJHM : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |       Mean |     Error |    StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |-----------:|----------:|----------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                        Jil |   451.8 ns | 8.3706 ns | 7.8299 ns |   446.6 ns |   445.7 ns |   463.6 ns |      0.0143 |           - |           - |                96 B |
|                   JSON.NET | 1,177.9 ns | 1.0522 ns | 0.8786 ns | 1,177.8 ns | 1,177.0 ns | 1,180.2 ns |      0.0709 |           - |           - |               448 B |
|                   Utf8Json |   286.7 ns | 0.1234 ns | 0.1030 ns |   286.7 ns |   286.6 ns |   287.0 ns |           - |           - |           - |                   - |
| DataContractJsonSerializer | 2,111.0 ns | 2.4330 ns | 2.2759 ns | 2,110.7 ns | 2,108.2 ns | 2,115.4 ns |      0.1605 |           - |           - |              1008 B |
|           System.Text.Json | 1,373.4 ns | 3.8975 ns | 3.6457 ns | 1,374.8 ns | 1,365.5 ns | 1,377.5 ns |      0.1252 |           - |           - |               808 B |
|     System.Text.Json_Async | 1,360.9 ns | 1.6562 ns | 1.4682 ns | 1,360.7 ns | 1,358.7 ns | 1,363.3 ns |      0.0756 |           - |           - |               496 B |
|        MartinCl2.Text.Json |   612.6 ns | 0.9984 ns | 0.8851 ns |   612.8 ns |   611.2 ns |   614.5 ns |      0.0686 |           - |           - |               432 B |
|  MartinCl2.Text.Json_Async |   745.9 ns | 1.8611 ns | 1.7409 ns |   744.9 ns |   744.3 ns |   749.9 ns |      0.0237 |           - |           - |               152 B |

## Json_ToStream_IndexViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-HAPJHM : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 34.73 us | 0.0379 us | 0.0354 us | 34.72 us | 34.68 us | 34.79 us |           - |           - |           - |                96 B |
|                   JSON.NET | 32.98 us | 0.1064 us | 0.0995 us | 32.96 us | 32.84 us | 33.16 us |      0.2631 |           - |           - |              2448 B |
|                   Utf8Json | 20.13 us | 0.3967 us | 0.4074 us | 20.17 us | 19.60 us | 20.78 us |           - |           - |           - |                   - |
| DataContractJsonSerializer | 73.52 us | 0.2502 us | 0.2341 us | 73.58 us | 73.07 us | 73.81 us |      0.2939 |           - |           - |              2432 B |
|           System.Text.Json | 32.51 us | 0.0555 us | 0.0492 us | 32.50 us | 32.43 us | 32.59 us |      1.9531 |           - |           - |             12472 B |
|     System.Text.Json_Async | 32.32 us | 0.0754 us | 0.0669 us | 32.30 us | 32.25 us | 32.45 us |      1.1574 |           - |           - |              7784 B |
|        MartinCl2.Text.Json | 19.57 us | 0.0120 us | 0.0113 us | 19.57 us | 19.55 us | 19.59 us |      0.7044 |           - |           - |              4848 B |
|  MartinCl2.Text.Json_Async | 19.14 us | 0.3392 us | 0.3173 us | 18.95 us | 18.85 us | 19.67 us |           - |           - |           - |               192 B |

## Json_ToStream_MyEventsListerViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-HAPJHM : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |      Error |     StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|-----------:|-----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 440.6 us |  0.8454 us |  0.7495 us | 440.6 us | 439.1 us | 441.6 us |     32.6923 |           - |           - |           207.09 KB |
|                   JSON.NET | 671.5 us | 12.7772 us | 11.3267 us | 666.1 us | 662.1 us | 693.6 us |     37.2093 |           - |           - |            229.7 KB |
|                   Utf8Json | 532.3 us |  1.5443 us |  1.4445 us | 532.0 us | 530.0 us | 535.4 us |     40.3397 |     40.3397 |     40.3397 |           200.25 KB |
| DataContractJsonSerializer | 706.0 us |  0.8780 us |  0.8213 us | 706.1 us | 704.5 us | 707.4 us |      2.8169 |           - |           - |            23.63 KB |
|           System.Text.Json | 585.2 us |  0.9010 us |  0.7987 us | 584.9 us | 584.4 us | 587.0 us |     46.2963 |           - |           - |           295.52 KB |
|     System.Text.Json_Async | 589.5 us |  0.9227 us |  0.7705 us | 589.4 us | 588.1 us | 590.8 us |     46.0526 |           - |           - |           290.94 KB |
|        MartinCl2.Text.Json | 498.3 us |  6.3951 us |  5.9819 us | 498.6 us | 491.6 us | 508.6 us |     36.2903 |           - |           - |           225.88 KB |
|  MartinCl2.Text.Json_Async | 606.3 us |  1.4433 us |  1.3501 us | 606.0 us | 604.0 us | 608.8 us |     34.2466 |           - |           - |           221.33 KB |

## Json_ToStream_CollectionsOfPrimitives_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host]     : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT
  Job-HAPJHM : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 4.700.19.37902, CoreFX 4.700.19.40503), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |       Mean |     Error |    StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |-----------:|----------:|----------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                        Jil |   264.7 us | 0.4846 us | 0.4296 us |   264.6 us |   264.3 us |   265.9 us |           - |           - |           - |                96 B |
|                   JSON.NET |   356.4 us | 1.2230 us | 1.1440 us |   356.0 us |   355.0 us |   358.9 us |     17.0455 |           - |           - |            107136 B |
|                   Utf8Json |   218.4 us | 0.3460 us | 0.3067 us |   218.3 us |   218.0 us |   219.0 us |           - |           - |           - |              2760 B |
| DataContractJsonSerializer | 1,208.2 us | 2.3727 us | 2.2194 us | 1,208.1 us | 1,204.2 us | 1,212.6 us |      9.6618 |           - |           - |             74928 B |
|           System.Text.Json |         NA |        NA |        NA |         NA |         NA |         NA |           - |           - |           - |                   - |
|     System.Text.Json_Async |         NA |        NA |        NA |         NA |         NA |         NA |           - |           - |           - |                   - |
|        MartinCl2.Text.Json | 1,642.6 us | 4.5183 us | 3.7730 us | 1,642.5 us | 1,637.3 us | 1,649.5 us |     39.7351 |           - |           - |            283472 B |
|  MartinCl2.Text.Json_Async | 1,669.9 us | 4.8922 us | 4.3368 us | 1,668.3 us | 1,665.6 us | 1,679.3 us |     40.0000 |           - |           - |            278816 B |

Benchmarks with issues:
  Json_ToStream<CollectionsOfPrimitives>.System.Text.Json: Job-HAPJHM(IterationTime=250.0000 ms, MaxIterationCount=20, MinIterationCount=15, WarmupCount=1)
  Json_ToStream<CollectionsOfPrimitives>.System.Text.Json_Async: Job-HAPJHM(IterationTime=250.0000 ms, MaxIterationCount=20, MinIterationCount=15, WarmupCount=1)
