// Copyright (c) Customize+.
// Licensed under the MIT license.

using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using Dalamud.Plugin.Services;
using OtterGui.Log;
using Penumbra.GameData.Actors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Templates;

public class TemplateEditorManager : IDisposable
{
    private readonly TemplateChanged _event;
    private readonly Logger _logger;
    private readonly GameObjectService _gameObjectService;
    private readonly TemplateManager _templateManager;
    private readonly IClientState _clientState;
    private readonly PluginConfiguration _configuration;

    /// <summary>
    /// Immutable snapshot of the template state when the current editing session started.
    /// </summary>
    private Template _currentlyEditedTemplateOriginal = null!;

    /// <summary>
    /// Internal profile for the editor
    /// </summary>
    public Profile EditorProfile { get; private set; }

    /// <summary>
    /// Original ID of the template which is currently being edited
    /// </summary>
    public Guid CurrentlyEditedTemplateId { get; private set; }

    /// <summary>
    /// A copy of currently edited template, all changes must be done on this template
    /// </summary>
    public Template? CurrentlyEditedTemplate { get; private set; }

    public bool IsEditorActive { get; private set; }

    /// <summary>
    /// Is editor currently paused? Happens automatically when editor is not compatible with the current game state.
    /// Keeps editor state frozen and prevents any changes to it, also sets editor profile as disabled.
    /// </summary>
    public bool IsEditorPaused { get; private set; }

    /// <summary>
    /// Indicates if there are any changes in current editing session or not
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// Name of the preview character for the editor
    /// </summary>
    public ActorIdentifier Character => EditorProfile.Characters[0];

    /// <summary>
    /// Checks if preview character exists at the time of call
    /// </summary>
    public bool IsCharacterFound
    {
        get
        {
            var playerName = _gameObjectService.GetCurrentPlayerName();
            return _gameObjectService.FindActorsByIdentifierIgnoringOwnership(Character)
                .Where(x => x.Item1.Type != Penumbra.GameData.Enums.IdentifierType.Owned || x.Item1.IsOwnedByLocalPlayer())
                .Any();
        }
    }

    public bool IsKeepOnlyEditorProfileActive { get; set; } //todo

    public TemplateEditorManager(
        TemplateChanged @event,
        Logger logger,
        TemplateManager templateManager,
        GameObjectService gameObjectService,
        IClientState clientState,
        PluginConfiguration configuration)
    {
        _event = @event;
        _logger = logger;
        _templateManager = templateManager;
        _gameObjectService = gameObjectService;
        _clientState = clientState;
        _configuration = configuration;

        _clientState.Login += OnLogin;

        EditorProfile = new Profile()
        {
            Templates = new List<Template>(),
            Enabled = false,
            Name = "Template editor profile",
            ProfileType = ProfileType.Editor,
        };

        EditorProfile.Characters.Add(configuration.EditorConfiguration.PreviewCharacter);
    }

    public void Dispose()
    {
        _clientState.Login -= OnLogin;
    }

    /// <summary>
    /// Turn on editing of a specific template. If character name not set will default to local player.
    /// </summary>
    internal bool EnableEditor(Template template)
    {
        if (IsEditorActive || IsEditorPaused)
            return false;

        _logger.Debug($"Enabling editor profile for {template.Name} via character {Character.Incognito(null)}");

        CurrentlyEditedTemplateId = template.UniqueId;
        _currentlyEditedTemplateOriginal = new Template(template);
        CurrentlyEditedTemplate = new Template(template)
        {
            CreationDate = DateTimeOffset.UtcNow,
            ModifiedDate = DateTimeOffset.UtcNow,
            UniqueId = Guid.NewGuid(),
            Name = "Template editor temporary template"
        };

        if (!Character.IsValid) //safeguard
            ChangeEditorCharacterInternal(_gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent()); //will set EditorProfile.Character

        EditorProfile.Templates.Clear(); //safeguard
        EditorProfile.Templates.Add(CurrentlyEditedTemplate);
        EditorProfile.Enabled = true;
        HasChanges = false;
        IsEditorActive = true;

        _event.Invoke(TemplateChanged.Type.EditorEnabled, template, Character);

        return true;
    }

