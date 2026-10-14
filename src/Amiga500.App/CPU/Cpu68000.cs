using System;
using Amiga500.Memory;

namespace Amiga500.CPU;

public class Cpu68000
{
    public uint[] D = new uint[8];
    public uint[] A = new uint[8];
    public uint PC;
    public ushort SR;
    public uint USP;
    public uint SSP;
    public bool Stopped;
    public bool Halted;

    public IMemoryBus Memory { get; set; }

    // Status Register Flag Bitmasks
    public const ushort FlagC = 0x0001; // Carry
    public const ushort FlagV = 0x0002; // Overflow
    public const ushort FlagZ = 0x0004; // Zero
    public const ushort FlagN = 0x0008; // Negative
    public const ushort FlagX = 0x0010; // Extend
    public const ushort FlagS = 0x2000; // Supervisor Mode
    public const ushort FlagT = 0x8000; // Trace

    public bool C { get => (SR & FlagC) != 0; set => SR = (ushort)(value ? (SR | FlagC) : (SR & ~FlagC)); }
    public bool V { get => (SR & FlagV) != 0; set => SR = (ushort)(value ? (SR | FlagV) : (SR & ~FlagV)); }
    public bool Z { get => (SR & FlagZ) != 0; set => SR = (ushort)(value ? (SR | FlagZ) : (SR & ~FlagZ)); }
    public bool N { get => (SR & FlagN) != 0; set => SR = (ushort)(value ? (SR | FlagN) : (SR & ~FlagN)); }
    public bool X { get => (SR & FlagX) != 0; set => SR = (ushort)(value ? (SR | FlagX) : (SR & ~FlagX)); }
    public bool Supervisor
    {
        get => (SR & FlagS) != 0;
        set
        {
            if (Supervisor != value)
            {
                if (value)
                {
                    USP = A[7];
                    A[7] = SSP;
                    SR |= FlagS;
                }
                else
                {
                    SSP = A[7];
                    A[7] = USP;
                    SR = (ushort)(SR & ~FlagS);
                }
            }
        }
    }

    public int InterruptMask
    {
        get => (SR >> 8) & 0x07;
        set => SR = (ushort)((SR & ~0x0700) | ((value & 0x07) << 8));
    }

    public int PendingInterruptLevel { get; set; } = 0;
    public byte PendingInterruptVector { get; set; } = 0; // 0 = Auto-vector

    public Cpu68000(IMemoryBus memory)
    {
        Memory = memory;
    }

    public void Reset()
    {
        SR = FlagS | 0x0700; // Supervisor mode, Interrupts disabled (level 7 mask)
        SSP = Memory.ReadLong(0);
        A[7] = SSP;
        PC = Memory.ReadLong(4);
        Stopped = false;
        Halted = false;
    }

    public ushort FetchWord()
    {
        ushort val = Memory.ReadWord(PC);
        PC += 2;
        return val;
    }

    public uint FetchLong()
    {
        uint val = Memory.ReadLong(PC);
        PC += 4;
        return val;
    }

    public void PushWord(ushort value)
    {
        A[7] -= 2;
        Memory.WriteWord(A[7], value);
    }

    public void PushLong(uint value)
    {
        A[7] -= 4;
        Memory.WriteLong(A[7], value);
    }

    public ushort PopWord()
    {
        ushort val = Memory.ReadWord(A[7]);
        A[7] += 2;
        return val;
    }

    public uint PopLong()
    {
        uint val = Memory.ReadLong(A[7]);
        A[7] += 4;
        return val;
    }

    public void TriggerException(int vector)
    {
        Stopped = false;
        ushort oldSR = SR;
        Supervisor = true;

        PushLong(PC);
        PushWord(oldSR);

        uint vectorAddress = (uint)(vector * 4);
        PC = Memory.ReadLong(vectorAddress);
    }

    public void CheckInterrupts()
    {
        if (PendingInterruptLevel > InterruptMask || PendingInterruptLevel == 7)
        {
            int level = PendingInterruptLevel;
            int vector = (PendingInterruptVector != 0) ? PendingInterruptVector : (24 + level);
            ushort oldSR = SR;
            Supervisor = true;
            InterruptMask = level;

            PushLong(PC);
            PushWord(oldSR);

            uint vectorAddress = (uint)(vector * 4);
            PC = Memory.ReadLong(vectorAddress);
            Stopped = false;
        }
    }

    public int ExecuteInstruction()
    {
        if (Halted) return 4;

        if (PendingInterruptLevel > InterruptMask || (PendingInterruptLevel == 7 && PendingInterruptLevel > 0))
        {
            CheckInterrupts();
            return 44;
        }

        if (Stopped) return 4;

        uint opcodePC = PC;
        ushort opcode = FetchWord();

        return DecodeAndExecute(opcode, opcodePC);
    }

