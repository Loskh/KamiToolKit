using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes.Component;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KamiToolKit.Nodes;

public abstract unsafe class ComponentNode(NodeType nodeType,bool buildVirtualTable=true) : NodeBase<AtkComponentNode>(nodeType,buildVirtualTable) {
    public abstract AtkComponentBase* ComponentBase { get; }
    public abstract AtkUldComponentDataBase* DataBase { get; }
    public abstract AtkComponentNode* InternalComponentNode { get; }
}

public static unsafe class ComponentNodeFunction {

    public delegate void BuildComponentDelegate(AtkUldManager* AtkUldManager, nint res, uint componentId, ushort* timeline, AtkUldAsset* uldAsset, AtkUldPartsList* uldPartList, ushort assetNum, ushort partsNum, AtkResourceRendererManager* renderManager, bool a, bool b);
    public delegate void BuildComponentTimelineDelegate(AtkUldManager* AtkUldManager, nint res, uint componentId, AtkTimelineManager* timelineManager, AtkResNode* resNode);

    public static BuildComponentDelegate BuildComponent = Marshal.GetDelegateForFunctionPointer<BuildComponentDelegate>(DalamudInterface.Instance.SigScanner.ScanText("E8 ?? ?? ?? ?? 49 8B 86 ?? ?? ?? ?? 48 85 C0 74 21"));
    public static BuildComponentTimelineDelegate BuildComponentTimeline = Marshal.GetDelegateForFunctionPointer<BuildComponentTimelineDelegate>(DalamudInterface.Instance.SigScanner.ScanText("48 89 6C 24 ?? 48 89 74 24 ?? 41 56 48 83 EC 30 4C 89 49"));
}

public abstract unsafe class ComponentNode<T, TU> : ComponentNode where T : unmanaged, ICreatable where TU : unmanaged {

    public readonly CollisionNode CollisionNode;
    public override AtkComponentBase* ComponentBase => (AtkComponentBase*)Component;
    public override AtkUldComponentDataBase* DataBase => (AtkUldComponentDataBase*)Data;
    public override AtkComponentNode* InternalComponentNode => (AtkComponentNode*)ResNode;

