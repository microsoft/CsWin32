// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1202 // Elements should be ordered by access - because field initializer depend on each other

internal static class MyReferenceAssemblies
{
    private static readonly ImmutableArray<PackageIdentity> AdditionalModernPackages = [
        ExtraPackages.Unsafe,
        ExtraPackages.Memory,
        ExtraPackages.Registry,
    ];

    private static readonly ImmutableArray<PackageIdentity> AdditionalLegacyPackagesNetFX = [
        new PackageIdentity("Microsoft.Windows.SDK.Contracts", "10.0.22621.2428"),
    ];

    private static readonly ImmutableArray<PackageIdentity> AdditionalLegacyPackagesNET = [
        new PackageIdentity("Microsoft.Windows.SDK.NET.Ref", "10.0.22621.57"),
    ];

    internal static readonly ReferenceAssemblies NetStandard20 = ReferenceAssemblies.NetStandard.NetStandard20.AddPackages([.. AdditionalLegacyPackagesNetFX, .. AdditionalModernPackages]);

    internal static class NetFramework
    {
        internal static readonly ReferenceAssemblies Net35 = ReferenceAssemblies.NetFramework.Net35.WindowsForms.AddPackages(AdditionalLegacyPackagesNetFX);
        internal static readonly ReferenceAssemblies Net472 = ReferenceAssemblies.NetFramework.Net472.WindowsForms.AddPackages([.. AdditionalLegacyPackagesNetFX, .. AdditionalModernPackages]);
    }

    internal static class Net
    {
        internal static readonly ReferenceAssemblies Net80 = ReferenceAssemblies.Net.Net80.AddPackages([.. AdditionalLegacyPackagesNET, .. AdditionalModernPackages]);
        internal static readonly ReferenceAssemblies Net90 = ReferenceAssemblies.Net.Net90.AddPackages([.. AdditionalLegacyPackagesNET, .. AdditionalModernPackages]);
    }

    internal static class ExtraPackages
    {
        internal static readonly PackageIdentity Unsafe = new PackageIdentity("System.Runtime.CompilerServices.Unsafe", "6.0.0");
        internal static readonly PackageIdentity Memory = new PackageIdentity("System.Memory", "4.5.5");
        internal static readonly PackageIdentity Registry = new PackageIdentity("Microsoft.Win32.Registry", "5.0.0");
    }
}
