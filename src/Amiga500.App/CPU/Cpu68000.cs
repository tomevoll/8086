using System;
using Amiga500.Memory;

namespace Amiga500.CPU;

/// <summary>
/// Complete MC68000 interpreter (all 68000 instructions, correct flags, stack switching,
/// exception frames, address errors). Cycle counts are approximate (4 per bus access plus
/// per-instruction extras), which is good enough to pace a cycle-budgeted scheduler.
/// </summary>
public sealed class Cpu68000
{
    public uint[] D = new uint[8];
    public uint[] A = new uint[8];          // A[7] is always the ACTIVE stack pointer
    public uint PC;
    public ushort SR;
    public uint USP;                        // user SP while in supervisor mode
    public uint SSP;                        // supervisor SP while in user mode
    public bool Stopped;
    public bool Halted;

    public IMemoryBus Memory { get; set; }

    public const ushort FlagC = 0x0001;
    public const ushort FlagV = 0x0002;
    public const ushort FlagZ = 0x0004;
    public const ushort FlagN = 0x0008;
    public const ushort FlagX = 0x0010;
    public const ushort FlagS = 0x2000;
    public const ushort FlagT = 0x8000;

    public bool C { get => (SR & FlagC) != 0; set => SR = (ushort)(value ? (SR | FlagC) : (SR & ~FlagC)); }
    public bool V { get => (SR & FlagV) != 0; set => SR = (ushort)(value ? (SR | FlagV) : (SR & ~FlagV)); }
    public bool Z { get => (SR & FlagZ) != 0; set => SR = (ushort)(value ? (SR | FlagZ) : (SR & ~FlagZ)); }
    public bool N { get => (SR & FlagN) != 0; set => SR = (ushort)(value ? (SR | FlagN) : (SR & ~FlagN)); }
    public bool X { get => (SR & FlagX) != 0; set => SR = (ushort)(value ? (SR | FlagX) : (SR & ~FlagX)); }

    public bool Supervisor
    {
        get => (SR & FlagS) != 0;
        set => SetSR((ushort)(value ? (SR | FlagS) : (SR & ~FlagS)));
    }

    public int InterruptMask
    {
        get => (SR >> 8) & 0x07;
        set => SR = (ushort)((SR & ~0x0700) | ((value & 0x07) << 8));
    }

    public int PendingInterruptLevel { get; set; }
    public byte PendingInterruptVector { get; set; }   // 0 = autovector

    // ---- diagnostics ------------------------------------------------------------------
    /// <summary>Set to e.g. Console.WriteLine to see illegal opcodes / traps / address errors.</summary>
    public Action<string>? Log { get; set; }
    public int LogBudget = 100;
    public bool TraceEnabled { get; set; }
    public readonly uint[] TracePc = new uint[256];  // ring buffer of the last 256 instruction addresses
    public int TraceIndex;
    public long InstructionCount;

    private int cycles;

    private sealed class BusFault : Exception
    {
        public readonly uint Address;
        public readonly bool Write;
        public readonly bool Instruction;
        public BusFault(uint address, bool write, bool instruction)
        {
            Address = address; Write = write; Instruction = instruction;
        }
    }

    public Cpu68000(IMemoryBus memory)
    {
        Memory = memory;
    }

    public void Reset()
    {
        Array.Clear(D, 0, 8);
        Array.Clear(A, 0, 8);
        SR = (ushort)(FlagS | 0x0700);
        USP = 0;
        Stopped = false;
        Halted = false;
        SSP = Memory.ReadLong(0);
        A[7] = SSP;
        PC = Memory.ReadLong(4);
    }

    /// <summary>Writes SR and swaps USP/SSP when the S bit changes.</summary>
    public void SetSR(ushort value)
    {
        value &= 0xA71F;
        bool wasSup = (SR & FlagS) != 0;
        bool nowSup = (value & FlagS) != 0;
        if (wasSup != nowSup)
        {
            if (wasSup) { SSP = A[7]; A[7] = USP; }
            else { USP = A[7]; A[7] = SSP; }
        }
        SR = value;
    }

    // =====================================================================================
    //  Main entry point
    // =====================================================================================
    public int ExecuteInstruction()
    {
        if (Halted) return 4;
        cycles = 0;

        int level = PendingInterruptLevel;
        if (level > 0 && level > InterruptMask)
        {
            try { ServiceInterrupt(level); }
            catch (BusFault) { Halted = true; Note("Double bus fault during interrupt entry - CPU halted"); }
            return 44;
        }

        if (Stopped) return 4;

        uint opcodePC = PC;
        if (TraceEnabled) TracePc[TraceIndex++ & 255] = opcodePC;
        InstructionCount++;

        ushort opcode = 0;
        try
        {
            opcode = FetchWord();
            Execute(opcode, opcodePC);
        }
        catch (BusFault f)
        {
            RaiseAddressError(f, opcode, opcodePC);
        }

        return cycles < 4 ? 4 : cycles;
    }

    private void Note(string message)
    {
        if (Log != null && LogBudget > 0) { LogBudget--; Log(message); }
    }

    // =====================================================================================
    //  Exceptions
    // =====================================================================================
    private void Exception(int vector, uint stackedPC)
    {
        ushort oldSR = SR;
        SetSR((ushort)((SR | FlagS) & ~FlagT));
        PushLong(stackedPC);
        PushWord(oldSR);
        PC = Memory.ReadLong((uint)(vector * 4));
        Stopped = false;
        cycles += 34;
    }

