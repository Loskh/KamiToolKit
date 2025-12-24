using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KamiToolKit.Nodes.Component {
    internal unsafe static class UldGenerator {
        private delegate ResourceHandle* GetResourceSyncDelegate(ResourceCategory* category, uint* type, byte* path, nint para);
        private delegate void BuildWidgetDelegate(AtkUldManager* AtkUldManager, byte* a1, byte* a2);

        private static GetResourceSyncDelegate getResourceSync = Marshal.GetDelegateForFunctionPointer<GetResourceSyncDelegate>(DalamudInterface.Instance.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 85 C0 40 0F B6 CF"));
        private static BuildWidgetDelegate buildWidget = Marshal.GetDelegateForFunctionPointer<BuildWidgetDelegate>(DalamudInterface.Instance.SigScanner.ScanText("E8 ?? ?? ?? ?? F6 83 ?? ?? ?? ?? ?? 75 17"));

        private static readonly Dictionary<string, nint> UldManagerCache = new();

        private static unsafe ResourceHandle* GetResourceSyncWrapper(ResourceCategory category, uint type, string path, nint para) {
            var catagoryPtr = stackalloc uint[1];
            var typePtr = stackalloc uint[1];
            var strPtr = stackalloc byte[path.Length];
            Marshal.WriteInt32((nint)catagoryPtr, (int)category);
            Marshal.WriteInt32((nint)typePtr, (int)type);

            int utf8StringLengthname = Encoding.UTF8.GetByteCount(path);
            Span<byte> nameBytes = utf8StringLengthname <= 512 ? stackalloc byte[utf8StringLengthname + 1] : new byte[utf8StringLengthname + 1];
            Encoding.UTF8.GetBytes(path, nameBytes);
            nameBytes[utf8StringLengthname] = 0;
            fixed (byte* namePtr = nameBytes) {
                return getResourceSync((ResourceCategory*)catagoryPtr, typePtr, namePtr, para);
            }
        }

        public static AtkUldManager* GetUldManager(string uldPath) {
            if (UldManagerCache.TryGetValue(uldPath, out var uldPtr)) {
                Log.Debug($"Got UldManager in cache:{uldPath}");
                return (AtkUldManager*)uldPtr;
            }
            Log.Debug($"Building UldManager:{uldPath}");
            var uldResourceHandle = GetResourceSyncWrapper(ResourceCategory.Ui, 0x756C64, uldPath, nint.Zero);
            var resourcePtr = uldResourceHandle->GetData();
            var newUldManager = (AtkUldManager*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldManager), 8uL);
            IMemorySpace.Memset(newUldManager, 0, (ulong)sizeof(AtkUldManager));
            newUldManager->UldResourceHandle = uldResourceHandle;
            newUldManager->ResourceFlags = AtkUldManagerResourceFlag.Initialized;
            newUldManager->BaseType = AtkUldManagerBaseType.Widget;
            buildWidget(newUldManager, (byte*)(char*)&resourcePtr[*((uint*)resourcePtr + 2)], (byte*)(char*)&resourcePtr[*((uint*)resourcePtr + 3)]);
            UldManagerCache[uldPath] = (nint)newUldManager;
            return newUldManager;
        }
    }
}
