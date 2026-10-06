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
            Reg.EIP = Reg.ProtectedMode ? Reg.EIP + 1 : (ushort)(Reg.EIP + 1);
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
            bool repPrefix = false;
            SegmentRegister? overrideSegment = null;

            while (true)
            {
                byte prefix = Memory.Read8(LinearAddress(Reg.CS, Reg.EIP));
                if (prefix == 0x66) { operandSize32 = true; Reg.EIP++; }
                else if (prefix == 0x67) { addressSize32 = true; Reg.EIP++; }
                else if (prefix == 0xF3 || prefix == 0xF2) { repPrefix = true; Reg.EIP++; }
                else if (prefix == 0x2E) { overrideSegment = Reg.CS; Reg.EIP++; }
                else if (prefix == 0x36) { overrideSegment = Reg.SS; Reg.EIP++; }
                else if (prefix == 0x3E) { overrideSegment = Reg.DS; Reg.EIP++; }
                else if (prefix == 0x26) { overrideSegment = Reg.ES; Reg.EIP++; }
                else if (prefix == 0x64) { overrideSegment = Reg.FS; Reg.EIP++; }
                else if (prefix == 0x65) { overrideSegment = Reg.GS; Reg.EIP++; }
                else break;
            }

            byte opcode = Fetch8();
            if (opcode == 0x0F)
            {
                byte subOpcode = Fetch8();
                Execute0FOpcode(subOpcode, operandSize32, addressSize32, overrideSegment);
            }
            else
            {
                ExecuteOpcode(opcode, operandSize32, addressSize32, repPrefix, overrideSegment);
            }
        }

        private struct ModRM
        {
            public int Mod;
            public int Reg;
            public int RM;
            public uint EA;
            public bool IsReg => Mod == 3;
        }

        private ModRM DecodeModRM(bool addressSize32, SegmentRegister defaultSeg)
        {
            byte modrm = Fetch8();
            ModRM m = new ModRM
            {
                Mod = (modrm >> 6) & 3,
                Reg = (modrm >> 3) & 7,
                RM = modrm & 7
            };

            if (m.Mod != 3)
            {
                m.EA = GetEA(m.Mod, m.RM, addressSize32, defaultSeg);
            }
            else
            {
                m.EA = (uint)m.RM;
            }

            return m;
        }

        private byte ReadRm8(ModRM m) => m.IsReg ? Reg.GetGpr8(m.RM) : Memory.Read8(m.EA);
        private void WriteRm8(ModRM m, byte val) { if (m.IsReg) Reg.SetGpr8(m.RM, val); else Memory.Write8(m.EA, val); }

        private ushort ReadRm16(ModRM m) => m.IsReg ? Reg.GetGpr16(m.RM) : Memory.Read16(m.EA);
        private void WriteRm16(ModRM m, ushort val) { if (m.IsReg) Reg.SetGpr16(m.RM, val); else Memory.Write16(m.EA, val); }

        private uint ReadRm32(ModRM m) => m.IsReg ? Reg.GetGpr32(m.RM) : Memory.Read32(m.EA);
        private void WriteRm32(ModRM m, uint val) { if (m.IsReg) Reg.SetGpr32(m.RM, val); else Memory.Write32(m.EA, val); }

        private void AddRelEip(int rel)
        {
            if (Reg.ProtectedMode) Reg.EIP = (uint)(Reg.EIP + rel);
            else Reg.EIP = (ushort)(Reg.EIP + rel);
        }

        private void Execute0FOpcode(byte subOpcode, bool operandSize32, bool addressSize32, SegmentRegister? overrideSeg)
        {
            SegmentRegister defaultDs = overrideSeg ?? Reg.DS;

            switch (subOpcode)
            {
                // LGDT / LIDT (0x0F 0x01)
                case 0x01:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        ushort limit = Memory.Read16(m.EA);
                        uint baseAddr = Memory.Read32(m.EA + 2);
                    }
                    break;

                // MOV r32, CR0/CR2/CR3 (0x0F 0x20)
                case 0x20:
                    {
                        byte modrm = Fetch8();
                        int crIndex = (modrm >> 3) & 7;
                        int regIndex = modrm & 7;
                        uint crVal = crIndex switch
                        {
                            0 => Reg.CR0,
                            2 => Reg.CR2,
                            3 => Reg.CR3,
                            _ => 0
                        };
                        Reg.SetGpr32(regIndex, crVal);
                    }
                    break;

                // MOV CR0/CR2/CR3, r32 (0x0F 0x22)
                case 0x22:
                    {
                        byte modrm = Fetch8();
                        int crIndex = (modrm >> 3) & 7;
                        int regIndex = modrm & 7;
                        uint val = Reg.GetGpr32(regIndex);
                        if (crIndex == 0) Reg.CR0 = val;
                        else if (crIndex == 2) Reg.CR2 = val;
                        else if (crIndex == 3) Reg.CR3 = val;
                    }
                    break;

                // Near Jcc rel16/rel32 (0x0F 0x80 .. 0x0F 0x8F)
                case var _ when (subOpcode >= 0x80 && subOpcode <= 0x8F):
                    {
                        int rel = operandSize32 ? (int)Fetch32() : (short)Fetch16();
                        if (CheckCondition(subOpcode & 0x0F))
                        {
                            AddRelEip(rel);
                        }
                    }
                    break;

                // MOVZX r16/32, r/m8 (0x0F 0xB6)
                case 0xB6:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte val = ReadRm8(m);
                        if (operandSize32) Reg.SetGpr32(m.Reg, val);
                        else Reg.SetGpr16(m.Reg, val);
                    }
                    break;

                // MOVZX r16/32, r/m16 (0x0F 0xB7)
                case 0xB7:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        ushort val = ReadRm16(m);
                        if (operandSize32) Reg.SetGpr32(m.Reg, val);
                        else Reg.SetGpr16(m.Reg, val);
                    }
                    break;

                // MOVSX r16/32, r/m8 (0x0F 0xBE)
                case 0xBE:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        sbyte val = (sbyte)ReadRm8(m);
                        if (operandSize32) Reg.SetGpr32(m.Reg, (uint)val);
                        else Reg.SetGpr16(m.Reg, (ushort)val);
                    }
                    break;

                // MOVSX r16/32, r/m16 (0x0F 0xBF)
                case 0xBF:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        short val = (short)ReadRm16(m);
                        if (operandSize32) Reg.SetGpr32(m.Reg, (uint)val);
                        else Reg.SetGpr16(m.Reg, (ushort)val);
                    }
                    break;

                default:
                    break;
            }
        }

        private void ExecuteOpcode(byte opcode, bool operandSize32, bool addressSize32, bool repPrefix, SegmentRegister? overrideSeg)
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

                case 0x9C: // PUSHF
                    if (operandSize32) Push32((uint)Reg.EFlags);
                    else Push16((ushort)Reg.EFlags);
                    break;

                case 0x9D: // POPF
                    if (operandSize32) Reg.EFlags = (EFlags)Pop32();
                    else Reg.EFlags = (EFlags)Pop16();
                    break;

                case 0x9E: // SAHF
                    Reg.EFlags = (Reg.EFlags & (EFlags)0xFFFFFF00) | (EFlags)Reg.AH;
                    break;

                case 0x9F: // LAHF
                    Reg.AH = (byte)((uint)Reg.EFlags & 0xFF);
                    break;

                case 0x60: // PUSHA / PUSHAD
                    if (operandSize32)
                    {
                        uint tempSp = Reg.ESP;
                        Push32(Reg.EAX); Push32(Reg.ECX); Push32(Reg.EDX); Push32(Reg.EBX);
                        Push32(tempSp); Push32(Reg.EBP); Push32(Reg.ESI); Push32(Reg.EDI);
                    }
                    else
                    {
                        ushort tempSp = Reg.SP;
                        Push16(Reg.AX); Push16(Reg.CX); Push16(Reg.DX); Push16(Reg.BX);
                        Push16(tempSp); Push16(Reg.BP); Push16(Reg.SI); Push16(Reg.DI);
                    }
                    break;

                case 0x61: // POPA / POPAD
                    if (operandSize32)
                    {
                        Reg.EDI = Pop32(); Reg.ESI = Pop32(); Reg.EBP = Pop32(); Pop32();
                        Reg.EBX = Pop32(); Reg.EDX = Pop32(); Reg.ECX = Pop32(); Reg.EAX = Pop32();
                    }
                    else
                    {
                        Reg.DI = Pop16(); Reg.SI = Pop16(); Reg.BP = Pop16(); Pop16();
                        Reg.BX = Pop16(); Reg.DX = Pop16(); Reg.CX = Pop16(); Reg.AX = Pop16();
                    }
                    break;

                case 0xC9: // LEAVE
                    Reg.ESP = Reg.EBP;
                    if (operandSize32) Reg.EBP = Pop32();
                    else Reg.BP = Pop16();
                    break;

                // PUSH imm16/32 (0x68)
                case 0x68:
                    if (operandSize32) Push32(Fetch32());
                    else Push16(Fetch16());
                    break;

                // PUSH imm8 (0x6A)
                case 0x6A:
                    {
                        sbyte imm8 = (sbyte)Fetch8();
                        if (operandSize32) Push32((uint)(int)imm8);
                        else Push16((ushort)(short)imm8);
                    }
                    break;

                // XCHG r/m8, r8 (0x86)
                case 0x86:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte val1 = ReadRm8(m);
                        byte val2 = Reg.GetGpr8(m.Reg);
                        WriteRm8(m, val2);
                        Reg.SetGpr8(m.Reg, val1);
                    }
                    break;

                // XCHG r/m16/32, r16/32 (0x87)
                case 0x87:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32)
                        {
                            uint val1 = ReadRm32(m);
                            uint val2 = Reg.GetGpr32(m.Reg);
                            WriteRm32(m, val2);
                            Reg.SetGpr32(m.Reg, val1);
                        }
                        else
                        {
                            ushort val1 = ReadRm16(m);
                            ushort val2 = Reg.GetGpr16(m.Reg);
                            WriteRm16(m, val2);
                            Reg.SetGpr16(m.Reg, val1);
                        }
                    }
                    break;

                // XCHG AX/EAX, reg16/32 (0x91 .. 0x97)
                case var _ when (opcode >= 0x91 && opcode <= 0x97):
                    {
                        int reg = opcode - 0x90;
                        if (operandSize32)
                        {
                            uint tmp = Reg.EAX;
                            Reg.EAX = Reg.GetGpr32(reg);
                            Reg.SetGpr32(reg, tmp);
                        }
                        else
                        {
                            ushort tmp = Reg.AX;
                            Reg.AX = Reg.GetGpr16(reg);
                            Reg.SetGpr16(reg, tmp);
                        }
                    }
                    break;

                // CALL FAR ptr16:16 / ptr16:32 (0x9A)
                case 0x9A:
                    {
                        if (operandSize32)
                        {
                            uint newEip = Fetch32();
                            ushort newCs = Fetch16();
                            Push32(Reg.CS.Selector);
                            Push32(Reg.EIP);
                            SetSegmentSelector(1, newCs);
                            Reg.EIP = newEip;
                        }
                        else
                        {
                            ushort newIp = Fetch16();
                            ushort newCs = Fetch16();
                            Push16(Reg.CS.Selector);
                            Push16((ushort)Reg.EIP);
                            SetSegmentSelector(1, newCs);
                            Reg.EIP = newIp;
                        }
                    }
                    break;

                // IRET (0xCF)
                case 0xCF:
                    {
                        if (operandSize32)
                        {
                            Reg.EIP = Pop32();
                            SetSegmentSelector(1, (ushort)Pop32());
                            Reg.EFlags = (EFlags)Pop32();
                        }
                        else
                        {
                            Reg.EIP = Pop16();
                            SetSegmentSelector(1, Pop16());
                            Reg.EFlags = (EFlags)Pop16();
                        }
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

                // OR AL, imm8 (0x0C)
                case 0x0C:
                    Reg.AL = Or8(Reg.AL, Fetch8());
                    break;

                // OR AX/EAX, imm16/32 (0x0D)
                case 0x0D:
                    if (operandSize32) Reg.EAX = Or32(Reg.EAX, Fetch32());
                    else Reg.AX = Or16(Reg.AX, Fetch16());
                    break;

                // AND AL, imm8 (0x24)
                case 0x24:
                    Reg.AL = And8(Reg.AL, Fetch8());
                    break;

                // AND AX/EAX, imm16/32 (0x25)
                case 0x25:
                    if (operandSize32) Reg.EAX = And32(Reg.EAX, Fetch32());
                    else Reg.AX = And16(Reg.AX, Fetch16());
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

                // XOR AL, imm8 (0x34)
                case 0x34:
                    Reg.AL = Xor8(Reg.AL, Fetch8());
                    break;

                // XOR AX/EAX, imm16/32 (0x35)
                case 0x35:
                    if (operandSize32) Reg.EAX = Xor32(Reg.EAX, Fetch32());
                    else Reg.AX = Xor16(Reg.AX, Fetch16());
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

                // TEST AL, imm8 (0xA8)
                case 0xA8:
                    And8(Reg.AL, Fetch8());
                    break;

                // TEST AX/EAX, imm16/32 (0xA9)
                case 0xA9:
                    if (operandSize32) And32(Reg.EAX, Fetch32());
                    else And16(Reg.AX, Fetch16());
                    break;

                // TEST r/m8, r8 (0x84)
                case 0x84:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        And8(ReadRm8(m), Reg.GetGpr8(m.Reg));
                    }
                    break;

                // TEST r/m16/32, r16/32 (0x85)
                case 0x85:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32) And32(ReadRm32(m), Reg.GetGpr32(m.Reg));
                        else And16(ReadRm16(m), Reg.GetGpr16(m.Reg));
                    }
                    break;

                // LOOPNZ / LOOPNE (0xE0)
                case 0xE0:
                    {
                        sbyte rel = (sbyte)Fetch8();
                        if (addressSize32) { Reg.ECX--; if (Reg.ECX != 0 && !Reg.GetFlag(EFlags.ZF)) AddRelEip(rel); }
                        else { Reg.CX--; if (Reg.CX != 0 && !Reg.GetFlag(EFlags.ZF)) AddRelEip(rel); }
                    }
                    break;

                // LOOPZ / LOOPE (0xE1)
                case 0xE1:
                    {
                        sbyte rel = (sbyte)Fetch8();
                        if (addressSize32) { Reg.ECX--; if (Reg.ECX != 0 && Reg.GetFlag(EFlags.ZF)) AddRelEip(rel); }
                        else { Reg.CX--; if (Reg.CX != 0 && Reg.GetFlag(EFlags.ZF)) AddRelEip(rel); }
                    }
                    break;

                // LOOP (0xE2)
                case 0xE2:
                    {
                        sbyte rel = (sbyte)Fetch8();
                        if (addressSize32) { Reg.ECX--; if (Reg.ECX != 0) AddRelEip(rel); }
                        else { Reg.CX--; if (Reg.CX != 0) AddRelEip(rel); }
                    }
                    break;

                // JCXZ / JECXZ (0xE3)
                case 0xE3:
                    {
                        sbyte rel = (sbyte)Fetch8();
                        uint count = addressSize32 ? Reg.ECX : Reg.CX;
                        if (count == 0) AddRelEip(rel);
                    }
                    break;

                // Group 2 Shifts / Rotates: C0, C1, D0, D1, D2, D3
                case 0xC0:
                case 0xC1:
                case 0xD0:
                case 0xD1:
                case 0xD2:
                case 0xD3:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte count = opcode switch
                        {
                            0xC0 or 0xC1 => Fetch8(),
                            0xD0 or 0xD1 => 1,
                            _ => Reg.CL
                        };
                        bool is1632 = (opcode & 1) != 0;
                        if (!is1632) WriteRm8(m, ExecuteShift8(m.Reg, ReadRm8(m), count));
                        else if (operandSize32) WriteRm32(m, ExecuteShift32(m.Reg, ReadRm32(m), count));
                        else WriteRm16(m, ExecuteShift16(m.Reg, ReadRm16(m), count));
                    }
                    break;

                // Group 3: F6, F7
                case 0xF6:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte val = ReadRm8(m);
                        if (m.Reg == 0 || m.Reg == 1) // TEST
                        {
                            byte imm = Fetch8();
                            And8(val, imm);
                        }
                        else if (m.Reg == 2) WriteRm8(m, (byte)~val); // NOT
                        else if (m.Reg == 3) WriteRm8(m, Sub8(0, val)); // NEG
                        else if (m.Reg == 4) // MUL AL
                        {
                            ushort res = (ushort)(Reg.AL * val);
                            Reg.AX = res;
                            bool carry = (res >> 8) != 0;
                            Reg.SetFlag(EFlags.CF, carry); Reg.SetFlag(EFlags.OF, carry);
                        }
                        else if (m.Reg == 6) // DIV AL
                        {
                            if (val != 0)
                            {
                                ushort num = Reg.AX;
                                Reg.AL = (byte)(num / val);
                                Reg.AH = (byte)(num % val);
                            }
                        }
                    }
                    break;

                case 0xF7:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32)
                        {
                            uint val = ReadRm32(m);
                            if (m.Reg == 0 || m.Reg == 1) // TEST
                            {
                                uint imm = Fetch32();
                                And32(val, imm);
                            }
                            else if (m.Reg == 2) WriteRm32(m, ~val); // NOT
                            else if (m.Reg == 3) WriteRm32(m, Sub32(0, val)); // NEG
                            else if (m.Reg == 4) // MUL EAX
                            {
                                ulong res = (ulong)Reg.EAX * val;
                                Reg.EAX = (uint)res;
                                Reg.EDX = (uint)(res >> 32);
                                bool carry = Reg.EDX != 0;
                                Reg.SetFlag(EFlags.CF, carry); Reg.SetFlag(EFlags.OF, carry);
                            }
                            else if (m.Reg == 6) // DIV EAX
                            {
                                ulong num = ((ulong)Reg.EDX << 32) | Reg.EAX;
                                if (val != 0)
                                {
                                    Reg.EAX = (uint)(num / val);
                                    Reg.EDX = (uint)(num % val);
                                }
                            }
                        }
                        else
                        {
                            ushort val = ReadRm16(m);
                            if (m.Reg == 0 || m.Reg == 1) // TEST
                            {
                                ushort imm = Fetch16();
                                And16(val, imm);
                            }
                            else if (m.Reg == 2) WriteRm16(m, (ushort)~val); // NOT
                            else if (m.Reg == 3) WriteRm16(m, Sub16(0, val)); // NEG
                            else if (m.Reg == 4) // MUL AX
                            {
                                uint res = (uint)Reg.AX * val;
                                Reg.AX = (ushort)res;
                                Reg.DX = (ushort)(res >> 16);
                                bool carry = Reg.DX != 0;
                                Reg.SetFlag(EFlags.CF, carry); Reg.SetFlag(EFlags.OF, carry);
                            }
                            else if (m.Reg == 6) // DIV AX
                            {
                                uint num = (uint)((Reg.DX << 16) | Reg.AX);
                                if (val != 0)
                                {
                                    Reg.AX = (ushort)(num / val);
                                    Reg.DX = (ushort)(num % val);
                                }
                            }
                        }
                    }
                    break;

                // LEA r16/32, m (0x8D)
                case 0x8D:
                    {
                        byte modrm = Fetch8();
                        int reg = (modrm >> 3) & 7;
                        uint ea = GetEA((modrm >> 6) & 3, modrm & 7, addressSize32, defaultDs);
                        if (operandSize32) Reg.SetGpr32(reg, ea);
                        else Reg.SetGpr16(reg, (ushort)ea);
                    }
                    break;

                // Group 1 Opcodes: 80, 81, 82, 83
                case 0x80:
                case 0x82:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte src = ReadRm8(m);
                        byte imm = Fetch8();
                        byte res = ExecuteAlu8(m.Reg, src, imm);
                        if (m.Reg != 7) WriteRm8(m, res);
                    }
                    break;

                case 0x81:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32)
                        {
                            uint src = ReadRm32(m);
                            uint imm = Fetch32();
                            uint res = ExecuteAlu32(m.Reg, src, imm);
                            if (m.Reg != 7) WriteRm32(m, res);
                        }
                        else
                        {
                            ushort src = ReadRm16(m);
                            ushort imm = Fetch16();
                            ushort res = ExecuteAlu16(m.Reg, src, imm);
                            if (m.Reg != 7) WriteRm16(m, res);
                        }
                    }
                    break;

                case 0x83:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        sbyte imm8 = (sbyte)Fetch8();
                        if (operandSize32)
                        {
                            uint src = ReadRm32(m);
                            uint imm = (uint)(int)imm8;
                            uint res = ExecuteAlu32(m.Reg, src, imm);
                            if (m.Reg != 7) WriteRm32(m, res);
                        }
                        else
                        {
                            ushort src = ReadRm16(m);
                            ushort imm = (ushort)(short)imm8;
                            ushort res = ExecuteAlu16(m.Reg, src, imm);
                            if (m.Reg != 7) WriteRm16(m, res);
                        }
                    }
                    break;

                // Standard ALU ModRM Opcodes
                case var _ when (opcode <= 0x3B && ((opcode & 7) < 4)):
                    {
                        int op = (opcode >> 3) & 7;
                        int dir = (opcode >> 1) & 1;
                        bool is1632 = (opcode & 1) != 0;

                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (!is1632)
                        {
                            byte src1 = dir == 0 ? ReadRm8(m) : Reg.GetGpr8(m.Reg);
                            byte src2 = dir == 0 ? Reg.GetGpr8(m.Reg) : ReadRm8(m);
                            byte res = ExecuteAlu8(op, src1, src2);
                            if (op != 7)
                            {
                                if (dir == 0) WriteRm8(m, res);
                                else Reg.SetGpr8(m.Reg, res);
                            }
                        }
                        else if (operandSize32)
                        {
                            uint src1 = dir == 0 ? ReadRm32(m) : Reg.GetGpr32(m.Reg);
                            uint src2 = dir == 0 ? Reg.GetGpr32(m.Reg) : ReadRm32(m);
                            uint res = ExecuteAlu32(op, src1, src2);
                            if (op != 7)
                            {
                                if (dir == 0) WriteRm32(m, res);
                                else Reg.SetGpr32(m.Reg, res);
                            }
                        }
                        else
                        {
                            ushort src1 = dir == 0 ? ReadRm16(m) : Reg.GetGpr16(m.Reg);
                            ushort src2 = dir == 0 ? Reg.GetGpr16(m.Reg) : ReadRm16(m);
                            ushort res = ExecuteAlu16(op, src1, src2);
                            if (op != 7)
                            {
                                if (dir == 0) WriteRm16(m, res);
                                else Reg.SetGpr16(m.Reg, res);
                            }
                        }
                    }
                    break;

                // Group 4/5 Opcodes: FE, FF
                case 0xFE:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte val = ReadRm8(m);
                        if (m.Reg == 0) WriteRm8(m, Inc8(val));
                        else if (m.Reg == 1) WriteRm8(m, Dec8(val));
                    }
                    break;

                case 0xFF:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (m.Reg == 0)
                        {
                            if (operandSize32) WriteRm32(m, Inc32(ReadRm32(m)));
                            else WriteRm16(m, Inc16(ReadRm16(m)));
                        }
                        else if (m.Reg == 1)
                        {
                            if (operandSize32) WriteRm32(m, Dec32(ReadRm32(m)));
                            else WriteRm16(m, Dec16(ReadRm16(m)));
                        }
                        else if (m.Reg == 2)
                        {
                            uint target = operandSize32 ? ReadRm32(m) : ReadRm16(m);
                            if (operandSize32) Push32(Reg.EIP); else Push16((ushort)Reg.EIP);
                            Reg.EIP = target;
                        }
                        else if (m.Reg == 4)
                        {
                            uint target = operandSize32 ? ReadRm32(m) : ReadRm16(m);
                            Reg.EIP = target;
                        }
                        else if (m.Reg == 6)
                        {
                            if (operandSize32) Push32(ReadRm32(m));
                            else Push16(ReadRm16(m));
                        }
                    }
                    break;

                // Push Segment Registers
                case 0x06: Push16(Reg.ES.Selector); break;
                case 0x0E: Push16(Reg.CS.Selector); break;
                case 0x16: Push16(Reg.SS.Selector); break;
                case 0x1E: Push16(Reg.DS.Selector); break;

                // Pop Segment Registers
                case 0x07: SetSegmentSelector(0, Pop16()); break;
                case 0x17: SetSegmentSelector(2, Pop16()); break;
                case 0x1F: SetSegmentSelector(3, Pop16()); break;

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

                // MOV AL, [offset] (0xA0)
                case 0xA0:
                    {
                        uint offset = addressSize32 ? Fetch32() : Fetch16();
                        Reg.AL = Memory.Read8(LinearAddress(defaultDs, offset));
                    }
                    break;

                // MOV AX/EAX, [offset] (0xA1)
                case 0xA1:
                    {
                        uint offset = addressSize32 ? Fetch32() : Fetch16();
                        if (operandSize32) Reg.EAX = Memory.Read32(LinearAddress(defaultDs, offset));
                        else Reg.AX = Memory.Read16(LinearAddress(defaultDs, offset));
                    }
                    break;

                // MOV [offset], AL (0xA2)
                case 0xA2:
                    {
                        uint offset = addressSize32 ? Fetch32() : Fetch16();
                        Memory.Write8(LinearAddress(defaultDs, offset), Reg.AL);
                    }
                    break;

                // MOV [offset], AX/EAX (0xA3)
                case 0xA3:
                    {
                        uint offset = addressSize32 ? Fetch32() : Fetch16();
                        if (operandSize32) Memory.Write32(LinearAddress(defaultDs, offset), Reg.EAX);
                        else Memory.Write16(LinearAddress(defaultDs, offset), Reg.AX);
                    }
                    break;

                // MOV r/m8, r8 (0x88)
                case 0x88:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        WriteRm8(m, Reg.GetGpr8(m.Reg));
                    }
                    break;

                // MOV r/m16/32, r16/32 (0x89)
                case 0x89:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32) WriteRm32(m, Reg.GetGpr32(m.Reg));
                        else WriteRm16(m, Reg.GetGpr16(m.Reg));
                    }
                    break;

                // MOV r8, r/m8 (0x8A)
                case 0x8A:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        Reg.SetGpr8(m.Reg, ReadRm8(m));
                    }
                    break;

                // MOV r16/32, r/m16/32 (0x8B)
                case 0x8B:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32) Reg.SetGpr32(m.Reg, ReadRm32(m));
                        else Reg.SetGpr16(m.Reg, ReadRm16(m));
                    }
                    break;

                // MOV r/m8, imm8 (0xC6 /0)
                case 0xC6:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        byte imm = Fetch8();
                        WriteRm8(m, imm);
                    }
                    break;

                // MOV r/m16/32, imm16/32 (0xC7 /0)
                case 0xC7:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        if (operandSize32) WriteRm32(m, Fetch32());
                        else WriteRm16(m, Fetch16());
                    }
                    break;

                // MOV Sreg, r/m16 (0x8E)
                case 0x8E:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        ushort selector = ReadRm16(m);
                        SetSegmentSelector(m.Reg, selector);
                    }
                    break;

                // MOV r16/32, Sreg (0x8C)
                case 0x8C:
                    {
                        ModRM m = DecodeModRM(addressSize32, defaultDs);
                        ushort val = GetSegmentSelector(m.Reg);
                        WriteRm16(m, val);
                    }
                    break;

                // STOSB (0xAA)
                case 0xAA:
                    {
                        int count = repPrefix ? (int)Reg.CX : 1;
                        int step = Reg.GetFlag(EFlags.DF) ? -1 : 1;
                        for (int i = 0; i < count; i++)
                        {
                            Memory.Write8(LinearAddress(Reg.ES, Reg.DI), Reg.AL);
                            Reg.DI = (ushort)(Reg.DI + step);
                        }
                        if (repPrefix) Reg.CX = 0;
                    }
                    break;

                // STOSW / STOSD (0xAB)
                case 0xAB:
                    {
                        int count = repPrefix ? (int)Reg.CX : 1;
                        int step = (Reg.GetFlag(EFlags.DF) ? -1 : 1) * (operandSize32 ? 4 : 2);
                        for (int i = 0; i < count; i++)
                        {
                            if (operandSize32) Memory.Write32(LinearAddress(Reg.ES, Reg.EDI), Reg.EAX);
                            else Memory.Write16(LinearAddress(Reg.ES, Reg.DI), Reg.AX);

                            if (addressSize32) Reg.EDI = (uint)(Reg.EDI + step);
                            else Reg.DI = (ushort)(Reg.DI + step);
                        }
                        if (repPrefix) Reg.CX = 0;
                    }
                    break;

                // LODSB (0xAC)
                case 0xAC:
                    {
                        Reg.AL = Memory.Read8(LinearAddress(defaultDs, Reg.SI));
                        int step = Reg.GetFlag(EFlags.DF) ? -1 : 1;
                        Reg.SI = (ushort)(Reg.SI + step);
                    }
                    break;

                // LODSW / LODSD (0xAD)
                case 0xAD:
                    {
                        if (operandSize32) Reg.EAX = Memory.Read32(LinearAddress(defaultDs, Reg.ESI));
                        else Reg.AX = Memory.Read16(LinearAddress(defaultDs, Reg.SI));

                        int step = (Reg.GetFlag(EFlags.DF) ? -1 : 1) * (operandSize32 ? 4 : 2);
                        if (addressSize32) Reg.ESI = (uint)(Reg.ESI + step);
                        else Reg.SI = (ushort)(Reg.SI + step);
                    }
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
                        AddRelEip(rel);
                    }
                    break;

                // JMP rel16/32 (0xE9)
                case 0xE9:
                    {
                        if (operandSize32)
                        {
                            int rel = (int)Fetch32();
                            AddRelEip(rel);
                        }
                        else
                        {
                            short rel = (short)Fetch16();
                            AddRelEip(rel);
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
                            AddRelEip(rel);
                        }
                        else
                        {
                            short rel = (short)Fetch16();
                            Push16((ushort)Reg.EIP);
                            AddRelEip(rel);
                        }
                    }
                    break;

                // RET (0xC3)
                case 0xC3:
                    if (operandSize32) Reg.EIP = Pop32();
                    else Reg.EIP = Pop16();
                    break;

                // RETF (0xCB) - Far Return
                case 0xCB:
                    {
                        if (operandSize32)
                        {
                            Reg.EIP = Pop32();
                            SetSegmentSelector(1, (ushort)Pop32());
                        }
                        else
                        {
                            Reg.EIP = Pop16();
                            SetSegmentSelector(1, Pop16());
                        }
                    }
                    break;

                // Conditional Jumps rel8 (0x70 .. 0x7F)
                case var _ when (opcode >= 0x70 && opcode <= 0x7F):
                    {
                        sbyte rel = (sbyte)Fetch8();
                        if (CheckCondition(opcode & 0x0F))
                        {
                            AddRelEip(rel);
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

        private byte ExecuteShift8(int op, byte val, byte cnt)
        {
            cnt &= 0x1F;
            if (cnt == 0) return val;
            for (int i = 0; i < cnt; i++)
            {
                if (op == 4) // SHL / SAL
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x80) != 0);
                    val = (byte)(val << 1);
                }
                else if (op == 5) // SHR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x01) != 0);
                    val = (byte)(val >> 1);
                }
                else if (op == 7) // SAR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x01) != 0);
                    val = (byte)((val >> 1) | (val & 0x80));
                }
                else if (op == 0) // ROL
                {
                    bool top = (val & 0x80) != 0;
                    val = (byte)((val << 1) | (top ? 1 : 0));
                    Reg.SetFlag(EFlags.CF, top);
                }
                else if (op == 1) // ROR
                {
                    bool bot = (val & 0x01) != 0;
                    val = (byte)((val >> 1) | (bot ? 0x80 : 0));
                    Reg.SetFlag(EFlags.CF, bot);
                }
            }
            Reg.UpdateZeroSignParity8(val);
            return val;
        }

        private ushort ExecuteShift16(int op, ushort val, byte cnt)
        {
            cnt &= 0x1F;
            if (cnt == 0) return val;
            for (int i = 0; i < cnt; i++)
            {
                if (op == 4) // SHL
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x8000) != 0);
                    val = (ushort)(val << 1);
                }
                else if (op == 5) // SHR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x0001) != 0);
                    val = (ushort)(val >> 1);
                }
                else if (op == 7) // SAR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x0001) != 0);
                    val = (ushort)((val >> 1) | (val & 0x8000));
                }
                else if (op == 0) // ROL
                {
                    bool top = (val & 0x8000) != 0;
                    val = (ushort)((val << 1) | (top ? 1 : 0));
                    Reg.SetFlag(EFlags.CF, top);
                }
                else if (op == 1) // ROR
                {
                    bool bot = (val & 0x0001) != 0;
                    val = (ushort)((val >> 1) | (bot ? 0x8000 : 0));
                    Reg.SetFlag(EFlags.CF, bot);
                }
            }
            Reg.UpdateZeroSignParity16(val);
            return val;
        }

        private uint ExecuteShift32(int op, uint val, byte cnt)
        {
            cnt &= 0x1F;
            if (cnt == 0) return val;
            for (int i = 0; i < cnt; i++)
            {
                if (op == 4) // SHL
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x80000000) != 0);
                    val = val << 1;
                }
                else if (op == 5) // SHR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x00000001) != 0);
                    val = val >> 1;
                }
                else if (op == 7) // SAR
                {
                    Reg.SetFlag(EFlags.CF, (val & 0x00000001) != 0);
                    val = (val >> 1) | (val & 0x80000000);
                }
                else if (op == 0) // ROL
                {
                    bool top = (val & 0x80000000) != 0;
                    val = (val << 1) | (top ? 1u : 0u);
                    Reg.SetFlag(EFlags.CF, top);
                }
                else if (op == 1) // ROR
                {
                    bool bot = (val & 0x00000001) != 0;
                    val = (val >> 1) | (bot ? 0x80000000u : 0u);
                    Reg.SetFlag(EFlags.CF, bot);
                }
            }
            Reg.UpdateZeroSignParity32(val);
            return val;
        }

        private byte ExecuteAlu8(int op, byte src, byte imm) => op switch
        {
            0 => Add8(src, imm),
            1 => Or8(src, imm),
            2 => Adc8(src, imm),
            3 => Sbb8(src, imm),
            4 => And8(src, imm),
            5 => Sub8(src, imm),
            6 => Xor8(src, imm),
            7 => Sub8(src, imm), // CMP
            _ => src
        };

        private ushort ExecuteAlu16(int op, ushort src, ushort imm) => op switch
        {
            0 => Add16(src, imm),
            1 => Or16(src, imm),
            2 => Adc16(src, imm),
            3 => Sbb16(src, imm),
            4 => And16(src, imm),
            5 => Sub16(src, imm),
            6 => Xor16(src, imm),
            7 => Sub16(src, imm), // CMP
            _ => src
        };

        private uint ExecuteAlu32(int op, uint src, uint imm) => op switch
        {
            0 => Add32(src, imm),
            1 => Or32(src, imm),
            2 => Adc32(src, imm),
            3 => Sbb32(src, imm),
            4 => And32(src, imm),
            5 => Sub32(src, imm),
            6 => Xor32(src, imm),
            7 => Sub32(src, imm), // CMP
            _ => src
        };

        private uint GetEA(int mod, int rm, bool addressSize32, SegmentRegister defaultSeg)
        {
            if (!addressSize32)
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
            else
            {
                uint offset;
                if (rm == 4) // SIB byte
                {
                    byte sib = Fetch8();
                    int scale = (sib >> 6) & 3;
                    int indexReg = (sib >> 3) & 7;
                    int baseReg = sib & 7;

                    uint baseVal = (baseReg == 5 && mod == 0) ? Fetch32() : Reg.GetGpr32(baseReg);
                    uint indexVal = (indexReg == 4) ? 0 : Reg.GetGpr32(indexReg);

                    offset = baseVal + (indexVal << scale);
                }
                else
                {
                    offset = rm switch
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
                }

                if (mod == 1) offset = (uint)(offset + (sbyte)Fetch8());
                else if (mod == 2) offset = (uint)(offset + (int)Fetch32());

                return LinearAddress(defaultSeg, offset);
            }
        }

        public void TriggerInterrupt(byte vector)
        {
            if (Reg.ProtectedMode)
            {
                Push32((uint)Reg.EFlags);
                Push32(Reg.CS.Selector);
                Push32(Reg.EIP);
            }
            else
            {
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

        private bool CheckCondition(int cond) => cond switch
        {
            0x0 => Reg.GetFlag(EFlags.OF),
            0x1 => !Reg.GetFlag(EFlags.OF),
            0x2 => Reg.GetFlag(EFlags.CF),
            0x3 => !Reg.GetFlag(EFlags.CF),
            0x4 => Reg.GetFlag(EFlags.ZF),
            0x5 => !Reg.GetFlag(EFlags.ZF),
            0x6 => Reg.GetFlag(EFlags.CF) || Reg.GetFlag(EFlags.ZF),
            0x7 => !Reg.GetFlag(EFlags.CF) && !Reg.GetFlag(EFlags.ZF),
            0x8 => Reg.GetFlag(EFlags.SF),
            0x9 => !Reg.GetFlag(EFlags.SF),
            0xA => Reg.GetFlag(EFlags.PF),
            0xB => !Reg.GetFlag(EFlags.PF),
            0xC => Reg.GetFlag(EFlags.SF) != Reg.GetFlag(EFlags.OF),
            0xD => Reg.GetFlag(EFlags.SF) == Reg.GetFlag(EFlags.OF),
            0xE => Reg.GetFlag(EFlags.ZF) || (Reg.GetFlag(EFlags.SF) != Reg.GetFlag(EFlags.OF)),
            0xF => !Reg.GetFlag(EFlags.ZF) && (Reg.GetFlag(EFlags.SF) == Reg.GetFlag(EFlags.OF)),
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

        private byte Adc8(byte a, byte b)
        {
            int c = Reg.GetFlag(EFlags.CF) ? 1 : 0;
            int res = a + b + c;
            Reg.SetFlag(EFlags.CF, res > 0xFF);
            Reg.SetFlag(EFlags.OF, ((a ^ res) & (b ^ res) & 0x80) != 0);
            byte r = (byte)res;
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Adc16(ushort a, ushort b)
        {
            int c = Reg.GetFlag(EFlags.CF) ? 1 : 0;
            int res = a + b + c;
            Reg.SetFlag(EFlags.CF, res > 0xFFFF);
            Reg.SetFlag(EFlags.OF, ((a ^ res) & (b ^ res) & 0x8000) != 0);
            ushort r = (ushort)res;
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Adc32(uint a, uint b)
        {
            ulong c = Reg.GetFlag(EFlags.CF) ? 1u : 0u;
            ulong res = (ulong)a + b + c;
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

        private byte Sbb8(byte a, byte b)
        {
            int c = Reg.GetFlag(EFlags.CF) ? 1 : 0;
            int res = a - b - c;
            Reg.SetFlag(EFlags.CF, a < (b + c));
            Reg.SetFlag(EFlags.OF, ((a ^ b) & (a ^ (byte)res) & 0x80) != 0);
            byte r = (byte)res;
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Sbb16(ushort a, ushort b)
        {
            int c = Reg.GetFlag(EFlags.CF) ? 1 : 0;
            int res = a - b - c;
            Reg.SetFlag(EFlags.CF, a < (b + c));
            Reg.SetFlag(EFlags.OF, ((a ^ b) & (a ^ (ushort)res) & 0x8000) != 0);
            ushort r = (ushort)res;
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Sbb32(uint a, uint b)
        {
            long c = Reg.GetFlag(EFlags.CF) ? 1 : 0;
            long res = (long)a - b - c;
            Reg.SetFlag(EFlags.CF, (ulong)a < ((ulong)b + (ulong)c));
            Reg.SetFlag(EFlags.OF, (((a ^ b) & (a ^ (uint)res) & 0x80000000)) != 0);
            uint r = (uint)res;
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private byte Or8(byte a, byte b)
        {
            byte r = (byte)(a | b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Or16(ushort a, ushort b)
        {
            ushort r = (ushort)(a | b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Or32(uint a, uint b)
        {
            uint r = a | b;
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private byte And8(byte a, byte b)
        {
            byte r = (byte)(a & b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort And16(ushort a, ushort b)
        {
            ushort r = (ushort)(a & b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint And32(uint a, uint b)
        {
            uint r = a & b;
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private byte Xor8(byte a, byte b)
        {
            byte r = (byte)(a ^ b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity8(r);
            return r;
        }

        private ushort Xor16(ushort a, ushort b)
        {
            ushort r = (ushort)(a ^ b);
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity16(r);
            return r;
        }

        private uint Xor32(uint a, uint b)
        {
            uint r = a ^ b;
            Reg.SetFlag(EFlags.CF, false);
            Reg.SetFlag(EFlags.OF, false);
            Reg.UpdateZeroSignParity32(r);
            return r;
        }

        private byte Inc8(byte a)
        {
            byte r = (byte)(a + 1);
            Reg.SetFlag(EFlags.OF, a == 0x7F);
            Reg.UpdateZeroSignParity8(r);
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

        private byte Dec8(byte a)
        {
            byte r = (byte)(a - 1);
            Reg.SetFlag(EFlags.OF, a == 0x80);
            Reg.UpdateZeroSignParity8(r);
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