    private void ServiceInterrupt(int level)
    {
        ushort oldSR = SR;
        SetSR((ushort)(((SR | FlagS) & ~FlagT & ~0x0700) | (level << 8)));
        PushLong(PC);
        PushWord(oldSR);
        int vector = PendingInterruptVector != 0 ? PendingInterruptVector : 24 + level;
        PC = Memory.ReadLong((uint)(vector * 4));
        Stopped = false;
    }

    private void RaiseAddressError(BusFault f, ushort ir, uint opcodePC)
    {
        Note($"Address error: {(f.Write ? "write" : "read")} {(f.Instruction ? "(fetch) " : "")}at ${f.Address:X8}, instruction at ${opcodePC:X6} op=${ir:X4}");
        bool wasSup = (SR & FlagS) != 0;
        ushort oldSR = SR;
        SetSR((ushort)((SR | FlagS) & ~FlagT));

        if ((A[7] & 1) != 0) { Halted = true; Note("Double fault (odd SSP) - CPU halted"); return; }

        ushort status = (ushort)((wasSup ? 4 : 0) | (f.Instruction ? 2 : 1) | (f.Write ? 0 : 0x10));
        A[7] -= 4; Memory.WriteLong(A[7], opcodePC);
        A[7] -= 2; Memory.WriteWord(A[7], oldSR);
        A[7] -= 2; Memory.WriteWord(A[7], ir);
        A[7] -= 4; Memory.WriteLong(A[7], f.Address);
        A[7] -= 2; Memory.WriteWord(A[7], status);
        PC = Memory.ReadLong(3 * 4);
        Stopped = false;
        cycles += 50;
    }

    private void Illegal(ushort op, uint opPC)
    {
        Note($"Illegal opcode ${op:X4} at ${opPC:X6}");
        Exception(4, opPC);
    }

    private void Privilege(uint opPC) => Exception(8, opPC);

    // =====================================================================================
    //  Memory access helpers (all raise BusFault on odd word/long access)
    // =====================================================================================
    private ushort FetchWord()
    {
        if ((PC & 1) != 0) throw new BusFault(PC, false, true);
        ushort v = Memory.ReadWord(PC);
        PC += 2;
        cycles += 4;
        return v;
    }

    private uint FetchLong()
    {
        uint hi = FetchWord();
        uint lo = FetchWord();
        return (hi << 16) | lo;
    }

    private uint Read(uint addr, int size)
    {
        if (size != 1 && (addr & 1) != 0) throw new BusFault(addr, false, false);
        if (size == 1) { cycles += 4; return Memory.ReadByte(addr); }
        if (size == 2) { cycles += 4; return Memory.ReadWord(addr); }
        cycles += 8;
        return Memory.ReadLong(addr);
    }

    private void Write(uint addr, uint value, int size)
    {
        if (size != 1 && (addr & 1) != 0) throw new BusFault(addr, true, false);
        if (size == 1) { cycles += 4; Memory.WriteByte(addr, (byte)value); }
        else if (size == 2) { cycles += 4; Memory.WriteWord(addr, (ushort)value); }
        else { cycles += 8; Memory.WriteLong(addr, value); }
    }

    public void PushWord(ushort value) { A[7] -= 2; Write(A[7], value, 2); }
    public void PushLong(uint value) { A[7] -= 4; Write(A[7], value, 4); }
    public ushort PopWord() { uint v = Read(A[7], 2); A[7] += 2; return (ushort)v; }
    public uint PopLong() { uint v = Read(A[7], 4); A[7] += 4; return v; }

    // =====================================================================================
    //  Effective addresses
    // =====================================================================================
    private static uint Mask(int size) => size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : 0xFFFFFFFFu;
    private static uint Sign(int size) => size == 1 ? 0x80u : size == 2 ? 0x8000u : 0x80000000u;
    private static uint Inc(int reg, int size) => (size == 1 && reg == 7) ? 2u : (uint)size;

    /// <summary>Address for memory modes 2..7 (consumes extension words, applies pre/post inc).</summary>
    private uint Ea(int mode, int reg, int size)
    {
        switch (mode)
        {
            case 2: return A[reg];
            case 3: { uint a = A[reg]; A[reg] += Inc(reg, size); return a; }
            case 4: { A[reg] -= Inc(reg, size); cycles += 2; return A[reg]; }
            case 5: { short d = (short)FetchWord(); return A[reg] + (uint)(int)d; }
            case 6: return IndexAddress(A[reg]);
            case 7:
                switch (reg)
                {
                    case 0: return (uint)(int)(short)FetchWord();
                    case 1: return FetchLong();
                    case 2: { uint b = PC; short d = (short)FetchWord(); return b + (uint)(int)d; }
                    case 3: { uint b = PC; return IndexAddress(b); }
                }
                break;
        }
        throw new InvalidOperationException("bad EA");
    }

    private uint IndexAddress(uint baseAddr)
    {
        ushort ext = FetchWord();
        int xr = (ext >> 12) & 7;
        uint x = (ext & 0x8000) != 0 ? A[xr] : D[xr];
        if ((ext & 0x0800) == 0) x = (uint)(int)(short)x;
        int disp = (sbyte)(ext & 0xFF);
        cycles += 2;
        return baseAddr + x + (uint)disp;
    }

    /// <summary>Resolves the memory address of an operand, or 0 for Dn / An / #imm.</summary>
    private uint Locate(int mode, int reg, int size)
    {
        if (mode < 2 || (mode == 7 && reg == 4)) return 0;
        return Ea(mode, reg, size);
    }

    private uint Load(int mode, int reg, int size, uint ea)
    {
        if (mode == 0) return D[reg] & Mask(size);
        if (mode == 1) return A[reg] & Mask(size);
        if (mode == 7 && reg == 4) return size == 4 ? FetchLong() : (FetchWord() & Mask(size));
        return Read(ea, size);
    }

