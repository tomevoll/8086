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

    // Encodes a complete 11-sector Amiga MFM track with sync words ($4489) following standard Amiga trackdisk layout
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

            // Header info & 16-byte sector label
            uint[] headerAndLabel = new uint[5];
            headerAndLabel[0] = headerInfo;

            uint oddHeader = (headerAndLabel[0] >> 1) & 0x55555555u;
            uint evenHeader = headerAndLabel[0] & 0x55555555u;

            mfm[idx++] = (ushort)(oddHeader >> 16);
            mfm[idx++] = (ushort)(oddHeader & 0xFFFF);
            mfm[idx++] = (ushort)(evenHeader >> 16);
            mfm[idx++] = (ushort)(evenHeader & 0xFFFF);

            // 16 bytes OS label (4 longs)
            for (int l = 1; l < 5; l++)
            {
                mfm[idx++] = 0x0000;
                mfm[idx++] = 0x0000;
                mfm[idx++] = 0x0000;
                mfm[idx++] = 0x0000;
            }

            // Header checksum
            uint headerChecksum = oddHeader ^ evenHeader;
            mfm[idx++] = (ushort)(headerChecksum >> 16);
            mfm[idx++] = (ushort)(headerChecksum & 0xFFFF);
            mfm[idx++] = 0x0000;
            mfm[idx++] = 0x0000;

            // Sector Data (512 bytes = 128 longs) split into 128 odd longs then 128 even longs
            byte[] sectorBytes = GetSectorData(cylinder, side, s);
            uint[] dataLongs = new uint[128];
            uint[] oddDataLongs = new uint[128];
            uint[] evenDataLongs = new uint[128];
            uint dataChecksum = 0;

            for (int l = 0; l < 128; l++)
            {
                dataLongs[l] = ((uint)sectorBytes[l * 4] << 24) |
                               ((uint)sectorBytes[l * 4 + 1] << 16) |
                               ((uint)sectorBytes[l * 4 + 2] << 8) |
                               sectorBytes[l * 4 + 3];

                oddDataLongs[l] = (dataLongs[l] >> 1) & 0x55555555u;
                evenDataLongs[l] = dataLongs[l] & 0x55555555u;

                dataChecksum ^= oddDataLongs[l] ^ evenDataLongs[l];
            }

            // Data checksum long
            mfm[idx++] = (ushort)(dataChecksum >> 16);
            mfm[idx++] = (ushort)(dataChecksum & 0xFFFF);
            mfm[idx++] = 0x0000;
            mfm[idx++] = 0x0000;

            // 128 odd longs
            for (int l = 0; l < 128; l++)
            {
                mfm[idx++] = (ushort)(oddDataLongs[l] >> 16);
                mfm[idx++] = (ushort)(oddDataLongs[l] & 0xFFFF);
            }

            // 128 even longs
            for (int l = 0; l < 128; l++)
            {
                mfm[idx++] = (ushort)(evenDataLongs[l] >> 16);
                mfm[idx++] = (ushort)(evenDataLongs[l] & 0xFFFF);
            }
        }

        while (idx < mfm.Length) mfm[idx++] = 0xAAAA;
        return mfm;
    }
}
