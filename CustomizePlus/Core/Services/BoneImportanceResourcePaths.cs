// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace CustomizePlus.Core.Services;

/// <summary>One actor's resource-path snapshot, owned only by a single probe or resolve.</summary>
internal sealed class BoneImportanceResourcePaths(Func<IReadOnlyDictionary<string, HashSet<string>>> load)
{
    private IReadOnlyDictionary<string, HashSet<string>>? _paths;

    public IReadOnlyDictionary<string, HashSet<string>> Paths => _paths ??= load();
}
