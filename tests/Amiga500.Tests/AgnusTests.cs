using Amiga500.Chipset;
using Amiga500.Config;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class AgnusTests
{
    [Fact]
    public void Scheduler_ExecutesEventsInOrder()
    {
        var scheduler = new HardwareScheduler();
        int order = 0;
        int evt1Order = 0;
        int evt2Order = 0;

        scheduler.ScheduleEvent(20, () => evt2Order = ++order, "evt2");
        scheduler.ScheduleEvent(10, () => evt1Order = ++order, "evt1");

        scheduler.AdvanceCycles(30);

        Assert.Equal(1, evt1Order);
        Assert.Equal(2, evt2Order);
        Assert.Equal(30, scheduler.CurrentCycle);
    }

    [Fact]
    public void Agnus_RasterCounters_AdvancingStepsLineAndFrame()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        var agnus = new Agnus(bus, VideoStandard.PAL);

        // Step 228 color clocks (1 scanline)
        for (int i = 0; i < 228; i++) agnus.StepColorClock();

        Assert.Equal(1, agnus.VPOS);
        Assert.Equal(0, agnus.HPOS);

        // Step 311 more lines (completing PAL frame of 312 lines)
        for (int i = 0; i < 311 * 228; i++) agnus.StepColorClock();

        Assert.Equal(0, agnus.VPOS);
        Assert.Equal(0, agnus.HPOS);
        Assert.True((agnus.INTREQ & 0x0020) != 0); // VBLANK interrupt
    }

    [Fact]
    public void Copper_ExecutesMoveAndWait()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        var mockCustom = new MockCustomRegisters();
        bus.CustomRegisters = mockCustom;

        bus.OverlayEnabled = false;
        var agnus = new Agnus(bus);
        agnus.DMACON = 0x0280; // Master DMA + Copper DMA enable

        // Copper program in Chip RAM at $1000
        // Inst 1: MOVE $0180, $0F00 (COLOR00 = Red) -> $0180 $0F00
        // Inst 2: WAIT V=10, H=0 -> $0A01 $FF00
        // Inst 3: MOVE $0180, $00F0 (COLOR00 = Green) -> $0180 $00F0
        bus.WriteWord(0x1000, 0x0180);
        bus.WriteWord(0x1002, 0x0F00);
        bus.WriteWord(0x1004, 0x0A01);
        bus.WriteWord(0x1006, 0xFF00);
        bus.WriteWord(0x1008, 0x0180);
        bus.WriteWord(0x100A, 0x00F0);

        agnus.COP1LC = 0x1000;
        agnus.COPPC = 0x1000;

        agnus.StepCopper(); // Executes MOVE $0180, $0F00
        Assert.Equal(0x0F00, mockCustom.Registers[0x180 >> 1]);

        agnus.StepCopper(); // Executes WAIT V=10, H=0
        Assert.True(agnus.CopperWaiting);

        // Move raster to line 10
        agnus.VPOS = 10;
        agnus.HPOS = 0;

        agnus.StepCopper(); // Resumes from WAIT and executes MOVE $0180, $00F0
        Assert.False(agnus.CopperWaiting);
        Assert.Equal(0x00F0, mockCustom.Registers[0x180 >> 1]);
    }

    [Fact]
    public void Blitter_PerformsCopyOperation()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        bus.OverlayEnabled = false;
        var agnus = new Agnus(bus);
        agnus.DMACON = 0x0240; // Master DMA + Blitter DMA enable

        // Set source data at $2000
        bus.WriteWord(0x2000, 0x1234);
        bus.WriteWord(0x2002, 0x5678);

        agnus.BLTCON0 = 0x09F0; // Use A, D, Minterm $F0 (D = A)
        agnus.BLTCON1 = 0x0000;
        agnus.BLTAPTH = 0x2000;
        agnus.BLTDPTH = 0x3000;
        agnus.BLTAFWM = 0xFFFF;
        agnus.BLTALWM = 0xFFFF;

        // Trigger Blitter: Height = 1 line, Width = 2 words (BLTSIZE = (1 << 6) | 2 = 0x0042)
        agnus.TriggerBlitter(0x0042);

        Assert.Equal(0x1234, bus.ReadWord(0x3000));
        Assert.Equal(0x5678, bus.ReadWord(0x3002));
        Assert.True((agnus.INTREQ & 0x0040) != 0); // Blitter interrupt
    }
}
