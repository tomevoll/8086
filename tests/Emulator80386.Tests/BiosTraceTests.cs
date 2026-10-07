using System;
using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTraceTests
    {
        [Fact]
        public void TestAwardBiosPostSequence()
        {
            string? biosFile = RealBiosTests.FindBiosFile();
            if (biosFile == null || !File.Exists(biosFile)) return;

            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = biosFile };
            var mb = new Motherboard(config);
            mb.Boot();

            byte lastPostCode = 0;
            for (int step = 0; step < 500000; step++)
            {
                if (mb.PostDma.LastPostCode != lastPostCode)
                {
                    lastPostCode = mb.PostDma.LastPostCode;
                    Console.WriteLine($"[Award BIOS Step {step:D6}] POST Code Port 0x80 = 0x{lastPostCode:X2}");
                }

                if (mb.Cpu.Halted) break;
                mb.Cpu.Step();
            }

            string screen = mb.Vga.DumpTextScreen(mb.Memory.Vram);
            Console.WriteLine("=== Award BIOS VRAM OUTPUT ===");
            Console.WriteLine(screen);
            Console.WriteLine("=============================");
        }
    }
}