    private int DecodeAndExecute(ushort opcode, uint opcodePC)
    {
        ushort topNibble = (ushort)(opcode >> 12);

        switch (topNibble)
        {
            case 0x0: return ExecuteGroup0(opcode);
            case 0x1: return ExecuteMove(opcode, 1);
            case 0x2: return ExecuteMove(opcode, 4);
            case 0x3: return ExecuteMove(opcode, 2);
            case 0x4: return ExecuteGroup4(opcode);
            case 0x5: return ExecuteGroup5(opcode, opcodePC);
            case 0x6: return ExecuteGroup6(opcode, opcodePC);
            case 0x7: return ExecuteMoveq(opcode);
            case 0x8: return ExecuteGroup8(opcode);
            case 0x9: return ExecuteGroup9(opcode);
            case 0xB: return ExecuteGroupB(opcode);
            case 0xC: return ExecuteGroupC(opcode);
            case 0xD: return ExecuteGroupD(opcode);
            case 0xE: return ExecuteShiftRotate(opcode);
            default:
                TriggerException(4);
                return 34;
        }
    }

    private int ExecuteGroup0(ushort opcode)
    {
        if (opcode == 0x003C) { // ORI to CCR
            ushort imm = FetchWord();
            SR = (ushort)((SR & 0xFF00) | ((SR | imm) & 0x00FF));
            return 20;
        }
        if (opcode == 0x007C) { // ORI to SR
            ushort imm = FetchWord();
            if (!Supervisor) { TriggerException(8); return 34; }
            SR |= imm;
            return 20;
        }
        if ((opcode & 0x01C0) == 0x0180) { // MOVEP
            return ExecuteMovep(opcode);
        }
        if ((opcode & 0xFF00) == 0x0200) return ExecuteAndi(opcode);
        if ((opcode & 0xFF00) == 0x0000) return ExecuteOri(opcode);
        if ((opcode & 0xFF00) == 0x0A00) return ExecuteEori(opcode);
        if ((opcode & 0xFF00) == 0x0C00) return ExecuteCmpi(opcode);
        if ((opcode & 0xFF00) == 0x0600) return ExecuteAddi(opcode);
        if ((opcode & 0xFF00) == 0x0400) return ExecuteSubi(opcode);

        return ExecuteBitOp(opcode);
    }

    private int ExecuteMovep(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        bool toMem = (opcode & 0x0080) != 0;
        bool isLong = (opcode & 0x0040) != 0;
        int areg = opcode & 7;
        short disp = (short)FetchWord();
        uint addr = (uint)(A[areg] + disp);

        if (toMem)
        {
            if (isLong)
            {
                Memory.WriteByte(addr, (byte)(D[reg] >> 24));
                Memory.WriteByte(addr + 2, (byte)(D[reg] >> 16));
                Memory.WriteByte(addr + 4, (byte)(D[reg] >> 8));
                Memory.WriteByte(addr + 6, (byte)D[reg]);
            }
            else
            {
                Memory.WriteByte(addr, (byte)(D[reg] >> 8));
                Memory.WriteByte(addr + 2, (byte)D[reg]);
            }
        }
        else
        {
            if (isLong)
            {
                uint b0 = Memory.ReadByte(addr);
                uint b1 = Memory.ReadByte(addr + 2);
                uint b2 = Memory.ReadByte(addr + 4);
                uint b3 = Memory.ReadByte(addr + 6);
                D[reg] = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
            }
            else
            {
                uint b0 = Memory.ReadByte(addr);
                uint b1 = Memory.ReadByte(addr + 2);
                D[reg] = (D[reg] & 0xFFFF0000) | (b0 << 8) | b1;
            }
        }
        return 24;
    }

    private int ExecuteMove(ushort opcode, int sizeCode)
    {
        int dstMode = (opcode >> 6) & 7;
        int dstReg = (opcode >> 9) & 7;
        int srcMode = (opcode >> 3) & 7;
        int srcReg = opcode & 7;

        if (dstMode == 1)
        {
            uint srcVal = ReadEa(srcMode, srcReg, sizeCode == 2 ? 2 : 4);
            if (sizeCode == 2) srcVal = (uint)(short)srcVal;
            A[dstReg] = srcVal;
            return 4;
        }

        uint val = ReadEa(srcMode, srcReg, sizeCode);
        WriteEa(dstMode, dstReg, sizeCode, val);
        SetNzFlags(val, sizeCode);
        V = false;
        C = false;
        return 4 + GetEaCycles(srcMode, sizeCode) + GetEaCycles(dstMode, sizeCode);
    }

    private int ExecuteMoveq(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        sbyte imm = (sbyte)(opcode & 0xFF);
        D[reg] = (uint)(int)imm;
        SetNzFlags(D[reg], 4);
        V = false; C = false;
        return 4;
    }

