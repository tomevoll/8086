using Emulator80386.App.CPU;
using Emulator80386.App.GFX;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;
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
        public void TestCpuStosStringOperations()
        {
            var memory = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(memory, io);

            cpu.Reg.CS.Selector = 0x0000;
            cpu.Reg.CS.Base = 0x00000;

            cpu.Reg.AX = 0x1F41; // 'A' with White on Blue attribute
            cpu.Reg.ES.Selector = 0xB800;
            cpu.Reg.ES.Base = 0xB8000;
            cpu.Reg.DI = 0;
            cpu.Reg.CX = 10;

            // Opcode 0xF3 0xAB -> REP STOSW
            memory.Write8(0x1000, 0xF3);
            memory.Write8(0x1001, 0xAB);
            cpu.Reg.EIP = 0x1000;

            cpu.Step();

            Assert.Equal((byte)'A', memory.Read8(0xB8000));
            Assert.Equal(0x1F, memory.Read8(0xB8001));
            Assert.Equal((byte)'A', memory.Read8(0xB8012));
            Assert.Equal(0x1F, memory.Read8(0xB8013));
        }

        [Fact]
        public void TestGlDisplayWindowHeadlessModeAndRender()
        {
            using var window = new GlDisplayWindow("Test Window", 640, 400);
            bool continueRunning = window.PollEvents(onScancode: null);
            Assert.True(continueRunning);

            uint[] mockPixels = new uint[640 * 400];
            window.RenderFrameBuffer(mockPixels);
            window.SwapBuffers();
        }
    }
}
