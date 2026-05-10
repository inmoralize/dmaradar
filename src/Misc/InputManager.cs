#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Misc;
using eft_dma_radar.Tarkov;
using VmmSharpEx;

namespace eft_dma_radar.Common.Misc
{
    public static class InputManager
    {
        private static volatile bool _initialized = false;
        private static bool _safeMode = false;

        private static readonly byte[] _currentStateBitmap = new byte[64];
        private static readonly byte[] _previousStateBitmap = new byte[64];
        private static readonly HashSet<int> _pressedKeys = new();

        private static IInputProvider? _provider;

        private const int KEY_CHECK_DELAY = 100; // in milliseconds

        private static readonly Dictionary<int, DateTime> _lastKeyTapTime = new();
        private static readonly Dictionary<int, bool> _heldStates = new();
        private const int DoubleTapThresholdMs = 300;

        public static bool IsReady => _initialized && _provider != null && _provider.IsReady;

        public static event EventHandler? ReadyChanged;

        private static readonly Dictionary<int, List<KeyActionHandler>> _keyActionHandlers = new();
        private static readonly object _eventLock = new();
        private static int _nextActionId = 1;

        /// <summary>
        /// Attempts to load Input Manager.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (MemoryInterface.Memory?.VmmHandle == null)
                {
                    _safeMode = true;
                    Log.WriteLine("[InputManager] Starting in Safe Mode - Input functionality disabled");
                    NotificationsShared.Warning("[InputManager] Safe Mode - Input functionality disabled");
                    return;
                }

                var vmm = MemoryInterface.Memory.VmmHandle;
                _provider = new DmaInputProvider(vmm);

                if (_provider.IsReady)
                {
                    _initialized = true;
                    Log.WriteLine("[InputManager] Initialized with DMA provider.");
                    NotificationsShared.Success("[InputManager] Initialized successfully!");
                    ReadyChanged?.Invoke(null, EventArgs.Empty);

                    new Thread(Worker)
                    {
                        IsBackground = true,
                        Name = "InputManagerThread"
                    }.Start();
                }
                else
                {
                    _safeMode = true;
                    Log.WriteLine("ERROR Initializing Input Provider");
                    NotificationsShared.Error("[InputManager] Failed to initialize provider, hotkeys disabled.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[InputManager] Error during initialization: {ex.Message}");
                _safeMode = true;
                NotificationsShared.Warning("[InputManager] Initialization failed - Safe Mode active");
            }
        }

        public static void UpdateKeys()
        {
            if (!IsReady || _safeMode || _provider == null)
                return;

            Array.Copy(_currentStateBitmap, _previousStateBitmap, 64);

            _provider.Update();

            _pressedKeys.Clear();

            for (int vk = 0; vk < 256; ++vk)
            {
                var isDown = _provider.IsKeyDown(vk);

                // Maintain bitmap for transition detection (wasDown != isDown)
                int byteIdx = vk * 2 / 8;
                int bitMask = 1 << (vk % 4 * 2);
                
                if (isDown)
                {
                    _currentStateBitmap[byteIdx] |= (byte)bitMask;
                    _pressedKeys.Add(vk);
                }
                else
                {
                    _currentStateBitmap[byteIdx] &= (byte)~bitMask;
                }

                var wasDown = (_previousStateBitmap[byteIdx] & bitMask) != 0;

                if (wasDown != isDown)
                {
                    KeyActionHandler[] snapshot;
                    lock (_eventLock)
                    {
                        if (!_keyActionHandlers.TryGetValue(vk, out var handlers))
                            continue;
                        snapshot = handlers.ToArray();
                    }

                    foreach (var handler in snapshot)
                    {
                        try
                        {
                            handler.Handler?.Invoke(null, new KeyEventArgs(vk, isDown));
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLine($"Error executing key handler for action '{handler.ActionName}': {ex.Message}");
                        }
                    }
                }
            }
        }

        public static int RegisterKeyAction(int keyCode, string actionName, KeyStateChangedHandler handler)
        {
            if (!IsReady || _safeMode || handler == null || string.IsNullOrEmpty(actionName))
                return -1;

            lock (_eventLock)
            {
                if (!_keyActionHandlers.ContainsKey(keyCode))
                    _keyActionHandlers[keyCode] = new List<KeyActionHandler>();

                var existingAction = _keyActionHandlers[keyCode].FirstOrDefault(h => h.ActionName == actionName);
                if (existingAction != null)
                {
                    existingAction.Handler = handler;
                    return existingAction.ActionId;
                }

                var actionId = _nextActionId++;
                _keyActionHandlers[keyCode].Add(new KeyActionHandler
                {
                    ActionId = actionId,
                    ActionName = actionName,
                    Handler = handler
                });

                return actionId;
            }
        }

