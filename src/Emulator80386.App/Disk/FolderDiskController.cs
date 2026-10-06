using System;
using System.IO;

namespace Emulator80386.App.Disk
{
    public class FolderDiskController
    {
        public string FolderPath { get; }
        public string BootSecPath => Path.Combine(FolderPath, ".bootsec");

        public FolderDiskController(string folderPath)
        {
            FolderPath = folderPath;
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            EnsureBootSector();
        }

        public void EnsureBootSector()
        {
            if (!File.Exists(BootSecPath))
            {
                byte[] defaultBootSec = new byte[512];
                // Simple default bootloader machine code that prints "BOOTING..." or infinite loop
                // 0xEB, 0xFE (JMP $) infinite loop
                defaultBootSec[0] = 0xEB;
                defaultBootSec[1] = 0xFE;
                // Boot sector magic number at offset 510
                defaultBootSec[510] = 0x55;
                defaultBootSec[511] = 0xAA;

                File.WriteAllBytes(BootSecPath, defaultBootSec);
            }
        }

        public byte[] ReadBootSector()
        {
            EnsureBootSector();
            byte[] bootSec = File.ReadAllBytes(BootSecPath);
            if (bootSec.Length < 512)
            {
                Array.Resize(ref bootSec, 512);
            }
            return bootSec;
        }

        public void WriteBootSector(byte[] sectorData)
        {
            if (sectorData == null) throw new ArgumentNullException(nameof(sectorData));
            byte[] buffer = new byte[512];
            Array.Copy(sectorData, buffer, Math.Min(sectorData.Length, 512));
            File.WriteAllBytes(BootSecPath, buffer);
        }

        public byte[] ReadSector(uint lbaSector)
        {
            if (lbaSector == 0)
            {
                return ReadBootSector();
            }

            // For non-boot sectors, mock 512-byte zeroed sector or simulated file access
            return new byte[512];
        }

        public void WriteSector(uint lbaSector, byte[] sectorData)
        {
            if (lbaSector == 0)
            {
                WriteBootSector(sectorData);
            }
        }
    }
}
