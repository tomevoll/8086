using System;

namespace Emulator80386.App.CPU
{
    [Flags]
    public enum EFlags : uint
    {
        CF = 1 << 0,  // Carry Flag
        Reserved1 = 1 << 1, // Always 1 in EFLAGS
        PF = 1 << 2,  // Parity Flag
        AF = 1 << 4,  // Auxiliary Carry Flag
        ZF = 1 << 6,  // Zero Flag
        SF = 1 << 7,  // Sign Flag
        TF = 1 << 8,  // Trap Flag
        IF = 1 << 9,  // Interrupt Enable Flag
        DF = 1 << 10, // Direction Flag
        OF = 1 << 11, // Overflow Flag
        IOPL = 3 << 12, // I/O Privilege Level
        NT = 1 << 14, // Nested Task Flag
        RF = 1 << 16, // Resume Flag
        VM = 1 << 17, // Virtual 8086 Mode
        AC = 1 << 18, // Alignment Check
        VIF = 1 << 19, // Virtual Interrupt Flag
        VIP = 1 << 20, // Virtual Interrupt Pending
        ID = 1 << 21  // ID Flag
    }

    public class SegmentRegister
    {
        public ushort Selector { get; set; }
        public uint Base { get; set; }
        public uint Limit { get; set; } = 0xFFFF;
        public byte Attributes { get; set; }
        public bool Is32Bit { get; set; }
    }

    public class CpuRegisters
    {
        // General Purpose Registers (GPRs): 0:EAX, 1:ECX, 2:EDX, 3:EBX, 4:ESP, 5:EBP, 6:ESI, 7:EDI
        private readonly uint[] _gpr = new uint[8];

        public uint EAX { get => _gpr[0]; set => _gpr[0] = value; }
        public uint ECX { get => _gpr[1]; set => _gpr[1] = value; }
        public uint EDX { get => _gpr[2]; set => _gpr[2] = value; }
        public uint EBX { get => _gpr[3]; set => _gpr[3] = value; }
        public uint ESP { get => _gpr[4]; set => _gpr[4] = value; }
        public uint EBP { get => _gpr[5]; set => _gpr[5] = value; }
        public uint ESI { get => _gpr[6]; set => _gpr[6] = value; }
        public uint EDI { get => _gpr[7]; set => _gpr[7] = value; }

        public ushort AX { get => (ushort)(EAX & 0xFFFF); set => EAX = (EAX & 0xFFFF0000) | value; }
        public ushort CX { get => (ushort)(ECX & 0xFFFF); set => ECX = (ECX & 0xFFFF0000) | value; }
        public ushort DX { get => (ushort)(EDX & 0xFFFF); set => EDX = (EDX & 0xFFFF0000) | value; }
        public ushort BX { get => (ushort)(EBX & 0xFFFF); set => EBX = (EBX & 0xFFFF0000) | value; }
        public ushort SP { get => (ushort)(ESP & 0xFFFF); set => ESP = (ESP & 0xFFFF0000) | value; }
        public ushort BP { get => (ushort)(EBP & 0xFFFF); set => EBP = (EBP & 0xFFFF0000) | value; }
        public ushort SI { get => (ushort)(ESI & 0xFFFF); set => ESI = (ESI & 0xFFFF0000) | value; }
        public ushort DI { get => (ushort)(EDI & 0xFFFF); set => EDI = (EDI & 0xFFFF0000) | value; }

        public byte AL { get => (byte)(EAX & 0xFF); set => EAX = (EAX & 0xFFFFFF00) | value; }
        public byte AH { get => (byte)((EAX >> 8) & 0xFF); set => EAX = (EAX & 0xFFFF00FF) | (uint)(value << 8); }
        public byte CL { get => (byte)(ECX & 0xFF); set => ECX = (ECX & 0xFFFFFF00) | value; }
        public byte CH { get => (byte)((ECX >> 8) & 0xFF); set => ECX = (ECX & 0xFFFF00FF) | (uint)(value << 8); }
        public byte DL { get => (byte)(EDX & 0xFF); set => EDX = (EDX & 0xFFFFFF00) | value; }
        public byte DH { get => (byte)((EDX >> 8) & 0xFF); set => EDX = (EDX & 0xFFFF00FF) | (uint)(value << 8); }
        public byte BL { get => (byte)(EBX & 0xFF); set => EBX = (EBX & 0xFFFFFF00) | value; }
        public byte BH { get => (byte)((EBX >> 8) & 0xFF); set => EBX = (EBX & 0xFFFF00FF) | (uint)(value << 8); }

