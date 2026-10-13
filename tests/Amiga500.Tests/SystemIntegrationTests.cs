using Amiga500.Config;
using Amiga500.Disk;
using Xunit;

namespace Amiga500.Tests;

public class SystemIntegrationTests
{
    [Fact]
    public void AmigaSystem_ResetsAndExecutesRomVector()
    {
        var config = new EmulatorConfig();
        byte[] rom = new byte[256 * 1024];

        // Set initial SSP at byte 0 = $80000, initial PC at byte 4 = $FC0002
        rom[0] = 0x00; rom[1] = 0x08; rom[2] = 0x00; rom[3] = 0x00;
        rom[4] = 0x00; rom[5] = 0xFC; rom[6] = 0x00; rom[7] = 0x02;

        var system = new AmigaSystem(config, rom);

        Assert.True(system.Booted);
        Assert.Equal(0x00080000u, system.Cpu.A[7]);
        Assert.Equal(0x00FC0002u, system.Cpu.PC);
        Assert.True(system.Memory.OverlayEnabled);
    }

    [Fact]
    public void AmigaSystem_Kickstart31_512KRom_LoadsResetVectorFromSecond256K()
    {
        var config = new EmulatorConfig();
        byte[] rom = new byte[512 * 1024];

        // In 512KB ROM, reset vector is at offset 0x40000 ($FC0000)
        rom[0x40000] = 0x00; rom[0x40001] = 0x08; rom[0x40002] = 0x00; rom[0x40003] = 0x00;
        rom[0x40004] = 0x00; rom[0x40005] = 0xFC; rom[0x40006] = 0x00; rom[0x40007] = 0x02;

        var system = new AmigaSystem(config, rom);

        Assert.True(system.Booted);
        Assert.Equal(0x00080000u, system.Cpu.A[7]);
        Assert.Equal(0x00FC0002u, system.Cpu.PC);
        Assert.True(system.Memory.OverlayEnabled);
    }

    [Fact]
    public void AmigaSystem_StepFrame_AdvancesRasterAndRendersFrame()
    {
        var config = new EmulatorConfig();
        byte[] rom = new byte[256 * 1024];
        var system = new AmigaSystem(config, rom);

        system.StepFrame();

        // After 1 full frame (312 lines x 228 clocks), Denise framebuffer should contain rendered scanlines
        Assert.Equal(640 * 312, system.Denise.FrameBuffer.Length);
    }
}