    private void StoreD(int r, int size, uint v)
    {
        if (size == 4) D[r] = v;
        else if (size == 2) D[r] = (D[r] & 0xFFFF0000u) | (v & 0xFFFFu);
        else D[r] = (D[r] & 0xFFFFFF00u) | (v & 0xFFu);
    }

    private void Store(int mode, int reg, int size, uint ea, uint v)
    {
        if (mode == 0) { StoreD(reg, size, v); return; }
        if (mode == 1) { A[reg] = size == 2 ? (uint)(int)(short)v : v; return; }
        Write(ea, v, size);
    }

    // =====================================================================================
    //  Flag helpers
    // =====================================================================================
    private void SetFlags(bool n, bool z, bool v, bool c)
    {
        int s = SR & ~(FlagN | FlagZ | FlagV | FlagC);
        if (n) s |= FlagN;
        if (z) s |= FlagZ;
        if (v) s |= FlagV;
        if (c) s |= FlagC;
        SR = (ushort)s;
    }

    private void LogicFlags(uint res, int size)
    {
        res &= Mask(size);
        SetFlags((res & Sign(size)) != 0, res == 0, false, false);
    }

    private uint Add(uint s, uint d, int size, bool extend)
    {
        uint m = Mask(size), sg = Sign(size);
        s &= m; d &= m;
        ulong sum = (ulong)s + d + ((extend && X) ? 1UL : 0UL);
        uint r = (uint)(sum & m);
        bool c = sum > m;
        bool v = ((~(s ^ d)) & (s ^ r) & sg) != 0;
        bool z = extend ? (Z && r == 0) : r == 0;
        SetFlags((r & sg) != 0, z, v, c);
        X = c;
        return r;
    }

    /// <summary>d - s (- X). Used by SUB, SUBX, NEG, NEGX and CMP (updateX=false).</summary>
    private uint Sub(uint s, uint d, int size, bool extend, bool updateX)
    {
        uint m = Mask(size), sg = Sign(size);
        s &= m; d &= m;
        ulong cin = (extend && X) ? 1UL : 0UL;
        uint r = (uint)(((ulong)d - s - cin) & m);
        bool c = ((ulong)s + cin) > d;
        bool v = ((s ^ d) & (d ^ r) & sg) != 0;
        bool z = extend ? (Z && r == 0) : r == 0;
        SetFlags((r & sg) != 0, z, v, c);
        if (updateX) X = c;
        return r;
    }

    private bool TestCondition(int cc)
    {
        bool c = C, v = V, z = Z, n = N;
        switch (cc)
        {
            case 0: return true;
            case 1: return false;
            case 2: return !c && !z;
            case 3: return c || z;
            case 4: return !c;
            case 5: return c;
            case 6: return !z;
            case 7: return z;
            case 8: return !v;
            case 9: return v;
            case 10: return !n;
            case 11: return n;
            case 12: return n == v;
            case 13: return n != v;
            case 14: return !z && (n == v);
            default: return z || (n != v);
        }
    }

    // =====================================================================================
    //  Decoder
    // =====================================================================================
    private void Execute(ushort op, uint opPC)
    {
        switch (op >> 12)
        {
            case 0x0: Group0(op, opPC); break;
            case 0x1: Move(op, 1); break;
            case 0x2: Move(op, 4); break;
            case 0x3: Move(op, 2); break;
            case 0x4: Group4(op, opPC); break;
            case 0x5: Group5(op, opPC); break;
            case 0x6: Branch(op, opPC); break;
            case 0x7:
                if ((op & 0x0100) != 0) { Illegal(op, opPC); break; }
                D[(op >> 9) & 7] = (uint)(int)(sbyte)(op & 0xFF);
                LogicFlags(D[(op >> 9) & 7], 4);
                break;
            case 0x8: Group8(op, opPC); break;
            case 0x9: AddSub(op, true); break;
            case 0xA: Note($"Line-A opcode ${op:X4} at ${opPC:X6}"); Exception(10, opPC); break;
            case 0xB: GroupB(op); break;
            case 0xC: GroupC(op); break;
            case 0xD: AddSub(op, false); break;
            case 0xE: ShiftGroup(op, opPC); break;
            default: Note($"Line-F opcode ${op:X4} at ${opPC:X6}"); Exception(11, opPC); break;
        }
    }

    // ---- MOVE / MOVEA -------------------------------------------------------------------
    private void Move(ushort op, int size)
    {
        int smode = (op >> 3) & 7, sreg = op & 7;
        int dmode = (op >> 6) & 7, dreg = (op >> 9) & 7;

        uint sea = Locate(smode, sreg, size);
        uint v = Load(smode, sreg, size, sea);

        if (dmode == 1)
        {
            A[dreg] = size == 2 ? (uint)(int)(short)v : v;   // MOVEA: no flags
            return;
        }

        uint dea = Locate(dmode, dreg, size);
        Store(dmode, dreg, size, dea, v);
        LogicFlags(v, size);
    }

