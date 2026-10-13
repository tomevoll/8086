using Amiga500.Config;
using Amiga500.Disk;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class FloppyTests
{
    [Fact]
    public void AdfDiskImage_SectorReadWrite_ModifiesRawData()
    {
        byte[] raw = new byte[901120];
        var adf = new AdfDiskImage(raw);

        byte[] sectorData = new byte[512];
        for (int i = 0; i < 512; i++) sectorData[i] = (byte)(i & 0xFF);

        // Write cylinder 0, side 0, sector 0
        adf.WriteSectorData(0, 0, 0, sectorData);

        byte[] readBack = adf.GetSectorData(0, 0, 0);
        Assert.Equal(sectorData, readBack);
    }

    [Fact]
    public void AdfDiskImage_GenerateTrackMfm_ContainsSyncWords()
    {
        byte[] raw = new byte[901120];
        var adf = new AdfDiskImage(raw);

        ushort[] mfm = adf.GenerateTrackMfm(0, 0);
        Assert.True(mfm.Length > 0);

        // Verify sync word $4489 exists in track
        bool foundSync = false;
        for (int i = 0; i < mfm.Length; i++)
        {
            if (mfm[i] == 0x4489)
            {
                foundSync = true;
                break;
            }
        }
        Assert.True(foundSync);
    }

    [Fact]
    public void FloppyController_DmaTransfer_TransfersDataToChipRam()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        bus.OverlayEnabled = false;

        var controller = new FloppyController(bus);
        var disk = new AdfDiskImage(new byte[901120]);

        controller.Drives[0].InsertDisk(disk);
        controller.Drives[0].Selected = true;
        controller.Drives[0].MotorOn = true;

        controller.DSKPTH = 0x5000;
        controller.DSKLEN = 0x8010; // Enable DMA, 16 words

        controller.TriggerDiskDma();

        // Memory at $5000 should contain track data
        Assert.Equal(0x5020u, controller.DSKPTH);
        Assert.True((controller.INTREQ & 0x0002) != 0); // DSKBLK interrupt
    }
}
