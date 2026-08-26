// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Internal publication identity. The native portion is deliberately private to the armature lifecycle
/// and must never be used as a public structural fingerprint or persisted value.
/// </summary>
internal readonly record struct ArmaturePublicationIdentity(string StructuralSignature, string NativeBindingIdentity)
{
    public string PendingKey => StructuralSignature + "|native=" + NativeBindingIdentity;
}

internal static class ArmatureBindingLifecycle
{
    public static bool RequiresPublication(bool isBuilt, ArmaturePublicationIdentity published, ArmaturePublicationIdentity candidate)
        => !isBuilt
           || !string.Equals(published.StructuralSignature, candidate.StructuralSignature, StringComparison.Ordinal)
           || !string.Equals(published.NativeBindingIdentity, candidate.NativeBindingIdentity, StringComparison.Ordinal);
}
