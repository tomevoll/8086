using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTests
    {
        [Fact]
        public void TestBiosBootSequenceAndVgaPostMessage()
        {
            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = "non_existent.bin" };
            var motherboard = new Motherboard(config);

            motherboard.Boot();

            // Run CPU steps to execute BIOS POST instructions
            motherboard.Step(instructionsCount: 500);

            // Read VGA text mode memory at 0xB8000 (VRAM offset 0x18000)
            byte firstChar = motherboard.Memory.Read8(0xB8000);
            byte firstAttr = motherboard.Memory.Read8(0xB8001);

            Assert.Equal((byte)'P', firstChar);
            Assert.Equal(0x1F, firstAttr); // White on Blue
        }

        [Fact]
        public void TestSplitLowHighBiosRomLoading()
        {
            string lowFile = Path.GetTempFileName();
            string highFile = Path.GetTempFileName();

            try
            {
                // Low bytes (even indices): 0xAA, 0xCC
                // High bytes (odd indices): 0xBB, 0xDD
                File.WriteAllBytes(lowFile, new byte[] { 0xAA, 0xCC });
                File.WriteAllBytes(highFile, new byte[] { 0xBB, 0xDD });

                var config = new EmulatorConfig
                {
                    RomLowPath = lowFile,
                    RomHighPath = highFile
                };

                byte[] bios = BiosLoader.LoadOrGenerateBios(config);

                Assert.Equal(4, bios.Length);
                Assert.Equal(0xAA, bios[0]);
                Assert.Equal(0xBB, bios[1]);
                Assert.Equal(0xCC, bios[2]);
                Assert.Equal(0xDD, bios[3]);
            }
            finally
            {
                if (File.Exists(lowFile)) File.Delete(lowFile);
                if (File.Exists(highFile)) File.Delete(highFile);
            }
        }
    }
}