    // ---- Group 0: immediate ops, bit ops, MOVEP -------------------------------------------
    private void Group0(ushort op, uint opPC)
    {
        int mode = (op >> 3) & 7, reg = op & 7;

        if ((op & 0x0100) != 0)
        {
            if (mode == 1) { Movep(op); return; }
            BitOp((op >> 6) & 3, D[(op >> 9) & 7], mode, reg);
            return;
        }

        switch ((op >> 9) & 7)
        {
            case 0: ImmOp(op, opPC, 0); break;   // ORI
            case 1: ImmOp(op, opPC, 1); break;   // ANDI
            case 2: ImmOp(op, opPC, 2); break;   // SUBI
            case 3: ImmOp(op, opPC, 3); break;   // ADDI
            case 4:                              // BTST/BCHG/BCLR/BSET #n
            {
                uint bit = FetchWord() & 0xFFu;
                BitOp((op >> 6) & 3, bit, mode, reg);
                break;
            }
            case 5: ImmOp(op, opPC, 5); break;   // EORI
            case 6: ImmOp(op, opPC, 6); break;   // CMPI
            default: Illegal(op, opPC); break;
        }
    }

    private void ImmOp(ushort op, uint opPC, int kind)
    {
        int mode = (op >> 3) & 7, reg = op & 7;
        int sizeBits = (op >> 6) & 3;

        if (mode == 7 && reg == 4 && (kind == 0 || kind == 1 || kind == 5))
        {
            if (sizeBits == 0)   // ... to CCR
            {
                uint imm = FetchWord() & 0xFFu;
                uint ccr = (uint)(SR & 0x1F);
                ccr = kind == 0 ? (ccr | imm) : kind == 1 ? (ccr & imm) : (ccr ^ imm);
                SR = (ushort)((SR & 0xFF00) | (ccr & 0x1F));
                cycles += 8;
                return;
            }
            if (sizeBits == 1)   // ... to SR (privileged)
            {
                if (!Supervisor) { Privilege(opPC); return; }
                uint imm = FetchWord();
                uint sr = SR;
                sr = kind == 0 ? (sr | imm) : kind == 1 ? (sr & imm) : (sr ^ imm);
                SetSR((ushort)sr);
                cycles += 8;
                return;
            }
            Illegal(op, opPC);
            return;
        }

        if (sizeBits == 3) { Illegal(op, opPC); return; }
        int size = 1 << sizeBits;
        uint immediate = size == 4 ? FetchLong() : (FetchWord() & Mask(size));
        uint ea = Locate(mode, reg, size);
        uint d = Load(mode, reg, size, ea);
        uint res;
        switch (kind)
        {
            case 0: res = d | immediate; LogicFlags(res, size); break;
            case 1: res = d & immediate; LogicFlags(res, size); break;
            case 5: res = d ^ immediate; LogicFlags(res, size); break;
            case 2: res = Sub(immediate, d, size, false, true); break;
            case 3: res = Add(immediate, d, size, false); break;
            default: Sub(immediate, d, size, false, false); return;   // CMPI
        }
        Store(mode, reg, size, ea, res);
    }

    private void BitOp(int kind, uint bitNum, int mode, int reg)
    {
        if (mode == 0)
        {
            int n = (int)(bitNum & 31);
            uint v = D[reg];
            bool set = ((v >> n) & 1) != 0;
            Z = !set;
            if (kind == 1) v ^= 1u << n;
            else if (kind == 2) v &= ~(1u << n);
            else if (kind == 3) v |= 1u << n;
            if (kind != 0) D[reg] = v;
            cycles += kind == 0 ? 2 : 4;
            return;
        }
        else
        {
            int n = (int)(bitNum & 7);
            uint ea = Locate(mode, reg, 1);
            uint v = Load(mode, reg, 1, ea);
            bool set = ((v >> n) & 1) != 0;
            Z = !set;
            if (kind == 0) return;
            if (kind == 1) v ^= 1u << n;
            else if (kind == 2) v &= ~(1u << n);
            else v |= 1u << n;
            Write(ea, v, 1);
        }
    }

    private void Movep(ushort op)
    {
        int dreg = (op >> 9) & 7, an = op & 7, opm = (op >> 6) & 7;
        short disp = (short)FetchWord();
        uint a = A[an] + (uint)(int)disp;
        switch (opm)
        {
            case 4: D[dreg] = (D[dreg] & 0xFFFF0000u) | (((uint)Read(a, 1) << 8) | Read(a + 2, 1)); break;
            case 5: D[dreg] = (Read(a, 1) << 24) | (Read(a + 2, 1) << 16) | (Read(a + 4, 1) << 8) | Read(a + 6, 1); break;
            case 6: Write(a, D[dreg] >> 8, 1); Write(a + 2, D[dreg], 1); break;
            default: Write(a, D[dreg] >> 24, 1); Write(a + 2, D[dreg] >> 16, 1); Write(a + 4, D[dreg] >> 8, 1); Write(a + 6, D[dreg], 1); break;
        }
    }

