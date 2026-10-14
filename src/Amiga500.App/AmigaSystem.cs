using System;
using System.IO;
using Amiga500.Audio;
using Amiga500.Chipset;
using Amiga500.Config;
using Amiga500.CPU;
using Amiga500.Disk;
using Amiga500.GFX;
using Amiga500.IO;
using Amiga500.Memory;

namespace Amiga500;

public class AmigaSystem : ICustomRegisterDevice
{
    public EmulatorConfig Config { get; }
    public AmigaMemoryBus Memory { get; }
    public Cpu68000 Cpu { get; }
    public HardwareScheduler Scheduler { get; }
    public Agnus Agnus { get; }
    public Denise Denise { get; }
    public PaulaAudio Paula { get; }
    public Cia8520 CiaA { get; }
    public Cia8520 CiaB { get; }
    public InterruptController Interrupts { get; }
    public FloppyController Floppy { get; }

    public bool Booted { get; private set; }

    public AmigaSystem(EmulatorConfig config, byte[] romData)
    {
        Config = config;
        Memory = new AmigaMemoryBus(config, romData);
        Cpu = new Cpu68000(Memory);
        Scheduler = new HardwareScheduler();

        Agnus = new Agnus(Memory, config.VideoStandard);
        Denise = new Denise(Memory) { Agnus = Agnus };
        Paula = new PaulaAudio(Memory);

        CiaA = new Cia8520("CIA-A");
        CiaB = new Cia8520("CIA-B");

        Interrupts = new InterruptController();
        Floppy = new FloppyController(Memory);

        Memory.CiaA = CiaA;
        Memory.CiaB = CiaB;
        Memory.CustomRegisters = this;

        CiaA.OnPortAChanged = (pra) => Memory.UpdateOverlayFromCiaAPortA(pra);
        CiaB.OnPortBChanged = (prb) => Floppy.UpdateControlSignals(prb, CiaA.PRA);

        // Supply CIA-A PRA external hardware inputs (Floppy status signals)
        CiaA.GetExternalPraInputs = () =>
        {
            byte inputs = 0xFF;
            var drive = Floppy.ActiveDrive;
            if (drive != null)
            {
                // Bit 2: /DKRDY (0 = ready/inserted)
                if (drive.Disk != null) inputs = (byte)(inputs & ~0x04);
                // Bit 3: /TK0 (0 = head at track 0)
                if (drive.CurrentCylinder == 0) inputs = (byte)(inputs & ~0x08);
                // Bit 4: /WPROT (0 = write-protected)
                if (drive.Disk != null && drive.Disk.WriteProtected) inputs = (byte)(inputs & ~0x10);
                // Bit 5: /CHANGE (0 = disk change latch set)
                if (drive.DiskChanged) inputs = (byte)(inputs & ~0x20);
            }
            return inputs;
        };

        // Load configured ADF files into drives
        for (int i = 0; i < 4; i++)
        {
            if (!string.IsNullOrEmpty(config.FloppyPaths[i]) && File.Exists(config.FloppyPaths[i]))
            {
                var adf = AdfDiskImage.LoadFromFile(config.FloppyPaths[i]!);
                if (adf != null) Floppy.Drives[i].InsertDisk(adf);
            }
        }

        Reset();
    }

    public void Reset()
    {
        Memory.OverlayEnabled = true;
        Scheduler.Reset();
        Cpu.Reset();
        Booted = true;
    }

