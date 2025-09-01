using MyWinKeys.Infrastructure;

namespace MyWinKeys.Core;

internal sealed class RemapEngine
{
    private readonly AppConfig _cfg;

    private readonly object _lock = new();

    private class KeyState
    {
        public bool Down;
        public DateTime DownAt;
        public bool SentHold; // decision made (tap or hold)
        public bool HoldActive; // modifier is currently pressed down
        public bool OutputEmitted; // e.g., Space tap already sent in Space+Other mode
    }

    private readonly Dictionary<int, KeyState> _states = [];
    private readonly Queue<(int vk, DateTime at)> _recentDowns = new();
    private readonly HashSet<int> _comboKeys;
    private readonly Dictionary<int, DateTime> _pendingCombos = [];
    private readonly Dictionary<int, int> _mappedArrowKeys = [];

    public RemapEngine(AppConfig cfg)
    {
        _cfg = cfg;
        _comboKeys = [
            _cfg.VK_J, _cfg.VK_W, _cfg.VK_E, _cfg.VK_I, _cfg.VK_O,
            _cfg.VK_1, _cfg.VK_2, _cfg.VK_3, _cfg.VK_4,
            _cfg.VK_A, _cfg.VK_S, _cfg.VK_Z, _cfg.VK_H, _cfg.VK_L,
            _cfg.VK_C, _cfg.VK_M, _cfg.VK_OEM_COMMA, _cfg.VK_OEM_PERIOD
        ];
    }

    public bool ProcessEvent(int vk, bool isDown)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            var isTabHeld = _states.TryGetValue(_cfg.VK_Tab, out var tabSt) && tabSt.Down;
            var swallowRepeat = false;
            if (vk == _cfg.VK_K || vk == _cfg.VK_D) // For D/K, only swallow if Tab is not held
            {
                if (!isTabHeld) swallowRepeat = true;
            }
            else if (vk == _cfg.VK_Space || vk == _cfg.VK_Tab)
            {
                swallowRepeat = true;
            }

            // For repeats on Space/D/K/Tab we skip processing entirely; avoid logging spam as well
            if (isDown && _states.TryGetValue(vk, out var stRpt) && stRpt.Down && swallowRepeat)
            {
                return true; // swallow repeats
            }
            Logger.Info($"Key {(isDown ? "Down" : "Up")} VK=0x{vk:X}");

            // Simple replacements
            if (vk == _cfg.VK_CapsLock || (_cfg.AltCapsVks?.Contains(vk) ?? false))
            {
                if (isDown) { Logger.Info("Caps->Kanji"); InputSender.Tap(_cfg.VK_Kanji); } // IME toggle (Hankaku/Zenkaku)
                return true;
            }
            if (vk == _cfg.VK_Muhenkan)
            {
                if (isDown)
                {
                    // Respect SandS (Space-as-Shift) when mapping
                    EnsureSandSChordActive(vk);
                    Logger.Info("Muhenkan->Backspace");
                    InputSender.Tap(_cfg.VK_Back);
                }
                return true;
            }
            if (vk == _cfg.VK_Henkan)
            {
                if (isDown)
                {
                    // Respect SandS (Space-as-Shift) when mapping
                    EnsureSandSChordActive(vk);
                    Logger.Info("Henkan->Enter");
                    InputSender.Tap(_cfg.VK_Return);
                }
                return true;
            }

            // Track state
            if (!_states.TryGetValue(vk, out var st))
            {
                st = new KeyState();
                _states[vk] = st;
            }