    private int ExecuteGroup4(ushort opcode)
    {
        if ((opcode & 0xF1C0) == 0x41C0) { // LEA
            int reg = (opcode >> 9) & 7;
            int eaMode = (opcode >> 3) & 7;
            int eaReg = opcode & 7;
            A[reg] = GetEaAddress(eaMode, eaReg);
            return 4;
        }
        if ((opcode & 0xFFF8) == 0x4840) { // SWAP
            int reg = opcode & 7;
            uint val = D[reg];
            D[reg] = (val << 16) | (val >> 16);
            SetNzFlags(D[reg], 4);
            V = false; C = false;
            return 4;
        }
        if ((opcode & 0xFFF8) == 0x4E50) { // LINK
            int reg = opcode & 7;
            short disp = (short)FetchWord();
            PushLong(A[reg]);
            A[reg] = A[7];
            A[7] = (uint)(A[7] + disp);
            return 16;
        }
        if ((opcode & 0xFFF8) == 0x4E58) { // UNLK
            int reg = opcode & 7;
            A[7] = A[reg];
            A[reg] = PopLong();
            return 12;
        }
        if ((opcode & 0xFFC0) == 0x4AC0) { // TAS
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint addr = ResolveEaAddress(mode, reg, 1);
            byte val = (byte)ReadEaAddress(mode, reg, 1, addr);
            SetNzFlags(val, 1);
            V = false; C = false;
            val |= 0x80;
            WriteEaAddress(mode, reg, 1, addr, val);
            return 12;
        }
        if ((opcode & 0xFFB8) == 0x4800) { // EXT (.W or .L)
            int reg = opcode & 7;
            if ((opcode & 0x0040) != 0) {
                D[reg] = (uint)(short)D[reg];
                SetNzFlags(D[reg], 4);
            } else {
                ushort w = (ushort)(sbyte)(byte)D[reg];
                D[reg] = (D[reg] & 0xFFFF0000) | w;
                SetNzFlags(w, 2);
            }
            V = false; C = false;
            return 4;
        }
        if ((opcode & 0xFFC0) == 0x4880 || (opcode & 0xFFC0) == 0x48C0) return ExecuteMovemToEa(opcode);
        if ((opcode & 0xFFC0) == 0x4C80 || (opcode & 0xFFC0) == 0x4CC0) return ExecuteMovemFromEa(opcode);
        if ((opcode & 0xFF00) == 0x4200) { // CLR
            int size = GetSizeFromOpcode(opcode);
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            WriteEa(mode, reg, size, 0);
            N = false; Z = true; V = false; C = false;
            return 4;
        }
        if ((opcode & 0xFF00) == 0x4400) { // NEG
            int size = GetSizeFromOpcode(opcode);
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint addr = ResolveEaAddress(mode, reg, size);
            uint src = ReadEaAddress(mode, reg, size, addr);
            uint res = SubWithFlags(0, src, size);
            WriteEaAddress(mode, reg, size, addr, res);
            return 6;
        }
        if ((opcode & 0xFF00) == 0x4600) { // NOT
            int size = GetSizeFromOpcode(opcode);
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint addr = ResolveEaAddress(mode, reg, size);
            uint src = ReadEaAddress(mode, reg, size, addr);
            uint res = ~src;
            WriteEaAddress(mode, reg, size, addr, res);
            SetNzFlags(res, size);
            V = false; C = false;
            return 6;
        }
        if ((opcode & 0xFF00) == 0x4A00) { // TST
            int size = GetSizeFromOpcode(opcode);
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint src = ReadEa(mode, reg, size);
            SetNzFlags(src, size);
            V = false; C = false;
            return 4;
        }
        if ((opcode & 0xFFC0) == 0x4E80) { // JSR
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint target = GetEaAddress(mode, reg);
            PushLong(PC);
            PC = target;
            return 18;
        }
        if ((opcode & 0xFFC0) == 0x4EC0) { // JMP
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            PC = GetEaAddress(mode, reg);
            return 10;
        }
        if (opcode == 0x4E71) return 4; // NOP
        if (opcode == 0x4E75) { PC = PopLong(); return 16; } // RTS
        if (opcode == 0x4E76) { if (V) TriggerException(7); return 4; } // TRAPV
        if (opcode == 0x4E73) { // RTE - PREVENT STACK CORRUPTION: POP SR AND PC BEFORE ASSIGNING SR
            if (!Supervisor) { TriggerException(8); return 34; }
            ushort newSR = PopWord();
            uint newPC = PopLong();
            SR = newSR;
            PC = newPC;
            return 20;
        }
        if (opcode == 0x4E77) { // RTR
            ushort ccr = PopWord();
            uint newPC = PopLong();
            SR = (ushort)((SR & 0xFF00) | (ccr & 0x00FF));
            PC = newPC;
            return 20;
        }
        if (opcode == 0x4E70) {
            if (!Supervisor) { TriggerException(8); return 34; }
            return 132;
        }
        if (opcode == 0x4E72) {
            if (!Supervisor) { TriggerException(8); return 34; }
            SR = FetchWord();
            Stopped = true;
            return 4;
        }
        if ((opcode & 0xFFF0) == 0x4E40) {
            int vector = 32 + (opcode & 0x0F);
            TriggerException(vector);
            return 34;
        }
        if ((opcode & 0xFFF8) == 0x4E60) {
            if (!Supervisor) { TriggerException(8); return 34; }
            A[opcode & 7] = USP;
            return 4;
        }
        if ((opcode & 0xFFF8) == 0x4E68) {
            if (!Supervisor) { TriggerException(8); return 34; }
            USP = A[opcode & 7];
            return 4;
        }
        if ((opcode & 0xFFC0) == 0x4840) { // PEA
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            uint addr = GetEaAddress(mode, reg);
            PushLong(addr);
            return 12;
        }

        TriggerException(4);
        return 34;
    }

