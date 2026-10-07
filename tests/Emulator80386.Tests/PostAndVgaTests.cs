using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.CPU;
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

            // Test Start Address Register (0x0C / 0x0D)
            vga.Write8(0x3D4, 0x0C); vga.Write8(0x3D5, 0x01);
            vga.Write8(0x3D4, 0x0D); vga.Write8(0x3D5, 0x40);

            Assert.Equal(0x01, vga.CrtcRegs[0x0C]);
            Assert.Equal(0x40, vga.CrtcRegs[0x0D]);

            // Test Input Status 1 vertical retrace toggle (port 0x3DA)
            byte status1 = vga.Read8(0x3DA);
            byte status2 = vga.Read8(0x3DA);

            Assert.NotEqual(status1, status2);
        }

        [Fact]
        public void TestVgaModeControl3D8AndAttributeFlipFlop()
        {
            var vga = new VgaController();

            // Test 0x3D8 Mode Control
            vga.Write8(0x3D8, 0x29); // Video Enable (bit 3) + 80x25 text (bit 0) + Blink (bit 5)
            Assert.True(vga.VideoEnabled);
            Assert.Equal(0x29, vga.Read8(0x3D8));

            vga.Write8(0x3D8, 0x01); // Video Disabled (bit 3 = 0)
            Assert.False(vga.VideoEnabled);

            // Test Attribute Controller 0x3C0 Flip-Flop
            vga.Read8(0x3DA); // Reset flip-flop to index expecting state
            Assert.False(vga.AttributeFlipFlop);

            vga.Write8(0x3C0, 0x30); // Write index 0x10 with PAS bit 5 set (0x20 | 0x10)
            Assert.True(vga.AttributeFlipFlop);
            Assert.True(vga.PaletteAddressSource);

            vga.Write8(0x3C0, 0x0C); // Write data 0x0C to Attribute Reg 0x10
            Assert.False(vga.AttributeFlipFlop);
            Assert.Equal(0x0C, vga.AttributeRegs[0x10]);
        }

        [Fact]
        public void TestMemoryBusPureRamSemantics()
        {
            var mem = new MemoryBus(16);

            // Read uninitialized lower RAM
            Assert.Equal(0x00, mem.Read8(0x00000));
            Assert.Equal(0x00, mem.Read8(0x01000));
            Assert.Equal(0x00, mem.Read8(0x07C00));

            // Write and read back from RAM
            mem.Write8(0x01000, 0x42);
            Assert.Equal(0x42, mem.Read8(0x01000));

            // Verify BiosRom at 0xF0000
            Assert.Equal(mem.BiosRom[0x10000], mem.Read8(0xF0000));
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
        public void TestRepStoswScreenClearAndCrtc()
        {
            var memory = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(memory, io);

            cpu.Reg.CS.Selector = 0x0000; cpu.Reg.CS.Base = 0x00000;
            cpu.Reg.AX = 0x0720; // Space character with light gray attribute
            cpu.Reg.ES.Selector = 0xB800; cpu.Reg.ES.Base = 0xB8000;
            cpu.Reg.DI = 0;
            cpu.Reg.CX = 2000; // 80x25 text screen cells

            // 0xF3 0xAB -> REP STOSW
            memory.Write8(0x1000, 0xF3);
            memory.Write8(0x1001, 0xAB);
            cpu.Reg.EIP = 0x1000;

            cpu.Step();

            Assert.Equal((byte)' ', memory.Read8(0xB8000));
            Assert.Equal(0x07, memory.Read8(0xB8001));
            Assert.Equal((byte)' ', memory.Read8(0xB8F9E));
            Assert.Equal(0x07, memory.Read8(0xB8F9F));
            Assert.Equal(0, (int)cpu.Reg.CX);
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
        public void TestQemuVgaBiosLoadingAndSignature()
        {
            var config = new EmulatorConfig { RamSizeMB = 16, VgaRomPath = "roms/vgabios.bin" };
            var mb = new Motherboard(config);

            // Read Option ROM header at 0xC0000
            byte m1 = mb.Memory.Read8(0xC0000);
            byte m2 = mb.Memory.Read8(0xC0001);

            Assert.Equal(0x55, m1);
            Assert.Equal(0xAA, m2);
        }
    }
}
