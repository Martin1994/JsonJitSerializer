using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MartinCl2.Text.Json.Serialization.Compiler
{
    public static class JitAssembly
    {
        private static readonly Lazy<ModuleBuilder> _jitModuleBuilder = new Lazy<ModuleBuilder>(InitializeModuleBuilder);
        internal static ModuleBuilder JitModuleBuilder { get => _jitModuleBuilder.Value; }

        public static readonly string GeneratedModuleNamespace = @"MartinCl2.Text.Json.Serialization.Dynamic";

        private static ModuleBuilder InitializeModuleBuilder()
        {
            AssemblyBuilder ab =
                AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName(GeneratedModuleNamespace),
                    AssemblyBuilderAccess.Run);

            ModuleBuilder mb =
                ab.DefineDynamicModule(GeneratedModuleNamespace);

            return mb;
        }
    }
}