    /// <summary>
    /// Turn off editing of a specific template
    /// </summary>
    internal bool DisableEditor()
    {
        if (!IsEditorActive || IsEditorPaused)
            return false;

        _logger.Debug($"Disabling editor profile");

        //todo: can be optimized by storing actual reference to original template somewhere
        var template = _templateManager.GetTemplate(CurrentlyEditedTemplateId);
        var hasChanges = HasChanges;

        CurrentlyEditedTemplateId = Guid.Empty;
        CurrentlyEditedTemplate = null;
        EditorProfile.Enabled = false;
        EditorProfile.Templates.Clear();
        IsEditorActive = false;
        HasChanges = false;

        _event.Invoke(TemplateChanged.Type.EditorDisabled, template, (Character, hasChanges));

        return true;
    }

    public void SaveChangesAndDisableEditor(bool asCopy = false)
    {
        if (!IsEditorActive || IsEditorPaused)
            return;

        if (!HasChanges)
        {
            DisableEditor();
            return;
        }

        var targetTemplate = _templateManager.GetTemplate(CurrentlyEditedTemplateId);
        if (targetTemplate == null)
            throw new Exception($"Fatal editor error: Template with ID {CurrentlyEditedTemplateId} not found in template manager");

        if (asCopy)
        {
            targetTemplate = _templateManager.Clone(targetTemplate, $"{targetTemplate.Name} - Copy {Guid.NewGuid().ToString().Substring(0, 4)}", false);
            HasChanges = false; //do this so EditorDisabled event sends proper info about the state of *currently edited* template
        }

        _templateManager.ApplyBoneChangesAndSave(targetTemplate, CurrentlyEditedTemplate!);

        DisableEditor();
    }

    public bool ChangeEditorCharacter(ActorIdentifier character)
    {
        if (!IsEditorActive || Character == character || IsEditorPaused || !character.IsValid)
            return false;

        return ChangeEditorCharacterInternal(character);
    }

    private bool ChangeEditorCharacterInternal(ActorIdentifier character)
    {
        _logger.Debug($"Changing character name for editor profile from {EditorProfile.Characters.FirstOrDefault().Incognito(null)} to {character.Incognito(null)}");

        EditorProfile.Characters.Clear();
        EditorProfile.Characters.Add(character);

        _configuration.EditorConfiguration.PreviewCharacter = character;
        _configuration.Save();

        _event.Invoke(TemplateChanged.Type.EditorCharacterChanged, CurrentlyEditedTemplate, (character, EditorProfile));

        return true;
    }

    /// <summary>
    /// Resets changes in currently edited template to default values
    /// </summary>
    public bool ResetBoneAttributeChanges(string boneName, BoneAttribute attribute)
    {
        if (!IsEditorActive || IsEditorPaused)
            return false;

        if (!CurrentlyEditedTemplate!.Bones.TryGetValue(boneName, out var currentTransform) || currentTransform == null)
            return false;

        var resetValue = GetResetValueForAttribute(attribute);
        var defaultPropagationState = false;
        var updatedTransform = new BoneTransform(currentTransform);

        switch (attribute)
        {
            case BoneAttribute.Position:
                if ((resetValue == currentTransform.Translation) &&
                    (defaultPropagationState == currentTransform.PropagateTranslation))
                    return false;
                break;
            case BoneAttribute.Rotation:
                if ((resetValue == currentTransform.Rotation) &&
                    (defaultPropagationState == currentTransform.PropagateRotation))
                    return false;
                break;
            case BoneAttribute.Scale:
                if ((resetValue == currentTransform.Scaling) &&
                    (defaultPropagationState == currentTransform.PropagateScale) &&
                    (Vector3.One == currentTransform.ChildScaling) &&
                    (false == currentTransform.ChildScalingIndependent))
                    return false;
                break;
        }

        updatedTransform.UpdateAttribute(attribute, resetValue, defaultPropagationState);
        return ModifyBoneTransform(boneName, updatedTransform);
    }

