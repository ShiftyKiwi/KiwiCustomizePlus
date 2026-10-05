// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Bindings.ImGui;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ProfilesTab
{
    private readonly ProfileFileSystemSelector _selector;
    private readonly ProfilePanel _panel;
    private readonly ActorEffectiveStateInspectorPanel _inspector;

    public ProfilesTab(ProfileFileSystemSelector selector, ProfilePanel panel, ActorEffectiveStateInspectorPanel inspector)
    {
        _selector = selector;
        _panel = panel;
        _inspector = inspector;
    }

    public void Draw()
    {
        _inspector.Draw();
        ImGui.Separator();
        _selector.Draw();
        ImGui.SameLine();
        _panel.Draw();
    }
}
