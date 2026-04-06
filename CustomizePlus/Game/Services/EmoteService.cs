// Copyright (c) Customize+.
// Licensed under the MIT license.

using Penumbra.GameData.Interop;
using OtterGui.Log;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Game.Services;

public class EmoteService
{
    private const long StateSweepIntervalMs = 10000;
    private const long StateExpiryMs = 30000;
    private const long VerboseLogIntervalMs = 5000;

    private readonly Logger _logger;
    private readonly object _stateLock = new();
    private readonly Dictionary<ushort, ObservedEmoteState> _observedStates = new();
    private long _lastSweepAt;

    private sealed class ObservedEmoteState
    {
        public ushort EmoteId { get; set; }
        public bool Sitting { get; set; }
        public long LastSeenAt { get; set; }
        public long LastVerboseLogAt { get; set; }
        public bool Initialized { get; set; }
    }

    public EmoteService(Logger logger)
    {
        _logger = logger;
    }

    private static readonly ushort[] ChairSitEmotes = { 50, 95, 96, 254, 255 }; // not groundsit

    public unsafe bool IsSitting(Actor actor)
    {
        if (!actor.Valid || !actor.IsCharacter || actor.AsCharacter == null)
            return false;

        var now = Environment.TickCount64;
        var actorIndex = actor.Index.Index;
        var emoteId = actor.AsCharacter->EmoteController.EmoteId;
        var isSitting = ChairSitEmotes.Contains(emoteId);
        var actorName = actor.Utf8Name.ToString();

        lock (_stateLock)
        {
            SweepStaleStates(now);

            if (!_observedStates.TryGetValue(actorIndex, out var state))
            {
                state = new ObservedEmoteState();
                _observedStates[actorIndex] = state;
            }

            if (state.Initialized)
            {
                if (state.EmoteId != emoteId || state.Sitting != isSitting)
                {
                    _logger.Debug(
                        $"Actor {actorName} emote state changed: EmoteId {state.EmoteId} -> {emoteId}, Sitting {state.Sitting} -> {isSitting}");
                }
                else if (now - state.LastVerboseLogAt >= VerboseLogIntervalMs)
                {
                    _logger.Verbose($"Actor {actorName} emote state unchanged: EmoteId {emoteId}, Sitting {isSitting}");
                    state.LastVerboseLogAt = now;
                }
            }

            state.EmoteId = emoteId;
            state.Sitting = isSitting;
            state.LastSeenAt = now;
            state.Initialized = true;
        }

        return isSitting;
    }

    private void SweepStaleStates(long now)
    {
        if (now - _lastSweepAt < StateSweepIntervalMs)
            return;

        _lastSweepAt = now;
        if (_observedStates.Count == 0)
            return;

        var staleActors = _observedStates
            .Where(kvp => now - kvp.Value.LastSeenAt >= StateExpiryMs)
            .Select(static kvp => kvp.Key)
            .ToArray();

        foreach (var actorIndex in staleActors)
            _observedStates.Remove(actorIndex);
    }
}
