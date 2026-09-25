using CustomizePlus.Armatures.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class ArmatureActorSelectionTests
{
    private static readonly ArmatureActorSelectionCandidate WorldValid = new(false, true, true, true, true);
    private static readonly ArmatureActorSelectionCandidate WorldUnavailable = new(false, true, false, false, true);
    private static readonly ArmatureActorSelectionCandidate GPoseValid = new(true, true, true, true, true);
    private static readonly ArmatureActorSelectionCandidate GPoseWrongIdentity = new(true, true, true, true, false);
    private static readonly ArmatureLobbyActorSelectionCandidate LobbyValid = new(true, true, true);
    private static readonly ArmatureLobbyActorSelectionCandidate LobbyNoSkeleton = new(true, true, false);

    [Fact]
    public void NormalWorld_PreservesExistingFirstObjectSelection()
    {
        var result = ArmatureActorSelection.Select([WorldUnavailable, GPoseValid], isInGPose: false);

        Assert.True(result.IsSelected);
        Assert.Equal(0, result.Index);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void ValidatedSelection_IsRequiredForGPoseOrGroupedCutsceneCopies(bool isInGPose, bool hasCutsceneActor, bool expected)
        => Assert.Equal(expected, ArmatureActorSelection.RequiresValidatedSelection(isInGPose, hasCutsceneActor));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GPose_SelectsTheOnlyValidatedCutsceneCopyRegardlessOfOrder(bool gposeFirst)
    {
        var candidates = gposeFirst
            ? new[] { GPoseValid, WorldUnavailable }
            : new[] { WorldUnavailable, GPoseValid };

        var result = ArmatureActorSelection.Select(candidates, isInGPose: true);

        Assert.True(result.IsSelected);
        Assert.Equal(gposeFirst ? 0 : 1, result.Index);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuestCutscene_SelectsTheExactValidatedCopyRegardlessOfObjectOrder(bool cutsceneFirst)
    {
        var candidates = cutsceneFirst
            ? new[] { GPoseValid, WorldUnavailable }
            : new[] { WorldUnavailable, GPoseValid };

        // The caller only requests this path when its existing permanent identity/parent mapping
        // identifies a cutscene representation. The selector must not depend on group ordering.
        var result = ArmatureActorSelection.Select(candidates, isInGPose: true);

        Assert.True(result.IsSelected);
        Assert.Equal(cutsceneFirst ? 0 : 1, result.Index);
    }

    [Fact]
    public void GPose_DoesNotFallBackToWorldObjectWhenGPoseCopyIsUnbindable()
    {
        var result = ArmatureActorSelection.Select([WorldValid, GPoseWrongIdentity], isInGPose: true);

        Assert.False(result.IsSelected);
        Assert.Equal("no bindable GPose/cutscene actor copy was available", result.Reason);
    }

    [Fact]
    public void GPose_AmbiguousBindableCopiesFailClosed()
    {
        var result = ArmatureActorSelection.Select([WorldUnavailable, GPoseValid, GPoseValid], isInGPose: true);

        Assert.False(result.IsSelected);
        Assert.Equal("multiple bindable GPose/cutscene actor copies were available", result.Reason);
    }

    [Fact]
    public void GPose_IdentityMismatchCannotSelectAnotherEventNpcCopy()
    {
        var result = ArmatureActorSelection.Select([WorldUnavailable, GPoseWrongIdentity], isInGPose: true);

        Assert.False(result.IsSelected);
    }

    [Fact]
    public void QuestCutscene_IdentityMismatchCannotUseANameModelOrRaceFallback()
    {
        var result = ArmatureActorSelection.Select([WorldUnavailable, GPoseWrongIdentity], isInGPose: true);

        Assert.False(result.IsSelected);
        Assert.Equal("no bindable GPose/cutscene actor copy was available", result.Reason);
    }

    [Fact]
    public void GPoseExit_ReturnsToTheNormalWorldSelection()
    {
        var result = ArmatureActorSelection.Select([WorldValid, GPoseValid], isInGPose: false);

        Assert.True(result.IsSelected);
        Assert.Equal(0, result.Index);
    }

    [Fact]
    public void Lobby_SelectsTheSingleAuthoritativeBindablePreview()
    {
        var result = ArmatureActorSelection.SelectLobby([LobbyValid]);

        Assert.True(result.IsSelected);
        Assert.Equal(0, result.Index);
    }

    [Fact]
    public void Lobby_RejectsAnUnavailableAuthoritativePreview()
    {
        var result = ArmatureActorSelection.SelectLobby([LobbyNoSkeleton]);

        Assert.False(result.IsSelected);
        Assert.Equal("authoritative lobby preview actor was not currently bindable", result.Reason);
    }

    [Fact]
    public void Lobby_RejectsAmbiguousOrMissingAuthoritativeGroups()
    {
        var empty = ArmatureActorSelection.SelectLobby([]);
        var multiple = ArmatureActorSelection.SelectLobby([LobbyValid, LobbyValid]);

        Assert.False(empty.IsSelected);
        Assert.False(multiple.IsSelected);
    }

    [Fact]
    public void Lobby_IndexReuseRequiresTheCurrentAuthoritativeCandidateToBeBindable()
    {
        var characterA = ArmatureActorSelection.SelectLobby([LobbyValid]);
        var characterB = ArmatureActorSelection.SelectLobby([LobbyNoSkeleton]);
        var characterAReturn = ArmatureActorSelection.SelectLobby([LobbyValid]);

        Assert.True(characterA.IsSelected);
        Assert.False(characterB.IsSelected);
        Assert.True(characterAReturn.IsSelected);
    }
}