    // ---- Group 4 ----------------------------------------------------------------------------
    private void Group4(ushort op, uint opPC)
    {
        int mode = (op >> 3) & 7, reg = op & 7;

        if ((op & 0xF1C0) == 0x41C0)   // LEA
        {
            A[(op >> 9) & 7] = Ea(mode, reg, 4);
            return;
        }
        if ((op & 0xF1C0) == 0x4180)   // CHK
        {
            uint ea = Locate(mode, reg, 2);
            short bound = (short)Load(mode, reg, 2, ea);
            short val = (short)D[(op >> 9) & 7];
            if (val < 0) { N = true; Exception(6, PC); }
            else if (val > bound) { N = false; Exception(6, PC); }
            return;
        }

        switch (op & 0xFFC0)
        {
            case 0x40C0:   // MOVE from SR
            {
                uint ea = Locate(mode, reg, 2);
                Store(mode, reg, 2, ea, SR);
                return;
            }
            case 0x44C0:   // MOVE to CCR
            {
                uint ea = Locate(mode, reg, 2);
                uint v = Load(mode, reg, 2, ea);
                SR = (ushort)((SR & 0xFF00) | (v & 0x1F));
                return;
            }
            case 0x46C0:   // MOVE to SR
            {
                if (!Supervisor) { Privilege(opPC); return; }
                uint ea = Locate(mode, reg, 2);
                uint v = Load(mode, reg, 2, ea);
                SetSR((ushort)v);
                return;
            }
            case 0x4800:   // NBCD
            {
                uint ea = Locate(mode, reg, 1);
                uint v = Load(mode, reg, 1, ea);
                uint res = (0x9Au - v - (X ? 1u : 0u)) & 0xFF;
                bool c = false;
                if (res != 0x9A)
                {
                    if ((res & 0x0F) == 0x0A) res = ((res & 0xF0) + 0x10) & 0xFF;
                    c = true;
                    Store(mode, reg, 1, ea, res);
                }
                else res = 0;
                SetFlags((res & 0x80) != 0, Z && res == 0, false, c);
                X = c;
                return;
            }
            case 0x4840:   // SWAP / PEA
                if (mode == 0)
                {
                    D[reg] = (D[reg] << 16) | (D[reg] >> 16);
                    LogicFlags(D[reg], 4);
                }
                else
                {
                    PushLong(Ea(mode, reg, 4));
                }
                return;
            case 0x4880:   // EXT.W / MOVEM regs->mem (word)
                if (mode == 0)
                {
                    uint w = (uint)(ushort)(short)(sbyte)D[reg];
                    D[reg] = (D[reg] & 0xFFFF0000u) | w;
                    LogicFlags(w, 2);
                    return;
                }
                Movem(op);
                return;
            case 0x48C0:   // EXT.L / MOVEM regs->mem (long)
                if (mode == 0)
                {
                    D[reg] = (uint)(int)(short)D[reg];
                    LogicFlags(D[reg], 4);
                    return;
                }
                Movem(op);
                return;
            case 0x4C80:
            case 0x4CC0:
                Movem(op);
                return;
            case 0x4AC0:   // TAS (or ILLEGAL when op == 0x4AFC)
            {
                if (op == 0x4AFC) { Illegal(op, opPC); return; }
                uint ea = Locate(mode, reg, 1);
                uint v = Load(mode, reg, 1, ea);
                LogicFlags(v, 1);
                Store(mode, reg, 1, ea, v | 0x80);
                return;
            }
            case 0x4E80:   // JSR
            {
                uint ea = Ea(mode, reg, 4);
                PushLong(PC);
                PC = ea;
                cycles += 4;
                return;
            }
            case 0x4EC0:   // JMP
                PC = Ea(mode, reg, 4);
                cycles += 2;
                return;
        }

        // NEGX / CLR / NEG / NOT / TST
        switch (op & 0xFF00)
        {
            case 0x4000: UnaryOp(op, opPC, 0); return;
            case 0x4200: UnaryOp(op, opPC, 1); return;
            case 0x4400: UnaryOp(op, opPC, 2); return;
            case 0x4600: UnaryOp(op, opPC, 3); return;
            case 0x4A00: UnaryOp(op, opPC, 4); return;
        }

        // 0x4E4x - 0x4E7x
        if ((op & 0xFFF0) == 0x4E40) { Exception(32 + (op & 0x0F), PC); return; }   // TRAP
        if ((op & 0xFFF8) == 0x4E50)   // LINK
        {
            short disp = (short)FetchWord();
            PushLong(A[reg]);
            A[reg] = A[7];
            A[7] += (uint)(int)disp;
            return;
        }
        if ((op & 0xFFF8) == 0x4E58)   // UNLK
        {
            A[7] = A[reg];
            A[reg] = PopLong();
            return;
        }
        if ((op & 0xFFF0) == 0x4E60)   // MOVE USP
        {
            if (!Supervisor) { Privilege(opPC); return; }
            if ((op & 0x0008) != 0) A[reg] = USP; else USP = A[reg];
            return;
        }

        switch (op)
        {
            case 0x4E70:   // RESET
                if (!Supervisor) { Privilege(opPC); return; }
                cycles += 128;
                return;
            case 0x4E71: return;   // NOP
            case 0x4E72:           // STOP
            {
                if (!Supervisor) { Privilege(opPC); return; }
                ushort imm = FetchWord();
                SetSR(imm);
                Stopped = true;
                return;
            }
            case 0x4E73:           // RTE
            {
                if (!Supervisor) { Privilege(opPC); return; }
                ushort sr = PopWord();
                uint pc = PopLong();
                SetSR(sr);
                PC = pc;
                cycles += 8;
                return;
            }
            case 0x4E75:           // RTS
                PC = PopLong();
                cycles += 4;
                return;
            case 0x4E76:           // TRAPV
                if (V) Exception(7, PC);
                return;
            case 0x4E77:           // RTR
            {
                ushort ccr = PopWord();
                SR = (ushort)((SR & 0xFF00) | (ccr & 0x1F));
                PC = PopLong();
                cycles += 4;
                return;
            }
        }

        Illegal(op, opPC);
    }

    private void UnaryOp(ushort op, uint opPC, int kind)
    {
        int sizeBits = (op >> 6) & 3;
        if (sizeBits == 3) { Illegal(op, opPC); return; }
        int size = 1 << sizeBits;
        int mode = (op >> 3) & 7, reg = op & 7;
        uint ea = Locate(mode, reg, size);

        switch (kind)
        {
            case 1:   // CLR
                Store(mode, reg, size, ea, 0);
                SetFlags(false, true, false, false);
                return;
            case 4:   // TST
                LogicFlags(Load(mode, reg, size, ea), size);
                return;
        }

        uint v = Load(mode, reg, size, ea);
        uint res;
        if (kind == 0) res = Sub(v, 0, size, true, true);            // NEGX
        else if (kind == 2) res = Sub(v, 0, size, false, true);      // NEG
        else { res = ~v & Mask(size); LogicFlags(res, size); }       // NOT
        Store(mode, reg, size, ea, res);
    }

