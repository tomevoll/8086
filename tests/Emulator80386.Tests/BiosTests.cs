using Emulator80386.App;
using Emulator80386.App.Config;
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
    }
}
