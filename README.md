# JSON Serializer JIT Compiler for .NET Core 3.0

Emit CIL/MSIL code at runtime for JSON serialization, utilizing the `System.Test.Json` namespace provided since .NET Core 3.0.

# Installation

```sh
dotnet add package MartinCl2.Text.Json.Serialization
```

https://www.nuget.org/packages/MartinCl2.Text.Json.Serialization

# Usage

```C#
public static class Example<T>
{
    private static JsonJitSerializer<T> serializer = JsonJitSerializer<T>.Compile(new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    public static string SerializeToString<T>(T obj)
    {
        return serializer.Serialize(obj);
    }

    public static async Task SerializeToStreamAsync(Stream stream, T obj)
    {
        await serializer.SerializeAsync(stream, obj);
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
* Value type enumerator optimization.
* O(`depth of structure` + `number of dictionary`) runtime memory consumption.

# Roadmap (randomly ordered)

* Support element converter for `IEnumerable<T>` and `IDictionary<string, T>`. _(Not supported in the official `JsonSerializer` yet)_
* Optionally skip null. _(Looks like [the official implementation always keeps null](https://github.com/dotnet/corefx/issues/38492).)_
* Avoid boxing for struct?
* Pointer type serialization?
* Pointer getter?
* Unit test for Enum.

# How to: Run unit tests and generate code coverage

`dotnet test ./tests/JsonJitSerializerTests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat="lcov" /p:CoverletOutput=../coverage/`

# Benchmark

Framework: my fork of [dotnet/performance/micro](https://github.com/Martin1994/performance/tree/system-text-json-benchmarks/src/benchmarks/micro) (forked from a [fork](https://github.com/NickCraver/performance/tree/craver/system-text-json-benchmarks) of [dotnet/performance](https://github.com/dotnet/performance))

Command: `dotnet run -c Release -f netcoreapp3.0 --filter *Json_ToStream*`

## Json_ToStream_LoginViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview9-014004
  [Host]     : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT
  Job-RUHLRH : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 201.1 ns | 1.8839 ns | 1.7622 ns | 200.1 ns | 199.7 ns | 205.0 ns |           - |           - |           - |                   - |
|                   JSON.NET | 500.2 ns | 0.8144 ns | 0.6801 ns | 500.4 ns | 498.6 ns | 501.2 ns |      0.0699 |           - |           - |               448 B |
|                   Utf8Json | 121.6 ns | 0.1952 ns | 0.1630 ns | 121.6 ns | 121.4 ns | 121.9 ns |           - |           - |           - |                   - |
| DataContractJsonSerializer | 885.6 ns | 2.1066 ns | 1.7591 ns | 885.6 ns | 883.6 ns | 889.9 ns |      0.1603 |           - |           - |              1008 B |
|           System.Text.Json | 578.6 ns | 1.7358 ns | 1.6236 ns | 578.4 ns | 576.3 ns | 582.2 ns |      0.0975 |           - |           - |               616 B |
|     System.Text.Json_Async | 592.6 ns | 1.3711 ns | 1.2825 ns | 592.6 ns | 590.9 ns | 594.8 ns |      0.0471 |           - |           - |               304 B |
|        MartinCl2.Text.Json | 245.7 ns | 1.0642 ns | 0.9954 ns | 245.6 ns | 244.5 ns | 247.6 ns |      0.0680 |           - |           - |               432 B |
|  MartinCl2.Text.Json_Async | 346.0 ns | 1.6620 ns | 1.5547 ns | 345.4 ns | 344.4 ns | 349.2 ns |      0.0239 |           - |           - |               152 B |

## Json_ToStream_Location_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview9-014004
  [Host]     : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT
  Job-RUHLRH : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |       Mean |     Error |    StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |-----------:|----------:|----------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                        Jil |   431.8 ns | 0.5931 ns | 0.5548 ns |   431.9 ns |   430.9 ns |   432.7 ns |      0.0140 |           - |           - |                96 B |
|                   JSON.NET | 1,186.6 ns | 1.7353 ns | 1.5383 ns | 1,186.6 ns | 1,184.2 ns | 1,189.0 ns |      0.0710 |           - |           - |               448 B |
|                   Utf8Json |   278.0 ns | 0.3577 ns | 0.3171 ns |   277.9 ns |   277.6 ns |   278.6 ns |           - |           - |           - |                   - |
| DataContractJsonSerializer | 2,069.2 ns | 3.5186 ns | 2.9382 ns | 2,068.3 ns | 2,065.5 ns | 2,074.2 ns |      0.1570 |           - |           - |              1008 B |
|           System.Text.Json | 1,345.9 ns | 6.0559 ns | 5.6647 ns | 1,344.9 ns | 1,335.8 ns | 1,357.7 ns |      0.1283 |           - |           - |               808 B |
|     System.Text.Json_Async | 1,328.9 ns | 1.4862 ns | 1.2410 ns | 1,329.0 ns | 1,327.1 ns | 1,331.0 ns |      0.0744 |           - |           - |               496 B |
|        MartinCl2.Text.Json |   599.0 ns | 0.7990 ns | 0.6238 ns |   599.0 ns |   597.7 ns |   600.2 ns |      0.0673 |           - |           - |               432 B |
|  MartinCl2.Text.Json_Async |   720.0 ns | 1.1003 ns | 1.0293 ns |   720.2 ns |   717.6 ns |   721.6 ns |      0.0231 |           - |           - |               152 B |

## Json_ToStream_IndexViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview9-014004
  [Host]     : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT
  Job-RUHLRH : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 33.84 us | 0.2850 us | 0.2527 us | 33.72 us | 33.53 us | 34.45 us |           - |           - |           - |                96 B |
|                   JSON.NET | 32.29 us | 0.1049 us | 0.0876 us | 32.28 us | 32.19 us | 32.54 us |      0.3891 |           - |           - |              2448 B |
|                   Utf8Json | 19.09 us | 0.0342 us | 0.0303 us | 19.09 us | 19.03 us | 19.14 us |           - |           - |           - |                   - |
| DataContractJsonSerializer | 69.33 us | 0.1981 us | 0.1853 us | 69.37 us | 68.93 us | 69.67 us |      0.2778 |           - |           - |              2432 B |
|           System.Text.Json | 34.17 us | 0.1403 us | 0.1313 us | 34.11 us | 34.03 us | 34.45 us |      8.4607 |      0.8188 |           - |             53376 B |
|     System.Text.Json_Async | 32.10 us | 0.1040 us | 0.0868 us | 32.10 us | 32.00 us | 32.32 us |      1.1527 |           - |           - |              7784 B |
|        MartinCl2.Text.Json | 19.78 us | 0.0664 us | 0.0588 us | 19.79 us | 19.67 us | 19.90 us |      7.2509 |      0.5517 |           - |             45712 B |
|  MartinCl2.Text.Json_Async | 18.57 us | 0.0414 us | 0.0387 us | 18.56 us | 18.52 us | 18.64 us |           - |           - |           - |               152 B |

## Json_ToStream_MyEventsListerViewModel_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview9-014004
  [Host]     : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT
  Job-RUHLRH : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |     Mean |     Error |    StdDev |   Median |      Min |      Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|------------:|------------:|------------:|--------------------:|
|                        Jil | 447.8 us | 1.6482 us | 1.4611 us | 447.1 us | 446.4 us | 451.0 us |     38.3212 |           - |           - |           236.34 KB |
|                   JSON.NET | 670.7 us | 2.6532 us | 2.4818 us | 669.7 us | 666.9 us | 676.1 us |     40.5405 |           - |           - |           258.95 KB |
|                   Utf8Json | 527.5 us | 1.5446 us | 1.2898 us | 527.5 us | 525.7 us | 530.1 us |     39.0879 |     39.0879 |     39.0879 |            229.5 KB |
| DataContractJsonSerializer | 696.7 us | 0.6338 us | 0.4948 us | 696.6 us | 695.9 us | 697.3 us |      2.8011 |           - |           - |            23.63 KB |
|           System.Text.Json | 643.6 us | 4.0669 us | 3.3961 us | 642.7 us | 639.8 us | 650.2 us |     80.8824 |     40.4412 |     40.4412 |           430.45 KB |
|     System.Text.Json_Async | 578.9 us | 3.3254 us | 2.5963 us | 578.5 us | 576.4 us | 586.7 us |     49.3421 |           - |           - |           320.19 KB |
|        MartinCl2.Text.Json | 533.5 us | 2.3295 us | 2.1790 us | 533.5 us | 530.5 us | 539.2 us |     81.2500 |     39.5833 |     39.5833 |           357.91 KB |
|  MartinCl2.Text.Json_Async | 607.8 us | 4.1591 us | 3.8904 us | 606.4 us | 602.2 us | 616.8 us |     38.7409 |           - |           - |           247.65 KB |

## Json_ToStream_CollectionsOfPrimitives_
``` ini