    private int ExecuteGroup5(ushort opcode, uint opcodePC)
    {
        if ((opcode & 0x00C0) == 0x00C0) {
            int condition = (opcode >> 8) & 0x0F;
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;
            bool condVal = TestCondition(condition);

            if (mode == 1) { // DBcc
                short disp = (short)FetchWord();
                if (!condVal) {
                    ushort count = (ushort)(D[reg] & 0xFFFF);
                    count--;
                    D[reg] = (D[reg] & 0xFFFF0000) | count;
                    if (count != 0xFFFF) {
                        PC = (uint)(opcodePC + 2 + disp);
                        return 10;
                    }
                }
                return 12;
            } else { // Scc
                WriteEa(mode, reg, 1, condVal ? (byte)0xFF : (byte)0x00);
                return condVal ? 8 : 10;
            }
        }
        else { // ADDQ / SUBQ
            uint val = (uint)((opcode >> 9) & 7);
            if (val == 0) val = 8;
            int size = GetSizeFromOpcode(opcode);
            bool isSub = (opcode & 0x0100) != 0;
            int mode = (opcode >> 3) & 7;
            int reg = opcode & 7;

            if (mode == 1) {
                if (isSub) A[reg] -= val;
                else A[reg] += val;
                return 8;
            }

            uint addr = ResolveEaAddress(mode, reg, size);
            uint src = ReadEaAddress(mode, reg, size, addr);
            uint res = isSub ? SubWithFlags(src, val, size) : AddWithFlags(src, val, size);
            WriteEaAddress(mode, reg, size, addr, res);
            return 8;
        }
    }

    private int ExecuteGroup6(ushort opcode, uint opcodePC)
    {
        int condition = (opcode >> 8) & 0x0F;
        sbyte disp8 = (sbyte)(opcode & 0xFF);
        int disp = disp8;
        if (disp8 == 0) disp = (short)FetchWord();

        bool branch = false;
        if (condition == 0) branch = true;
        else if (condition == 1) {
            PushLong(PC);
            branch = true;
        } else {
            branch = TestCondition(condition);
        }

        if (branch) {
            PC = (uint)(opcodePC + 2 + disp);
            return 10;
        }
        return 8;
    }

    private int ExecuteGroup8(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        int opmode = (opcode >> 6) & 7;
        int mode = (opcode >> 3) & 7;
        int eaReg = opcode & 7;

        if (opmode == 3) {
            uint src = ReadEa(mode, eaReg, 2);
            if (src == 0) { TriggerException(5); return 38; }
            uint dividend = D[reg];
            uint quot = dividend / src;
            uint rem = dividend % src;
            if (quot > 0xFFFF) { V = true; }
            else {
                D[reg] = (((uint)rem & 0xFFFFu) << 16) | ((uint)quot & 0xFFFFu);
                SetNzFlags((ushort)quot, 2);
                V = false; C = false;
            }
            return 140;
        }
        if (opmode == 7) {
            short src = (short)ReadEa(mode, eaReg, 2);
            if (src == 0) { TriggerException(5); return 38; }
            int dividend = (int)D[reg];
            int quot = dividend / src;
            int rem = dividend % src;
            if (quot > 32767 || quot < -32768) { V = true; }
            else {
                D[reg] = (((uint)rem & 0xFFFFu) << 16) | ((uint)quot & 0xFFFFu);
                SetNzFlags((ushort)(short)quot, 2);
                V = false; C = false;
            }
            return 158;
        }

        int size = GetSizeFromOpMode(opmode);
        uint addr = ResolveEaAddress(mode, eaReg, size);
        uint srcVal = ReadEaAddress(mode, eaReg, size, addr);
        if ((opmode & 4) == 0) {
            D[reg] = ApplyBitwiseOr(D[reg], srcVal, size);
            SetNzFlags(D[reg], size);
        } else {
            uint res = ApplyBitwiseOr(srcVal, D[reg], size);
            WriteEaAddress(mode, eaReg, size, addr, res);
            SetNzFlags(res, size);
        }
        V = false; C = false;
        return 4;
    }

    private int ExecuteGroup9(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        int opmode = (opcode >> 6) & 7;
        int mode = (opcode >> 3) & 7;
        int eaReg = opcode & 7;

        if (opmode == 3 || opmode == 7) {
            int size = opmode == 3 ? 2 : 4;
            uint src = ReadEa(mode, eaReg, size);
            if (size == 2) src = (uint)(short)src;
            A[reg] -= src;
            return 8;
        }

        int sizeVal = GetSizeFromOpMode(opmode);

        // Check ADDX / SUBX
        if ((opmode & 4) != 0 && (opcode & 0x0030) == 0x0000)
        {
            // SUBX
            bool memMode = (opcode & 0x0008) != 0;
            uint dst = memMode ? ReadEa(4, reg, sizeVal) : D[reg] & GetMask(sizeVal);
            uint src = memMode ? ReadEa(4, eaReg, sizeVal) : D[eaReg] & GetMask(sizeVal);
            uint mask = GetMask(sizeVal);
            uint subx = (src + (X ? 1u : 0u)) & mask;
            uint res = (dst - subx) & mask;
            bool borrow = dst < subx;
            uint signBit = 1u << (sizeVal * 8 - 1);
            bool overflow = (((dst ^ src) & (dst ^ res) & signBit) != 0);

            if (res != 0) Z = false;
            N = (res & signBit) != 0;
            V = overflow; C = borrow; X = borrow;

            if (memMode) WriteEa(4, reg, sizeVal, res);
            else D[reg] = SetBitRange(D[reg], res, sizeVal);
            return 6;
        }

        uint addr = ResolveEaAddress(mode, eaReg, sizeVal);
        uint srcVal = ReadEaAddress(mode, eaReg, sizeVal, addr);
        if ((opmode & 4) == 0) {
            D[reg] = SubWithFlags(D[reg], srcVal, sizeVal);
        } else {
            uint res = SubWithFlags(srcVal, D[reg], sizeVal);
            WriteEaAddress(mode, eaReg, sizeVal, addr, res);
        }
        return 4;
    }

