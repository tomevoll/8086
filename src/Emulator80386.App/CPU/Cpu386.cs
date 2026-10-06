using System;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;

namespace Emulator80386.App.CPU
{
    public class Cpu386
    {
        public CpuRegisters Reg { get; } = new CpuRegisters();
        public MemoryBus Memory { get; }
        public IOPortBus IOPort { get; }

        public bool Halted { get; set; }

        public Cpu386(MemoryBus memory, IOPortBus ioPort)
        {
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            IOPort = ioPort ?? throw new ArgumentNullException(nameof(ioPort));
        }

        public uint LinearAddress(SegmentRegister seg, uint offset)
        {
            if (Reg.ProtectedMode)
            {
                return seg.Base + offset;
            }
            return (uint)((seg.Selector << 4) + (offset & 0xFFFF));
        }

        public byte Fetch8()
        {
            uint addr = LinearAddress(Reg.CS, Reg.EIP);
            Reg.EIP++;
            return Memory.Read8(addr);
        }

        public ushort Fetch16()
        {
            byte b0 = Fetch8();
            byte b1 = Fetch8();
            return (ushort)(b0 | (b1 << 8));
        }

        public uint Fetch32()
        {
            ushort w0 = Fetch16();
            ushort w1 = Fetch16();
            return (uint)(w0 | (w1 << 16));
        }

        public void Step()
        {
            if (Halted) return;

            bool operandSize32 = false;
            bool addressSize32 = false;
            SegmentRegister? overrideSegment = null;

            while (true)
            {
                byte prefix = Memory.Read8(LinearAddress(Reg.CS, Reg.EIP));
                if (prefix == 0x66) // Operand size override
                {
                    operandSize32 = true;
                    Reg.EIP++;
                }
                else if (prefix == 0x67) // Address size override
                {
                    addressSize32 = true;
                    Reg.EIP++;
                }
                else if (prefix == 0x2E) { overrideSegment = Reg.CS; Reg.EIP++; }
                else if (prefix == 0x36) { overrideSegment = Reg.SS; Reg.EIP++; }
                else if (prefix == 0x3E) { overrideSegment = Reg.DS; Reg.EIP++; }
                else if (prefix == 0x26) { overrideSegment = Reg.ES; Reg.EIP++; }
                else if (prefix == 0x64) { overrideSegment = Reg.FS; Reg.EIP++; }
                else if (prefix == 0x65) { overrideSegment = Reg.GS; Reg.EIP++; }
                else break;
            }

            byte opcode = Fetch8();
            ExecuteOpcode(opcode, operandSize32, addressSize32, overrideSegment);
        }