    private void Movem(ushort op)
    {
        bool toReg = (op & 0x0400) != 0;
        int size = (op & 0x0040) != 0 ? 4 : 2;
        int mode = (op >> 3) & 7, reg = op & 7;
        ushort mask = FetchWord();

        if (!toReg)
        {
            if (mode == 4)
            {
                // Pre-decrement: mask bit 0 = A7 ... bit 15 = D0; start from the UNMODIFIED An.
                uint addr = A[reg];
                for (int i = 0; i < 16; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    int r = 15 - i;
                    uint val = r < 8 ? D[r] : A[r - 8];
                    addr -= (uint)size;
                    Write(addr, val, size);
                }
                A[reg] = addr;
            }
            else
            {
                uint addr = Ea(mode, reg, size);
                for (int i = 0; i < 16; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    uint val = i < 8 ? D[i] : A[i - 8];
                    Write(addr, val, size);
                    addr += (uint)size;
                }
            }
        }
        else
        {
            uint addr = mode == 3 ? A[reg] : Ea(mode, reg, size);
            for (int i = 0; i < 16; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                uint val = Read(addr, size);
                if (size == 2) val = (uint)(int)(short)val;
                if (i < 8) D[i] = val; else A[i - 8] = val;
                addr += (uint)size;
            }
            if (mode == 3) A[reg] = addr;
        }
    }

    // ---- Group 5: ADDQ / SUBQ / Scc / DBcc ------------------------------------------------------
    private void Group5(ushort op, uint opPC)
    {
        int mode = (op >> 3) & 7, reg = op & 7;

        if (((op >> 6) & 3) == 3)
        {
            int cond = (op >> 8) & 15;
            if (mode == 1)   // DBcc
            {
                short disp = (short)FetchWord();
                if (TestCondition(cond)) { cycles += 4; return; }
                ushort cnt = (ushort)(D[reg] - 1);
                D[reg] = (D[reg] & 0xFFFF0000u) | cnt;
                if (cnt != 0xFFFF) { PC = opPC + 2 + (uint)(int)disp; cycles += 2; }
                else cycles += 6;
                return;
            }
            uint ea = Locate(mode, reg, 1);
            bool t = TestCondition(cond);
            Store(mode, reg, 1, ea, t ? 0xFFu : 0u);
            if (mode == 0 && t) cycles += 2;
            return;
        }

        uint data = (uint)((op >> 9) & 7);
        if (data == 0) data = 8;
        bool sub = (op & 0x0100) != 0;
        int size = 1 << ((op >> 6) & 3);

        if (mode == 1)
        {
            A[reg] = sub ? A[reg] - data : A[reg] + data;   // whole register, no flags
            cycles += 4;
            return;
        }

        uint ea2 = Locate(mode, reg, size);
        uint d = Load(mode, reg, size, ea2);
        uint res = sub ? Sub(data, d, size, false, true) : Add(data, d, size, false);
        Store(mode, reg, size, ea2, res);
        if (mode == 0 && size == 4) cycles += 4;
    }

    // ---- Group 6: Bcc / BRA / BSR --------------------------------------------------------------------
    private void Branch(ushort op, uint opPC)
    {
        int cond = (op >> 8) & 15;
        int disp = (sbyte)(op & 0xFF);
        bool word = (op & 0xFF) == 0;
        if (word) disp = (short)FetchWord();
        uint target = opPC + 2 + (uint)disp;

        if (cond == 1)   // BSR
        {
            PushLong(PC);
            PC = target;
            cycles += 6;
            return;
        }
        if (cond == 0 || TestCondition(cond)) { PC = target; cycles += 6; }
        else cycles += 4;
    }

    // ---- Group 8: OR / DIV / SBCD ---------------------------------------------------------------------
    private void Group8(ushort op, uint opPC)
    {
        int dreg = (op >> 9) & 7, opmode = (op >> 6) & 7, mode = (op >> 3) & 7, reg = op & 7;

        if (opmode == 3)   // DIVU.W
        {
            uint ea = Locate(mode, reg, 2);
            uint divisor = Load(mode, reg, 2, ea) & 0xFFFF;
            if (divisor == 0) { Exception(5, PC); return; }
            uint dividend = D[dreg];
            uint q = dividend / divisor;
            uint rem = dividend % divisor;
            cycles += 136;
            if (q > 0xFFFF) { SetFlags(true, false, true, false); return; }
            D[dreg] = (rem << 16) | q;
            SetFlags((q & 0x8000) != 0, q == 0, false, false);
            return;
        }
        if (opmode == 7)   // DIVS.W
        {
            uint ea = Locate(mode, reg, 2);
            int divisor = (short)Load(mode, reg, 2, ea);
            if (divisor == 0) { Exception(5, PC); return; }
            int dividend = (int)D[dreg];
            long q = (long)dividend / divisor;
            long rem = (long)dividend % divisor;
            cycles += 150;
            if (q > 32767 || q < -32768) { SetFlags(true, false, true, false); return; }
            D[dreg] = (((uint)(ushort)(short)rem) << 16) | ((uint)(ushort)(short)q);
            SetFlags(q < 0, q == 0, false, false);
            return;
        }
        if ((op & 0xF1F0) == 0x8100)   // SBCD
        {
            BcdOp(op, false);
            return;
        }

        int size = 1 << (opmode & 3);
        if ((opmode & 4) == 0)   // <ea>,Dn
        {
            uint ea = Locate(mode, reg, size);
            uint s = Load(mode, reg, size, ea);
            uint res = (D[dreg] | s) & Mask(size);
            StoreD(dreg, size, res);
            LogicFlags(res, size);
        }
        else                     // Dn,<ea>
        {
            uint ea = Locate(mode, reg, size);
            uint d = Load(mode, reg, size, ea);
            uint res = (d | D[dreg]) & Mask(size);
            Store(mode, reg, size, ea, res);
            LogicFlags(res, size);
        }
    }