    private int ExecuteGroupB(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        int opmode = (opcode >> 6) & 7;
        int mode = (opcode >> 3) & 7;
        int eaReg = opcode & 7;

        if (opmode == 3 || opmode == 7) {
            int size = opmode == 3 ? 2 : 4;
            uint src = ReadEa(mode, eaReg, size);
            if (size == 2) src = (uint)(short)src;
            SubWithFlags(A[reg], src, 4, updateX: false);
            return 6;
        }

        int sizeVal = GetSizeFromOpMode(opmode);
        if ((opmode & 4) == 0) { // CMP
            uint addr = ResolveEaAddress(mode, eaReg, sizeVal);
            uint srcVal = ReadEaAddress(mode, eaReg, sizeVal, addr);
            SubWithFlags(D[reg], srcVal, sizeVal, updateX: false);
        } else {
            if (mode == 1) { // CMPM (Ay)+, (Ax)+
                uint srcVal = ReadEa(3, eaReg, sizeVal);
                uint dstVal = ReadEa(3, reg, sizeVal);
                SubWithFlags(dstVal, srcVal, sizeVal, updateX: false);
            } else { // EOR
                uint addr = ResolveEaAddress(mode, eaReg, sizeVal);
                uint srcVal = ReadEaAddress(mode, eaReg, sizeVal, addr);
                uint res = ApplyBitwiseEor(srcVal, D[reg], sizeVal);
                WriteEaAddress(mode, eaReg, sizeVal, addr, res);
                SetNzFlags(res, sizeVal);
                V = false; C = false;
            }
        }
        return 4;
    }

    private int ExecuteGroupC(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        int opmode = (opcode >> 6) & 7;
        int mode = (opcode >> 3) & 7;
        int eaReg = opcode & 7;

        if (opmode == 3) {
            ushort src = (ushort)ReadEa(mode, eaReg, 2);
            ushort dst = (ushort)D[reg];
            uint res = (uint)src * dst;
            D[reg] = res;
            SetNzFlags(res, 4);
            V = false; C = false;
            return 70;
        }
        if (opmode == 7) {
            short src = (short)ReadEa(mode, eaReg, 2);
            short dst = (short)D[reg];
            int res = src * dst;
            D[reg] = (uint)res;
            SetNzFlags((uint)res, 4);
            V = false; C = false;
            return 70;
        }
        if (opmode == 4 && mode == 1) {
            int reg2 = opcode & 7;
            uint tmp = D[reg]; D[reg] = D[reg2]; D[reg2] = tmp;
            return 6;
        }

        int size = GetSizeFromOpMode(opmode);
        uint addr = ResolveEaAddress(mode, eaReg, size);
        uint srcVal = ReadEaAddress(mode, eaReg, size, addr);
        if ((opmode & 4) == 0) {
            D[reg] = ApplyBitwiseAnd(D[reg], srcVal, size);
            SetNzFlags(D[reg], size);
        } else {
            uint res = ApplyBitwiseAnd(srcVal, D[reg], size);
            WriteEaAddress(mode, eaReg, size, addr, res);
            SetNzFlags(res, size);
        }
        V = false; C = false;
        return 4;
    }

    private int ExecuteGroupD(ushort opcode)
    {
        int reg = (opcode >> 9) & 7;
        int opmode = (opcode >> 6) & 7;
        int mode = (opcode >> 3) & 7;
        int eaReg = opcode & 7;

        if (opmode == 3 || opmode == 7) {
            int size = opmode == 3 ? 2 : 4;
            uint src = ReadEa(mode, eaReg, size);
            if (size == 2) src = (uint)(short)src;
            A[reg] += src;
            return 8;
        }

        int sizeVal = GetSizeFromOpMode(opmode);

        // Check ADDX
        if ((opmode & 4) != 0 && (opcode & 0x0030) == 0x0000)
        {
            bool memMode = (opcode & 0x0008) != 0;
            uint dst = memMode ? ReadEa(4, reg, sizeVal) : D[reg] & GetMask(sizeVal);
            uint src = memMode ? ReadEa(4, eaReg, sizeVal) : D[eaReg] & GetMask(sizeVal);
            uint mask = GetMask(sizeVal);
            uint addx = (src + (X ? 1u : 0u)) & mask;
            uint res = (dst + addx) & mask;
            bool carry = (dst + addx) > mask;
            uint signBit = 1u << (sizeVal * 8 - 1);
            bool overflow = (((dst ^ res) & (src ^ res) & signBit) != 0);

            if (res != 0) Z = false;
            N = (res & signBit) != 0;
            V = overflow; C = carry; X = carry;

            if (memMode) WriteEa(4, reg, sizeVal, res);
            else D[reg] = SetBitRange(D[reg], res, sizeVal);
            return 6;
        }

        uint addr = ResolveEaAddress(mode, eaReg, sizeVal);
        uint srcVal = ReadEaAddress(mode, eaReg, sizeVal, addr);
        if ((opmode & 4) == 0) {
            D[reg] = AddWithFlags(D[reg], srcVal, sizeVal);
        } else {
            uint res = AddWithFlags(srcVal, D[reg], sizeVal);
            WriteEaAddress(mode, eaReg, sizeVal, addr, res);
        }
        return 4;
    }

