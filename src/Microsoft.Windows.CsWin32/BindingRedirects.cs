// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Windows.CsWin32;

internal static class BindingRedirects
{
    private static readonly string SourceGeneratorAssemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly Lazy<Dictionary<string, string>> LocalAssemblies;

    static BindingRedirects()
    {
        LocalAssemblies = new Lazy<Dictionary<string, string>>(
            () => Directory.GetFiles(SourceGeneratorAssemblyDirectory, "*.dll").ToDictionary(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsNetFramework => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase);

#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    internal static void ApplyBindingRedirects()
    {
        if (IsNetFramework)
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }
    }

    private static Assembly? CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        AssemblyName expected = new(args.Name);
        if (LocalAssemblies.Value.TryGetValue(expected.Name, out string? path))
        {
            AssemblyName actual = AssemblyName.GetAssemblyName(path);
            if (actual.Version >= expected.Version)
            {
                return Assembly.LoadFile(path);
            }
        }

        return null;
    }
}
