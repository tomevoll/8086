using Amiga500.Config;
using Amiga500.IO;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class MockCustomRegisters : ICustomRegisterDevice
{
    public ushort[] Registers = new ushort[256];

    public ushort ReadRegister(uint regOffset) => Registers[(regOffset & 0x1FE) >> 1];

    public void WriteRegister(uint regOffset, ushort value)
    {
        Registers[(regOffset & 0x1FE) >> 1] = value;
    }
}

public class MemoryTests
{
    [Fact]
    public void MemoryBus_OverlayEnabled_ReadsRomAtZero()
    {
        var config = new EmulatorConfig { ChipRamKB = 512 };
        byte[] rom = new byte[256 * 1024];
        rom[0] = 0x12;
        rom[1] = 0x34;
        rom[2] = 0x56;
        rom[3] = 0x78;

        var bus = new AmigaMemoryBus(config, rom);
        Assert.True(bus.OverlayEnabled);

        Assert.Equal(0x12345678u, bus.ReadLong(0));

        // Disable Overlay
        bus.OverlayEnabled = false;
        bus.WriteLong(0, 0xAABBCCDD);

        Assert.Equal(0xAABBCCDDu, bus.ReadLong(0));
    }

    [Fact]
    public void Kickstart13_DisablesOverlay_WhenWritingCiaAPra()
    {
        var config = new EmulatorConfig { ChipRamKB = 512 };
        byte[] rom = new byte[256 * 1024];
        rom[0] = 0x00; rom[1] = 0x08; rom[2] = 0x00; rom[3] = 0x00;
        var bus = new AmigaMemoryBus(config, rom);
        var ciaA = new Cia8520("CIA-A");
        bus.CiaA = ciaA;
        ciaA.OnPortAChanged = (pra) => bus.UpdateOverlayFromCiaAPortA(pra);

        Assert.True(bus.OverlayEnabled);

        // Kickstart sets DDRA bit 0 to output
        ciaA.DDRA = 0x01;

        // Kickstart writes 0x01 to $BFE001 (CIA-A PRA) to turn off overlay (OVL=1 turns off overlay)
        bus.WriteByte(0xBFE001, 0x01);

        Assert.False(bus.OverlayEnabled);

        // Write test pattern to $000000
        bus.WriteWord(0x000000, 0x5555);
        Assert.Equal(0x5555, bus.ReadWord(0x000000));
    }

    [Fact]
    public void MemoryBus_Kickstart256K_MirrorsIn512KRange()
    {
        var config = new EmulatorConfig();
        byte[] rom = new byte[256 * 1024];
        rom[0] = 0xAA;
        rom[256 * 1024 - 1] = 0xBB;

        var bus = new AmigaMemoryBus(config, rom);

        // $F80000 (first mirror)
        Assert.Equal(0xAA, bus.ReadByte(0xF80000));
        // $FC0000 (second mirror)
        Assert.Equal(0xAA, bus.ReadByte(0xFC0000));
    }

    [Fact]
    public void MemoryBus_CustomRegisters_ReadAndWriteWord()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        var mockCustom = new MockCustomRegisters();
        bus.CustomRegisters = mockCustom;

        bus.WriteWord(0xDFF180, 0x0F00); // COLOR00 = Red
        Assert.Equal(0x0F00, bus.ReadWord(0xDFF180));
        Assert.Equal(0x0F00, mockCustom.Registers[(0x180 & 0x1FE) >> 1]);
    }
}