    private int ExecuteAndi(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        if (mode == 7 && reg == 4) {
            SR = (ushort)((SR & 0xFF00) | (SR & imm & 0x00FF));
            return 20;
        }

        uint addr = ResolveEaAddress(mode, reg, size);
        uint dst = ReadEaAddress(mode, reg, size, addr);
        uint res = dst & imm;
        WriteEaAddress(mode, reg, size, addr, res);
        SetNzFlags(res, size);
        V = false; C = false;
        return 8;
    }

    private int ExecuteOri(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        uint addr = ResolveEaAddress(mode, reg, size);
        uint dst = ReadEaAddress(mode, reg, size, addr);
        uint res = dst | imm;
        WriteEaAddress(mode, reg, size, addr, res);
        SetNzFlags(res, size);
        V = false; C = false;
        return 8;
    }

    private int ExecuteEori(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        uint addr = ResolveEaAddress(mode, reg, size);
        uint dst = ReadEaAddress(mode, reg, size, addr);
        uint res = dst ^ imm;
        WriteEaAddress(mode, reg, size, addr, res);
        SetNzFlags(res, size);
        V = false; C = false;
        return 8;
    }

    private int ExecuteCmpi(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        uint dst = ReadEa(mode, reg, size);
        SubWithFlags(dst, imm, size, updateX: false);
        return 8;
    }

    private int ExecuteAddi(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        uint addr = ResolveEaAddress(mode, reg, size);
        uint dst = ReadEaAddress(mode, reg, size, addr);
        uint res = AddWithFlags(dst, imm, size);
        WriteEaAddress(mode, reg, size, addr, res);
        return 8;
    }

    private int ExecuteSubi(ushort opcode)
    {
        int size = GetSizeFromOpcode(opcode);
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        uint imm = size switch { 1 => FetchWord() & 0xFFu, 2 => FetchWord(), 4 => FetchLong(), _ => 0 };

        uint addr = ResolveEaAddress(mode, reg, size);
        uint dst = ReadEaAddress(mode, reg, size, addr);
        uint res = SubWithFlags(dst, imm, size);
        WriteEaAddress(mode, reg, size, addr, res);
        return 8;
    }

    private int ExecuteBitOp(ushort opcode)
    {
        bool staticBit = (opcode & 0x0100) == 0;
        int op = (opcode >> 6) & 3;
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;

        uint bitNum = staticBit ? (uint)(FetchWord() & 0x1F) : (D[(opcode >> 9) & 7] & 0x1F);
        int size = (mode == 0) ? 4 : 1;
        if (mode != 0) bitNum &= 7;

        uint addr = ResolveEaAddress(mode, reg, size);
        uint val = ReadEaAddress(mode, reg, size, addr);
        bool bitState = (val & (1u << (int)bitNum)) != 0;
        Z = !bitState;

        if (op == 1) val ^= (1u << (int)bitNum);
        else if (op == 2) val &= ~(1u << (int)bitNum);
        else if (op == 3) val |= (1u << (int)bitNum);

        if (op != 0) WriteEaAddress(mode, reg, size, addr, val);
        return 8;
    }

    private int ExecuteMovemToEa(ushort opcode)
    {
        int size = ((opcode & 0x0040) != 0) ? 4 : 2;
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        ushort mask = FetchWord();
        uint addr = GetEaAddress(mode, reg);

        if (mode == 4) {
            for (int i = 0; i < 16; i++) {
                if ((mask & (1 << i)) != 0) {
                    addr -= (uint)size;
                    int regIdx = 15 - i;
                    uint val = (regIdx < 8) ? D[regIdx] : A[regIdx - 8];
                    if (size == 2) Memory.WriteWord(addr, (ushort)val);
                    else Memory.WriteLong(addr, val);
                }
            }
            A[reg] = addr;
        } else {
            for (int i = 0; i < 16; i++) {
                if ((mask & (1 << i)) != 0) {
                    uint val = (i < 8) ? D[i] : A[i - 8];
                    if (size == 2) Memory.WriteWord(addr, (ushort)val);
                    else Memory.WriteLong(addr, val);
                    addr += (uint)size;
                }
            }
        }
        return 12;
    }

