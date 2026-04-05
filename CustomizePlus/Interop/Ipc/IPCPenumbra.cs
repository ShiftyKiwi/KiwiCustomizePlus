// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Plugin;
using Newtonsoft.Json.Linq;
using OtterGui.Log;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Interop.Ipc;

public sealed class PenumbraIpcHandler : IIpcSubscriber
{
    private readonly Logger _log;
    private readonly ApiVersion _version;

    private readonly EventSubscriber<JObject, ushort, string> _pcpCreated;
    private readonly EventSubscriber<JObject, string, Guid> _pcpParsed;
    private readonly ResolveGameObjectPath _resolveGameObjectPath;
    private readonly GetGameObjectResourcePaths _getGameObjectResourcePaths;
    private readonly IDisposable _penumbraInit;
    private readonly IDisposable _penumbraDisp;

    private const int RequiredMajor = 5;
    private const int RequiredMinor = 8;
    private int CurrentMajor = 0;
    private int CurrentMinor = 0;

    private bool _available = false;
    public bool Available => _available;

    private bool _shownVersionWarning = false;

    public PenumbraIpcHandler(IDalamudPluginInterface pi, Logger log)
    {
        _log = log;
        _version = new ApiVersion(pi);

        _pcpCreated = CreatingPcp.Subscriber(pi);
        _pcpParsed = ParsingPcp.Subscriber(pi);
        _resolveGameObjectPath = new ResolveGameObjectPath(pi);
        _getGameObjectResourcePaths = new GetGameObjectResourcePaths(pi);

        _penumbraInit = Initialized.Subscriber(pi, Initialize);
        _penumbraDisp = Disposed.Subscriber(pi, Disable);

        Initialize();
    }

    public event Action<JObject, ushort, string> PcpCreated
    {
        add => _pcpCreated.Event += value;
        remove => _pcpCreated.Event -= value;
    }

    public event Action<JObject, string, Guid> PcpParsed
    {
        add => _pcpParsed.Event += value;
        remove => _pcpParsed.Event -= value;
    }

    public bool TryResolveGameObjectPath(string gamePath, int gameObjectIndex, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (!_available || string.IsNullOrWhiteSpace(gamePath) || gameObjectIndex < 0)
            return false;

        try
        {
            resolvedPath = _resolveGameObjectPath.Invoke(gamePath, gameObjectIndex);
            return !string.IsNullOrWhiteSpace(resolvedPath);
        }
        catch
        {
            resolvedPath = string.Empty;
            return false;
        }
    }

    public bool TryGetGameObjectResourcePaths(ushort gameObjectIndex, out Dictionary<string, HashSet<string>> resourcePaths)
    {
        resourcePaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (!_available)
            return false;

        try
        {
            var paths = _getGameObjectResourcePaths.Invoke(gameObjectIndex);
            var first = paths.Length > 0 ? paths[0] : null;
            if (first == null || first.Count == 0)
                return false;

            resourcePaths = first;
            return true;
        }
        catch
        {
            resourcePaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    public bool CheckApiVersion()
    {
        try
        {
            var (major, minor) = _version.Invoke();
            CurrentMajor = major;
            CurrentMinor = minor;

            var valid = major == RequiredMajor && minor >= RequiredMinor;
            if (!valid && !_shownVersionWarning)
            {
                _shownVersionWarning = true;
                _log.Warning($"Penumbra IPC version is not supported. Required: {RequiredMajor}.{RequiredMinor}+");
            }

            return valid;
        }
        catch
        {
            return false;
        }
    }

    public void Initialize()
    {
        Disable();

        if (!CheckApiVersion())
            return;

        _available = true;
        _pcpCreated.Enable();
        _pcpParsed.Enable();

        _log.Information($"Penumbra IPC initialized. Version {CurrentMajor}.{CurrentMinor}.");
    }
    public void Disable()
    {
        if (!_available)
            return;

        _available = false;
        _pcpCreated.Disable();
        _pcpParsed.Disable();

        _log.Information("Penumbra IPC disabled.");
    }
    public void Dispose()
    {
        Disable();
        _pcpCreated.Dispose();
        _pcpParsed.Dispose();
        _penumbraInit.Dispose();
        _penumbraDisp.Dispose();

        _log.Information("Penumbra IPC disposed.");
    }
}
