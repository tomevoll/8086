using Amiga500.CPU;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class TestMemoryBus : IMemoryBus
{
    private readonly byte[] _memory = new byte[1024 * 1024]; // 1MB test memory

    public byte ReadByte(uint address) => _memory[address & 0xFFFFF];

    public ushort ReadWord(uint address)
    {
        uint addr = address & 0xFFFFF;
        return (ushort)((_memory[addr] << 8) | _memory[addr + 1]);
    }

    public uint ReadLong(uint address)
    {
        uint addr = address & 0xFFFFF;
        return ((uint)_memory[addr] << 24) |
               ((uint)_memory[addr + 1] << 16) |
               ((uint)_memory[addr + 2] << 8) |
               _memory[addr + 3];
    }

    public void WriteByte(uint address, byte value) => _memory[address & 0xFFFFF] = value;

    public void WriteWord(uint address, ushort value)
    {
        uint addr = address & 0xFFFFF;
        _memory[addr] = (byte)(value >> 8);
        _memory[addr + 1] = (byte)value;
    }

    public void WriteLong(uint address, uint value)
    {
        uint addr = address & 0xFFFFF;
        _memory[addr] = (byte)(value >> 24);
        _memory[addr + 1] = (byte)(value >> 16);
        _memory[addr + 2] = (byte)(value >> 8);
        _memory[addr + 3] = (byte)value;
    }
}

public class Cpu68000Tests
{
    private readonly TestMemoryBus _mem;
    private readonly Cpu68000 _cpu;

    public Cpu68000Tests()
    {
        _mem = new TestMemoryBus();
        _cpu = new Cpu68000(_mem);
    }

    [Fact]
    public void Reset_LoadsInitialSspAndPc()
    {
        _mem.WriteLong(0, 0x00080000); // Initial SSP = 512KB
        _mem.WriteLong(4, 0x00001000); // Initial PC = $1000

        _cpu.Reset();

        Assert.Equal(0x00080000u, _cpu.A[7]);
        Assert.Equal(0x00080000u, _cpu.SSP);
        Assert.Equal(0x00001000u, _cpu.PC);
        Assert.True(_cpu.Supervisor);
        Assert.Equal(7, _cpu.InterruptMask);
    }

    [Fact]
    public void Execute_MoveqAndAdd()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _cpu.Reset();

        // MOVEQ #42, D0 -> $702A
        // ADD.L D0, D1 -> $D280
        _mem.WriteWord(0x1000, 0x702A);
        _mem.WriteWord(0x1002, 0xD280);

        _cpu.D[1] = 10;

        _cpu.ExecuteInstruction(); // MOVEQ
        Assert.Equal(42u, _cpu.D[0]);
        Assert.False(_cpu.Z);
        Assert.False(_cpu.N);

        _cpu.ExecuteInstruction(); // ADD
        Assert.Equal(52u, _cpu.D[1]);
        Assert.False(_cpu.Z);
    }

    [Fact]
    public void Execute_JsrAndRts()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _cpu.Reset();

        // JSR $2000 -> $4EB9 0000 2000
        _mem.WriteWord(0x1000, 0x4EB9);
        _mem.WriteLong(0x1002, 0x2000);

        // RTS -> $4E75
        _mem.WriteWord(0x2000, 0x4E75);

        _cpu.ExecuteInstruction(); // JSR
        Assert.Equal(0x2000u, _cpu.PC);
        Assert.Equal(0x7FFFCu, _cpu.A[7]); // Stack pushed long PC ($1006)

        _cpu.ExecuteInstruction(); // RTS
        Assert.Equal(0x1006u, _cpu.PC);
        Assert.Equal(0x80000u, _cpu.A[7]);
    }

    [Fact]
    public void Execute_MuluAndDivu()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _cpu.Reset();

        // MULU D0, D1 ($C2C0)
        _mem.WriteWord(0x1000, 0xC2C0);
        _cpu.D[0] = 30;
        _cpu.D[1] = 20;

        _cpu.ExecuteInstruction();
        Assert.Equal(600u, _cpu.D[1]);

        // DIVU D0, D1 ($82C0)
        _mem.WriteWord(0x1002, 0x82C0);
        _cpu.D[0] = 10;
        _cpu.D[1] = 600;

        _cpu.ExecuteInstruction();
        Assert.Equal(60u, _cpu.D[1] & 0xFFFF); // Quotient 60
        Assert.Equal(0u, (_cpu.D[1] >> 16) & 0xFFFF); // Remainder 0
    }

    [Fact]
    public void Execute_BitOperations()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _cpu.Reset();

        // BSET #3, D0 -> $08C0 0003
        _mem.WriteWord(0x1000, 0x08C0);
        _mem.WriteWord(0x1002, 0x0003);

        _cpu.D[0] = 0;
        _cpu.ExecuteInstruction();

        Assert.True((_cpu.D[0] & (1 << 3)) != 0);
        Assert.True(_cpu.Z); // Bit was previously 0
    }

    [Fact]
    public void Execute_ZeroDivideException()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _mem.WriteLong(20, 0x00005000); // Vector 5 (Zero Divide) -> $5000
        _cpu.Reset();

        // DIVU D0, D1 ($82C0) with D0 = 0
        _mem.WriteWord(0x1000, 0x82C0);
        _cpu.D[0] = 0;
        _cpu.D[1] = 100;

        _cpu.ExecuteInstruction();

        Assert.Equal(0x00005000u, _cpu.PC); // Jumped to vector 5 handler
    }

    [Fact]
    public void Execute_LeaAndPea()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _cpu.Reset();

        // LEA ($10, A0), A1 -> $43E8 0010
        _mem.WriteWord(0x1000, 0x43E8);
        _mem.WriteWord(0x1002, 0x0010);
        _cpu.A[0] = 0x2000;

        _cpu.ExecuteInstruction();
        Assert.Equal(0x2010u, _cpu.A[1]);

        // PEA ($20, A1) -> $4869 0020
        _mem.WriteWord(0x1004, 0x4869);
        _mem.WriteWord(0x1006, 0x0020);

        _cpu.ExecuteInstruction();
        Assert.Equal(0x7FFFCu, _cpu.A[7]);
        Assert.Equal(0x2030u, _mem.ReadLong(0x7FFFC));
    }

    [Fact]
    public void Execute_InterruptHandling()
    {
        _mem.WriteLong(0, 0x80000);
        _mem.WriteLong(4, 0x1000);
        _mem.WriteLong(28 * 4, 0x00006000); // Vector 28 (Level 4 autovector) -> $6000
        _cpu.Reset();

        _cpu.InterruptMask = 3; // Allow level 4 interrupt
        _cpu.PendingInterruptLevel = 4;
        _cpu.PendingInterruptVector = 0; // Auto-vector

        _cpu.ExecuteInstruction();

        Assert.Equal(0x00006000u, _cpu.PC);
        Assert.Equal(4, _cpu.InterruptMask);
    }
}
