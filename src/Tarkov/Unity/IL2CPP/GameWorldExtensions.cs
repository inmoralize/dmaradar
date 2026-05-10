#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    public static class GameWorldExtensions
    {
        private static ResolverDiagnostics _diag = new();

        private struct ResolverDiagnostics
        {
            public int InvalidPointers;
            public int ChainReadFailures;
            public int MapReadFailures;
            public int UnknownMaps;
            public int NonGameWorldObjects;
            public int ComponentCountExceeded;
            
            public void Reset()
            {
                InvalidPointers = 0;
                ChainReadFailures = 0;
                MapReadFailures = 0;
                UnknownMaps = 0;
                NonGameWorldObjects = 0;
                ComponentCountExceeded = 0;
            }

            public override string ToString() =>
                $"InvalidPtr: {InvalidPointers}, ChainFail: {ChainReadFailures}, MapFail: {MapReadFailures}, UnknownMap: {UnknownMaps}, NotGW: {NonGameWorldObjects}, CompLimit: {ComponentCountExceeded}";
        }

        // ────────────────────────────────────────────────────────────────────────────────
        // ENTRY POINT
        // ────────────────────────────────────────────────────────────────────────────────

        public static ulong GetGameWorld(
            ulong gomAddress,
            CancellationToken ct,
            out string? map)
        {
            map = null;
            _diag.Reset();

            Log.WriteLine("[IL2CPP] Resolving GameWorld...");

            // Phase 1: Component scan (previously called TypeIndex scan)
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryComponentScan(gomAddress, ct, out var result))
                {
                    map = result!.Map;
                    Log.WriteLine($"[IL2CPP] GameWorld found (ComponentScan) on Map: {map}");
                    return result.GameWorld;
                }

                Thread.Sleep(100);
            }

            // Phase 2: Name-based shallow scan (backup)
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (TryShallowScan(gomAddress, ct, out var result))
                {
                    map = result!.Map;
                    Log.WriteLine($"[IL2CPP] GameWorld found (ShallowScan) on Map: {map}");
                    return result.GameWorld;
                }

                Thread.Sleep(100);
            }

            // Phase 3: Full legacy scan
            try
            {
                var gw = FullScan(gomAddress, ct, out map);
                Log.WriteLine($"[IL2CPP] GameWorld found (FullScan) on Map: {map}");
                return gw;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[IL2CPP] GameWorld resolution FAILED. Diagnostics: {_diag}");
                throw new InvalidOperationException($"GameWorld not found. Diag: {_diag}", ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────
        // PHASE 1 — COMPONENT SCAN
        // ────────────────────────────────────────────────────────────────────────────────

        private static bool TryComponentScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult? result)
        {
            result = null;

            try
            {
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    ulong go = current.ThisObject;
                    if (!go.IsValidVirtualAddress())
                    {
                        _diag.InvalidPointers++;
                        break;
                    }

                    if (TryParseGameWorldByComponent(go) is GameWorldResult gw)
                    {
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"TryComponentScan failed: {ex.Message}", "GameWorld");
            }

            return false;
        }

        private static GameWorldResult? TryParseGameWorldByComponent(ulong gameObject)
        {
            try
            {
                ulong components = Memory.ReadValue<ulong>(
                    gameObject + UnityOffsets.GameObject.ComponentsOffset);

                if (!components.IsValidVirtualAddress())
                {
                    _diag.InvalidPointers++;
                    return null;
                }

                int count = Memory.ReadValue<int>(
                    components + UnityOffsets.Il2CppArray.Length);

                if (count <= 0 || count > 64)
                {
                    if (count > 64) _diag.ComponentCountExceeded++;
                    return null;
                }

                ulong data = components + UnityOffsets.Il2CppArray.Data;

                // GameWorld is typically the first or second component on the GameObject named "GameWorld"
                for (int i = 0; i < count; i++)
                {
                    ulong component = Memory.ReadValue<ulong>(
                        data + (ulong)(i * sizeof(ulong)));

                    if (!component.IsValidVirtualAddress())
                        continue;

                    ulong gameWorld = Memory.ReadPtrChain(
                        gameObject, UnityOffsets.GameWorldChain);

                    if (!gameWorld.IsValidVirtualAddress())
                    {
                        _diag.ChainReadFailures++;
                        continue;
                    }

                    if (TryResolveMap(gameWorld, out var map))
                    {
                        return new GameWorldResult
                        {
                            GameWorld = gameWorld,
                            Map = map
                        };
                    }
                }
            }
            catch { }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────────────
        // PHASE 2 — NAME SHALLOW SCAN
        // ────────────────────────────────────────────────────────────────────────────────

        private static bool TryShallowScan(
            ulong gomAddress,
            CancellationToken ct,
            out GameWorldResult? result)
        {
            result = null;

            try
            {
                var gom = GameObjectManager.Get(gomAddress);
                var current = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);

                for (int i = 0; i < 5000; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!current.ThisObject.IsValidVirtualAddress())
                    {
                        _diag.InvalidPointers++;
                        break;
                    }

                    if (ParseGameWorldByName(ref current) is GameWorldResult gw)
                    {
                        result = gw;
                        return true;
                    }

                    current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
                }
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"TryShallowScan failed: {ex.Message}", "GameWorld");
            }

            return false;
        }

        // ────────────────────────────────────────────────────────────────────────────────
        // PHASE 3 — FULL LEGACY SCAN
        // ────────────────────────────────────────────────────────────────────────────────

        private static ulong FullScan(
            ulong gomAddress,
            CancellationToken ct,
            out string? map)
        {
            map = null;

            var gom = GameObjectManager.Get(gomAddress);
            var first = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
            var last = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);

            var current = first;
            int depth = 0;

            while (current.ThisObject.IsValidVirtualAddress() && depth++ < 100_000)
            {
                ct.ThrowIfCancellationRequested();

                if (ParseGameWorldByName(ref current) is GameWorldResult result)
                {
                    map = result.Map;
                    return result.GameWorld;
                }

                if (current.ThisObject == last.ThisObject)
                    break;

                current = Memory.ReadValue<LinkedListObject>(current.NextObjectLink);
            }

            throw new InvalidOperationException("GameWorld not found after full scan.");
        }

        private static GameWorldResult? ParseGameWorldByName(ref LinkedListObject node)
        {
            try
            {
                var go = node.ThisObject;
                if (!go.IsValidVirtualAddress())
                    return null;

                var namePtr = Memory.ReadValue<ulong>(
                    go + UnityOffsets.GameObject.NameOffset);

                if (!namePtr.IsValidVirtualAddress())
                {
                    _diag.InvalidPointers++;
                    return null;
                }

                var name = Memory.ReadString(namePtr, 64, useCache: false);
                if (!name.Equals("GameWorld", StringComparison.OrdinalIgnoreCase))
                {
                    _diag.NonGameWorldObjects++;
                    return null;
                }

                var gameWorld = Memory.ReadPtrChain(go, UnityOffsets.GameWorldChain);
                if (!gameWorld.IsValidVirtualAddress())
                {
                    _diag.ChainReadFailures++;
                    return null;
                }

                if (TryResolveMap(gameWorld, out var map))
                {
                    return new GameWorldResult
                    {
                        GameWorld = gameWorld,
                        Map = map
                    };
                }
            }
            catch { }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────────────
        // SHARED HELPERS
        // ────────────────────────────────────────────────────────────────────────────────

        private static bool TryResolveMap(ulong gameWorld, out string? map)
        {
            map = null;

            try
            {
                ulong mapPtr = Memory.ReadValue<ulong>(
                    gameWorld + Offsets.ClientLocalGameWorld.LocationId);

                if (!mapPtr.IsValidVirtualAddress())
                {
                    ulong lp = Memory.ReadValue<ulong>(
                        gameWorld + Offsets.ClientLocalGameWorld.MainPlayer);

                    if (!lp.IsValidVirtualAddress())
                    {
                        _diag.MapReadFailures++;
                        return false;
                    }

                    mapPtr = Memory.ReadValue<ulong>(
                        lp + Offsets.Player.Location);
                }

                if (!mapPtr.IsValidVirtualAddress())
                {
                    _diag.MapReadFailures++;
                    return false;
                }

                var mapName = Memory.ReadUnityString(mapPtr, 128);
                if (string.IsNullOrEmpty(mapName))
                {
                    _diag.MapReadFailures++;
                    return false;
                }

                if (!GameData.MapNames.ContainsKey(mapName))
                {
                    Log.WriteOnce(AppLogLevel.Warning, $"UnknownMap_{mapName}", $"Unknown map name detected: '{mapName}'", "GameWorld");
                    _diag.UnknownMaps++;
                    // return false; // We don't return false here anymore, let it resolve so we can see the data
                }

                map = mapName;
                return true;
            }
            catch
            {
                _diag.MapReadFailures++;
                return false;
            }
        }

        private sealed class GameWorldResult
        {
            public ulong GameWorld { get; init; }
            public string? Map { get; init; }
        }
    }
}