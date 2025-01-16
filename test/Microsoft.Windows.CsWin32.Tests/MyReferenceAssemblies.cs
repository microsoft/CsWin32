// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

internal static class MyReferenceAssemblies
{
#pragma warning disable SA1202 // Elements should be ordered by access - because field initializer depend on each other
    private static readonly ImmutableArray<PackageIdentity> AdditionalLegacyPackages = ImmutableArray.Create(
        new PackageIdentity("Microsoft.Windows.SDK.Contracts", "10.0.22621.2428"));

    private static readonly ImmutableArray<PackageIdentity> AdditionalModernPackages = AdditionalLegacyPackages.AddRange(ImmutableArray.Create(
        ExtraPackages.Unsafe,
        ExtraPackages.Memory,
        ExtraPackages.Registry));

    internal static readonly ReferenceAssemblies NetStandard20 = ReferenceAssemblies.NetStandard.NetStandard20.AddPackages(AdditionalModernPackages);
#pragma warning restore SA1202 // Elements should be ordered by access

    internal static class NetFramework
    {
        internal static readonly ReferenceAssemblies Net35 = ReferenceAssemblies.NetFramework.Net35.WindowsForms.AddPackages(AdditionalLegacyPackages);
        internal static readonly ReferenceAssemblies Net472 = ReferenceAssemblies.NetFramework.Net472.WindowsForms.AddPackages(AdditionalModernPackages);
    }

    internal static class Net
    {
        internal static readonly ReferenceAssemblies Net80 = ReferenceAssemblies.Net.Net80.AddPackages(AdditionalModernPackages);
        internal static readonly ReferenceAssemblies Net90 = ReferenceAssemblies.Net.Net90.AddPackages(AdditionalModernPackages);
    }

    internal static class ExtraPackages
    {
        internal static readonly PackageIdentity Unsafe = new PackageIdentity("System.Runtime.CompilerServices.Unsafe", "6.0.0");
        internal static readonly PackageIdentity Memory = new PackageIdentity("System.Memory", "4.5.5");
        internal static readonly PackageIdentity Registry = new PackageIdentity("Microsoft.Win32.Registry", "5.0.0");
    }
}
