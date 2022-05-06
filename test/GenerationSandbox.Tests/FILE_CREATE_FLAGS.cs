// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.Sdk;

/// <summary>
/// Written here explicitly to verify that the code generator will suppress codegen where a type already exists.
/// </summary>
internal enum FILE_CREATE_FLAGS
{
    CREATE_NEW = 1,
    CREATE_ALWAYS = 2,
    OPEN_EXISTING = 3,
    OPEN_ALWAYS = 4,
    TRUNCATE_EXISTING = 5,
}
