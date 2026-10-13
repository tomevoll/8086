using System;
using Amiga500.Config;
using Amiga500.Memory;

namespace Amiga500.Chipset;

public class Agnus
{
    public ushort VPOS { get; set; } = 0;  // Vertical line counter
    public ushort HPOS { get; set; } = 0;  // Horizontal clock counter (0 - 227)
    public VideoStandard VideoStandard { get; set; } = VideoStandard.PAL;

    public ushort DMACON { get; set; } = 0; // DMA Control
    public ushort INTREQ { get; set; } = 0; // Interrupt Request

    // Copper Registers
    public uint COP1LC { get; set; }
    public uint COP2LC { get; set; }
    public uint COPPC { get; set; }
    public bool CopperWaiting { get; set; }
    public ushort CopperWaitV { get; set; }
    public ushort CopperWaitH { get; set; }
    public ushort CopperWaitMaskV { get; set; }
    public ushort CopperWaitMaskH { get; set; }

    // Blitter Registers
    public ushort BLTCON0 { get; set; }
    public ushort BLTCON1 { get; set; }
    public ushort BLTAFWM { get; set; } = 0xFFFF;
    public ushort BLTALWM { get; set; } = 0xFFFF;
    public uint BLTAPTH { get; set; }
    public uint BLTBPTH { get; set; }
    public uint BLTCPTH { get; set; }
    public uint BLTDPTH { get; set; }
    public short BLTAMOD { get; set; }
    public short BLTBMOD { get; set; }
    public short BLTCMOD { get; set; }
    public short BLTDMOD { get; set; }
    public ushort BLTCDAT { get; set; }
    public ushort BLTBDAT { get; set; }
    public ushort BLTADAT { get; set; }
    public bool BlitterBusy { get; set; }

    public ushort MaxLines => VideoStandard == VideoStandard.PAL ? (ushort)312 : (ushort)262;

    public AmigaMemoryBus Memory { get; set; }

    public Agnus(AmigaMemoryBus memory, VideoStandard videoStandard = VideoStandard.PAL)
    {
        Memory = memory;
        VideoStandard = videoStandard;
    }

    public void StepColorClock()
    {
        HPOS++;
        if (HPOS >= 228)
        {
            HPOS = 0;
            VPOS++;
            if (VPOS >= MaxLines)
            {
                VPOS = 0;
                // Vertical Blank Interrupt (INTREQ bit 5)
                INTREQ |= 0x0020;
                // Reset Copper to COP1LC on VBLANK
                if ((DMACON & 0x0280) == 0x0280) // Master DMA + Copper DMA enabled
                {
                    COPPC = COP1LC;
                    CopperWaiting = false;
                }
            }
        }

        // Check Copper progress
        if ((DMACON & 0x0280) == 0x0280) // Master DMA + Copper DMA enabled
        {
            StepCopper();
        }
    }

    public void StepCopper()
    {
        if (CopperWaiting)
        {
            ushort currentV = (ushort)(VPOS & CopperWaitMaskV);
            ushort currentH = (ushort)(HPOS & CopperWaitMaskH);

            if (currentV > CopperWaitV || (currentV == CopperWaitV && currentH >= CopperWaitH))
            {
                CopperWaiting = false;
            }
            else
            {
                return;
            }
        }

        // Fetch Copper Instruction (2 words: Inst1, Inst2)
        ushort inst1 = Memory.ReadWord(COPPC);
        ushort inst2 = Memory.ReadWord(COPPC + 2);
        COPPC += 4;

        if ((inst1 & 1) == 0) // MOVE Instruction: Inst1 = RegOffset, Inst2 = Value
        {
            uint regOffset = (uint)(inst1 & 0x01FE);
            // Copper write restriction: cannot write to registers below $080 if CDANG bit in COPCON is not set
            if (regOffset >= 0x080)
            {
                Memory.WriteWord(0xDFF000 + regOffset, inst2);
            }
        }
        else // WAIT or SKIP Instruction
        {
            bool isSkip = (inst2 & 1) != 0;
            ushort targetV = (ushort)((inst1 >> 8) & 0xFF);
            ushort targetH = (ushort)(inst1 & 0xFE);
            ushort maskV = (ushort)(((inst2 >> 8) & 0x7F) | 0x80);
            ushort maskH = (ushort)(inst2 & 0xFE);

            if (isSkip)
            {
                ushort curV = (ushort)(VPOS & maskV);
                ushort curH = (ushort)(HPOS & maskH);
                if (curV > targetV || (curV == targetV && curH >= targetH))
                {
                    COPPC += 4; // Skip next Copper instruction
                }
            }
            else
            { // WAIT
                CopperWaitV = targetV;
                CopperWaitH = targetH;
                CopperWaitMaskV = maskV;
                CopperWaitMaskH = maskH;
                CopperWaiting = true;
            }
        }
    }

