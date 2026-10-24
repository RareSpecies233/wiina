using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wiina.App.Interop;

/// <summary>
/// In-process import address table (IAT) hook for <c>user32!SetCursor</c>.
/// </summary>
/// <remarks>
/// WinUI 3 draws the pointer from the CoreMessaging / Content input stack: a
/// thread that is not the XAML thread re-applies the arrow on every frame, so
/// <c>SetCursor</c>/<c>ShowCursor</c>/<c>WM_SETCURSOR</c>/<c>ProtectedCursor</c>
/// issued from our own code have no visible effect (measured in v1.3.0/v1.3.1).
/// However the re-application itself is a <c>SetCursor</c> call made by the
/// input thread — and <c>SetCursor</c> only changes the cursor of the calling
/// thread. Redirecting those calls to <c>SetCursor(NULL)</c> therefore hides the
/// pointer on exactly the thread that owns it.
///
/// The hook only rewrites IAT slots, never code, so it cannot corrupt another
/// module's instructions. It is installed once, is idempotent, and can be
/// disabled with <c>WIINA_CURSOR_HOOK=0</c>. If no module imports
/// <c>SetCursor</c> from user32.dll the hook reports that and leaves the
/// fallback machinery untouched.
/// </remarks>
internal static unsafe class CursorHider
{
    private const int IdcArrow = 32512;
    private const uint PageReadWrite = 0x04;
    private const uint ModuleHandleFromAddress = 0x00000004;

    private static nint _realSetCursor;
    private static volatile bool _hidden;
    private static volatile bool _installed;
    private static int _intercepts;
    private static Action<string>? _log;

    // Only modules that can own the WinUI/Content pointer are rewritten. A
    // process-wide sweep is tempting but far riskier: one module that caches the
    // SetCursor pointer or whose IAT page cannot be made writable turns a
    // cosmetic feature into an access violation during startup.
    private static readonly string[] CandidateModules =
    {
        "Microsoft.UI.Input.dll",
        "Microsoft.InputStateManager.dll",
        "CoreMessaging.dll",
        "Microsoft.UI.Content.dll",
        "Microsoft.UI.Xaml.dll",
        "Microsoft.UI.Windowing.Core.dll",
        "Windows.UI.Immersive.dll",
    };

    // Caller sampling must not allocate: it runs on the input thread inside a
    // reverse P/Invoke. A fixed ring of return addresses is resolved to module
    // names later from a normal managed thread.
    private static readonly nint[] RecentCallers = new nint[8];
    private static int _callerIndex;

