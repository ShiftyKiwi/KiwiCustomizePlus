// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Linq;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using Dalamud.Bindings.ImGui;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

/// <summary>Release-safe, read-only presentation of state already published for an armature.</summary>
public sealed class ActorEffectiveStateInspectorPanel
{
    private readonly ArmatureManager _armatureManager;
    private readonly PluginConfiguration _configuration;

    private string _selectedActor = string.Empty;
    private ActorEffectiveStateSnapshot? _snapshot;

    public ActorEffectiveStateInspectorPanel(ArmatureManager armatureManager, PluginConfiguration configuration)
    {
        _armatureManager = armatureManager;
        _configuration = configuration;
    }

    public void Draw()
    {
        if (!ImGui.CollapsingHeader("Actor / Effective-State Inspector"))
            return;

        ImGui.TextDisabled("Read-only view of an already-published armature. Refreshing never selects actors, resolves profiles, or writes transforms.");
        var armatures = _armatureManager.Armatures.Values
            .OrderBy(static armature => armature.ActorIdentifier.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (armatures.Length == 0)
        {
            DrawUnavailable(ActorEffectiveStateSnapshot.Unavailable("No currently published armatures are available."));
            return;
        }

        var selected = armatures.FirstOrDefault(armature => string.Equals(armature.ActorIdentifier.ToString(), _selectedActor, StringComparison.Ordinal))
                       ?? armatures[0];
        var selectedName = selected.ActorIdentifier.ToString();
        if (ImGui.BeginCombo("Published actor", selectedName))
        {
            foreach (var armature in armatures)
            {
                var actorName = armature.ActorIdentifier.ToString();
                if (ImGui.Selectable(actorName, string.Equals(actorName, selectedName, StringComparison.Ordinal)))
                {
                    selected = armature;
                    selectedName = actorName;
                    _selectedActor = actorName;
                    _snapshot = null;
                }
            }

            ImGui.EndCombo();
        }

        if (_snapshot == null || !string.Equals(_selectedActor, selectedName, StringComparison.Ordinal))
        {
            _selectedActor = selectedName;
            _snapshot = ActorEffectiveStateSnapshot.Capture(selected, _configuration.AdvancedBodyScalingSettings);
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh inspector snapshot"))
            _snapshot = ActorEffectiveStateSnapshot.Capture(selected, _configuration.AdvancedBodyScalingSettings);

        var snapshot = _snapshot;
        if (snapshot == null || !snapshot.IsAvailable)
        {
            DrawUnavailable(snapshot ?? ActorEffectiveStateSnapshot.Unavailable("The inspector snapshot could not be created."));
            return;
        }

        DrawOverview(snapshot);
        DrawProfile(snapshot);
        DrawTemplates(snapshot);
        DrawAdvancedBodyScaling(snapshot);
        DrawRuntime(snapshot);
    }

    private static void DrawOverview(ActorEffectiveStateSnapshot snapshot)
    {
        if (!ImGui.TreeNodeEx("Actor and representation", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        DrawFact("Actor", snapshot.Actor);
        DrawFact("Representation", snapshot.Representation);
        DrawFact("Binding", snapshot.Binding);
        DrawFact("Capabilities", snapshot.CapabilityState);
        ImGui.TreePop();
    }

    private static void DrawProfile(ActorEffectiveStateSnapshot snapshot)
    {
        if (!ImGui.TreeNodeEx("Winning profile", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        DrawFact("Profile", snapshot.WinningProfile);
        ImGui.TextUnformatted($"Enabled: {(snapshot.ProfileEnabled ? "yes" : "no")}; priority: {snapshot.ProfilePriority}; profile overrides: {(snapshot.ProfileUsesOverrides ? $"on ({snapshot.ProfileOverrideCount})" : "off")}");
        DrawFact("Why this profile won", snapshot.ProfileMatchReason);
        ImGui.TreePop();
    }

    private static void DrawTemplates(ActorEffectiveStateSnapshot snapshot)
    {
        if (!ImGui.TreeNode("Templates"))
            return;

        DrawFact("Published participation", snapshot.TemplateParticipation);
        DrawFact("Per-bone provenance", snapshot.PerBoneTemplateProvenance);
        if (ImGui.BeginTable("##ActorEffectiveTemplateAssignments", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Template");
            ImGui.TableSetupColumn("Enabled");
            ImGui.TableSetupColumn("Weight");
            ImGui.TableSetupColumn("Saved rows");
            ImGui.TableSetupColumn("Capability requirement");
            ImGui.TableHeadersRow();
            foreach (var template in snapshot.TemplateAssignments)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template.Enabled ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template.Weight.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template.SavedTransformCount.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template.RequiredCapabilities);
            }

            ImGui.EndTable();
        }

        ImGui.TreePop();
    }

    private static void DrawAdvancedBodyScaling(ActorEffectiveStateSnapshot snapshot)
    {
        if (!ImGui.TreeNodeEx("Effective Advanced Body Scaling", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        DrawFact("Effective", snapshot.EffectiveAdvancedBodyScaling);
        DrawFact("Race/preset contribution", snapshot.RacePresetProvenance);
        ImGui.TextUnformatted($"Global configured: {(snapshot.GlobalAdvancedBodyScalingEnabled ? "enabled" : "disabled")} ({snapshot.GlobalAdvancedBodyScalingMode})");
        if (snapshot.EffectiveAdvancedBodyScalingEnabled.HasValue)
        {
            ImGui.TextUnformatted($"Effective mode: {snapshot.EffectiveAdvancedBodyScalingMode}; surface {snapshot.EffectiveSurfaceBalancingStrength:0.00}; mass {snapshot.EffectiveMassRedistributionStrength:0.00}; naturalization {snapshot.EffectiveNaturalizationStrength:0.00}");
            ImGui.TextUnformatted($"Effective guardrail: {snapshot.EffectiveGuardrailMode}; pose validation: {snapshot.EffectivePoseValidationMode}");
        }

        DrawFact("Bone Importance", snapshot.BoneImportance);
        ImGui.TreePop();
    }

    private static void DrawRuntime(ActorEffectiveStateSnapshot snapshot)
    {
        if (!ImGui.TreeNode("Runtime and binding"))
            return;

        ImGui.TextUnformatted($"Revisions: armature {snapshot.ArmatureRevision}; native {snapshot.NativeBindingGeneration}; profile {snapshot.ProfileResolutionRevision}; deformation {snapshot.DeformationRevision}");
        ImGui.TextUnformatted($"Resolved static transforms: {snapshot.ResolvedTransformCount}; bound ModelBones: {snapshot.BoundModelBoneCount}; pending observations: {snapshot.PendingPublicationObservations}");
        ImGui.TextUnformatted($"Native safety counters: stale {snapshot.StaleBindingSkips}; unsafe {snapshot.UnsafeTransformSkips}");

        if (snapshot.Capabilities.Count > 0 && ImGui.TreeNode("Capability states"))
        {
            foreach (var capability in snapshot.Capabilities)
                ImGui.TextUnformatted($"{capability.Name}: {capability.State}");
            ImGui.TreePop();
        }

        if (snapshot.RuntimeLayers.Count > 0 && ImGui.TreeNode("Dynamic layers"))
        {
            foreach (var layer in snapshot.RuntimeLayers)
                ImGui.TextUnformatted($"{layer.Name}: {(layer.Enabled ? (layer.Active ? "active" : "enabled / idle") : "disabled")}{(string.IsNullOrWhiteSpace(layer.Summary) ? string.Empty : $" - {layer.Summary}")}");
            ImGui.TreePop();
        }

        if (snapshot.OptionalLayerHealth.Count > 0 && ImGui.TreeNode("Optional-layer health"))
        {
            foreach (var layer in snapshot.OptionalLayerHealth)
                ImGui.TextUnformatted($"{layer.Layer}: {layer.MostRecentFailureType}; {layer.RepeatedFailureCount} recent occurrence(s){(layer.Recovered ? "; recovered" : string.Empty)}");
            ImGui.TreePop();
        }

        if (snapshot.RuntimeTimings.Count > 0 && ImGui.TreeNode("Runtime timing summaries"))
        {
            foreach (var timing in snapshot.RuntimeTimings)
                ImGui.TextUnformatted($"{timing.Stage}: latest {timing.LatestMilliseconds:0.00} ms; avg {timing.AverageMilliseconds:0.00} ms; max {timing.MaxMilliseconds:0.00} ms ({timing.Samples} samples)");
            ImGui.TreePop();
        }

        ImGui.TreePop();
    }

    private static void DrawUnavailable(ActorEffectiveStateSnapshot snapshot)
        => DrawFact("Inspector", snapshot.Actor);

    private static void DrawFact(string label, ActorEffectiveStateFact fact)
    {
        ImGui.TextUnformatted($"{label}: ");
        ImGui.SameLine();
        if (fact.Kind == ActorEffectiveStateFactKind.Unavailable)
            ImGui.TextDisabled(fact.Value);
        else
            ImGui.TextUnformatted(fact.Value);

        if (!string.IsNullOrWhiteSpace(fact.Detail))
            ImGui.TextDisabled(fact.Detail);
    }
}
