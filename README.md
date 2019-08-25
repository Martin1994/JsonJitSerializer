# JSON Serializer JIT Compiler for .NET Core 3.0

__Note: This package is currently only for experiment purpose. APIs may be changed at any time.__

Emit CIL (MSIL) code at runtime for serializing JSON object, using the `System.Test.Json` namespace provided by .Net Core 3.0.

# How to play with it

Modify `Program.cs`.

# High level design

For each object type, the compiler will create a dynamic class inside a dynamic assembly,
which contains a static method to serialize an object instance of that type, as well as
all converters needed for serialization in its static fields.

# Roadmap (randomly ordered)

* Support async call by introducing a stack.
* Support IEnumerable and IDictionary.
* Compile with `JsonSerializerOptions`.
* Benchmark.