        private void ExecuteOpcode(byte opcode, bool operandSize32, bool addressSize32, SegmentRegister? overrideSeg)
        {
            SegmentRegister defaultDs = overrideSeg ?? Reg.DS;

            switch (opcode)
            {
                case 0x90: // NOP
                    break;

                case 0xF4: // HLT
                    Halted = true;
                    break;

                case 0xFA: // CLI
                    Reg.SetFlag(EFlags.IF, false);
                    break;

                case 0xFB: // STI
                    Reg.SetFlag(EFlags.IF, true);
                    break;

                case 0xFC: // CLD
                    Reg.SetFlag(EFlags.DF, false);
                    break;

                case 0xFD: // STD
                    Reg.SetFlag(EFlags.DF, true);
                    break;

                case 0xF8: // CLC
                    Reg.SetFlag(EFlags.CF, false);
                    break;

                case 0xF9: // STC
                    Reg.SetFlag(EFlags.CF, true);
                    break;

                // MOV reg8, imm8 (0xB0 .. 0xB7)
                case var _ when (opcode >= 0xB0 && opcode <= 0xB7):
                    {
                        byte imm = Fetch8();
                        Reg.SetGpr8(opcode - 0xB0, imm);
                    }
                    break;

                // MOV reg16/32, imm16/32 (0xB8 .. 0xBF)
                case var _ when (opcode >= 0xB8 && opcode <= 0xBF):
                    {
                        int reg = opcode - 0xB8;
                        if (operandSize32)
                        {
                            Reg.SetGpr32(reg, Fetch32());
                        }
                        else
                        {
                            Reg.SetGpr16(reg, Fetch16());
                        }
                    }
                    break;

                // MOV r/m8, r8 (0x88)
                case 0x88:
                    {
                        DecodeModRM(addressSize32, defaultDs, out var ea, out var reg);
                        Memory.Write8(ea, Reg.GetGpr8(reg));
                    }
                    break;

                // MOV r/m16/32, r16/32 (0x89)
                case 0x89:
                    {
                        DecodeModRM(addressSize32, defaultDs, out var ea, out var reg);
                        if (operandSize32) Memory.Write32(ea, Reg.GetGpr32(reg));
                        else Memory.Write16(ea, Reg.GetGpr16(reg));
                    }
                    break;

                // MOV r8, r/m8 (0x8A)
                case 0x8A:
                    {
                        DecodeModRM(addressSize32, defaultDs, out var ea, out var reg);
                        Reg.SetGpr8(reg, Memory.Read8(ea));
                    }
                    break;

                // MOV r16/32, r/m16/32 (0x8B)
                case 0x8B:
                    {
                        DecodeModRM(addressSize32, defaultDs, out var ea, out var reg);
                        if (operandSize32) Reg.SetGpr32(reg, Memory.Read32(ea));
                        else Reg.SetGpr16(reg, Memory.Read16(ea));
                    }
                    break;

                // MOV r/m8, imm8 (0xC6 /0)
                case 0xC6:
                    {
                        byte modrm = Fetch8();
                        uint ea = GetEA(modrm, addressSize32, defaultDs);
                        byte imm = Fetch8();
                        Memory.Write8(ea, imm);
                    }
                    break;

                // MOV r/m16/32, imm16/32 (0xC7 /0)
                case 0xC7:
                    {
                        byte modrm = Fetch8();
                        uint ea = GetEA(modrm, addressSize32, defaultDs);
                        if (operandSize32) Memory.Write32(ea, Fetch32());
                        else Memory.Write16(ea, Fetch16());
                    }
                    break;

                // MOV Sreg, r/m16 (0x8E)
                case 0x8E:
                    {
                        byte modrm = Fetch8();
                        int sreg = (modrm >> 3) & 7;
                        ushort selector = (modrm >= 0xC0) ? Reg.GetGpr16(modrm & 7) : Memory.Read16(GetEA(modrm, addressSize32, defaultDs));
                        SetSegmentSelector(sreg, selector);
                    }
                    break;

                // MOV r16/32, Sreg (0x8C)
                case 0x8C:
                    {
                        byte modrm = Fetch8();
                        int sreg = (modrm >> 3) & 7;
                        ushort val = GetSegmentSelector(sreg);
                        if (modrm >= 0xC0) Reg.SetGpr16(modrm & 7, val);
                        else Memory.Write16(GetEA(modrm, addressSize32, defaultDs), val);
                    }
                    break;

                // ADD AL, imm8 (0x04)
                case 0x04:
                    Reg.AL = Add8(Reg.AL, Fetch8());
                    break;

                // ADD AX/EAX, imm16/32 (0x05)
                case 0x05:
                    if (operandSize32) Reg.EAX = Add32(Reg.EAX, Fetch32());
                    else Reg.AX = Add16(Reg.AX, Fetch16());
                    break;

                // SUB AL, imm8 (0x2C)
                case 0x2C:
                    Reg.AL = Sub8(Reg.AL, Fetch8());
                    break;

                // SUB AX/EAX, imm16/32 (0x2D)
                case 0x2D:
                    if (operandSize32) Reg.EAX = Sub32(Reg.EAX, Fetch32());
                    else Reg.AX = Sub16(Reg.AX, Fetch16());
                    break;

                // CMP AL, imm8 (0x3C)
                case 0x3C:
                    Sub8(Reg.AL, Fetch8());
                    break;

                // CMP AX/EAX, imm16/32 (0x3D)
                case 0x3D:
                    if (operandSize32) Sub32(Reg.EAX, Fetch32());
                    else Sub16(Reg.AX, Fetch16());
                    break;

                // INC reg16/32 (0x40 .. 0x47)
                case var _ when (opcode >= 0x40 && opcode <= 0x47):
                    {
                        int reg = opcode - 0x40;
                        if (operandSize32) Reg.SetGpr32(reg, Inc32(Reg.GetGpr32(reg)));
                        else Reg.SetGpr16(reg, Inc16(Reg.GetGpr16(reg)));
                    }
                    break;

                // DEC reg16/32 (0x48 .. 0x4F)
                case var _ when (opcode >= 0x48 && opcode <= 0x4F):
                    {
                        int reg = opcode - 0x48;
                        if (operandSize32) Reg.SetGpr32(reg, Dec32(Reg.GetGpr32(reg)));
                        else Reg.SetGpr16(reg, Dec16(Reg.GetGpr16(reg)));
                    }
                    break;

                // PUSH reg16/32 (0x50 .. 0x57)
                case var _ when (opcode >= 0x50 && opcode <= 0x57):
                    {
                        int reg = opcode - 0x50;
                        if (operandSize32) Push32(Reg.GetGpr32(reg));
                        else Push16(Reg.GetGpr16(reg));
                    }
                    break;

                // POP reg16/32 (0x58 .. 0x5F)
                case var _ when (opcode >= 0x58 && opcode <= 0x5F):
                    {
                        int reg = opcode - 0x58;
                        if (operandSize32) Reg.SetGpr32(reg, Pop32());
                        else Reg.SetGpr16(reg, Pop16());
                    }
                    break;

                // JMP rel8 (0xEB)
                case 0xEB:
                    {
                        sbyte rel = (sbyte)Fetch8();
                        Reg.EIP = (uint)(Reg.EIP + rel);
                    }
                    break;

                // JMP rel16/32 (0xE9)
                case 0xE9:
                    {
                        if (operandSize32)
                        {
                            int rel = (int)Fetch32();
                            Reg.EIP = (uint)(Reg.EIP + rel);
                        }
                        else
                        {
                            short rel = (short)Fetch16();
                            Reg.EIP = (ushort)(Reg.EIP + rel);
                        }
                    }
                    break;

                // JMP ptr16:16 / ptr16:32 (0xEA) - Far Jump
                case 0xEA:
                    {
                        if (operandSize32)
                        {
                            uint newEip = Fetch32();
                            ushort newCs = Fetch16();
                            Reg.CS.Selector = newCs;
                            Reg.CS.Base = Reg.ProtectedMode ? Reg.CS.Base : (uint)(newCs << 4);
                            Reg.EIP = newEip;
                        }
                        else
                        {
                            ushort newIp = Fetch16();
                            ushort newCs = Fetch16();
                            Reg.CS.Selector = newCs;
                            Reg.CS.Base = Reg.ProtectedMode ? Reg.CS.Base : (uint)(newCs << 4);
                            Reg.EIP = newIp;
                        }
                    }
                    break;

                // CALL rel16/32 (0xE8)
                case 0xE8:
                    {
                        if (operandSize32)
                        {
                            int rel = (int)Fetch32();
                            Push32(Reg.EIP);
                            Reg.EIP = (uint)(Reg.EIP + rel);
                        }
                        else
                        {
                            short rel = (short)Fetch16();
                            Push16((ushort)Reg.EIP);
                            Reg.EIP = (ushort)(Reg.EIP + rel);
                        }
                    }
                    break;

                // RET (0xC3)
                case 0xC3:
                    if (operandSize32) Reg.EIP = Pop32();
                    else Reg.EIP = Pop16();
                    break;

                // Conditional Jumps rel8 (0x70 .. 0x7F)
                case var _ when (opcode >= 0x70 && opcode <= 0x7F):
                    {
                        sbyte rel = (sbyte)Fetch8();
                        if (CheckCondition(opcode & 0x0F))
                        {
                            Reg.EIP = (uint)(Reg.EIP + rel);
                        }
                    }
                    break;

                // IN AL, imm8 (0xE4)
                case 0xE4:
                    Reg.AL = IOPort.In8(Fetch8());
                    break;

                // IN AX/EAX, imm8 (0xE5)
                case 0xE5:
                    {
                        byte port = Fetch8();
                        if (operandSize32) Reg.EAX = IOPort.In32(port);
                        else Reg.AX = IOPort.In16(port);
                    }
                    break;

                // OUT imm8, AL (0xE6)
                case 0xE6:
                    IOPort.Out8(Fetch8(), Reg.AL);
                    break;

                // OUT imm8, AX/EAX (0xE7)
                case 0xE7:
                    {
                        byte port = Fetch8();
                        if (operandSize32) IOPort.Out32(port, Reg.EAX);
                        else IOPort.Out16(port, Reg.AX);
                    }
                    break;

                // IN AL, DX (0xEC)
                case 0xEC:
                    Reg.AL = IOPort.In8(Reg.DX);
                    break;

                // IN AX/EAX, DX (0xED)
                case 0xED:
                    if (operandSize32) Reg.EAX = IOPort.In32(Reg.DX);
                    else Reg.AX = IOPort.In16(Reg.DX);
                    break;

                // OUT DX, AL (0xEE)
                case 0xEE:
                    IOPort.Out8(Reg.DX, Reg.AL);
                    break;

                // OUT DX, AX/EAX (0xEF)
                case 0xEF:
                    if (operandSize32) IOPort.Out32(Reg.DX, Reg.EAX);
                    else IOPort.Out16(Reg.DX, Reg.AX);
                    break;

                // INT imm8 (0xCD)
                case 0xCD:
                    {
                        byte interruptNum = Fetch8();
                        TriggerInterrupt(interruptNum);
                    }
                    break;

                default:
                    // Unhandled opcode
                    break;
            }
        }