    private int ExecuteMovemFromEa(ushort opcode)
    {
        int size = ((opcode & 0x0040) != 0) ? 4 : 2;
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        ushort mask = FetchWord();
        uint addr = GetEaAddress(mode, reg);

        for (int i = 0; i < 16; i++) {
            if ((mask & (1 << i)) != 0) {
                uint val = (size == 2) ? (uint)(short)Memory.ReadWord(addr) : Memory.ReadLong(addr);
                if (i < 8) D[i] = val;
                else A[i - 8] = val;
                addr += (uint)size;
            }
        }
        if (mode == 3) A[reg] = addr;
        return 12;
    }

    private int ExecuteShiftRotate(ushort opcode)
    {
        int mode = (opcode >> 3) & 7;
        int reg = opcode & 7;
        int size = GetSizeFromOpcode(opcode);
        bool isMemoryShift = (opcode & 0x0020) == 0 && size == 3;

        if (isMemoryShift)
        {
            size = 2; // Memory shift is always 1 word
            int shiftTypeMem = (opcode >> 9) & 3;
            bool left = (opcode & 0x0100) != 0;
            uint addrMem = ResolveEaAddress(mode, reg, size);
            uint valMem = ReadEaAddress(mode, reg, size, addrMem);

            if (left)
            {
                bool msb = (valMem & 0x8000) != 0;
                valMem = (valMem << 1) & 0xFFFF;
                C = msb; X = msb;
            }
            else
            {
                bool lsb = (valMem & 1) != 0;
                if (shiftTypeMem == 0) // ASR
                {
                    ushort msbBit = (ushort)(valMem & 0x8000);
                    valMem = ((valMem >> 1) | msbBit) & 0xFFFF;
                }
                else
                {
                    valMem = (valMem >> 1) & 0xFFFF;
                }
                C = lsb; X = lsb;
            }

            WriteEaAddress(mode, reg, size, addrMem, valMem);
            SetNzFlags(valMem, size);
            V = false;
            return 8;
        }

        int shiftType = (opcode >> 3) & 3;
        bool dr = (opcode & 0x0100) != 0;
        bool ir = (opcode & 0x0020) != 0;

        uint count = ir ? (D[(opcode >> 9) & 7] & 63) : (uint)((opcode >> 9) & 7);
        if (!ir && count == 0) count = 8;

        uint addr = ResolveEaAddress(mode, reg, size);
        uint val = ReadEaAddress(mode, reg, size, addr);

        if (dr) {
            for (uint i = 0; i < count; i++) {
                bool msb = (val & (1u << (size * 8 - 1))) != 0;
                val <<= 1;
                C = msb;
                X = msb;
            }
        } else {
            for (uint i = 0; i < count; i++) {
                bool lsb = (val & 1) != 0;
                if (shiftType == 0) {
                    uint msbBit = val & (1u << (size * 8 - 1));
                    val = (val >> 1) | msbBit;
                } else {
                    val >>= 1;
                }
                C = lsb;
                X = lsb;
            }
        }

        val = MaskToSize(val, size);
        WriteEaAddress(mode, reg, size, addr, val);
        SetNzFlags(val, size);
        V = false;
        return 6;
    }

    private bool TestCondition(int condition) => condition switch
    {
        0 => true,
        1 => false,
        2 => !C && !Z,
        3 => C || Z,
        4 => !C,
        5 => C,
        6 => !Z,
        7 => Z,
        8 => !V,
        9 => V,
        10 => !N,
        11 => N,
        12 => (N && V) || (!N && !V),
        13 => (N && !V) || (!N && V),
        14 => (N && V && !Z) || (!N && !V && !Z),
        15 => Z || (N && !V) || (!N && V),
        _ => false
    };

    private uint ResolveEaAddress(int mode, int reg, int size) => mode switch
    {
        2 => A[reg],
        3 => PostIncAddr(reg, size),
        4 => PreDecAddr(reg, size),
        5 => A[reg] + (uint)(short)FetchWord(),
        6 => GetIndexAddress(A[reg]),
        7 => reg switch
        {
            0 => (uint)(short)FetchWord(),
            1 => FetchLong(),
            2 => (uint)(PC + (short)FetchWord()),
            3 => GetIndexAddress(PC),
            _ => 0
        },
        _ => 0
    };

    private uint PostIncAddr(int reg, int size)
    {
        uint addr = A[reg];
        A[reg] += (uint)(reg == 7 && size == 1 ? 2 : size);
        return addr;
    }

    private uint PreDecAddr(int reg, int size)
    {
        A[reg] -= (uint)(reg == 7 && size == 1 ? 2 : size);
        return A[reg];
    }

    private uint ReadEa(int mode, int reg, int size)
    {
        if (mode == 0) return MaskToSize(D[reg], size);
        if (mode == 1) return MaskToSize(A[reg], size);
        if (mode == 7 && reg == 4) return FetchImmediate(size);

        uint addr = ResolveEaAddress(mode, reg, size);
        return ReadMemory(addr, size);
    }

    private uint ReadEaAddress(int mode, int reg, int size, uint addr)
    {
        if (mode == 0) return MaskToSize(D[reg], size);
        if (mode == 1) return MaskToSize(A[reg], size);
        if (mode == 7 && reg == 4) return FetchImmediate(size);

        return ReadMemory(addr, size);
    }

