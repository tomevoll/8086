using System;
using Emulator80386.App.Disk;

namespace Emulator80386.App.IO
{
    public class IdeController : IIOPortDevice
    {
        private readonly FolderDiskController _disk;

        public byte SectorCount { get; set; } = 1;
        public byte LbaLow { get; set; } = 0;
        public byte LbaMid { get; set; } = 0;
        public byte LbaHigh { get; set; } = 0;
        public byte DriveHead { get; set; } = 0xA0;
        public byte Status { get; set; } = 0x50; // Drive Ready + Seek Complete

        public IdeController(FolderDiskController disk)
        {
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
        }

        public byte Read8(ushort port)
        {
            return port switch
            {
                0x1F1 => 0x00, // Error register (no error)
                0x1F2 => SectorCount,
                0x1F3 => LbaLow,
                0x1F4 => LbaMid,
                0x1F5 => LbaHigh,
                0x1F6 => DriveHead,
                0x1F7 => Status,
                0x3F6 => Status,
                _ => 0xFF
            };
        }

        public void Write8(ushort port, byte value)
        {
            switch (port)
            {
                case 0x1F2: SectorCount = value; break;
                case 0x1F3: LbaLow = value; break;
                case 0x1F4: LbaMid = value; break;
                case 0x1F5: LbaHigh = value; break;
                case 0x1F6: DriveHead = value; break;
                case 0x1F7: ExecuteCommand(value); break;
            }
        }

        private void ExecuteCommand(byte command)
        {
            if (command == 0xEC) // ATA Identify Drive
            {
                Status = 0x58; // Drive Ready + Data Request (DRQ)
            }
            else if (command == 0x20) // Read Sector(s)
            {
                Status = 0x58; // DRQ ready
            }
            else
            {
                Status = 0x50; // Drive Ready
            }
        }
    }
}
