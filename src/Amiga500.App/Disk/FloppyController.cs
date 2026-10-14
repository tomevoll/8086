using System;
using Amiga500.Memory;

namespace Amiga500.Disk;

public class FloppyDrive
{
    public AdfDiskImage? Disk { get; set; }
    public int CurrentCylinder { get; set; } = 0;
    public int CurrentSide { get; set; } = 0;
    public bool MotorOn { get; set; } = false;
    public bool Selected { get; set; } = false;
    public bool DiskChanged { get; set; } = false;

    public void InsertDisk(AdfDiskImage disk)
    {
        Disk = disk;
        DiskChanged = true;
    }

    public void EjectDisk()
    {
        Disk = null;
        DiskChanged = true;
    }

    public void Step(bool directionIn)
    {
        if (directionIn)
        {
            if (CurrentCylinder < 79) CurrentCylinder++;
        }
        else
        {
            if (CurrentCylinder > 0) CurrentCylinder--;
        }
    }
}

public class FloppyController
{
    public FloppyDrive[] Drives { get; } = new FloppyDrive[4];

    public uint DSKPTH { get; set; }
    public ushort DSKLEN { get; set; }
    public ushort DSKSYNC { get; set; } = 0x4489;
    public ushort INTREQ { get; set; }

    private bool _prevStepState = false;

    public AmigaMemoryBus Memory { get; set; }

    public FloppyController(AmigaMemoryBus memory)
    {
        Memory = memory;
        for (int i = 0; i < 4; i++)
        {
            Drives[i] = new FloppyDrive();
        }
    }

    public FloppyDrive? ActiveDrive
    {
        get
        {
            for (int i = 0; i < 4; i++)
            {
                if (Drives[i].Selected && Drives[i].MotorOn) return Drives[i];
            }
            return null;
        }
    }

    public void UpdateControlSignals(byte ciaB_Prb, byte ciaA_Pra)
    {
        bool stepActive = (ciaB_Prb & 0x01) == 0;
        bool fallingEdgeStep = stepActive && !_prevStepState; // Detect falling edge (1 -> 0) on /STEP line
        _prevStepState = stepActive;

        bool dirIn = (ciaB_Prb & 0x02) == 0;
        int side = (ciaB_Prb & 0x04) == 0 ? 1 : 0;
        bool motorOn = (ciaB_Prb & 0x80) == 0;

        bool sel0 = (ciaB_Prb & 0x08) == 0;
        bool sel1 = (ciaB_Prb & 0x10) == 0;
        bool sel2 = (ciaB_Prb & 0x20) == 0;
        bool sel3 = (ciaB_Prb & 0x40) == 0;

        Drives[0].Selected = sel0;
        Drives[1].Selected = sel1;
        Drives[2].Selected = sel2;
        Drives[3].Selected = sel3;

        for (int i = 0; i < 4; i++)
        {
            if (Drives[i].Selected)
            {
                Drives[i].MotorOn = motorOn;
                Drives[i].CurrentSide = side;
                if (fallingEdgeStep) Drives[i].Step(dirIn);
            }
        }
    }

    public void TriggerDiskDma()
    {
        if ((DSKLEN & 0x8000) == 0) return; // DMA Enable bit not set

        bool isWrite = (DSKLEN & 0x4000) != 0;
        int wordCount = DSKLEN & 0x3FFF;

        var drive = ActiveDrive;
        if (drive == null || drive.Disk == null)
        {
            // Signal DSKBLK completion interrupt even if no disk in drive
            INTREQ |= 0x0002;
            return;
        }

        ushort[] mfmTrack = drive.Disk.GenerateTrackMfm(drive.CurrentCylinder, drive.CurrentSide);

        if (!isWrite)
        {
            // Read track MFM words into Chip RAM
            uint ptr = DSKPTH;
            int wordsToTransfer = Math.Min(wordCount, mfmTrack.Length);
            for (int i = 0; i < wordsToTransfer; i++)
            {
                Memory.WriteWord(ptr, mfmTrack[i]);
                ptr += 2;
            }
            DSKPTH = ptr;
        }

        // Raise DSKBLK interrupt (bit 1) and DSKSYN interrupt (bit 12)
        INTREQ |= 0x1002;
    }
}