    public ushort ReadRegister(uint regOffset)
    {
        return regOffset switch
        {
            0x002 => Agnus.DMACON,
            0x004 => (ushort)((Agnus.VPOS << 8) | ((Agnus.VPOS >> 8) & 1)),
            0x006 => (ushort)(Agnus.HPOS & 0xFF),
            0x01A => 0x0000, // DSKBYTR
            0x01C => Interrupts.INTENA,
            0x01E => Interrupts.INTREQ,

            // Blitter Registers
            0x040 => Agnus.BLTCON0,
            0x042 => Agnus.BLTCON1,
            0x044 => Agnus.BLTAFWM,
            0x046 => Agnus.BLTALWM,
            0x048 => (ushort)(Agnus.BLTCPTH >> 16),
            0x04A => (ushort)(Agnus.BLTCPTH & 0xFFFF),
            0x04C => (ushort)(Agnus.BLTBPTH >> 16),
            0x04E => (ushort)(Agnus.BLTBPTH & 0xFFFF),
            0x050 => (ushort)(Agnus.BLTAPTH >> 16),
            0x052 => (ushort)(Agnus.BLTAPTH & 0xFFFF),
            0x054 => (ushort)(Agnus.BLTDPTH >> 16),
            0x056 => (ushort)(Agnus.BLTDPTH & 0xFFFF),
            0x060 => (ushort)Agnus.BLTCMOD,
            0x062 => (ushort)Agnus.BLTBMOD,
            0x064 => (ushort)Agnus.BLTAMOD,
            0x066 => (ushort)Agnus.BLTDMOD,
            0x070 => Agnus.BLTCDAT,
            0x072 => Agnus.BLTBDAT,
            0x074 => Agnus.BLTADAT,

            >= 0x180 and <= 0x1BE => Denise.Color[(regOffset - 0x180) >> 1],
            _ => 0xFFFF
        };
    }

