# EFT DMA Radar Patch-Day Maintenance Checklist

Follow this checklist whenever Escape From Tarkov (EFT) updates or when the radar starts behaving unexpectedly.

## 1. Initial Data Collection
- [ ] **Check Game Version**: Note the current EFT version from the launcher or game menu.
- [ ] **Run with Logging**: Start the radar with the `-logging` command-line argument.
- [ ] **Check Logs**: Look for `log-*.txt` in the application directory.
- [ ] **Diagnostics Report**: Run the radar with `-diag` (once implemented) to see the current resolver and offset health.

## 2. Offset & IL2CPP Validation
- [ ] **Force IL2CPP Update**: Use the "Force IL2CPP Update" button in the UI if offsets seem stale.
- [ ] **Check Offset Cache**: Verify `%AppData%/eft-dma-radar-public/il2cpp_offsets.json` was updated.
- [ ] **Validate Key Offsets**:
    - `ClientLocalGameWorld`
    - `MainPlayer`
    - `RegisteredPlayers`

## 3. GameWorld & Raid Testing
- [ ] **Enter Offline Raid**: Always test in an offline raid first to avoid detection risk and ensure basic resolution works.
- [ ] **Verify Player List**: Check if you and any bots show up on the radar.
- [ ] **Verify Loot**: Check if loot items are visible and correctly labeled.

## 4. Hotkey & Input Testing
- [ ] **Check Hotkey Status**: Verify hotkeys (e.g., toggle radar, toggle chams) are responding.
- [ ] **Input Provider**: If using DMA input, ensure the DMA card is initialized and accessible.

## 5. Reporting Issues
If something is broken, provide the following artifacts:
1. `log-*.txt`
2. `il2cpp_offsets.json` from AppData.
3. EFT version number.
4. Description of the failure (e.g., "GameWorld not found", "Players missing").
5. Windows version of both the Radar PC and Game PC.
