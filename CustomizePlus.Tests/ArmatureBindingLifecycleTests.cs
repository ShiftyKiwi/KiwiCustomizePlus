using CustomizePlus.Armatures.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class ArmatureBindingLifecycleTests
{
    [Fact]
    public void SameStructuralFingerprintWithNewNativeBinding_RequiresPublication()
    {
        var published = new ArmaturePublicationIdentity("ABC", "native-10");
        var replacement = new ArmaturePublicationIdentity("ABC", "native-11");

        Assert.True(ArmatureBindingLifecycle.RequiresPublication(true, published, replacement));
    }

    [Fact]
    public void StructuralChange_RequiresPublication()
    {
        var published = new ArmaturePublicationIdentity("ABC", "native-10");
        var replacement = new ArmaturePublicationIdentity("XYZ", "native-10");

        Assert.True(ArmatureBindingLifecycle.RequiresPublication(true, published, replacement));
    }

    [Fact]
    public void IdenticalPublishedBinding_DoesNotCauseRecurringPublication()
    {
        var identity = new ArmaturePublicationIdentity("ABC", "native-10");

        Assert.False(ArmatureBindingLifecycle.RequiresPublication(true, identity, identity));
    }

    [Fact]
    public void InitialArmaturePublication_IsAlwaysRequired()
    {
        var identity = new ArmaturePublicationIdentity("ABC", "native-10");

        Assert.True(ArmatureBindingLifecycle.RequiresPublication(false, identity, identity));
    }
}