            if (isDown)
            {
                // (Repeat already filtered above)
                st.Down = true;
                st.DownAt = now;
                st.SentHold = false;
                st.HoldActive = false;
                st.OutputEmitted = false;
                _recentDowns.Enqueue((vk, now));
                TrimRecent(now);

                // Check combos first (priority over tap-hold)
                if (CheckCombosOnDown(now))
                {
                    Logger.Info("Combo detected");
                    return true; // suppress both originals
                }

                // For keys that can participate in combos, buffer their tap briefly
                if (_comboKeys.Contains(vk))
                {
                    // Other key down may affect tap-hold interactions (Space/D/K/Tab)
                    if (!HandleTapHoldInteraction(vk, now))
                    {
                        _pendingCombos[vk] = now;
                    }
                    // Do not emit immediately
                    return true;
                }

                // For tap-hold keys: Space, D, K, Tab
                if (vk == _cfg.VK_Space || vk == _cfg.VK_D || vk == _cfg.VK_K || vk == _cfg.VK_Tab)
                {
                    // Apply interactions only for D/K to let SandS kick in; do NOT trigger on Space/Tab itself
                    if (vk == _cfg.VK_D || vk == _cfg.VK_K)
                        HandleTapHoldInteraction(vk, now);
                    // Defer decision until up or other activity
                    Logger.Info("TapHold key down buffered");
                    return true; // suppress original event for these keys until resolved
                }

                // Other key down may affect tap-hold interactions
                HandleTapHoldInteraction(vk, now);

                return false; // pass through others
            }
            else // key up
            {
                // If this key was buffered as a combo candidate and no combo consumed it, emit now to avoid lag
                if (_comboKeys.Contains(vk) && _pendingCombos.ContainsKey(vk))
                {
                    Logger.Info($"Combo candidate key up -> immediate tap VK=0x{vk:X}");
                    InputSender.Tap(vk);
                    _pendingCombos.Remove(vk);
                    if (_states.TryGetValue(vk, out var st0)) { st0.SentHold = true; st0.Down = false; }
                    return true;
                }

                if (!st.Down)
                {
                    return _comboKeys.Contains(vk) || vk == _cfg.VK_Space || vk == _cfg.VK_D || vk == _cfg.VK_K || vk == _cfg.VK_Tab;
                }

                st.Down = false;
                var heldMs = (int)(now - st.DownAt).TotalMilliseconds;
                Logger.Info($"Key Up VK=0x{vk:X} held for {heldMs}ms");

                if (_mappedArrowKeys.TryGetValue(vk, out var arrowVk))
                {
                    Logger.Info($"Tab + {(char)vk} released -> Arrow key UP");
                    InputSender.KeyUp_AllowRepeat(arrowVk);
                    _mappedArrowKeys.Remove(vk);
                    return true; // suppress original key up
                }

                if (vk == _cfg.VK_Space)
                {
                    // SandS spec:
                    // - Space alone: <HoldThreshold => Space; >=HoldThreshold => Shift+Space
                    // - Space+Other within HoldThreshold => Space then Other (send Space immediately on Other down),
                    //   and if Space kept held while typing, apply Shift to subsequent keys until Space release.
                    // - Space -> Other within TapGrace while Space still down => Shift+Other
                    if (st.HoldActive)
                    {
                        // Shift was active due to Space+typing mode; release it
                        Logger.Info("SandS: release Shift");
                        InputSender.KeyUp(_cfg.VK_Shift);
                    }
                    else if (st.OutputEmitted)
                    {
                        // Space was already emitted earlier due to Space+Other; nothing to do on release
                        Logger.Info("SandS: space already emitted earlier");
                    }
                    else if (heldMs >= _cfg.HoldThresholdMs)
                    {
                        Logger.Info("SandS: long hold -> Shift+Space");
                        InputSender.KeyDown(_cfg.VK_Shift);
                        InputSender.Tap(_cfg.VK_Space);
                        InputSender.KeyUp(_cfg.VK_Shift);
                        st.OutputEmitted = true; // mark that a space was injected for this hold
                    }
                    else
                    {
                        Logger.Info("SandS: tap Space");
                        InputSender.Tap(_cfg.VK_Space);
                    }
                    st.SentHold = true;
                    st.OutputEmitted = false;
                    return true;
                }

                if (vk == _cfg.VK_Tab)
                {
                    if (st.HoldActive)
                    {
                        // Modifier was active, do nothing on release
                        Logger.Info("Tab hold: released");
                        foreach (var arrVk in _mappedArrowKeys.Values)
                        {
                            InputSender.KeyUp_AllowRepeat(arrVk);
                        }
                        _mappedArrowKeys.Clear();
                        st.HoldActive = false;
                    }
                    else if (st.SentHold)
                    {
                        // Decision already made
                    }
                    else
                    {
                        Logger.Info("Tab tap");
                        InputSender.Tap(_cfg.VK_Tab);
                    }
                    st.SentHold = true;
                    return true;
                }

                if (vk == _cfg.VK_D)
                {
                    if (st.SentHold)
                    {
                        // Decision already made earlier by interaction; only release if a Ctrl hold was active
                        if (st.HoldActive)
                        { Logger.Info("D hold: release LCtrl"); InputSender.KeyUp(_cfg.VK_LControl); }
                        return true;
                    }
                    if (st.HoldActive)
                    {
                        Logger.Info("D hold: release LCtrl"); InputSender.KeyUp(_cfg.VK_LControl);
                    }
                    else if (heldMs >= _cfg.HoldThresholdMs)
                    {
                        Logger.Info("D long hold: tap LCtrl"); InputSender.Tap(_cfg.VK_LControl);
                    }
                    else
                    {
                        Logger.Info("D tap"); InputSender.Tap(_cfg.VK_D);
                    }
                    st.SentHold = true;
                    return true;
                }

                if (vk == _cfg.VK_K)
                {
                    if (st.SentHold)
                    {
                        // Decision already made earlier by interaction; only release if a Ctrl hold was active
                        if (st.HoldActive)
                        { Logger.Info("K hold: release RCtrl"); InputSender.KeyUp(_cfg.VK_RControl); }
                        return true;
                    }
                    if (st.HoldActive)
                    {
                        Logger.Info("K hold: release RCtrl"); InputSender.KeyUp(_cfg.VK_RControl);
                    }
                    else if (heldMs >= _cfg.HoldThresholdMs)
                    {
                        Logger.Info("K long hold: tap RCtrl"); InputSender.Tap(_cfg.VK_RControl);
                    }
                    else
                    {
                        Logger.Info("K tap"); InputSender.Tap(_cfg.VK_K);
                    }
                    st.SentHold = true;
                    return true;
                }

                // Other keys passthrough
                return false;
            }
        }
    }

    // If Space-based SandS chord should be active for a non-space key, ensure Shift is held before emitting mapped output
    private void EnsureSandSChordActive(int otherVk)
    {
        if (otherVk == _cfg.VK_Space) return;
        if (_states.TryGetValue(_cfg.VK_Space, out var space) && space.Down && !space.SentHold)
        {
            if (!space.HoldActive)
            {
                Logger.Info("SandS: hold Shift (chord) [via mapping]");
                InputSender.KeyDown(_cfg.VK_Shift);
                space.HoldActive = true;
            }
            _pendingCombos.Remove(_cfg.VK_Space);
        }
    }

    private void TrimRecent(DateTime now)
    {
        while (_recentDowns.Count > 0 && (now - _recentDowns.Peek().at).TotalMilliseconds > _cfg.HoldThresholdMs)
        {
            _recentDowns.Dequeue();
        }
    }

    private bool CheckCombosOnDown(DateTime now)
    {
        // Evaluate specific combos with window of ComboWindowMs
        // We only act when the second key in a combo is pressed within window after the first.
        if (_recentDowns.Count < 1) return false;

        var arr = _recentDowns.Reverse().Take(3).ToArray();

        // Helper
        bool Within((int vk, DateTime at) a, int vk2) => a.vk == vk2 && (now - a.at).TotalMilliseconds <= _cfg.ComboWindowMs;

        // Detect combos regardless of order for certain pairs?
        // For jk simultaneous: treat as unordered within window
        var latest = arr.First();

        // jk -> Esc (send Muhenkan then Esc as per spec: "無変換→Esc")
        bool jRecent = arr.Any(a => Within(a, _cfg.VK_J));
        bool kRecent = arr.Any(a => Within(a, _cfg.VK_K));
        if (jRecent && kRecent)
        {
            // Cancel J and K actions
            ConsumeKey(_cfg.VK_J);
            ConsumeKey(_cfg.VK_K);
            InputSender.Tap(_cfg.VK_Muhenkan);
            InputSender.Tap(_cfg.VK_Escape);
            Logger.Info("Combo jk -> Muhenkan, Esc");
            return true;
        }

        // we -> q (unordered within window)
        bool wRecent = arr.Any(a => Within(a, _cfg.VK_W));
        bool eRecent = arr.Any(a => Within(a, _cfg.VK_E));
        if (wRecent && eRecent)
        {
            ConsumeKey(_cfg.VK_W); ConsumeKey(_cfg.VK_E);
            InputSender.Tap(_cfg.VK_Q); return true;
        }
        bool iRecent = arr.Any(a => Within(a, _cfg.VK_I));
        bool oRecent = arr.Any(a => Within(a, _cfg.VK_O));
        if (iRecent && oRecent)
        {
            ConsumeKey(_cfg.VK_I); ConsumeKey(_cfg.VK_O);
            InputSender.Tap(_cfg.VK_P); return true;
        }
        // (1,2) -> TopLeft
        bool has1 = arr.Any(a => Within(a, _cfg.VK_1));
        bool has2 = arr.Any(a => Within(a, _cfg.VK_2));
        bool has3 = arr.Any(a => Within(a, _cfg.VK_3));
        bool has4 = arr.Any(a => Within(a, _cfg.VK_4));
        if (has1 && has2)
        {
            ConsumeKey(_cfg.VK_1); ConsumeKey(_cfg.VK_2);
            _pendingCombos.Remove(_cfg.VK_1); _pendingCombos.Remove(_cfg.VK_2);
            Logger.Info("Combo 1+2 -> (left-top)");
            WindowUtil.MoveCursorLeftTop(); return true;
        }
        // (2,3) -> title center
        if (has2 && has3)
        {
            ConsumeKey(_cfg.VK_2); ConsumeKey(_cfg.VK_3);
            _pendingCombos.Remove(_cfg.VK_2); _pendingCombos.Remove(_cfg.VK_3);
            Logger.Info("Combo 2+3 -> (title-center)");
            WindowUtil.MoveCursorTitleCenter(); return true;
        }
        // (3,4) -> TopRight
        if (has3 && has4)
        {
            ConsumeKey(_cfg.VK_3); ConsumeKey(_cfg.VK_4);
            _pendingCombos.Remove(_cfg.VK_3); _pendingCombos.Remove(_cfg.VK_4);
            Logger.Info("Combo 3+4 -> (right-top)");
            WindowUtil.MoveCursorRightTop();
            return true;
        }
        // (1,4) -> window center
        if (has1 && has4)
        {
            ConsumeKey(_cfg.VK_1); ConsumeKey(_cfg.VK_4);
            _pendingCombos.Remove(_cfg.VK_1); _pendingCombos.Remove(_cfg.VK_4);
            Logger.Info("Combo 1+4 -> cursor window center");
            WindowUtil.MoveCursorWindowCenter();
            return true;
        }
        // (1,3) -> LeftBottom
        if (has1 && has3)
        {
            ConsumeKey(_cfg.VK_1); ConsumeKey(_cfg.VK_3);
            _pendingCombos.Remove(_cfg.VK_1); _pendingCombos.Remove(_cfg.VK_3);
            Logger.Info("Combo 1+3 -> (left-bottom)");
            WindowUtil.MoveCursorLeftBottom(); return true;
        }
        // (2,4) -> RightBottom
        if (has2 && has4)
        {
            ConsumeKey(_cfg.VK_2); ConsumeKey(_cfg.VK_4);
            _pendingCombos.Remove(_cfg.VK_2); _pendingCombos.Remove(_cfg.VK_4);
            Logger.Info("Combo 2+4 -> (right-bottom)");
            WindowUtil.MoveCursorRightBottom(); return true;
        }

        // dc -> Muhenkan
        bool dRecent = arr.Any(a => Within(a, _cfg.VK_D));
        bool cRecent = arr.Any(a => Within(a, _cfg.VK_C));
        if (dRecent && cRecent)
        {
            ConsumeKey(_cfg.VK_D); ConsumeKey(_cfg.VK_C);
            InputSender.Tap(_cfg.VK_Muhenkan);
            Logger.Info("Combo dc -> Muhenkan");
            return true;
        }

        // sd -> Delete (unordered)
        bool sRecent = arr.Any(a => Within(a, _cfg.VK_S));
        if (sRecent && dRecent)
        {
            ConsumeKey(_cfg.VK_S); ConsumeKey(_cfg.VK_D);
            InputSender.Tap(_cfg.VK_Delete);
            Logger.Info("Combo sd -> Delete");
            return true;
        }

        // km -> Henkan
        bool mRecent = arr.Any(a => Within(a, _cfg.VK_M));
        if (kRecent && mRecent)
        {
            ConsumeKey(_cfg.VK_K); ConsumeKey(_cfg.VK_M);
            InputSender.Tap(_cfg.VK_Henkan);
            Logger.Info("Combo km -> Henkan");
            return true;
        }

        // ,. -> _
        bool commaRecent = arr.Any(a => Within(a, _cfg.VK_OEM_COMMA));
        bool periodRecent = arr.Any(a => Within(a, _cfg.VK_OEM_PERIOD));
        if (commaRecent && periodRecent)
        {
            ConsumeKey(_cfg.VK_OEM_COMMA); ConsumeKey(_cfg.VK_OEM_PERIOD);
            InputSender.TypeText("_");
            Logger.Info("Combo ,. -> _");
            return true;
        }

        // az -> "exit" (unordered)
        bool aRecent = arr.Any(a => Within(a, _cfg.VK_A));
        bool zRecent = arr.Any(a => Within(a, _cfg.VK_Z));
        if (aRecent && zRecent)
        {
            ConsumeKey(_cfg.VK_A); ConsumeKey(_cfg.VK_Z);
            InputSender.TypeText("exit"); return true;
        }

        return false;
    }

    private void ConsumeKey(int vk)
    {
        if (_states.TryGetValue(vk, out var st))
        {
            st.Down = false;
            st.SentHold = true; // prevent further action
        }
        _pendingCombos.Remove(vk);
    }

    // Additional logic: if while holding tap-hold keys another key is pressed within grace or before hold threshold
    // we need to send either tap or hold accordingly
    private bool HandleTapHoldInteraction(int otherVk, DateTime now)
    {
        // Tab-hold for arrow keys
        if (_states.TryGetValue(_cfg.VK_Tab, out var tab) && tab.Down && (!tab.SentHold || tab.HoldActive))
        {
            int arrowVk = 0;
            if      (otherVk == _cfg.VK_H) arrowVk = _cfg.VK_Left;
            else if (otherVk == _cfg.VK_J) arrowVk = _cfg.VK_Down;
            else if (otherVk == _cfg.VK_K) arrowVk = _cfg.VK_Up;
            else if (otherVk == _cfg.VK_L) arrowVk = _cfg.VK_Right;

            if (arrowVk != 0)
            {
                // Consume the key first to prevent re-entrancy issues from unmarked SendInput
                if (_states.TryGetValue(otherVk, out var st))
                {
                    st.SentHold = true;
                }
                _pendingCombos.Remove(otherVk);

                // Update Tab state
                tab.HoldActive = true;
                tab.SentHold = true;

                if (!_mappedArrowKeys.ContainsKey(otherVk))
                {
                    _mappedArrowKeys[otherVk] = arrowVk;
                }

                // Now, send the event
                Logger.Info($"Tab + {(char)otherVk} -> Arrow key DOWN");
                InputSender.KeyDown_AllowRepeat(arrowVk);

                return true;
            }
        }

        // Space behavior per new spec
        if (otherVk != _cfg.VK_Space && _states.TryGetValue(_cfg.VK_Space, out var space) && space.Down && !space.SentHold)
        {
            // Any other key while Space is held => act as Shift modifier (no leading space)
            if (!space.HoldActive)
            {
                Logger.Info("SandS: hold Shift (chord)");
                InputSender.KeyDown(_cfg.VK_Shift);
                space.HoldActive = true;
            }
            // Prevent future auto-tap
            _pendingCombos.Remove(_cfg.VK_Space);
        }

        // D/K: if another key is pressed while D/K is held, treat as Ctrl-hold (chord)
        foreach (var key in new[] { _cfg.VK_D, _cfg.VK_K })
        {
            if (_states.TryGetValue(key, out var st) && st.Down && !st.SentHold)
            {
                // Ignore interaction triggered by the same key (we only react to OTHER key presses)
                if (otherVk == key) { continue; }

                var elapsed = (int)(now - st.DownAt).TotalMilliseconds;
                if (elapsed < _cfg.TapGraceMs)
                {
                    // Very quick sequence with another key: resolve D/K as tap, unless Space chord is active
                    if (_states.TryGetValue(_cfg.VK_Space, out var sp) && sp.Down)
                    {
                        // Under SandS, don't emit D/K; let Shift chord take effect only
                        Logger.Info(key == _cfg.VK_D ? "D quick under Space -> suppress tap" : "K quick under Space -> suppress tap");
                        st.SentHold = true; // decision made, no output
                        // keep st.Down=true to suppress repeats until physical up
                    }
                    else
                    {
                        if (key == _cfg.VK_D) { Logger.Info("D quick -> tap"); InputSender.Tap(_cfg.VK_D); }
                        if (key == _cfg.VK_K) { Logger.Info("K quick -> tap"); InputSender.Tap(_cfg.VK_K); }
                        st.SentHold = true;
                        // keep st.Down=true to suppress repeats; physical up will be suppressed by SentHold check
                    }
                }
                else if (!st.HoldActive)
                {
                    if (key == _cfg.VK_D) { Logger.Info("D: chord -> hold LCtrl"); InputSender.KeyDown(_cfg.VK_LControl); }
                    if (key == _cfg.VK_K) { Logger.Info("K: chord -> hold RCtrl"); InputSender.KeyDown(_cfg.VK_RControl); }
                    st.HoldActive = true; st.SentHold = true;
                }
                _pendingCombos.Remove(key);
            }
        }
        return false;
    }

    public void Tick()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            // Flush pending combo taps after window elapsed
            var toSend = _pendingCombos.Where(kv => (now - kv.Value).TotalMilliseconds > _cfg.ComboWindowMs).Select(kv => kv.Key).ToList();
            foreach (var vk in toSend)
            {
                // Safety: never auto-tap Space from combo flush
                if (vk == _cfg.VK_Space) { _pendingCombos.Remove(vk); continue; }
                // If key still down, send a tap now (down+up). We won't mirror physical up later.
                Logger.Info($"Tick: flush pending tap VK=0x{vk:X}");
                InputSender.Tap(vk);
                _pendingCombos.Remove(vk);
                if (_states.TryGetValue(vk, out var st))
                {
                    st.SentHold = true;
                    st.Down = false;
                }
            }
        }
    }
}