BenchmarkDotNet=v0.11.3.1003-nightly, OS=Windows 10.0.18362
Intel Core i7-9700K CPU 3.60GHz, 1 CPU, 8 logical and 8 physical cores
.NET Core SDK=3.0.100-preview9-014004
  [Host]     : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT
  Job-RUHLRH : .NET Core 3.0.0-preview9-19423-09 (CoreCLR 4.700.19.42102, CoreFX 4.700.19.42104), 64bit RyuJIT

IterationTime=250.0000 ms  MaxIterationCount=20  MinIterationCount=15  
WarmupCount=1  

```
|                     Method |       Mean |     Error |    StdDev |     Median |        Min |        Max | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
|--------------------------- |-----------:|----------:|----------:|-----------:|-----------:|-----------:|------------:|------------:|------------:|--------------------:|
|                        Jil |   253.4 us | 1.0126 us | 0.8977 us |   253.1 us |   252.6 us |   255.7 us |           - |           - |           - |                96 B |
|                   JSON.NET |   330.4 us | 0.8561 us | 0.7149 us |   330.4 us |   328.9 us |   331.7 us |     10.5960 |           - |           - |             74688 B |
|                   Utf8Json |   213.8 us | 1.0085 us | 0.9433 us |   213.4 us |   212.8 us |   215.5 us |           - |           - |           - |              2760 B |
| DataContractJsonSerializer | 1,178.0 us | 1.9970 us | 1.6676 us | 1,178.0 us | 1,175.9 us | 1,182.1 us |      9.4787 |           - |           - |             74928 B |
|           System.Text.Json |   533.8 us | 0.7406 us | 0.6928 us |   533.9 us |   532.9 us |   535.0 us |     45.8333 |      6.2500 |           - |            299656 B |
|     System.Text.Json_Async |   528.1 us | 0.7238 us | 0.5651 us |   528.1 us |   527.4 us |   528.9 us |     29.1667 |           - |           - |            188800 B |
|        MartinCl2.Text.Json |   135.5 us | 0.6326 us | 0.5918 us |   135.5 us |   134.7 us |   136.8 us |     17.3913 |      2.1739 |           - |            110976 B |
|  MartinCl2.Text.Json_Async |   149.6 us | 0.2230 us | 0.2086 us |   149.6 us |   149.0 us |   149.8 us |           - |           - |           - |               152 B |
