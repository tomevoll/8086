using System.IO;
using Amiga500.Config;
using Xunit;

namespace Amiga500.Tests;

public class SaveStateTests
{
    [Fact]
    public void SaveAndLoadState_RestoresCpuAndMemoryState()
    {
        var config = new EmulatorConfig();
        byte[] rom = new byte[256 * 1024];
        var system = new AmigaSystem(config, rom);

        // Modify state
        system.Cpu.D[0] = 0x12345678;
        system.Cpu.A[0] = 0x00020000;
        system.Cpu.PC = 0x00004000;
        system.Memory.ChipRam[100] = 0xAB;

        string statePath = "test_state.json";
        SaveStateManager.SaveState(system, statePath);

        // Modify again before load
        system.Cpu.D[0] = 0;
        system.Memory.ChipRam[100] = 0;

        bool loaded = SaveStateManager.LoadState(system, statePath);

        Assert.True(loaded);
        Assert.Equal(0x12345678u, system.Cpu.D[0]);
        Assert.Equal(0x00020000u, system.Cpu.A[0]);
        Assert.Equal(0x00004000u, system.Cpu.PC);
        Assert.Equal(0xAB, system.Memory.ChipRam[100]);

        if (File.Exists(statePath)) File.Delete(statePath);
    }
}
