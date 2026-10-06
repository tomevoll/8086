using Emulator80386.App.CPU;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class Cpu386FullOpcodeTests
    {
        [Fact]
        public void TestBcdAdjustmentAndBitSearch()
        {
            var mem = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(mem, io);

            cpu.Reg.CS.Selector = 0; cpu.Reg.CS.Base = 0; cpu.Reg.EIP = 0x1000;

            // MOV AL, 0x35 (B0 35)
            // AAM 10      (D4 0A)
            // 0F BC C0    (BSF AX, AX)
            mem.Write8(0x1000, 0xB0); mem.Write8(0x1001, 0x35);
            mem.Write8(0x1002, 0xD4); mem.Write8(0x1003, 0x0A);
            mem.Write8(0x1004, 0x0F); mem.Write8(0x1005, 0xBC); mem.Write8(0x1006, 0xC0);

            cpu.Step(); // MOV
            Assert.Equal(0x35, cpu.Reg.AL);

            cpu.Step(); // AAM
            Assert.Equal(5, cpu.Reg.AH);
            Assert.Equal(3, cpu.Reg.AL);

            cpu.Step(); // BSF AX, AX (0x0503 -> lowest set bit is bit 0)
            Assert.Equal(0, cpu.Reg.AX);
        }

        [Fact]
        public void TestImulThreeOperandAndSetCc()
        {
            var mem = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(mem, io);

            cpu.Reg.CS.Selector = 0; cpu.Reg.CS.Base = 0; cpu.Reg.EIP = 0x1000;

            // MOV AX, 5     (B8 05 00)
            // IMUL CX, AX, 4 (69 C8 04 00)
            // CMP CX, 20    (81 F9 14 00)
            // SETE AL       (0F 94 C0)
            mem.Write8(0x1000, 0xB8); mem.Write8(0x1001, 0x05); mem.Write8(0x1002, 0x00);
            mem.Write8(0x1003, 0x69); mem.Write8(0x1004, 0xC8); mem.Write8(0x1005, 0x04); mem.Write8(0x1006, 0x00);
            mem.Write8(0x1007, 0x81); mem.Write8(0x1008, 0xF9); mem.Write8(0x1009, 0x14); mem.Write8(0x100A, 0x00);
            mem.Write8(0x100B, 0x0F); mem.Write8(0x100C, 0x94); mem.Write8(0x100D, 0xC0);

            cpu.Step(); // MOV AX, 5
            Assert.Equal(5, cpu.Reg.AX);

            cpu.Step(); // IMUL CX, AX, 4
            Assert.Equal(20, cpu.Reg.CX);

            cpu.Step(); // CMP CX, 20
            Assert.True(cpu.Reg.GetFlag(EFlags.ZF));

            cpu.Step(); // SETE AL
            Assert.Equal(1, cpu.Reg.AL);
        }

        [Fact]
        public void TestPushaPopaAndRealMode32BitLinearAddress()
        {
            var mem = new MemoryBus(16);
            var io = new IOPortBus();
            var cpu = new Cpu386(mem, io);

            cpu.Reg.CS.Selector = 0; cpu.Reg.CS.Base = 0; cpu.Reg.EIP = 0x1000;
            cpu.Reg.SS.Selector = 0; cpu.Reg.SS.Base = 0; cpu.Reg.ESP = 0x2000;

            cpu.Reg.EAX = 0x11112222;
            cpu.Reg.ECX = 0x33334444;

            // PUSHA (60)
            // POPA  (61)
            mem.Write8(0x1000, 0x60);
            mem.Write8(0x1001, 0x61);

            cpu.Step(); // PUSHA
            Assert.Equal((uint)(0x2000 - 16), cpu.Reg.ESP);

            cpu.Step(); // POPA
            Assert.Equal(0x2000u, cpu.Reg.ESP);
            Assert.Equal(0x11112222u, cpu.Reg.EAX);
            Assert.Equal(0x33334444u, cpu.Reg.ECX);

            // 32-bit linear address real mode test (0xB8000)
            uint linear = cpu.LinearAddress(cpu.Reg.DS, 0xB8000);
            Assert.Equal(0xB8000u, linear);
        }
    }
}