        public static bool UnregisterKeyAction(int keyCode, string actionName)
        {
            if (_safeMode) return false;
            lock (_eventLock)
            {
                if (_keyActionHandlers.TryGetValue(keyCode, out var handlers))
                {
                    var removed = handlers.RemoveAll(h => h.ActionName == actionName) > 0;
                    if (handlers.Count == 0) _keyActionHandlers.Remove(keyCode);
                    return removed;
                }
                return false;
            }
        }

        public static bool UnregisterKeyAction(int actionId)
        {
            if (_safeMode) return false;
            lock (_eventLock)
            {
                foreach (var kvp in _keyActionHandlers.ToList())
                {
                    var removed = kvp.Value.RemoveAll(h => h.ActionId == actionId) > 0;
                    if (kvp.Value.Count == 0) _keyActionHandlers.Remove(kvp.Key);
                    if (removed) return true;
                }
                return false;
            }
        }

        public static void ClearKeyActions(int keyCode)
        {
            if (_safeMode) return;
            lock (_eventLock) _keyActionHandlers.Remove(keyCode);
        }

        public static List<string> GetKeyActions(int keyCode)
        {
            if (_safeMode) return new List<string>();
            lock (_eventLock)
            {
                if (_keyActionHandlers.TryGetValue(keyCode, out var handlers))
                    return handlers.Select(h => h.ActionName).ToList();
                return new List<string>();
            }
        }

        public static Dictionary<int, List<string>> GetAllKeyActions()
        {
            if (_safeMode) return new Dictionary<int, List<string>>();
            lock (_eventLock)
            {
                var result = new Dictionary<int, List<string>>();
                foreach (var kvp in _keyActionHandlers)
                {
                    result[kvp.Key] = kvp.Value.Select(h => h.ActionName).ToList();
                }
                return result;
            }
        }

        public static bool IsKeyDown(int key) => IsReady && _pressedKeys.Contains(key);

        public static bool IsKeyPressed(int key)
        {
            if (!IsReady) return false;
            int byteIdx = key * 2 / 8;
            int bitMask = 1 << (key % 4 * 2);
            return _pressedKeys.Contains(key) && (_previousStateBitmap[byteIdx] & bitMask) == 0;
        }

        public static bool IsKeyHeldToggle(int key)
        {
            if (!IsReady) return false;
            if (!IsKeyPressed(key))
                return _heldStates.TryGetValue(key, out var held) && held;

            var now = DateTime.UtcNow;
            lock (_eventLock)
            {
                if (_lastKeyTapTime.TryGetValue(key, out var lastTap))
                {
                    var delta = (now - lastTap).TotalMilliseconds;
                    if (delta < DoubleTapThresholdMs)
                    {
                        _heldStates[key] = !_heldStates.GetValueOrDefault(key, false);
                        _lastKeyTapTime.Remove(key);
                    }
                    else _lastKeyTapTime[key] = now;
                }
                else _lastKeyTapTime[key] = now;
            }
            return _heldStates.TryGetValue(key, out var isHeld) && isHeld;
        }

        private static void Worker()
        {
            Log.WriteLine("[InputManager] Worker thread starting...");
            while (true)
            {
                try
                {
                    if (MemoryInterface.Memory is { IsDisposed: true }) break;
                    if (!_safeMode && MemDMABase.WaitForProcess())
                        UpdateKeys();
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log.WriteLine($"[InputManager] Worker thread error: {ex.Message}");
                }
                finally { Thread.Sleep(KEY_CHECK_DELAY); }
            }
            Log.WriteLine("[InputManager] Worker thread exiting.");
        }

        private class KeyActionHandler
        {
            public int ActionId { get; set; }
            public string ActionName { get; set; } = string.Empty;
            public KeyStateChangedHandler? Handler { get; set; }
        }

        public class KeyEventArgs : EventArgs
        {
            public int KeyCode { get; }
            public bool IsPressed { get; }
            public KeyEventArgs(int keyCode, bool isPressed)
            {
                KeyCode = keyCode;
                IsPressed = isPressed;
            }
        }

        public delegate void KeyStateChangedHandler(object? sender, KeyEventArgs e);
    }
}