        public void TriggerInterrupt(byte vector)
        {
            if (Reg.ProtectedMode)
            {
                // Basic Protected mode IDT interrupt stub
                Push32((uint)Reg.EFlags);
                Push32(Reg.CS.Selector);
                Push32(Reg.EIP);
            }
            else
            {
                // Real mode interrupt vector lookup (0x0000:vector*4)
                Push16((ushort)Reg.EFlags);
                Push16(Reg.CS.Selector);
                Push16((ushort)Reg.EIP);

                uint ivtAddr = (uint)(vector * 4);
                ushort newIp = Memory.Read16(ivtAddr);
                ushort newCs = Memory.Read16(ivtAddr + 2);

                Reg.CS.Selector = newCs;
                Reg.CS.Base = (uint)(newCs << 4);
                Reg.EIP = newIp;
            }
        }

        public void Push16(ushort val)
        {
            Reg.SP -= 2;
            Memory.Write16(LinearAddress(Reg.SS, Reg.SP), val);
        }

        public ushort Pop16()
        {
            ushort val = Memory.Read16(LinearAddress(Reg.SS, Reg.SP));
            Reg.SP += 2;
            return val;
        }

        public void Push32(uint val)
        {
            Reg.ESP -= 4;
            Memory.Write32(LinearAddress(Reg.SS, Reg.ESP), val);
        }

