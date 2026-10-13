using Amiga500.Chipset;
using Amiga500.IO;
using Xunit;

namespace Amiga500.Tests;

public class CiaAndInterruptTests
{
    [Fact]
    public void Cia_TimerA_CountsDownAndSignalsInterrupt()
    {
        var ciaA = new Cia8520("CIA-A");
        ciaA.LatchA = 5;
        ciaA.TimerA = 5;
        ciaA.ICRMask = 0x01; // Enable Timer A interrupt
        ciaA.CRA = 0x01;    // Start Timer A

        ciaA.StepEClock(6);

        Assert.True(ciaA.InterruptSignaled);
        Assert.True((ciaA.ICRData & 0x80) != 0);

        // Reading ICR clears interrupt
        byte icr = ciaA.ReadRegister(0x0D00);
        Assert.True((icr & 0x80) != 0);
        Assert.False(ciaA.InterruptSignaled);
    }

    [Fact]
    public void Cia_OverlayToggle_TriggersCallback()
    {
        var ciaA = new Cia8520("CIA-A");
        bool overlayState = true;
        ciaA.OnOverlayChanged = (ovl) => overlayState = ovl;

        ciaA.DDRA = 0x01; // Set PRA bit 0 as output
        ciaA.WriteRegister(0x0000, 0x00); // Clear OVL bit

        Assert.False(overlayState);
    }

    [Fact]
    public void InterruptController_SetClearSemanticsAndLevelEvaluation()
    {
        var ic = new InterruptController();

        // Enable INTENA Master + VERTB (Level 3: bit 5 = $0020, Master = $4000)
        ic.WriteIntEna(0x8000 | 0x4000 | 0x0020);
        Assert.True(ic.MasterEnable);
        Assert.Equal(0x4020, ic.INTENA);

        // Request VERTB ($0020)
        ic.WriteIntReq(0x8000 | 0x0020);
        Assert.Equal(3, ic.GetPendingInterruptLevel());

        // Enable EXTER (Level 6: bit 13 = $2000)
        ic.WriteIntEna(0x8000 | 0x2000);
        ic.WriteIntReq(0x8000 | 0x2000);

        // Priority 6 should override priority 3
        Assert.Equal(6, ic.GetPendingInterruptLevel());

        // Clear VERTB request (bit 15 = 0)
        ic.WriteIntReq(0x0020);
        Assert.Equal(0x2000, ic.INTREQ);
    }
}