    // ---- ADD/ADDA/ADDX and SUB/SUBA/SUBX ----------------------------------------------------------------
    private void AddSub(ushort op, bool isSub)
    {
        int dreg = (op >> 9) & 7, opmode = (op >> 6) & 7, mode = (op >> 3) & 7, reg = op & 7;

        if (opmode == 3 || opmode == 7)   // ADDA / SUBA
        {
            int asz = opmode == 3 ? 2 : 4;
            uint ea = Locate(mode, reg, asz);
            uint s = Load(mode, reg, asz, ea);
            if (asz == 2) s = (uint)(int)(short)s;
            A[dreg] = isSub ? A[dreg] - s : A[dreg] + s;
            cycles += 4;
            return;
        }

        int size = 1 << (opmode & 3);

        if ((opmode & 4) != 0 && (mode == 0 || mode == 1))   // ADDX / SUBX
        {
            if (mode == 0)
            {
                uint res = isSub ? Sub(D[reg], D[dreg], size, true, true) : Add(D[reg], D[dreg], size, true);
                StoreD(dreg, size, res);
            }
            else
            {
                A[reg] -= Inc(reg, size);
                uint s = Read(A[reg], size);
                A[dreg] -= Inc(dreg, size);
                uint d = Read(A[dreg], size);
                uint res = isSub ? Sub(s, d, size, true, true) : Add(s, d, size, true);
                Write(A[dreg], res, size);
            }
            return;
        }

        if ((opmode & 4) == 0)   // <ea>,Dn
        {
            uint ea = Locate(mode, reg, size);
            uint s = Load(mode, reg, size, ea);
            uint res = isSub ? Sub(s, D[dreg], size, false, true) : Add(s, D[dreg], size, false);
            StoreD(dreg, size, res);
        }
        else                     // Dn,<ea>
        {
            uint ea = Locate(mode, reg, size);
            uint d = Load(mode, reg, size, ea);
            uint res = isSub ? Sub(D[dreg], d, size, false, true) : Add(D[dreg], d, size, false);
            Store(mode, reg, size, ea, res);
        }
    }

    // ---- Group B: CMP / CMPA / CMPM / EOR ---------------------------------------------------------------
    private void GroupB(ushort op)
    {
        int dreg = (op >> 9) & 7, opmode = (op >> 6) & 7, mode = (op >> 3) & 7, reg = op & 7;

        if (opmode == 3 || opmode == 7)   // CMPA
        {
            int asz = opmode == 3 ? 2 : 4;
            uint ea = Locate(mode, reg, asz);
            uint s = Load(mode, reg, asz, ea);
            if (asz == 2) s = (uint)(int)(short)s;
            Sub(s, A[dreg], 4, false, false);
            cycles += 2;
            return;
        }

        int size = 1 << (opmode & 3);
        if ((opmode & 4) == 0)   // CMP <ea>,Dn
        {
            uint ea = Locate(mode, reg, size);
            uint s = Load(mode, reg, size, ea);
            Sub(s, D[dreg], size, false, false);
            return;
        }

        if (mode == 1)   // CMPM (Ay)+,(Ax)+
        {
            uint sa = A[reg]; A[reg] += Inc(reg, size);
            uint s = Read(sa, size);
            uint da = A[dreg]; A[dreg] += Inc(dreg, size);
            uint d = Read(da, size);
            Sub(s, d, size, false, false);
            return;
        }

        {   // EOR Dn,<ea>
            uint ea = Locate(mode, reg, size);
            uint d = Load(mode, reg, size, ea);
            uint res = (d ^ D[dreg]) & Mask(size);
            Store(mode, reg, size, ea, res);
            LogicFlags(res, size);
        }
    }

    // ---- Group C: AND / MUL / ABCD / EXG ------------------------------------------------------------------
    private void GroupC(ushort op)
    {
        int dreg = (op >> 9) & 7, opmode = (op >> 6) & 7, mode = (op >> 3) & 7, reg = op & 7;

        if (opmode == 3)   // MULU.W
        {
            uint ea = Locate(mode, reg, 2);
            uint s = Load(mode, reg, 2, ea) & 0xFFFF;
            uint res = s * (D[dreg] & 0xFFFF);
            D[dreg] = res;
            LogicFlags(res, 4);
            cycles += 34;
            return;
        }
        if (opmode == 7)   // MULS.W
        {
            uint ea = Locate(mode, reg, 2);
            int s = (short)Load(mode, reg, 2, ea);
            int res = s * (short)D[dreg];
            D[dreg] = (uint)res;
            LogicFlags((uint)res, 4);
            cycles += 34;
            return;
        }

        switch (op & 0xF1F8)
        {
            case 0xC140: { uint t = D[dreg]; D[dreg] = D[reg]; D[reg] = t; cycles += 2; return; }   // EXG Dx,Dy
            case 0xC148: { uint t = A[dreg]; A[dreg] = A[reg]; A[reg] = t; cycles += 2; return; }   // EXG Ax,Ay
            case 0xC188: { uint t = D[dreg]; D[dreg] = A[reg]; A[reg] = t; cycles += 2; return; }   // EXG Dx,Ay
        }

        if ((op & 0xF1F0) == 0xC100)   // ABCD
        {
            BcdOp(op, true);
            return;
        }

        int size = 1 << (opmode & 3);
        if ((opmode & 4) == 0)   // <ea>,Dn
        {
            uint ea = Locate(mode, reg, size);
            uint s = Load(mode, reg, size, ea);
            uint res = (D[dreg] & s) & Mask(size);
            StoreD(dreg, size, res);
            LogicFlags(res, size);
        }
        else                     // Dn,<ea>
        {
            uint ea = Locate(mode, reg, size);
            uint d = Load(mode, reg, size, ea);
            uint res = (d & D[dreg]) & Mask(size);
            Store(mode, reg, size, ea, res);
            LogicFlags(res, size);
        }
    }

