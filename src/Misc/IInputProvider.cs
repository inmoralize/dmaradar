#nullable enable
using System;

namespace eft_dma_radar.Common.Misc
{
    public interface IInputProvider
    {
        bool IsReady { get; }
        void Update();
        bool IsKeyDown(int vkeyCode);
    }
}