    protected ComponentNode() : base(NodeType.Component) {
        Component = NativeMemoryHelper.Create<T>();
        var componentBase = (AtkComponentBase*)Component;

        Data = NativeMemoryHelper.UiAlloc<TU>();

        componentBase->Initialize();

        CollisionNode = new CollisionNode {
            NodeId = 1,
            LinkedComponent = componentBase,
            NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.HasCollision |
                        NodeFlags.RespondToMouse | NodeFlags.Focusable | NodeFlags.EmitsEvents | NodeFlags.Fill,
        };

        CollisionNode.ResNode->ParentNode = ResNode;
        CollisionNode.ParentUldManager = &((AtkComponentBase*)Component)->UldManager;

        ChildNodes.Add(CollisionNode);

        componentBase->OwnerNode = Node;
        componentBase->ComponentFlags = 1;

        ref var uldManager = ref componentBase->UldManager;

        uldManager.Objects = (AtkUldObjectInfo*)NativeMemoryHelper.UiAlloc<AtkUldComponentInfo>();
        ref var objects = ref uldManager.Objects;
        uldManager.ObjectCount = 1;

        objects->NodeList = (AtkResNode**)NativeMemoryHelper.Malloc(8);
        objects->NodeList[0] = CollisionNode;
        objects->NodeCount = 1;
        objects->Id = 1001;

        uldManager.InitializeResourceRendererManager();
        uldManager.RootNode = CollisionNode;

        uldManager.UpdateDrawNodeList();
        uldManager.ResourceFlags = AtkUldManagerResourceFlag.Initialized | AtkUldManagerResourceFlag.ArraysAllocated;
        uldManager.LoadedState = AtkLoadState.Loaded;
    }
    protected ComponentNode(string uldPath, uint componentId, ComponentType componentType, uint collisionNodeId) : base(NodeType.Component,false) {
        Log.Debug($"Building ComponentNode:{uldPath} {componentId} {componentType}");
        var uldManager = UldGenerator.GetUldManager(uldPath);
        InternalComponentNode->AtkResNode.Ctor();
        InternalComponentNode->Type = unchecked((NodeType)componentId);
        Component = (T*)uldManager->CreateAtkComponent(componentType);
        var componentBase = (AtkComponentBase*)Component;
        componentBase->Initialize();
        componentBase->OwnerNode = InternalComponentNode;
        componentBase->ComponentFlags = 1;
        componentBase->UldManager.UldResourceHandle = uldManager->UldResourceHandle;
        componentBase->UldManager.ResourceFlags = AtkUldManagerResourceFlag.Initialized;

        InternalComponentNode->NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.EmitsEvents;

        var resourcePtr = uldManager->UldResourceHandle->GetData();
        var componentResourcePtr = (byte*)(char*)&resourcePtr[*((uint*)resourcePtr + 2)];
        var tinelineNum = stackalloc ushort[1];
        tinelineNum[0] = 223;
        ComponentNodeFunction.BuildComponent((AtkUldManager*)Unsafe.AsPointer(ref componentBase->UldManager), (nint)componentResourcePtr, componentId, tinelineNum, uldManager->Assets, uldManager->PartsList, uldManager->AssetCount, uldManager->PartsListCount, uldManager->ResourceRendererManager, true, true);
        ComponentNodeFunction.BuildComponentTimeline((AtkUldManager*)Unsafe.AsPointer(ref componentBase->UldManager), (nint)componentResourcePtr, componentId, uldManager->TimelineManager, (AtkResNode*)InternalComponentNode);
        BuildVirtualTable();
        CollisionNode = new CollisionNode(componentBase->UldManager.SearchNodeById(collisionNodeId)->GetAsAtkCollisionNode());
        CollisionNode.ResNode->ParentNode = ResNode;
        CollisionNode.ParentUldManager = &((AtkComponentBase*)Component)->UldManager;
        CollisionNode.LinkedComponent = (AtkComponentBase*)Component;
        CollisionNode.NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.HasCollision |
                        NodeFlags.RespondToMouse | NodeFlags.Focusable | NodeFlags.EmitsEvents | NodeFlags.Fill;
        uldManager->InitializeResourceRendererManager();
        uldManager->UpdateDrawNodeList();
        uldManager->ResourceFlags = AtkUldManagerResourceFlag.Initialized | AtkUldManagerResourceFlag.ArraysAllocated;
        uldManager->LoadedState = AtkLoadState.Loaded;
    }

    protected override void Dispose(bool disposing, bool isNativeDestructor) {
        if (disposing) {
            if (!isNativeDestructor) {
                NativeMemoryHelper.UiFree(Data);
                Data = null;

                ComponentBase->Deinitialize();
                ComponentBase->Dtor(1);
                Node->Component = null;
            }

            base.Dispose(disposing, isNativeDestructor);
        }
    }

    public static implicit operator AtkEventListener*(ComponentNode<T, TU> node) => &node.ComponentBase->AtkEventListener;

    protected void SetInternalComponentType(ComponentType type) {
        var componentInfo = (AtkUldComponentInfo*)ComponentBase->UldManager.Objects;

        componentInfo->ComponentType = type;
    }

    protected void InitializeComponentEvents() {
        ComponentBase->InitializeFromComponentData(DataBase);

        ComponentBase->Setup();
        ComponentBase->SetEnabledState(true);
    }

    protected override void OnSizeChanged() {
        base.OnSizeChanged();

        CollisionNode.Size = Size;
        ComponentBase->UldManager.RootNodeHeight = (ushort)Height;
        ComponentBase->UldManager.RootNodeWidth = (ushort)Width;
    }

    public bool IsEnabled {
        get => NodeFlags.HasFlag(NodeFlags.Enabled);
        set => ComponentBase->SetEnabledState(value);
    }

    public override int ChildCount => ComponentBase->UldManager.NodeListCount;

    internal T* Component {
        get => (T*)Node->Component;
        set => Node->Component = (AtkComponentBase*)value;
    }

    internal TU* Data {
        get => (TU*)Node->Component->UldManager.ComponentData;
        set => Node->Component->UldManager.ComponentData = (AtkUldComponentDataBase*)value;
    }
}