    // ---- BCD -------------------------------------------------------------------------------------------------
    private void BcdOp(ushort op, bool add)
    {
        int rx = (op >> 9) & 7, ry = op & 7;
        bool mem = (op & 0x0008) != 0;
        uint s, d;
        uint dstAddr = 0;
        if (!mem) { s = D[ry] & 0xFF; d = D[rx] & 0xFF; }
        else
        {
            A[ry] -= Inc(ry, 1); s = Read(A[ry], 1);
            A[rx] -= Inc(rx, 1); dstAddr = A[rx]; d = Read(dstAddr, 1);
        }

        uint res;
        bool c;
        if (add)
        {
            res = (s & 0x0F) + (d & 0x0F) + (X ? 1u : 0u);
            if (res > 9) res += 6;
            res += (s & 0xF0) + (d & 0xF0);
            c = res > 0x99;
            if (c) res -= 0xA0;
        }
        else
        {
            res = (d & 0x0F) - (s & 0x0F) - (X ? 1u : 0u);
            if (res > 9) res -= 6;
            res += (d & 0xF0) - (s & 0xF0);
            c = res > 0x99;
            if (c) res += 0xA0;
        }
        res &= 0xFF;

        SetFlags((res & 0x80) != 0, Z && res == 0, false, c);
        X = c;

        if (!mem) StoreD(rx, 1, res);
        else Write(dstAddr, res, 1);
    }

    // ---- Group E: shifts and rotates --------------------------------------------------------------------------
    private void ShiftGroup(ushort op, uint opPC)
    {
        bool left = (op & 0x0100) != 0;

        if (((op >> 6) & 3) == 3)   // memory shift/rotate: word, count 1
        {
            int mode = (op >> 3) & 7, reg = op & 7;
            if (mode < 2 || (mode == 7 && reg > 1)) { Illegal(op, opPC); return; }
            uint ea = Ea(mode, reg, 2);
            uint v = Read(ea, 2);
            uint r = ShiftValue((op >> 9) & 3, left, v, 1, 2);
            Write(ea, r, 2);
            return;
        }

        int size = 1 << ((op >> 6) & 3);
        int type = (op >> 3) & 3;
        bool regCount = (op & 0x0020) != 0;
        int cr = (op >> 9) & 7;
        int dr = op & 7;
        int count = regCount ? (int)(D[cr] & 63) : (cr == 0 ? 8 : cr);

        uint result = ShiftValue(type, left, D[dr] & Mask(size), count, size);
        StoreD(dr, size, result);
        cycles += 2 + 2 * count + (size == 4 ? 2 : 0);
    }

    /// <summary>type: 0=AS 1=LS 2=ROX 3=RO. Sets N Z V C (and X where applicable).</summary>
    private uint ShiftValue(int type, bool left, uint value, int count, int size)
    {
        uint m = Mask(size), sg = Sign(size);
        uint v = value & m;
        bool c = false, ov = false;
        bool x = X;

        if (count == 0)
        {
            c = type == 2 ? x : false;
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                switch (type)
                {
                    case 0:   // AS
                        if (left)
                        {
                            bool msb = (v & sg) != 0;
                            v = (v << 1) & m;
                            c = msb;
                            if (((v & sg) != 0) != msb) ov = true;
                        }
                        else
                        {
                            c = (v & 1) != 0;
                            v = (v >> 1) | (v & sg);
                        }
                        break;
                    case 1:   // LS
                        if (left) { c = (v & sg) != 0; v = (v << 1) & m; }
                        else { c = (v & 1) != 0; v >>= 1; }
                        break;
                    case 2:   // ROX
                        if (left)
                        {
                            bool msb = (v & sg) != 0;
                            v = ((v << 1) & m) | (x ? 1u : 0u);
                            x = msb; c = msb;
                        }
                        else
                        {
                            bool lsb = (v & 1) != 0;
                            v = (v >> 1) | (x ? sg : 0u);
                            x = lsb; c = lsb;
                        }
                        break;
                    default:  // RO
                        if (left)
                        {
                            bool msb = (v & sg) != 0;
                            v = ((v << 1) & m) | (msb ? 1u : 0u);
                            c = msb;
                        }
                        else
                        {
                            bool lsb = (v & 1) != 0;
                            v = (v >> 1) | (lsb ? sg : 0u);
                            c = lsb;
                        }
                        break;
                }
            }
        }

        SetFlags((v & sg) != 0, v == 0, ov, c);
        if (count > 0 && type != 3) X = type == 2 ? x : c;
        return v;
    }
}
