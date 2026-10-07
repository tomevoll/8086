using System;
using Emulator80386.App.Config;
using Emulator80386.App.CPU;
using Emulator80386.App.Disk;
using Emulator80386.App.GFX;
using Emulator80386.App.IO;
using Emulator80386.App.Memory;

namespace Emulator80386.App
{
    public class Motherboard
    {
        public EmulatorConfig Config { get; }
        public MemoryBus Memory { get; }
        public IOPortBus IOPort { get; }
        public Cpu386 Cpu { get; }
        public KeyboardController8042 Keyboard { get; }
        public Pic8259 PicMaster { get; }
        public Pic8259 PicSlave { get; }
        public Pit8253 Pit { get; }
        public CmosRtc Cmos { get; }
        public SystemControlPort SystemControl { get; }
        public IdeController Ide { get; }
        public FolderDiskController DiskC { get; }
        public VgaRenderer Vga { get; }
        public VgaController VgaIo { get; }
        public PostAndDmaController PostDma { get; }

        public Motherboard(EmulatorConfig config)
        {
            Config = config ?? new EmulatorConfig();
            Memory = new MemoryBus(Config.RamSizeMB);
            IOPort = new IOPortBus();

            Keyboard = new KeyboardController8042();
            Keyboard.OnCpuReset = ResetCpu;
            IOPort.RegisterDevice(0x60, Keyboard);
            IOPort.RegisterDevice(0x64, Keyboard);

            PicMaster = new Pic8259(isSlave: false);
            PicSlave = new Pic8259(isSlave: true);
            IOPort.RegisterDevice(0x20, PicMaster);
            IOPort.RegisterDevice(0x21, PicMaster);
            IOPort.RegisterDevice(0xA0, PicSlave);
            IOPort.RegisterDevice(0xA1, PicSlave);

            Pit = new Pit8253();
            IOPort.RegisterDevice(0x40, Pit);
            IOPort.RegisterDevice(0x43, Pit);

            Cmos = new CmosRtc(Config.RamSizeMB);
            IOPort.RegisterDevice(0x70, Cmos);
            IOPort.RegisterDevice(0x71, Cmos);

            SystemControl = new SystemControlPort();
            IOPort.RegisterDevice(0x61, SystemControl);
            IOPort.RegisterDevice(0x92, SystemControl);
            IOPort.RegisterDevice(0xDF, SystemControl);
            IOPort.RegisterDevice(0xEE, SystemControl);

            DiskC = new FolderDiskController(Config.DriveCFolder);

            Ide = new IdeController(DiskC);
            for (ushort p = 0x1F0; p <= 0x1F7; p++)
            {
                IOPort.RegisterDevice(p, Ide);
            }
            IOPort.RegisterDevice(0x3F6, Ide);

            Vga = new VgaRenderer();
            VgaIo = new VgaController();
            for (ushort p = 0x3B0; p <= 0x3DF; p++)
            {
                IOPort.RegisterDevice(p, VgaIo);
            }

            PostDma = new PostAndDmaController();
            for (ushort p = 0x00; p <= 0x0F; p++) IOPort.RegisterDevice(p, PostDma);
            for (ushort p = 0x80; p <= 0x8F; p++) IOPort.RegisterDevice(p, PostDma);
            for (ushort p = 0xC0; p <= 0xDE; p++) IOPort.RegisterDevice(p, PostDma);

            Cpu = new Cpu386(Memory, IOPort);

            byte[] biosRom = BiosLoader.LoadOrGenerateBios(Config);
            Memory.LoadBiosRom(biosRom);

            byte[] vgaRom = BiosLoader.LoadOrGenerateVgaOptionRom(Config.VgaRomPath);
            Memory.LoadVgaOptionRom(vgaRom);

            byte[] ideRom = BiosLoader.LoadOrGenerateIdeOptionRom(Config.IdeRomPath);
            Memory.LoadIdeOptionRom(ideRom);
        }

        public void Boot()
        {
            ResetCpu();
        }

        public void ResetCpu()
        {
            Cpu.Halted = false;
            Cpu.Reg.CR0 = 0; // Real mode
            Cpu.Reg.CS.Selector = 0xF000;
            Cpu.Reg.CS.Base = 0xF0000;
            Cpu.Reg.EIP = 0xFFF0;
        }

        public void Step(int instructionsCount = 1)
        {
            for (int i = 0; i < instructionsCount; i++)
            {
                if (Cpu.Halted) break;
                Cpu.Step();
            }
            Vga.RenderTextMode(Memory.Vram);
        }
    }
}