    private static readonly ConcurrentDictionary<string, int> PatchedModules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int> CallerModules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every module that imports a function named SetCursor (scan mode).</summary>
    private static readonly ConcurrentDictionary<string, int> ScanHits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True for every module that can provide the Win32 <c>SetCursor</c>: the
    /// classic <c>user32.dll</c> and the API-set forwarders
    /// (<c>ext-ms-win-ntuser-cursor-*</c>) that WinUI/CoreMessaging import from.
    /// </summary>
    private static bool IsCursorSource(string moduleName)
    {
        return moduleName.Contains("user32", StringComparison.OrdinalIgnoreCase)
            || moduleName.Contains("ntuser", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the IAT was rewritten at least once.</summary>
    public static bool Installed => _installed;

    /// <summary>Number of intercepted <c>SetCursor</c> calls while hidden.</summary>
    public static int Intercepts => Volatile.Read(ref _intercepts);

    /// <summary>Modules whose IAT was patched, with the number of slots.</summary>
    public static IReadOnlyDictionary<string, int> Patched => PatchedModules;

    /// <summary>Modules that were observed calling the intercepted pointer.</summary>
    public static IReadOnlyDictionary<string, int> Callers => CallerModules;

    /// <summary>
    /// Installs the hook. Safe to call repeatedly and from any thread; the
    /// rewritten slots are process-wide. Returns true when at least one import
    /// slot now points at the detour.
    /// </summary>
    public static bool Install(Action<string>? log = null)
    {
        if (log is not null)
        {
            _log = log;
        }

        if (Environment.GetEnvironmentVariable("WIINA_CURSOR_HOOK") == "0")
        {
            _log?.Invoke("cursor-hook: disabled by WIINA_CURSOR_HOOK=0");
            return false;
        }

        if (_installed)
        {
            return true;
        }

        try
        {
            nint user32 = GetModuleHandleW("user32.dll");
            if (user32 == 0)
            {
                _log?.Invoke("cursor-hook: user32.dll not loaded");
                return false;
            }

            nint proc = GetProcAddress(user32, "SetCursor");
            if (proc == 0)
            {
                _log?.Invoke("cursor-hook: user32!SetCursor not found");
                return false;
            }

            _realSetCursor = proc;
            nint detour = (nint)(delegate* unmanaged[Stdcall]<nint, nint>)&HookSetCursor;

            int slots = 0;
            bool scanOnly = Environment.GetEnvironmentVariable("WIINA_CURSOR_HOOK") == "scan";
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                try
                {
                    nint baseAddress = module.BaseAddress;
                    if (baseAddress == 0)
                    {
                        continue;
                    }

                    string name = module.ModuleName;
                    bool candidate = Array.Exists(
                        CandidateModules,
                        entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (!scanOnly && !candidate)
                    {
                        continue;
                    }

                    int patched = PatchModule(baseAddress, name, detour, patch: !scanOnly);
                    if (patched > 0)
                    {
                        slots += patched;
                        PatchedModules.AddOrUpdate(name, patched, (_, existing) => existing + patched);
                    }
                }
                catch
                {
                    // A module can unload between enumeration and parsing; skip it.
                }
            }

            if (scanOnly)
            {
                _log?.Invoke(
                    $"cursor-hook: scan-only candidates=[{DescribeModules()}]" +
                    $" imports=[{string.Join(",", ScanHits.Select(pair => $"{pair.Key}({pair.Value})"))}]");
                return false;
            }

            _installed = slots > 0;
            _log?.Invoke(
                $"cursor-hook: installed={_installed} slots={slots} modules={PatchedModules.Count}" +
                DescribeModules());
            return _installed;
        }
        catch (Exception error)
        {
            _log?.Invoke($"cursor-hook: install failed: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    /// <summary>
    /// Switches the hook between "force a hidden cursor" and "pass through".
    /// The state is process-wide because the intercepted calls arrive on the
    /// input thread while the decision is made on the UI thread.
    /// </summary>
    public static void SetHidden(bool hidden, Action<string>? log = null)
    {
        if (log is not null)
        {
            _log = log;
        }

        if (hidden)
        {
            Install(log);
        }

        if (_hidden == hidden)
        {
            return;
        }

        _hidden = hidden;
        if (_realSetCursor != 0)
        {
            // Immediate effect from the caller's own thread as well: hides the
            // pointer right away instead of waiting for the next input frame.
            SetCursorNative(hidden ? 0 : LoadCursorW(0, IdcArrow));
        }

        ResolveCallers();
        _log?.Invoke(
            $"cursor-hook: hidden={hidden} installed={_installed} intercepts={Intercepts}" +
            $" callers=[{DescribeCallers()}]" + DescribeModules());
    }

    /// <summary>Human readable summary for diagnostics.</summary>
    public static string Describe()
    {
        string patched = PatchedModules.Count == 0
            ? "none"
            : string.Join(",", PatchedModules.Select(pair => $"{pair.Key}:{pair.Value}"));
        string callers = CallerModules.Count == 0
            ? "none"
            : string.Join(",", CallerModules.Select(pair => $"{pair.Key}:{pair.Value}"));
        return $"installed={_installed} hidden={_hidden} intercepts={Intercepts} patched=[{patched}] callers=[{callers}]";
    }

    private static string DescribeModules()
    {
        if (PatchedModules.Count == 0)
        {
            return string.Empty;
        }

        return " patched=" + string.Join(
            ",",
            PatchedModules.Select(pair => $"{pair.Key}({pair.Value})"));
    }

    private static nint SetCursorNative(nint cursor)
    {
        try
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint>)_realSetCursor)(cursor);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// The detour. Runs on whichever thread called <c>SetCursor</c>, which for
    /// the WinUI input stack is the thread that actually owns the cursor.
    /// It must never throw and must not allocate on the hot path.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint HookSetCursor(nint hCursor)
    {
        try
        {
            if (!_hidden)
            {
                return ((delegate* unmanaged[Stdcall]<nint, nint>)_realSetCursor)(hCursor);
            }

            int count = Interlocked.Increment(ref _intercepts);
            if (count <= 8)
            {
                // Allocation-free caller sampling; resolved to names later.
                nint frame = 0;
                _ = RtlCaptureStackBackTrace(1, 1, &frame, out _);
                int slot = (count - 1) & 7;
                Volatile.Write(ref RecentCallers[slot], frame);
                _callerIndex = Math.Max(_callerIndex, slot + 1);
            }

            return ((delegate* unmanaged[Stdcall]<nint, nint>)_realSetCursor)(0);
        }
        catch
        {
            return hCursor;
        }
    }

    /// <summary>
    /// Resolves the sampled return addresses to module names. Called from a
    /// normal managed thread (diagnostics only), never from the detour itself.
    /// </summary>
    public static void ResolveCallers()
    {
        CallerModules.Clear();
        int count = Math.Min(_callerIndex, RecentCallers.Length);
        for (int i = 0; i < count; i++)
        {
            nint frame = Volatile.Read(ref RecentCallers[i]);
            if (frame == 0)
            {
                continue;
            }

            nint module = 0;
            if (GetModuleHandleExW(ModuleHandleFromAddress, frame, out module) && module != 0)
            {
                string? name = ModuleName(module);
                if (!string.IsNullOrEmpty(name))
                {
                    string leaf = Path.GetFileName(name);
                    CallerModules.AddOrUpdate(leaf, 1, (_, existing) => existing + 1);
                }
            }
        }
    }

    private static string DescribeCallers()
    {
        return CallerModules.Count == 0
            ? "unknown"
            : string.Join(",", CallerModules.Select(pair => $"{pair.Key}({pair.Value})"));
    }

    private static string? ModuleName(nint module)
    {
        char* buffer = stackalloc char[260];
        uint length = GetModuleFileNameW(module, buffer, 260);
        return length == 0 ? null : new string(buffer, 0, (int)length);
    }

    /// <summary>
    /// Rewrites every <c>SetCursor</c> slot of one module's user32 import
    /// descriptors. Returns the number of patched slots.
    /// </summary>
    private static int PatchModule(nint baseAddress, string moduleName, nint detour, bool patch)
    {
        if (!IsReadable(baseAddress, 0x40))
        {
            return 0;
        }

        if (*(ushort*)baseAddress != 0x5A4D) // MZ
        {
            return 0;
        }

        int lfanew = *(int*)(baseAddress + 0x3C);
        if (lfanew <= 0 || lfanew > 0x100000)
        {
            return 0;
        }

        nint nt = baseAddress + lfanew;
        if (!IsReadable(nt, 24 + 128) || *(uint*)nt != 0x00004550) // PE\0\0
        {
            return 0;
        }

        nint optional = nt + 24;
        ushort magic = *(ushort*)optional;
        int dataDirectoryOffset = magic switch
        {
            0x20B => 112, // PE32+
            0x10B => 96,  // PE32
            _ => -1,
        };

        if (dataDirectoryOffset < 0 || !IsReadable(optional + dataDirectoryOffset, 16))
        {
            return 0;
        }

        // DataDirectory[1] is the import table; [0] is the export table.
        uint importRva = *(uint*)(optional + dataDirectoryOffset + 8);
        uint importSize = *(uint*)(optional + dataDirectoryOffset + 12);
        if (importRva == 0)
        {
            return 0;
        }

        nint importAddress = baseAddress + (int)importRva;
        if (!IsReadable(importAddress, sizeof(ImageImportDescriptor)))
        {
            return 0;
        }

        int maxDescriptors = importSize >= sizeof(ImageImportDescriptor)
            ? (int)(importSize / sizeof(ImageImportDescriptor))
            : 0;
        if (maxDescriptors <= 0 || maxDescriptors > 1024)
        {
            maxDescriptors = 64;
        }

        int patched = 0;
        for (int descriptorIndex = 0; descriptorIndex < maxDescriptors; descriptorIndex++)
        {
            nint descriptorAddress = importAddress + (descriptorIndex * sizeof(ImageImportDescriptor));
            if (!IsReadable(descriptorAddress, sizeof(ImageImportDescriptor)))
            {
                break;
            }

            var descriptor = (ImageImportDescriptor*)descriptorAddress;
            if (descriptor->Name == 0)
            {
                break;
            }

            string? module = ReadAnsi(baseAddress + (int)descriptor->Name);
            if (module is not null && IsCursorSource(module))
            {
                nint iat = baseAddress + (int)descriptor->FirstThunk;
                nint names = descriptor->OriginalFirstThunk != 0
                    ? baseAddress + (int)descriptor->OriginalFirstThunk
                    : iat;

                for (int index = 0; index < 4096; index++)
                {
                    if (!IsReadable(names + (index * sizeof(nint)), sizeof(nint)))
                    {
                        break;
                    }

                    nint entry = *(nint*)(names + (index * sizeof(nint)));
                    if (entry == 0)
                    {
                        break;
                    }

                    if ((ulong)entry >= 0x8000000000000000UL)
                    {
                        continue; // imported by ordinal, no name to compare
                    }

                    string? function = ReadAnsi(baseAddress + (int)entry + 2);
                    if (function is null || !function.Equals("SetCursor", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ScanHits.AddOrUpdate($"{moduleName} <- {module}", 1, (_, existing) => existing + 1);

                    if (!patch)
                    {
                        patched++;
                        continue;
                    }

                    nint slot = iat + (index * sizeof(nint));
                    if (!IsReadable(slot, sizeof(nint)))
                    {
                        continue;
                    }

                    if (*(nint*)slot != detour
                        && VirtualProtect(slot, (nint)sizeof(nint), PageReadWrite, out uint oldProtect))
                    {
                        *(nint*)slot = detour;
                        VirtualProtect(slot, (nint)sizeof(nint), oldProtect, out _);
                        patched++;
                    }
                }
            }
        }

        return patched;
    }

    private static string? ReadAnsi(nint address)
    {
        if (address == 0)
        {
            return null;
        }

        const int MaxLength = 256;
        byte* buffer = stackalloc byte[MaxLength];
        for (int index = 0; index < MaxLength; index++)
        {
            // Validate every byte: a malformed import table must not raise an
            // uncatchable AccessViolationException.
            if (!IsReadable(address + index, 1))
            {
                return null;
            }

            byte value = *(byte*)(address + index);
            if (value == 0)
            {
                return index == 0
                    ? null
                    : System.Text.Encoding.ASCII.GetString(buffer, index);
            }

            buffer[index] = value;
        }

        return null;
    }

    /// <summary>True when every byte of the range is committed and readable.</summary>
    private static bool IsReadable(nint address, int size)
    {
        if (address == 0 || size <= 0)
        {
            return false;
        }

        nint end = address + size;
        nint cursor = address;
        nint structSize = Marshal.SizeOf<MemoryBasicInformation>();
        while (cursor < end)
        {
            if (VirtualQuery(cursor, out MemoryBasicInformation info, structSize) == 0)
            {
                return false;
            }

            const uint MemCommit = 0x1000;
            const uint PageNoAccess = 0x01;
            const uint PageGuard = 0x100;
            uint protection = info.Protect;
            if (info.State != MemCommit
                || (protection & 0xFF) == PageNoAccess
                || (protection & PageGuard) != 0)
            {
                return false;
            }

            nint regionEnd = info.BaseAddress + info.RegionSize;
            if (regionEnd <= cursor)
            {
                return false;
            }

            cursor = regionEnd;
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImageImportDescriptor
    {
        public uint OriginalFirstThunk;
        public uint TimeDateStamp;
        public uint ForwarderChain;
        public uint Name;
        public uint FirstThunk;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualQuery(nint address, out MemoryBasicInformation info, nint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string procName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameW(nint module, char* fileName, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nint size, uint newProtect, out uint oldProtect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadCursorW(nint instance, int cursorName);

    [DllImport("ntdll.dll")]
    private static extern uint RtlCaptureStackBackTrace(uint framesToSkip, uint framesToCapture, nint* backTrace, out uint hash);
}