    private void WriteEa(int mode, int reg, int size, uint value)
    {
        if (mode == 0) { D[reg] = SetBitRange(D[reg], value, size); return; }
        if (mode == 1) { A[reg] = (size == 2) ? (uint)(short)value : value; return; }

        uint addr = ResolveEaAddress(mode, reg, size);
        WriteMemory(addr, value, size);
    }

    private void WriteEaAddress(int mode, int reg, int size, uint addr, uint value)
    {
        if (mode == 0) D[reg] = SetBitRange(D[reg], value, size);
        else if (mode == 1) A[reg] = (size == 2) ? (uint)(short)value : value;
        else WriteMemory(addr, value, size);
    }

    private uint GetEaAddress(int mode, int reg) => ResolveEaAddress(mode, reg, 4);

    private uint GetIndexAddress(uint baseAddr)
    {
        ushort ext = FetchWord();
        bool isAddressReg = (ext & 0x8000) != 0;
        int idxReg = (ext >> 12) & 7;
        bool isLong = (ext & 0x0800) != 0;
        sbyte disp = (sbyte)(ext & 0xFF);

        uint idxVal = isAddressReg ? A[idxReg] : D[idxReg];
        if (!isLong) idxVal = (uint)(short)idxVal;

        return (uint)(baseAddr + idxVal + disp);
    }

    private uint ReadMemory(uint addr, int size)
    {
        if (size > 1 && (addr & 1) != 0)
        {
            TriggerException(3); // Address Error Exception Vector 3
            return 0;
        }

        return size switch
        {
            1 => Memory.ReadByte(addr),
            2 => Memory.ReadWord(addr),
            4 => Memory.ReadLong(addr),
            _ => 0
        };
    }

    private void WriteMemory(uint addr, uint val, int size)
    {
        if (size > 1 && (addr & 1) != 0)
        {
            TriggerException(3); // Address Error Exception Vector 3
            return;
        }

        switch (size)
        {
            case 1: Memory.WriteByte(addr, (byte)val); break;
            case 2: Memory.WriteWord(addr, (ushort)val); break;
            case 4: Memory.WriteLong(addr, val); break;
        }
    }

    private uint FetchImmediate(int size) => size switch
    {
        1 => FetchWord() & 0xFFu,
        2 => FetchWord(),
        4 => FetchLong(),
        _ => 0
    };

    private uint AddWithFlags(uint a, uint b, int size)
    {
        uint mask = GetMask(size);
        a &= mask;
        b &= mask;

        uint res = (a + b) & mask;
        uint signBit = 1u << (size * 8 - 1);

        bool carry = (a + b) > mask;
        bool overflow = (((a ^ res) & (b ^ res) & signBit) != 0);

        SetNzFlags(res, size);
        V = overflow;
        C = carry;
        X = carry;
        return res;
    }

    private uint SubWithFlags(uint a, uint b, int size, bool updateX = true)
    {
        uint mask = GetMask(size);
        a &= mask;
        b &= mask;

        uint res = (a - b) & mask;
        uint signBit = 1u << (size * 8 - 1);

        bool borrow = a < b;
        bool overflow = (((a ^ b) & (a ^ res) & signBit) != 0);

        SetNzFlags(res, size);
        V = overflow;
        C = borrow;
        if (updateX) X = borrow;
        return res;
    }

    private uint ApplyBitwiseAnd(uint a, uint b, int size)
    {
        uint mask = GetMask(size);
        return (a & b) & mask;
    }

    private uint ApplyBitwiseOr(uint a, uint b, int size)
    {
        uint mask = GetMask(size);
        return (a | b) & mask;
    }

    private uint ApplyBitwiseEor(uint a, uint b, int size)
    {
        uint mask = GetMask(size);
        return (a ^ b) & mask;
    }

    private void SetNzFlags(uint value, int size)
    {
        uint mask = GetMask(size);
        uint signBit = 1u << (size * 8 - 1);
        value &= mask;
        Z = value == 0;
        N = (value & signBit) != 0;
    }

    private static uint MaskToSize(uint val, int size) => val & GetMask(size);

    private static uint GetMask(int size) => size switch
    {
        1 => 0xFFu,
        2 => 0xFFFFu,
        4 => 0xFFFFFFFFu,
        _ => 0xFFFFFFFFu
    };

    private static uint SetBitRange(uint dst, uint src, int size) => size switch
    {
        1 => (dst & 0xFFFFFF00) | (src & 0xFF),
        2 => (dst & 0xFFFF0000) | (src & 0xFFFF),
        4 => src,
        _ => src
    };

    private static int GetSizeFromOpcode(ushort opcode) => ((opcode >> 6) & 3) switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        _ => 2
    };

    private static int GetSizeFromOpMode(int opmode) => (opmode & 3) switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        _ => 2
    };

    private static int GetEaCycles(int mode, int size) => mode switch
    {
        0 => 0,
        1 => 0,
        2 => 4,
        3 => 4,
        4 => 6,
        5 => 8,
        6 => 10,
        7 => 8,
        _ => 4
    };
}
