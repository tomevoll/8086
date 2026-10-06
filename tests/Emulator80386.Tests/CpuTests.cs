using Emulator80386.App.CPU;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class CpuTests
    {
        [Fact]
        public void TestMovImmediateAndAdd()
        {
            var memory = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(memory, io);

            // Set CS:EIP to 0x0000:0x1000
            cpu.Reg.CS.Selector = 0x0000;
            cpu.Reg.CS.Base = 0x0000;
            cpu.Reg.EIP = 0x1000;

            // Machine code:
            // MOV AL, 0x10 (B0 10)
            // ADD AL, 0x20 (04 20)
            // HLT         (F4)
            memory.Write8(0x1000, 0xB0);
            memory.Write8(0x1001, 0x10);
            memory.Write8(0x1002, 0x04);
            memory.Write8(0x1003, 0x20);
            memory.Write8(0x1004, 0xF4);

            cpu.Step(); // MOV AL, 0x10
            Assert.Equal(0x10, cpu.Reg.AL);

            cpu.Step(); // ADD AL, 0x20
            Assert.Equal(0x30, cpu.Reg.AL);
            Assert.False(cpu.Reg.GetFlag(EFlags.ZF));

            cpu.Step(); // HLT
            Assert.True(cpu.Halted);
        }

        [Fact]
        public void TestJmpAndStackPushPop()
        {
            var memory = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(memory, io);

            cpu.Reg.CS.Selector = 0x0000;
            cpu.Reg.CS.Base = 0x0000;
            cpu.Reg.SS.Selector = 0x0000;
            cpu.Reg.SS.Base = 0x0000;
            cpu.Reg.EIP = 0x1000;
            cpu.Reg.ESP = 0x8000;

            // Machine code:
            // MOV AX, 0x1234 (B8 34 12)
            // PUSH AX        (50)
            // POP BX         (5B)
            memory.Write8(0x1000, 0xB8);
            memory.Write8(0x1001, 0x34);
            memory.Write8(0x1002, 0x12);
            memory.Write8(0x1003, 0x50);
            memory.Write8(0x1004, 0x5B);

            cpu.Step(); // MOV AX, 0x1234
            Assert.Equal(0x1234, cpu.Reg.AX);

            cpu.Step(); // PUSH AX
            Assert.Equal(0x7FFE, (int)cpu.Reg.SP);

            cpu.Step(); // POP BX
            Assert.Equal(0x8000, (int)cpu.Reg.SP);
            Assert.Equal(0x1234, cpu.Reg.BX);
        }

        [Fact]
        public void Test32BitOperandOverride()
        {
            var memory = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(memory, io);

            cpu.Reg.CS.Selector = 0x0000;
            cpu.Reg.CS.Base = 0x0000;
            cpu.Reg.EIP = 0x1000;

            // 66 B8 78 56 34 12 (MOV EAX, 0x12345678)
            memory.Write8(0x1000, 0x66);
            memory.Write8(0x1001, 0xB8);
            memory.Write8(0x1002, 0x78);
            memory.Write8(0x1003, 0x56);
            memory.Write8(0x1004, 0x34);
            memory.Write8(0x1005, 0x12);

            cpu.Step();
            Assert.Equal(0x12345678u, cpu.Reg.EAX);
        }
    }
}
