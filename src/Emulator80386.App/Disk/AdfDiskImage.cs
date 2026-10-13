using System;
using System.IO;

namespace Amiga500.Disk;

public class AdfDiskImage
{
    public byte[] RawData { get; private set; } // 901,120 bytes (880 KB)
    public bool WriteProtected { get; set; } = false;

    public AdfDiskImage(byte[] data)
    {
        if (data.Length < 901120)
        {
            RawData = new byte[901120];
            Array.Copy(data, RawData, Math.Min(data.Length, RawData.Length));
        }
        else
        {
            RawData = data;
        }
    }

    public static AdfDiskImage? LoadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            return new AdfDiskImage(fileBytes);
        }
        catch
        {
            return null;
        }
    }

    public void SaveToFile(string path)
    {
        if (WriteProtected) return;
        try
        {
            File.WriteAllBytes(path, RawData);
        }
        catch { }
    }

    public byte[] GetSectorData(int cylinder, int side, int sector)
    {
        int trackNumber = (cylinder * 2) + side;
        int sectorOffset = (trackNumber * 11 + sector) * 512;
        byte[] buffer = new byte[512];
        if (sectorOffset + 512 <= RawData.Length)
        {
            Array.Copy(RawData, sectorOffset, buffer, 0, 512);
        }
        return buffer;
    }

    public void WriteSectorData(int cylinder, int side, int sector, byte[] buffer)
    {
        if (WriteProtected) return;
        int trackNumber = (cylinder * 2) + side;
        int sectorOffset = (trackNumber * 11 + sector) * 512;
        if (sectorOffset + 512 <= RawData.Length)
        {
            Array.Copy(buffer, 0, RawData, sectorOffset, Math.Min(buffer.Length, 512));
        }
    }

    // Encodes a complete 11-sector Amiga MFM track with sync words ($4489)
    public ushort[] GenerateTrackMfm(int cylinder, int side)
    {
        int trackNumber = (cylinder * 2) + side;
        ushort[] mfm = new ushort[7000]; // Standard Amiga track size in MFM words

        int idx = 0;
        // Gap words
        for (int g = 0; g < 100; g++) mfm[idx++] = 0xAAAA;

        for (int s = 0; s < 11; s++)
        {
            // Sync words $4489 $4489
            mfm[idx++] = 0x4489;
            mfm[idx++] = 0x4489;

            // Info / Header
            byte formatByte = 0xFF;
            byte trackByte = (byte)trackNumber;
            byte sectorByte = (byte)s;
            byte secsToGap = (byte)(11 - s);

            uint headerInfo = ((uint)formatByte << 24) | ((uint)trackByte << 16) | ((uint)sectorByte << 8) | secsToGap;
            EncodeMfmLong(mfm, ref idx, headerInfo);
            EncodeMfmLong(mfm, ref idx, 0x00000000); // Header OS sector label

            // Sector Data (512 bytes = 128 longs)
            byte[] sectorBytes = GetSectorData(cylinder, side, s);
            for (int l = 0; l < 128; l++)
            {
                uint val = ((uint)sectorBytes[l * 4] << 24) |
                           ((uint)sectorBytes[l * 4 + 1] << 16) |
                           ((uint)sectorBytes[l * 4 + 2] << 8) |
                           sectorBytes[l * 4 + 3];
                EncodeMfmLong(mfm, ref idx, val);
            }
        }

        while (idx < mfm.Length) mfm[idx++] = 0xAAAA;
        return mfm;
    }

    private static void EncodeMfmLong(ushort[] mfm, ref int idx, uint value)
    {
        uint oddBits = (value >> 1) & 0x55555555u;
        uint evenBits = value & 0x55555555u;

        mfm[idx++] = (ushort)(oddBits >> 16);
        mfm[idx++] = (ushort)(oddBits & 0xFFFF);
        mfm[idx++] = (ushort)(evenBits >> 16);
        mfm[idx++] = (ushort)(evenBits & 0xFFFF);
    }
}