        public uint Pop32()
        {
            uint val = Memory.Read32(LinearAddress(Reg.SS, Reg.ESP));
            Reg.ESP += 4;
            return val;
        }

        private void SetSegmentSelector(int index, ushort selector)
        {
            SegmentRegister seg = index switch
            {
                0 => Reg.ES,
                1 => Reg.CS,
                2 => Reg.SS,
                3 => Reg.DS,
                4 => Reg.FS,
                5 => Reg.GS,
                _ => Reg.DS
            };

            seg.Selector = selector;
            if (!Reg.ProtectedMode)
            {
                seg.Base = (uint)(selector << 4);
            }
        }

        private ushort GetSegmentSelector(int index) => index switch
        {
            0 => Reg.ES.Selector,
            1 => Reg.CS.Selector,
            2 => Reg.SS.Selector,
            3 => Reg.DS.Selector,
            4 => Reg.FS.Selector,
            5 => Reg.GS.Selector,
            _ => Reg.DS.Selector
        };

        private void DecodeModRM(bool addressSize32, SegmentRegister defaultSeg, out uint ea, out int reg)
        {
            byte modrm = Fetch8();
            reg = (modrm >> 3) & 7;
            ea = GetEA(modrm, addressSize32, defaultSeg);
        }

        private uint GetEA(byte modrm, bool addressSize32, SegmentRegister defaultSeg)
        {
            int mod = (modrm >> 6) & 3;
            int rm = modrm & 7;

            if (mod == 3) // Register operand
            {
                return (uint)rm;
            }

            if (!addressSize32) // 16-bit addressing
            {
                ushort offset = rm switch
                {
                    0 => (ushort)(Reg.BX + Reg.SI),
                    1 => (ushort)(Reg.BX + Reg.DI),
                    2 => (ushort)(Reg.BP + Reg.SI),
                    3 => (ushort)(Reg.BP + Reg.DI),
                    4 => Reg.SI,
                    5 => Reg.DI,
                    6 => (mod == 0) ? Fetch16() : Reg.BP,
                    7 => Reg.BX,
                    _ => 0
                };

                if (mod == 1) offset += (ushort)(sbyte)Fetch8();
                else if (mod == 2) offset += Fetch16();

                SegmentRegister seg = (rm == 2 || rm == 3 || (rm == 6 && mod != 0)) ? Reg.SS : defaultSeg;
                return LinearAddress(seg, offset);
            }
            else // 32-bit addressing
            {
                uint offset = rm switch
                {
                    0 => Reg.EAX,
                    1 => Reg.ECX,
                    2 => Reg.EDX,
                    3 => Reg.EBX,
                    4 => Reg.ESP,
                    5 => (mod == 0) ? Fetch32() : Reg.EBP,
                    6 => Reg.ESI,
                    7 => Reg.EDI,
                    _ => 0
                };

                if (mod == 1) offset = (uint)(offset + (sbyte)Fetch8());
                else if (mod == 2) offset = (uint)(offset + (int)Fetch32());

                return LinearAddress(defaultSeg, offset);
            }
        }

