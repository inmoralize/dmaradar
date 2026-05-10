#nullable enable
using System;
using eft_dma_radar.Misc;
using VmmSharpEx;

namespace eft_dma_radar.Common.Misc
{
    public sealed class DmaInputProvider : IInputProvider
    {
        private readonly DmaInputManager? _manager;
        private bool _isReady;

        public bool IsReady => _isReady;

        public DmaInputProvider(Vmm vmm)
        {
            try
            {
                _manager = new DmaInputManager(vmm);
                _isReady = true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DmaInputProvider] Initialization failed: {ex.Message}");
                _isReady = false;
            }
        }

        public void Update()
        {
            if (_isReady && _manager != null)
            {
                _manager.UpdateKeys();
            }
        }

        public bool IsKeyDown(int vkeyCode)
        {
            if (!_isReady || _manager == null)
                return false;

            return _manager.IsKeyDown((uint)vkeyCode);
        }
    }
}