        public uint EIP { get; set; } = 0x0000FFF0; // Reset vector in real mode (CS=0xF000, IP=0xFFF0)
        public EFlags EFlags { get; set; } = EFlags.Reserved1;

        // Segment Registers
        public SegmentRegister CS { get; } = new SegmentRegister { Selector = 0xF000, Base = 0xF0000 };
        public SegmentRegister DS { get; } = new SegmentRegister { Selector = 0x0000, Base = 0x00000 };
        public SegmentRegister SS { get; } = new SegmentRegister { Selector = 0x0000, Base = 0x00000 };
        public SegmentRegister ES { get; } = new SegmentRegister { Selector = 0x0000, Base = 0x00000 };
        public SegmentRegister FS { get; } = new SegmentRegister { Selector = 0x0000, Base = 0x00000 };
        public SegmentRegister GS { get; } = new SegmentRegister { Selector = 0x0000, Base = 0x00000 };

        // GDT & IDT
        public uint GdtBase { get; set; }
        public ushort GdtLimit { get; set; }
        public uint IdtBase { get; set; }
        public ushort IdtLimit { get; set; }

        // Control Registers
        public uint CR0 { get; set; }
        public uint CR2 { get; set; }
        public uint CR3 { get; set; }

        public bool ProtectedMode => (CR0 & 1) != 0;

        public uint GetGpr32(int index) => _gpr[index & 7];
        public void SetGpr32(int index, uint val) => _gpr[index & 7] = val;

        public ushort GetGpr16(int index) => (ushort)(_gpr[index & 7] & 0xFFFF);
        public void SetGpr16(int index, ushort val) => _gpr[index & 7] = (_gpr[index & 7] & 0xFFFF0000) | val;

        public byte GetGpr8(int index)
        {
            int reg = index & 7;
            if (reg < 4) return (byte)(_gpr[reg] & 0xFF);
            return (byte)((_gpr[reg - 4] >> 8) & 0xFF);
        }

        public void SetGpr8(int index, byte val)
        {
            int reg = index & 7;
            if (reg < 4)
                _gpr[reg] = (_gpr[reg] & 0xFFFFFF00) | val;
            else
                _gpr[reg - 4] = (_gpr[reg - 4] & 0xFFFF00FF) | (uint)(val << 8);
        }

        public void SetFlag(EFlags flag, bool value)
        {
            if (value) EFlags |= flag;
            else EFlags &= ~flag;
        }

        public bool GetFlag(EFlags flag) => (EFlags & flag) != 0;

        public void UpdateZeroSignParity8(byte val)
        {
            SetFlag(EFlags.ZF, val == 0);
            SetFlag(EFlags.SF, (val & 0x80) != 0);
            SetFlag(EFlags.PF, ComputeParity(val));
        }

        public void UpdateZeroSignParity16(ushort val)
        {
            SetFlag(EFlags.ZF, val == 0);
            SetFlag(EFlags.SF, (val & 0x8000) != 0);
            SetFlag(EFlags.PF, ComputeParity((byte)(val & 0xFF)));
        }

        public void UpdateZeroSignParity32(uint val)
        {
            SetFlag(EFlags.ZF, val == 0);
            SetFlag(EFlags.SF, (val & 0x80000000) != 0);
            SetFlag(EFlags.PF, ComputeParity((byte)(val & 0xFF)));
        }

        private static bool ComputeParity(byte b)
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                if ((b & (1 << i)) != 0) count++;
            }
            return (count % 2) == 0;
        }
    }
}
