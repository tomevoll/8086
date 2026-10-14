using System;

namespace Amiga500.Chipset;

public class InterruptController
{
    public ushort INTENA { get; set; } // Interrupt Enable Register
    public ushort INTREQ { get; set; } // Interrupt Request Register

    public bool MasterEnable => (INTENA & 0x4000) != 0;

    public void WriteIntEna(ushort value)
    {
        bool set = (value & 0x8000) != 0;
        ushort mask = (ushort)(value & 0x7FFF);
        if (set) INTENA |= mask;
        else INTENA &= (ushort)~mask;
    }

    public void WriteIntReq(ushort value)
    {
        bool set = (value & 0x8000) != 0;
        ushort mask = (ushort)(value & 0x7FFF);
        if (set) INTREQ |= mask;
        else INTREQ &= (ushort)~mask;
    }

    public int GetPendingInterruptLevel(bool ciaA_Irq = false, bool ciaB_Irq = false)
    {
        if (!MasterEnable) return 0;

        ushort activeReq = INTREQ;
        if (ciaA_Irq) activeReq |= 0x0008; // PORTS Level 2
        if (ciaB_Irq) activeReq |= 0x2000; // EXTER Level 6

        ushort active = (ushort)(INTENA & activeReq & 0x3FFF);
        if (active == 0) return 0;

        // Level 6: EXTER (bit 13), CLK (bit 12)
        if ((active & 0x3000) != 0) return 6;

        // Level 5: DSKSYN (bit 12), RBF (bit 11)
        if ((active & 0x1800) != 0) return 5;

        // Level 4: AUD3 (bit 10), AUD2 (bit 9), AUD1 (bit 8), AUD0 (bit 7)
        if ((active & 0x0780) != 0) return 4;

        // Level 3: BLIT (bit 6), VERTB (bit 5), COPER (bit 4)
        if ((active & 0x0070) != 0) return 3;

        // Level 2: PORTS (bit 3) - CIA-A
        if ((active & 0x0008) != 0) return 2;

        // Level 1: SOFT (bit 2), DSKBLK (bit 1), TBE (bit 0)
        if ((active & 0x0007) != 0) return 1;

        return 0;
    }
}
