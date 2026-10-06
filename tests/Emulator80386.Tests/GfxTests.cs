using Emulator80386.App.GFX;
using Xunit;

namespace Emulator80386.Tests
{
    public class GfxTests
    {
        [Fact]
        public void TestVgaRendererTextModeBuffer()
        {
            byte[] vram = new byte[128 * 1024];
            int textModeStart = 0x18000; // 0xB8000 - 0xA0000

            // Set char 'A' (0x41) with white on blue (0x1F) at top-left (0,0)
            vram[textModeStart] = (byte)'A';
            vram[textModeStart + 1] = 0x1F;

            var renderer = new VgaRenderer();
            renderer.RenderTextMode(vram);

            Assert.NotNull(renderer.PixelBuffer);
            Assert.Equal(640 * 400, renderer.PixelBuffer.Length);
            // Non-zero pixel generated in render output
            Assert.True(renderer.PixelBuffer[0] != 0);
        }

        [Fact]
        public void TestGlDisplayWindowHeadlessMode()
        {
            using var window = new GlDisplayWindow("Test Window", 640, 400);
            // Should gracefully handle environment without throwing unhandled exceptions
            bool continueRunning = window.PollEvents(onScancode: null);
            Assert.True(continueRunning);
        }
    }
}