    public void TriggerBlitter(ushort bltsize)
    {
        if ((DMACON & 0x0240) != 0x0240) return; // Master DMA + Blitter DMA disabled

        BlitterBusy = true;
        int height = (bltsize >> 6) & 0x03FF;
        if (height == 0) height = 1024;
        int widthWords = bltsize & 0x003F;
        if (widthWords == 0) widthWords = 64;

        bool useA = (BLTCON0 & 0x0800) != 0;
        bool useB = (BLTCON0 & 0x0400) != 0;
        bool useC = (BLTCON0 & 0x0200) != 0;
        bool useD = (BLTCON0 & 0x0100) != 0;
        byte minterm = (byte)(BLTCON0 & 0xFF);
        int shiftA = (BLTCON0 >> 12) & 0x0F;
        int shiftB = (BLTCON1 >> 12) & 0x0F;
        bool fillMode = (BLTCON1 & 0x0018) != 0;
        bool desc = (BLTCON1 & 0x0002) != 0; // Descending direction

        int step = desc ? -2 : 2;

        for (int h = 0; h < height; h++)
        {
            for (int w = 0; w < widthWords; w++)
            {
                ushort aVal = useA ? Memory.ReadWord(BLTAPTH) : BLTADAT;
                ushort bVal = useB ? Memory.ReadWord(BLTBPTH) : BLTBDAT;
                ushort cVal = useC ? Memory.ReadWord(BLTCPTH) : BLTCDAT;

                // First and last word masking on channel A
                if (w == 0) aVal &= BLTAFWM;
                if (w == widthWords - 1) aVal &= BLTALWM;

                // Shifts
                if (shiftA > 0) aVal = (ushort)(aVal >> shiftA);
                if (shiftB > 0) bVal = (ushort)(bVal >> shiftB);

                // Minterm logic calculation
                ushort dVal = EvaluateMinterm(minterm, aVal, bVal, cVal);

                if (useD)
                {
                    Memory.WriteWord(BLTDPTH, dVal);
                    BLTDPTH = (uint)(BLTDPTH + step);
                }

                if (useA) BLTAPTH = (uint)(BLTAPTH + step);
                if (useB) BLTBPTH = (uint)(BLTBPTH + step);
                if (useC) BLTCPTH = (uint)(BLTCPTH + step);
            }

            // Apply modulo at end of scanline
            if (useA) BLTAPTH = (uint)(BLTAPTH + (desc ? -BLTAMOD : BLTAMOD));
            if (useB) BLTBPTH = (uint)(BLTBPTH + (desc ? -BLTBMOD : BLTBMOD));
            if (useC) BLTCPTH = (uint)(BLTCPTH + (desc ? -BLTCMOD : BLTCMOD));
            if (useD) BLTDPTH = (uint)(BLTDPTH + (desc ? -BLTDMOD : BLTDMOD));
        }

        BlitterBusy = false;
        // Signal Blitter Interrupt (INTREQ bit 6)
        INTREQ |= 0x0040;
    }

    private static ushort EvaluateMinterm(byte minterm, ushort a, ushort b, ushort c)
    {
        ushort result = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((minterm & (1 << bit)) != 0)
            {
                ushort term = 0xFFFF;
                term &= ((bit & 4) != 0) ? a : (ushort)~a;
                term &= ((bit & 2) != 0) ? b : (ushort)~b;
                term &= ((bit & 1) != 0) ? c : (ushort)~c;
                result |= term;
            }
        }
        return result;
    }
}