    public void WriteRegister(uint regOffset, ushort value)
    {
        switch (regOffset)
        {
            case 0x020: Floppy.DSKPTH = (Floppy.DSKPTH & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x022: Floppy.DSKPTH = (Floppy.DSKPTH & 0xFFFF0000) | value; break;
            case 0x024:
                Floppy.DSKLEN = value;
                Floppy.TriggerDiskDma();
                break;
            case 0x07E: Floppy.DSKSYNC = value; break;

            // Blitter Registers
            case 0x040: Agnus.BLTCON0 = value; break;
            case 0x042: Agnus.BLTCON1 = value; break;
            case 0x044: Agnus.BLTAFWM = value; break;
            case 0x046: Agnus.BLTALWM = value; break;
            case 0x048: Agnus.BLTCPTH = (Agnus.BLTCPTH & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x04A: Agnus.BLTCPTH = (Agnus.BLTCPTH & 0xFFFF0000) | value; break;
            case 0x04C: Agnus.BLTBPTH = (Agnus.BLTBPTH & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x04E: Agnus.BLTBPTH = (Agnus.BLTBPTH & 0xFFFF0000) | value; break;
            case 0x050: Agnus.BLTAPTH = (Agnus.BLTAPTH & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x052: Agnus.BLTAPTH = (Agnus.BLTAPTH & 0xFFFF0000) | value; break;
            case 0x054: Agnus.BLTDPTH = (Agnus.BLTDPTH & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x056: Agnus.BLTDPTH = (Agnus.BLTDPTH & 0xFFFF0000) | value; break;
            case 0x058: Agnus.TriggerBlitter(value); break;
            case 0x060: Agnus.BLTCMOD = (short)value; break;
            case 0x062: Agnus.BLTBMOD = (short)value; break;
            case 0x064: Agnus.BLTAMOD = (short)value; break;
            case 0x066: Agnus.BLTDMOD = (short)value; break;
            case 0x070: Agnus.BLTCDAT = value; break;
            case 0x072: Agnus.BLTBDAT = value; break;
            case 0x074: Agnus.BLTADAT = value; break;

            case 0x080: Agnus.COP1LC = (Agnus.COP1LC & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x082: Agnus.COP1LC = (Agnus.COP1LC & 0xFFFF0000) | value; break;
            case 0x084: Agnus.COP2LC = (Agnus.COP2LC & 0x0000FFFF) | ((uint)value << 16); break;
            case 0x086: Agnus.COP2LC = (Agnus.COP2LC & 0xFFFF0000) | value; break;
            case 0x088: Agnus.COPPC = Agnus.COP1LC; Agnus.CopperWaiting = false; break;
            case 0x08A: Agnus.COPPC = Agnus.COP2LC; Agnus.CopperWaiting = false; break;

            case 0x08E: Denise.DIWSTRT = value; break;
            case 0x090: Denise.DIWSTOP = value; break;
            case 0x092: Denise.DDFSTRT = value; break;
            case 0x094: Denise.DDFSTOP = value; break;

            case 0x096:
                bool setDma = (value & 0x8000) != 0;
                ushort maskDma = (ushort)(value & 0x7FFF);
                if (setDma) Agnus.DMACON |= maskDma;
                else Agnus.DMACON &= (ushort)~maskDma;
                Paula.DMACON = Agnus.DMACON;
                break;

            case 0x09A: Interrupts.WriteIntEna(value); break;
            case 0x09C: Interrupts.WriteIntReq(value); break;

            case 0x100: Denise.BPLCON0 = value; break;
            case 0x102: Denise.BPLCON1 = value; break;
            case 0x104: Denise.BPLCON2 = value; break;
            case 0x108: Denise.BPL1MOD = value; break;
            case 0x10A: Denise.BPL2MOD = value; break;

            case 0x0E0: Denise.OnBplPointerWritten(0, (Denise.BPLPT[0] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0E2: Denise.OnBplPointerWritten(0, (Denise.BPLPT[0] & 0xFFFF0000) | value); break;
            case 0x0E4: Denise.OnBplPointerWritten(1, (Denise.BPLPT[1] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0E6: Denise.OnBplPointerWritten(1, (Denise.BPLPT[1] & 0xFFFF0000) | value); break;
            case 0x0E8: Denise.OnBplPointerWritten(2, (Denise.BPLPT[2] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0EA: Denise.OnBplPointerWritten(2, (Denise.BPLPT[2] & 0xFFFF0000) | value); break;
            case 0x0EC: Denise.OnBplPointerWritten(3, (Denise.BPLPT[3] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0EE: Denise.OnBplPointerWritten(3, (Denise.BPLPT[3] & 0xFFFF0000) | value); break;
            case 0x0F0: Denise.OnBplPointerWritten(4, (Denise.BPLPT[4] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0F2: Denise.OnBplPointerWritten(4, (Denise.BPLPT[4] & 0xFFFF0000) | value); break;
            case 0x0F4: Denise.OnBplPointerWritten(5, (Denise.BPLPT[5] & 0x0000FFFF) | ((uint)value << 16)); break;
            case 0x0F6: Denise.OnBplPointerWritten(5, (Denise.BPLPT[5] & 0xFFFF0000) | value); break;

            // Sprite Pointers 0-7 ($120 - $13E)
            case >= 0x120 and <= 0x13E:
                int sIdx = (int)((regOffset - 0x120) >> 2);
                if ((regOffset & 2) == 0) Denise.Sprites[sIdx].Pointer = (Denise.Sprites[sIdx].Pointer & 0x0000FFFF) | ((uint)value << 16);
                else Denise.Sprites[sIdx].Pointer = (Denise.Sprites[sIdx].Pointer & 0xFFFF0000) | value;
                break;

            // Sprite Position / Control / Data ($140 - $17E)
            case >= 0x140 and <= 0x17E:
                int spNum = (int)((regOffset - 0x140) >> 3);
                int spReg = (int)((regOffset & 6) >> 1);
                if (spReg == 0) Denise.Sprites[spNum].POS = value;
                else if (spReg == 1) Denise.Sprites[spNum].CTL = value;
                else if (spReg == 2) Denise.Sprites[spNum].DATA = value;
                else if (spReg == 3) Denise.Sprites[spNum].DATB = value;
                break;

            case >= 0x180 and <= 0x1BE: Denise.Color[(regOffset - 0x180) >> 1] = value; break;
        }
    }

    public void StepFrame()
    {
        int totalLines = Agnus.MaxLines;

        for (int line = 0; line < totalLines; line++)
        {
            if (line == 0) Denise.OnVblank();

            for (int clock = 0; clock < 228; clock++)
            {
                Agnus.StepColorClock();
                CiaA.StepEClock(1);
                CiaB.StepEClock(1);

                Interrupts.INTREQ |= Agnus.INTREQ; Agnus.INTREQ = 0;
                Interrupts.INTREQ |= Paula.INTREQ; Paula.INTREQ = 0;
                Interrupts.INTREQ |= Floppy.INTREQ; Floppy.INTREQ = 0;

                Cpu.PendingInterruptLevel = Interrupts.GetPendingInterruptLevel(CiaA.InterruptSignaled, CiaB.InterruptSignaled);

                Cpu.ExecuteInstruction();
            }

            Denise.RenderScanline(line);
        }
    }
}
