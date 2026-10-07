using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class PostAndVgaTests
    {
        [Fact]
        public void TestVgaControllerPortsAndInputStatus1()
        {
            var vga = new VgaController();

            vga.Write8(0x3D4, 0x0E); // Cursor location high index
            vga.Write8(0x3D5, 0x07); // Data

            Assert.Equal(0x0E, vga.Read8(0x3D4));
            Assert.Equal(0x07, vga.Read8(0x3D5));

            // Test Input Status 1 vertical retrace toggle (port 0x3DA)
            byte status1 = vga.Read8(0x3DA);
            byte status2 = vga.Read8(0x3DA);

            Assert.NotEqual(status1, status2);
        }

        [Fact]
        public void TestPort61RefreshBitToggle()
        {
            var sys = new SystemControlPort();

            byte read1 = sys.Read8(0x61);
            byte read2 = sys.Read8(0x61);

            // Bit 4 (0x10) should toggle on consecutive reads
            Assert.Equal(0x10, (read1 ^ read2) & 0x10);
        }

        [Fact]
        public void TestPostCodeAndDmaPorts()
        {
            var postDma = new PostAndDmaController();

            postDma.Write8(0x80, 0x15); // Write POST code 0x15
            Assert.Equal(0x15, postDma.Read8(0x80));

            postDma.Write8(0x81, 0x3F); // DMA page register
            Assert.Equal(0x3F, postDma.Read8(0x81));
        }

        [Fact]
        public void TestOptionRomChecksumValidity()
        {
            byte[] vgaRom = BiosLoader.LoadOrGenerateVgaOptionRom(vgaRomPath: "");

            Assert.Equal(0x55, vgaRom[0]);
            Assert.Equal(0xAA, vgaRom[1]);

            int len = vgaRom[2] * 512;
            byte sum = 0;
            for (int i = 0; i < len; i++)
            {
                sum += vgaRom[i];
            }

            // Sum modulo 256 MUST be 0 for BIOS POST to execute Option ROM
            Assert.Equal(0, sum);
        }

        [Fact]
        public void TestMotherboardFullPostInitialization()
        {
            var config = new EmulatorConfig { RamSizeMB = 16 };
            var mb = new Motherboard(config);

            mb.Boot();

            // Perform 2,000 CPU steps
            mb.Step(instructionsCount: 2000);

            // Verify VGA VRAM text was written
            byte char1 = mb.Memory.Read8(0xB8000);
            Assert.Equal((byte)'P', char1);
        }

        [Fact]
        public void TestQemuVgaBiosLoadingAndBda()
        {
            var config = new EmulatorConfig { RamSizeMB = 16, VgaRomPath = "roms/vgabios.bin" };
            var mb = new Motherboard(config);

            // Read Option ROM header at 0xC0000
            byte m1 = mb.Memory.Read8(0xC0000);
            byte m2 = mb.Memory.Read8(0xC0001);

            Assert.Equal(0x55, m1);
            Assert.Equal(0xAA, m2);

            // Verify BDA (BIOS Data Area) INT 10h vector at linear 0x00040
            ushort int10Ip = mb.Memory.Read16(0x00040);
            ushort int10Cs = mb.Memory.Read16(0x00042);

            Assert.Equal(0x000C, int10Ip);
            Assert.Equal(0xC000, int10Cs);
        }
    }
}