        private bool CheckCondition(int cond) => cond switch
        {
            0x0 => Reg.GetFlag(EFlags.OF),                     // JO
            0x1 => !Reg.GetFlag(EFlags.OF),                    // JNO
            0x2 => Reg.GetFlag(EFlags.CF),                     // JC / JB
            0x3 => !Reg.GetFlag(EFlags.CF),                    // JNC / JAE
            0x4 => Reg.GetFlag(EFlags.ZF),                     // JZ / JE
            0x5 => !Reg.GetFlag(EFlags.ZF),                    // JNZ / JNE
            0x6 => Reg.GetFlag(EFlags.CF) || Reg.GetFlag(EFlags.ZF), // JBE
            0x7 => !Reg.GetFlag(EFlags.CF) && !Reg.GetFlag(EFlags.ZF), // JA
            0x8 => Reg.GetFlag(EFlags.SF),                     // JS
            0x9 => !Reg.GetFlag(EFlags.SF),                    // JNS
            0xA => Reg.GetFlag(EFlags.PF),                     // JP
            0xB => !Reg.GetFlag(EFlags.PF),                    // JNP
            0xC => Reg.GetFlag(EFlags.SF) != Reg.GetFlag(EFlags.OF), // JL
            0xD => Reg.GetFlag(EFlags.SF) == Reg.GetFlag(EFlags.OF), // JGE
            0xE => Reg.GetFlag(EFlags.ZF) || (Reg.GetFlag(EFlags.SF) != Reg.GetFlag(EFlags.OF)), // JLE
            0xF => !Reg.GetFlag(EFlags.ZF) && (Reg.GetFlag(EFlags.SF) == Reg.GetFlag(EFlags.OF)), // JG
            _ => false
        };

        private byte Add8(byte a, byte b)
        {
            int res = a + b;
            Reg.SetFlag(EFlags.CF, res > 0xFF);
            Reg.SetFlag(EFlags.OF, ((a ^ res) & (b ^ res) & 0x80) != 0);
            byte r = (byte)res;
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Add16(ushort a, ushort b)
        {
            int res = a + b;
            Reg.SetFlag(EFlags.CF, res > 0xFFFF);
            Reg.SetFlag(EFlags.OF, ((a ^ res) & (b ^ res) & 0x8000) != 0);
            ushort r = (ushort)res;
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Add32(uint a, uint b)
        {
            ulong res = (ulong)a + b;
            Reg.SetFlag(EFlags.CF, res > 0xFFFFFFFF);
            Reg.SetFlag(EFlags.OF, (((a ^ (uint)res) & (b ^ (uint)res) & 0x80000000)) != 0);
            uint r = (uint)res;
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private byte Sub8(byte a, byte b)
        {
            int res = a - b;
            Reg.SetFlag(EFlags.CF, a < b);
            Reg.SetFlag(EFlags.OF, ((a ^ b) & (a ^ (byte)res) & 0x80) != 0);
            byte r = (byte)res;
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Sub16(ushort a, ushort b)
        {
            int res = a - b;
            Reg.SetFlag(EFlags.CF, a < b);
            Reg.SetFlag(EFlags.OF, ((a ^ b) & (a ^ (ushort)res) & 0x8000) != 0);
            ushort r = (ushort)res;
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Sub32(uint a, uint b)
        {
            long res = (long)a - b;
            Reg.SetFlag(EFlags.CF, a < b);
            Reg.SetFlag(EFlags.OF, (((a ^ b) & (a ^ (uint)res) & 0x80000000)) != 0);
            uint r = (uint)res;
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private ushort Inc16(ushort a)
        {
            ushort r = (ushort)(a + 1);
            Reg.SetFlag(EFlags.OF, a == 0x7FFF);
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Inc32(uint a)
        {
            uint r = a + 1;
            Reg.SetFlag(EFlags.OF, a == 0x7FFFFFFF);
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private ushort Dec16(ushort a)
        {
            ushort r = (ushort)(a - 1);
            Reg.SetFlag(EFlags.OF, a == 0x8000);
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Dec32(uint a)
        {
            uint r = a - 1;
            Reg.SetFlag(EFlags.OF, a == 0x80000000);
            Reg.UpdateZeroSignParity32(r);
            return r;
        }
    }
}
