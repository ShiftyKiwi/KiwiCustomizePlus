// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Plugin.Services;

namespace CustomizePlus.Game.Services.GPose.ExternalTools;

/// <summary>
///     Service which detects if Anamnesis/Ktisis posing mode is enabled.
/// </summary>
public class PosingModeDetectService
{
    // Borrowed from Ktisis:
    // If this is NOP'd, Anam posing is enabled.
    internal static unsafe byte* AnamnesisFreezePosition;
    internal static unsafe byte* AnamnesisFreezeRotation;
    internal static unsafe byte* AnamnesisFreezeScale;

    internal static unsafe bool IsAnamnesisPositionFrozen => IsFrozen(AnamnesisFreezePosition);

    internal static unsafe bool IsAnamnesisRotationFrozen => IsFrozen(AnamnesisFreezeRotation);

    internal static unsafe bool IsAnamnesisScalingFrozen => IsFrozen(AnamnesisFreezeScale);

    internal static bool IsAnamnesis =>
        IsAnamnesisPositionFrozen || IsAnamnesisRotationFrozen || IsAnamnesisScalingFrozen;

    public bool IsInPosingMode => IsAnamnesis; //Can't detect Ktisis for now

    public unsafe PosingModeDetectService(ISigScanner sigScanner)
    {
        AnamnesisFreezePosition = TryScan(sigScanner, "41 0F 29 24 12");
        AnamnesisFreezeRotation = TryScan(sigScanner, "41 0F 29 5C 12 10");
        AnamnesisFreezeScale = TryScan(sigScanner, "41 0F 29 44 12 20");
    }

    private static unsafe bool IsFrozen(byte* address)
        => address != null && (*address == 0x90 || *address == 0x00);

    private static unsafe byte* TryScan(ISigScanner sigScanner, string signature)
    {
        try
        {
            return (byte*)sigScanner.ScanText(signature);
        }
        catch
        {
            // An unresolved game signature means posing-mode detection stays disabled.
            return null;
        }
    }
}
