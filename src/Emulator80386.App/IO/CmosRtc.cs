using System;

namespace Emulator80386.App.IO
{
    public class CmosRtc : IIOPortDevice
    {
        private readonly byte[] _cmos = new byte[128];
        private byte _cmosIndex = 0;

        public CmosRtc(int ramSizeMB)
        {
            // Standard CMOS Register Settings
            _cmos[0x00] = 0x00; // Seconds
            _cmos[0x02] = 0x00; // Minutes
            _cmos[0x04] = 0x12; // Hours
            _cmos[0x06] = 0x01; // Day of week
            _cmos[0x07] = 0x01; // Day of month
            _cmos[0x08] = 0x01; // Month
            _cmos[0x09] = 0x24; // Year

            _cmos[0x0A] = 0x26; // Status A
            _cmos[0x0B] = 0x02; // Status B
            _cmos[0x0C] = 0x00; // Status C
            _cmos[0x0D] = 0x80; // Status D (Valid battery)

            // Base Memory in KB (640 KB = 0x0280)
            ushort baseKb = 640;
            _cmos[0x15] = (byte)(baseKb & 0xFF);
            _cmos[0x16] = (byte)((baseKb >> 8) & 0xFF);

            // Extended Memory in KB (RAM - 1MB)
            int extKb = Math.Max(0, (ramSizeMB - 1) * 1024);
            ushort extKb16 = (ushort)Math.Min(extKb, 0xFFFF);
            _cmos[0x17] = (byte)(extKb16 & 0xFF);
            _cmos[0x18] = (byte)((extKb16 >> 8) & 0xFF);
            _cmos[0x30] = _cmos[0x17];
            _cmos[0x31] = _cmos[0x18];

            // Equipment byte
            _cmos[0x14] = 0x2D; // VGA, Math coprocessor, 1 floppy

            UpdateChecksum();
        }

        private void UpdateChecksum()
        {
            ushort sum = 0;
            for (int i = 0x10; i <= 0x2D; i++)
            {
                sum += _cmos[i];
            }
            _cmos[0x2E] = (byte)((sum >> 8) & 0xFF);
            _cmos[0x2F] = (byte)(sum & 0xFF);
        }

        public byte Read8(ushort port)
        {
            if (port == 0x70)
            {
                return _cmosIndex;
            }
            else if (port == 0x71)
            {
                byte val = _cmos[_cmosIndex & 0x7F];
                if ((_cmosIndex & 0x7F) == 0x0C)
                {
                    _cmos[0x0C] = 0x00;
                }
                return val;
            }
            return 0xFF;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x70)
            {
                _cmosIndex = (byte)(value & 0x7F);
            }
            else if (port == 0x71)
            {
                _cmos[_cmosIndex & 0x7F] = value;
                if ((_cmosIndex & 0x7F) >= 0x10 && (_cmosIndex & 0x7F) <= 0x2D)
                {
                    UpdateChecksum();
                }
            }
        }
    }
}
