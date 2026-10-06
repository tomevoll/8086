using Emulator80386.App.IO;
using Xunit;

namespace Emulator80386.Tests
{
    public class IoTests
    {
        [Fact]
        public void TestKeyboardControllerSelfTestAndScancodeQueue()
        {
            var kbd = new KeyboardController8042();
            var ioBus = new IOPortBus();
            ioBus.RegisterDevice(0x60, kbd);
            ioBus.RegisterDevice(0x64, kbd);

            // Send self-test command 0xAA to port 0x64
            ioBus.Out8(0x64, 0xAA);
            byte status = ioBus.In8(0x64);
            Assert.True((status & 0x01) != 0); // Data available
            byte result = ioBus.In8(0x60);
            Assert.Equal(0x55, result);

            // Queue a keypress (e.g. key 'A' scancode 0x1E)
            kbd.EnqueueScancode(0x1E);
            status = ioBus.In8(0x64);
            Assert.True((status & 0x01) != 0);
            byte scancode = ioBus.In8(0x60);
            Assert.Equal(0x1E, scancode);
        }

        [Fact]
        public void TestPic8259Mask()
        {
            var masterPic = new Pic8259(isSlave: false);
            var ioBus = new IOPortBus();
            ioBus.RegisterDevice(0x20, masterPic);
            ioBus.RegisterDevice(0x21, masterPic);

            // Write interrupt mask
            ioBus.Out8(0x21, 0xFD); // Unmask IRQ1 (keyboard)
            Assert.Equal(0xFD, ioBus.In8(0x21));
        }

        [Fact]
        public void TestCmosRtcRamSizeRead()
        {
            var cmos = new CmosRtc(16); // 16MB
            var ioBus = new IOPortBus();
            ioBus.RegisterDevice(0x70, cmos);
            ioBus.RegisterDevice(0x71, cmos);

            // Select index 0x15 (Base memory low byte)
            ioBus.Out8(0x70, 0x15);
            byte low = ioBus.In8(0x71);

            // Select index 0x16 (Base memory high byte)
            ioBus.Out8(0x70, 0x16);
            byte high = ioBus.In8(0x71);

            ushort baseKb = (ushort)(low | (high << 8));
            Assert.Equal(640, baseKb);
        }

        [Fact]
        public void TestSystemControlPort92()
        {
            var sysControl = new SystemControlPort();
            var ioBus = new IOPortBus();
            ioBus.RegisterDevice(0x92, sysControl);

            Assert.Equal(0x02, ioBus.In8(0x92)); // Default A20 enabled
            ioBus.Out8(0x92, 0x00);
            Assert.Equal(0x00, ioBus.In8(0x92));
        }
    }
}
