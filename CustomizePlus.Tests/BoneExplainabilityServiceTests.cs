using System;
using System.Collections.Generic;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Services;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class BoneExplainabilityServiceTests
{
    [Fact]
    public void RetainedReasonCodes_AreCoveredByPureDecisionInputs()
    {
        var observed = new HashSet<BoneExplainabilityReasonCode>();
        void Collect(BoneExplainabilityDecisionInput input)
            => observed.UnionWith(BoneExplainabilityService.EvaluateReasons(input));

        Collect(Baseline() with { ArmatureAvailable = false });
        Collect(Baseline() with { AppearanceTransitionPending = true });
        Collect(Baseline() with { BindingCurrent = false, HasBindingIssue = true });
        Collect(Baseline() with { BonePresent = false });
        Collect(Baseline(BoneData.GetMetadata("iv_nitoukin_l")) with { CapabilityRequired = true, CapabilityPresent = false });
        Collect(Baseline() with { CompatibilityDormant = true });
        Collect(Baseline(BoneData.GetMetadata("future_custom_control")));
        Collect(Baseline(BoneData.GetMetadata("nf_shrt_a")));
        Collect(Baseline(BoneData.GetMetadata("nf_handprop_a_l")));
        Collect(Baseline(BoneData.GetMetadata("mkl_wingarm_a_l")));
        Collect(Baseline() with { ExplicitAuthority = true, AxisLocked = true, AxisPinned = true });
        Collect(Baseline() with { SolverEnabled = false });
        Collect(Baseline() with { ModelDerivedImportanceActive = true, BoneImportance = 0f });
        Collect(Baseline() with { ModelDerivedImportanceActive = true, BoneImportance = 0.10f });
        Collect(Baseline() with { HasResolvedContribution = false });

        Assert.Equal(Enum.GetValues<BoneExplainabilityReasonCode>().Length, observed.Count);
        Assert.True(observed.SetEquals(Enum.GetValues<BoneExplainabilityReasonCode>()));
    }

    private static BoneExplainabilityDecisionInput Baseline(BoneMetadata? metadata = null)
        => new(
            ArmatureAvailable: true,
            AppearanceTransitionPending: false,
            BindingCurrent: true,
            HasBindingIssue: false,
            BonePresent: true,
            CapabilityRequired: false,
            CapabilityPresent: true,
            CompatibilityDormant: false,
            Metadata: metadata ?? BoneData.GetMetadata("j_sebo_c"),
            ExplicitAuthority: false,
            AxisLocked: false,
            AxisPinned: false,
            SolverEnabled: true,
            ModelDerivedImportanceActive: false,
            BoneImportance: null,
            HasResolvedContribution: true);
}
