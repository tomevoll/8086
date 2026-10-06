using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class MemoryTests
    {
        [Fact]
        public void TestRamReadWrite()
        {
            var memory = new MemoryBus(16); // 16MB
            memory.Write8(0x1000, 0x42);
            Assert.Equal(0x42, memory.Read8(0x1000));

            memory.Write16(0x2000, 0x1234);
            Assert.Equal(0x1234, memory.Read16(0x2000));

            memory.Write32(0x3000, 0xDEADBEEF);
            Assert.Equal(0xDEADBEEF, memory.Read32(0x3000));
        }

        [Fact]
        public void TestVramMapping()
        {
            var memory = new MemoryBus(16);
            memory.Write8(0xB8000, (byte)'A');
            memory.Write8(0xB8001, 0x07);

            Assert.Equal((byte)'A', memory.Read8(0xB8000));
            Assert.Equal(0x07, memory.Read8(0xB8001));
            Assert.Equal((byte)'A', memory.Vram[0x18000]);
        }

        [Fact]
        public void TestBiosRomMappingAndResetVector()
        {
            var memory = new MemoryBus(16);
            byte[] mockBios = new byte[65536]; // 64KB
            mockBios[mockBios.Length - 16] = 0xEA; // JMP instruction at reset vector
            mockBios[mockBios.Length - 15] = 0x55;
            mockBios[mockBios.Length - 14] = 0xAA;

            memory.LoadBiosRom(mockBios);

            Assert.Equal(0xEA, memory.Read8(0xFFFF0));
            Assert.Equal(0x55, memory.Read8(0xFFFF1));

            Assert.Equal(0xEA, memory.Read8(0xFFFFFFF0));
            Assert.Equal(0x55, memory.Read8(0xFFFFFFF1));

            memory.Write8(0xFFFF0, 0x00);
            Assert.Equal(0xEA, memory.Read8(0xFFFF0));
        }

        [Fact]
        public void TestOptionRomMappingAtC0000AndC8000()
        {
            var memory = new MemoryBus(16);
            byte[] vgaRom = BiosLoader.LoadOrGenerateVgaOptionRom(vgaRomPath: "");
            byte[] ideRom = BiosLoader.LoadOrGenerateIdeOptionRom(ideRomPath: "");

            memory.LoadVgaOptionRom(vgaRom);
            memory.LoadIdeOptionRom(ideRom);

            // VGA Option ROM at 0xC0000 (0x55 0xAA magic header)
            Assert.Equal(0x55, memory.Read8(0xC0000));
            Assert.Equal(0xAA, memory.Read8(0xC0001));

            // IDE Option ROM at 0xC8000 (0x55 0xAA magic header)
            Assert.Equal(0x55, memory.Read8(0xC8000));
            Assert.Equal(0xAA, memory.Read8(0xC8001));
        }
    }
}