    /// <summary>
    /// Reverts changes in currently edited template to values set in saved copy of the template.
    /// Resets to default value if saved copy doesn't have that bone edited
    /// </summary>
    public bool RevertBoneAttributeChanges(string boneName, BoneAttribute attribute)
    {
        if (!IsEditorActive || IsEditorPaused)
            return false;

        if (!CurrentlyEditedTemplate!.Bones.TryGetValue(boneName, out var currentTransform) || currentTransform == null)
            return false;

        var updatedTransform = new BoneTransform(currentTransform);
        Vector3? originalValue = null!;
        bool originalPropagationState = false;

        if (_currentlyEditedTemplateOriginal.Bones.TryGetValue(boneName, out var originalTransform))
        {
            switch (attribute)
            {
                case BoneAttribute.Position:
                    originalValue = originalTransform.Translation;
                    originalPropagationState = originalTransform.PropagateTranslation;
                    if ((originalValue == currentTransform.Translation) &&
                        (originalPropagationState == currentTransform.PropagateTranslation))
                        return false;
                    break;
                case BoneAttribute.Rotation:
                    originalValue = originalTransform.Rotation;
                    originalPropagationState = originalTransform.PropagateRotation;
                    if ((originalValue == currentTransform.Rotation) &&
                        (originalPropagationState == currentTransform.PropagateRotation))
                        return false;
                    break;
                case BoneAttribute.Scale:
                    originalValue = originalTransform.Scaling;
                    originalPropagationState = originalTransform.PropagateScale;
                    var originalChildScaling = originalTransform.ChildScaling;
                    var originalChildScalingIndependent = originalTransform.ChildScalingIndependent;
                    if ((originalValue == currentTransform.Scaling) &&
                        (originalPropagationState == currentTransform.PropagateScale) &&
                        (originalChildScaling == currentTransform.ChildScaling) &&
                        (originalChildScalingIndependent == currentTransform.ChildScalingIndependent))
                        return false;

                    updatedTransform.ChildScaling = originalChildScaling;
                    updatedTransform.ChildScalingIndependent = originalChildScalingIndependent;
                    updatedTransform.PropagationFalloff = originalTransform.PropagationFalloff;
                    break;
            }
        }
        else
        {
            originalValue = GetResetValueForAttribute(attribute);
            originalPropagationState = false;
        }

        updatedTransform.UpdateAttribute(attribute, originalValue.Value, originalPropagationState);
        return ModifyBoneTransform(boneName, updatedTransform);
    }

    public bool ModifyBoneTransform(string boneName, BoneTransform transform)
    {
        if (!IsEditorActive || IsEditorPaused)
            return false;

        if (!_templateManager.ModifyBoneTransform(CurrentlyEditedTemplate!, boneName, transform))
            return false;

        if (!HasChanges)
            HasChanges = true;

        return true;
    }

    private void OnLogin()
    {
        if (_configuration.EditorConfiguration.SetPreviewToCurrentCharacterOnLogin ||
            !_configuration.EditorConfiguration.PreviewCharacter.IsValid)
        {
            var localPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier();
            if (!localPlayer.IsValid)
            {
                _logger.Warning("Can't retrieve local player on login");
                return;
            }

            localPlayer = localPlayer.CreatePermanent();

            if (_configuration.EditorConfiguration.PreviewCharacter != localPlayer)
            {
                _logger.Debug("Resetting editor character because automatic condition triggered in OnLogin");
                ChangeEditorCharacterInternal(localPlayer);
            }
        }
    }

    private Vector3 GetResetValueForAttribute(BoneAttribute attribute)
    {
        switch (attribute)
        {
            case BoneAttribute.Scale:
                return Vector3.One;
            default:
                return Vector3.Zero;
        }
    }
}
