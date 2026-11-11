using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
//using Object = UnityEngine.Object;

using UnityEngine.Splines.ExtrusionShapes; 

// we have a list but we can use type as a category also might remove resource spots as those are to be random generated
public enum BuildableType
{
    ConveyorBelt = 0,
    Smelter = 1,
    SubmissionBin = 2,
    ResourceSpot = 3,
    Presser = 4,
    CraftTable = 5,
    Drill = 6
}

public class BuildController : MonoBehaviour
{
    public struct SnapTarget
    {
        public Vector3 position;
        public string targetName;
        public FactoryMachine machine;
        public ConveyorReceiver receiver;
        public Conveyor conveyor;
        public bool isSnapped;
    }

    [Header("References")]
    public Camera viewCamera;
    public Transform playerTransform;

    [Header("Build Settings")]
    [Min(5f)] public float maxBuildDistance = 30f;
    [Min(0.1f)] public float conveyorHeightOffset = 1.0f;
    [Min(0.5f)] public float minConveyorLength = 1.0f;
    [Tooltip("Buildings stay upright. Steep terrain needs foundations in a later milestone.")]
    [Range(0f, 45f)] public float maxBuildingSlope = 20f;

    [SerializeField] private LayerMask blockPlacementMask;
    [Min(0.01f)] public float placementProbeRadius = 0.1f;
    [Min(0.05f)] public float conveyorConnectionClearance = 0.75f;


    [Header("Conveyor Settings")]
    [Tooltip("Radius / thickness of the extruded conveyor belt (default 0.5m; previously 0.83m).")]
    [Range(0.2f, 1.2f)] public float conveyorRadius = 0.5f;
    [Tooltip("Number of cross-section sides for the extruded conveyor mesh (default 8).")]
    [Range(4, 16)] public int conveyorSides = 4;
    [Tooltip("Number of mesh segments per unit along the conveyor curve (default 4).")]
    [Range(1f, 10f)] public float conveyorSegmentsPerUnit = 4f;

    [Header("Conveyor Curve Limits")]
    [Tooltip("Allow a conveyor connected to directed endpoints to bend using a cubic Bezier curve.")]
    public bool enableConveyorCurves = true;
    [Tooltip("A belt shorter than this stays straight; short curves are too sharp to read or traverse well.")]
    [Min(0.5f)] public float minCurveLength = 3f;
    [Tooltip("The start/end direction must differ from the direct route by at least this angle before a bend is added.")]
    [Range(0f, 89f)] public float minCurveAngle = 8f;
    [Tooltip("Directions that require a sharper turn than this are rejected and produce a straight belt.")]
    [Range(5f, 180f)] public float maxCurveAngle = 180f;
    [Tooltip("Endpoints farther apart vertically than this stay straight, preventing impractically steep bends.")]
    [Min(0f)] public float maxCurveHeightDifference = 2f;
    [Tooltip("Length of each Bezier control handle as a fraction of the endpoint distance.")]
    [Range(0.1f, 0.5f)] public float curveHandleLengthRatio = 0.33f;
    [Tooltip("Blocks endpoints this close to directly behind the start direction. Place slightly left or right of this zone to make a safe C-shaped bend.")]
    [Range(0f, 45f)] public float rearPlacementDeadZone = 12f;

    [Header("Materials (Optional overrides)")]
    public Material machineMaterial;
    public Material binMaterial;
    public Material oreMaterial;
    public Material yellowMaterial;
    public Material greenMaterial;
    public Material redMaterial;
    public Material ghostMaterial;

    [Header("Prefabs (Optional overrides)")]
    public FactoryItem ingotPrefab;

   
    [Header("Data-Driven Build Catalog")]
    [Tooltip("Optional definitions, in the same order as build slots. When assigned, their names and recipes drive build mode.")]
    public FactoryData[] buildableFactories;

    [Header("UI Placement")]
    public bool sidebarOnRight = true;

    public bool IsBuildModeActive { get; private set; }
    public BuildableType SelectedType { get; private set; } = BuildableType.ConveyorBelt;
    public float BuildingRotation { get; private set; } = 0f;
    
    
    public bool ConveyorHasStart => conveyorHasStart;
    public Vector3 ConveyorStartPoint => conveyorStartPoint;

    private bool conveyorHasStart;
    private Vector3 conveyorStartPoint;



    //remove or change this, this factorymachine only works for the smelter. there will be other machines added in the future with their own ui configuration and methods of changing with specific requirements
    private FactoryMachine activeStartMachine;
    // Retains the start attachment so the final belt and its preview use the same tangent direction.
    private SnapTarget conveyorStartSnap;
    // Direction captured when the first point is placed; free belts leave their start in this direction.
    private Vector3 conveyorStartDirection;
    private SnapTarget currentSnap;

    // Ghost preview objects
    private GameObject previewRoot;
    private GameObject previewStartMarker;
    private GameObject previewSplineObj;
    private SplineContainer previewSplineContainer;
    private Mesh previewSplineMesh;
    private GameObject previewBuildingObj;
    private readonly Collider[] placementOverlapResults = new Collider[32];
    [SerializeField]
    private BuildTabCatalog buildTabCatalog;
    private FactoryData selectedFactoryData;

    public FactoryData SelectedFactoryData => selectedFactoryData;

    [Header("Crafting Costs")]
    [Tooltip("When enabled, placing structures consumes required resources from the player's pouch.")]
    public bool enforceBuildCosts = true;

    public bool CanAfford(BuildableType type, out string message)
    {
        return CanAfford(GetFactoryData(type), out message);
    }

    public bool CanAfford(FactoryData data, out string message) // affordability for buildable items, checks if the player has enough resources to build the item
    {
        message = string.Empty;
        if (!enforceBuildCosts)
            return true;

        if (data == null || !data.HasBuildCost)
            return true;

        var player = GetComponent<FactoryPlayer>();
        if (player == null)
            return true;

        var inventory = Inventory.Instance;
        if (inventory == null)
        {
            message = "Inventory is unavailable.";
            return false;
        }

        Resource costItem = data.BuildCostResource;
        int costAmount = data.BuildCostQty;

        if (costAmount <= 0 || costItem == null)
            return true;

        if (!inventory.HasItem(costItem, costAmount))
        {
            // Backward compatibility fallback for tests checking raw ore/ingots
            Resource rawItem = data.FallbackCostItem;
            int rawAmount = data.FallbackCostQty;
            if (rawItem != null && rawAmount > 0 && inventory.HasItem(rawItem, rawAmount))
                return true;

            int current = inventory.GetItemCount(costItem);
            message = current == 0
                ? $"None in inventory (Craft at Table)"
                : $"Requires {costAmount}x {costItem.DisplayName} (You have: {current})";
            return false;
        }

        return true;
    }

    public void DeductBuildCost(BuildableType type)
    {
        // Existing callers select by enum; forward them to the data-driven implementation.
        DeductBuildCost(GetFactoryData(type));
    }

    public void DeductBuildCost(FactoryData data)
    {
        // A selected catalog entry can share a FactoryKind with another prefab.
        // Taking FactoryData directly ensures the selected entry pays its own cost.
        if (!enforceBuildCosts)
            return;

        var inventory = Inventory.Instance;
        if (inventory == null)
            return;

        if (data == null || !data.HasBuildCost)
            return;

        Resource costItem = data.BuildCostResource;
        int costAmount = data.BuildCostQty;

        if (costAmount > 0 && costItem != null && inventory.HasItem(costItem, costAmount))
        {
            inventory.TryRemove(costItem, costAmount);
            return;
        }

        // Backward compatibility fallback: deduct raw ingredients if player doesn't have machine item
        Resource rawItem = data.FallbackCostItem;
        int rawAmount = data.FallbackCostQty;
        if (rawAmount > 0 && rawItem != null && inventory.HasItem(rawItem, rawAmount))
        {
            inventory.TryRemove(rawItem, rawAmount);
        }
    }

    // FPSCore InputManager Actions
    private InputAction buildModeAction;
    private InputAction buildPlaceAction;
    private InputAction buildCancelAction;
    private InputAction buildRotateAction;
    private InputAction buildWidthAction;
    private InputAction buildScrollAction;

    private void Awake()
    {
        try
        {
            enforceBuildCosts = true;
            ResolveBuildCatalog();
            if (viewCamera == null)
                viewCamera = GetComponentInChildren<Camera>() ?? Camera.main;

            if (playerTransform == null)
                playerTransform = transform;

            ResolveMaterials();
            RegisterInputActions();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BuildController] Exception during Awake: {ex}");
        }
    }

    private void ResolveBuildCatalog()
    {
        if (buildableFactories != null && buildableFactories.Length > 0)
        {
            buildableFactories = System.Array.FindAll(buildableFactories, d => d != null && d.kind != FactoryKind.ResourceSpot);
            return;
        }

        var loadedDefinitions = Resources.LoadAll<FactoryData>("FactoryData");
        if (loadedDefinitions == null || loadedDefinitions.Length == 0)
            return;

        loadedDefinitions = System.Array.FindAll(loadedDefinitions, d => d != null && d.kind != FactoryKind.ResourceSpot);

        Array.Sort(loadedDefinitions, (left, right) =>
        {
            if (left == null) return 1;
            if (right == null) return -1;
            int kindOrder = left.kind.CompareTo(right.kind);
            return kindOrder != 0 ? kindOrder : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
        });
        buildableFactories = loadedDefinitions;
        selectedFactoryData = GetFactoryData(SelectedType);
    }

    private void Start()
    {
        try
        {
            EnsureInputActions();

            if (buildTabCatalog == null)
                buildTabCatalog = BuildTabCatalog.FindOrCreate(OnFactorySelected, BeginBuild);
            else
            {
                buildTabCatalog.BindCallbacks(OnFactorySelected, BeginBuild);
                buildTabCatalog.Initialize();
            }

            buildTabCatalog?.SetVisible(false);
            RefreshBuildCatalogUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BuildController] Exception during Start: {ex}");
        }
    }

    /// <summary>
    /// Input Actions
    /// </summary>
    private void RegisterInputActions()
    {
        EnsureInputActions();
    }

    private void EnsureInputActions()
    {
        var im = InputManager.Instance;
        if (im == null)
            return;

        buildModeAction ??= im.GetExtraAction("BuildMode", "<Keyboard>/b", "<Gamepad>/dpad/up");
        buildPlaceAction ??= im.GetExtraAction("BuildPlace", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
        buildCancelAction ??= im.GetExtraAction("BuildCancel", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
        buildRotateAction ??= im.GetExtraAction("BuildRotate", "<Keyboard>/r", "<Gamepad>/rightShoulder");
        buildWidthAction ??= im.GetExtraAction("BuildWidth", "<Keyboard>/t", "<Gamepad>/leftShoulder");
        buildScrollAction ??= im.GetExtraVector2Action("BuildScroll", "<Mouse>/scroll");
    }
    private void HandleToggleInput(bool togglePressed)
    {
        bool cancelPressed = (InputManager.Instance != null && InputManager.Instance.UICancelPressed)
            || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

        if (togglePressed)
        {
            if (IsBuildModeActive)
            {
                SetBuildModeActive(false);
            }
            else if (buildTabCatalog != null && buildTabCatalog.IsVisible)
            {
                CloseBuildTab();
            }
            else
            {
                Inventory.Instance?.SetOpen(false);

                var craftingTab = FindAnyObjectByType<CraftingTab>(FindObjectsInactive.Include);
                if (craftingTab != null && craftingTab.IsOpen)
                    craftingTab.Close();

                OpenBuildTab();
            }
            return;
        }

        if (buildTabCatalog != null && buildTabCatalog.IsVisible && cancelPressed)
        {
            CloseBuildTab();
            return;
        }

        if (IsBuildModeActive && cancelPressed)
        {
            if (conveyorHasStart)
            {
                conveyorHasStart = false;
                activeStartMachine = null;
                conveyorStartSnap = default;
                conveyorStartDirection = Vector3.zero;
            }
            else
            {
                SetBuildModeActive(false);
            }
        }
    }
    private void HandleSelectionInput()
    {
        int hotbar = IInput.HotbarPressedIndex;
        if (buildableFactories != null && hotbar >= 0 && hotbar < buildableFactories.Length)
        {
            SelectCatalogIndex(hotbar);
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) { SetSelectedType(BuildableType.ConveyorBelt); return; }
            if (Keyboard.current.digit2Key.wasPressedThisFrame) { SetSelectedType(BuildableType.Smelter); return; }
            if (Keyboard.current.digit3Key.wasPressedThisFrame) { SetSelectedType(BuildableType.SubmissionBin); return; }
            if (Keyboard.current.digit4Key.wasPressedThisFrame) { SetSelectedType(BuildableType.ResourceSpot); return; }
            if (Keyboard.current.digit5Key.wasPressedThisFrame) { SetSelectedType(BuildableType.Presser); return; }
            if (Keyboard.current.digit6Key.wasPressedThisFrame) { SetSelectedType(BuildableType.Drill); return; }
        }

        float scroll = 0f;
        if (buildScrollAction != null)
            scroll = buildScrollAction.ReadValue<Vector2>().y;
        else if (Mouse.current != null)
            scroll = Mouse.current.scroll.ReadValue().y;

        if (buildableFactories == null || buildableFactories.Length == 0)
            return;

        if (scroll > 0.1f)
        {
            SelectCatalogIndex((GetCatalogIndex() - 1 + buildableFactories.Length) % buildableFactories.Length);
        }
        else if (scroll < -0.1f)
        {
            SelectCatalogIndex((GetCatalogIndex() + 1) % buildableFactories.Length);
        }
    }

    /// <summary>
    /// Build Tab Functions
    /// </summary>
    public void CloseBuildTab()
    {
        if (buildTabCatalog != null && buildTabCatalog.IsVisible)
        {
            InputManager.Instance?.ExitUIMode(this);
            buildTabCatalog.SetVisible(false);
        }
    }

    private void Update()
    {
        EnsureInputActions();

        bool bPressed = (buildModeAction != null && buildModeAction.WasPressedThisFrame())
            || (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);

        // A workbench or inventory owns the cursor until its panel is closed.
        // If another UI menu owns the cursor, only allow input if the user explicitly presses B to open build menu.
        bool otherMenuOpen = InputManager.Instance != null && !InputManager.Instance.GameplayEnabled
            && !IsBuildModeActive && (buildTabCatalog == null || !buildTabCatalog.IsVisible);

        if (otherMenuOpen && !bPressed)
            return;

        HandleToggleInput(bPressed);

        if (!IsBuildModeActive)
            return;

        HandleSelectionInput();
        HandleRotationInput();
        HandleWidthInput();
        HandlePlacement();
        UpdatePreview();
        UpdateUI();
    }


   
    public void SetSelectedType(BuildableType type)
    {
        if (SelectedType == type && selectedFactoryData != null && selectedFactoryData.kind == (FactoryKind)type)
            return;

        SelectedType = type;
        selectedFactoryData = GetFactoryData(type);
        conveyorHasStart = false;
        activeStartMachine = null;
        conveyorStartSnap = default;
        conveyorStartDirection = Vector3.zero;
        UpdatePreviewMesh();
    }

    private void OnSlotClicked(int slotIndex)
    {
        SelectCatalogIndex(slotIndex);
    }

    private void OpenBuildTab() // the buildTab
    {
        if (buildTabCatalog == null)
            buildTabCatalog = BuildTabCatalog.FindOrCreate(OnFactorySelected, BeginBuild);
        else
            buildTabCatalog.BindCallbacks(OnFactorySelected, BeginBuild);

        buildTabCatalog?.SetVisible(true);
        RefreshBuildCatalogUI();
        if (buildTabCatalog != null && buildableFactories != null && buildableFactories.Length > 0)
        {
            buildTabCatalog.SelectCatalogIndex(GetCatalogIndex());
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        InputManager.Instance?.EnterUIMode(this);
    }

    private void OnFactorySelected(int catalogIndex)
    {
        SelectCatalogIndex(catalogIndex);
    }

    /// <summary>
    /// Building function
    /// </summary>
    private void BeginBuild() // begin build mode from the build tab, this is where the build mode is activated and the cursor is locked
    {
        if (selectedFactoryData == null)
            return;

        buildTabCatalog?.SetVisible(false);
        InputManager.Instance?.ExitUIMode(this); // 

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SetBuildModeActive(true);
    }

    private void SelectCatalogIndex(int catalogIndex)
    {
        if (buildableFactories == null || catalogIndex < 0 || catalogIndex >= buildableFactories.Length)
            return;

        FactoryData definition = buildableFactories[catalogIndex];
        if (definition == null)
            return;

        selectedFactoryData = definition;
        SelectedType = (BuildableType)definition.kind;
        conveyorHasStart = false;
        activeStartMachine = null;
        conveyorStartSnap = default;
        conveyorStartDirection = Vector3.zero;
        UpdatePreviewMesh();
    }

    private int GetCatalogIndex()
    {
        if (buildableFactories == null || buildableFactories.Length == 0)
            return 0;

        for (int i = 0; i < buildableFactories.Length; i++)
            if (buildableFactories[i] == selectedFactoryData)
                return i;

        return 0;
    }

    private FactoryData GetFactoryData(BuildableType type)
    {
        if (selectedFactoryData != null && selectedFactoryData.kind == (FactoryKind)type)
            return selectedFactoryData;
        if (buildableFactories != null)
            foreach (var definition in buildableFactories)
                if (definition != null && definition.kind == (FactoryKind)type)
                    return definition;
        if (type == BuildableType.ResourceSpot)
            return Resources.Load<FactoryData>("FactoryData/IronOreSpot");
        if (type == BuildableType.Drill)
            return Resources.Load<FactoryData>("FactoryData/Drill");
        return null;
    }

    private void RefreshBuildCatalogUI()
    {
        if (buildTabCatalog != null)
            buildTabCatalog.SetDefinitions(buildableFactories);
    }

    private void HandleRotationInput()
    {
        bool rotatePressed = false;
        if (buildRotateAction != null && buildRotateAction.WasPressedThisFrame())
            rotatePressed = true;
        else if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            rotatePressed = true;

        if (rotatePressed)
        {
            BuildingRotation = (BuildingRotation + 90f) % 360f;
        }
    }

    //testing help function to parse factorydata twopoint placement
    private bool UsesTwoPointPlacement()
    {
        return selectedFactoryData != null &&
               selectedFactoryData.useTwoPointPlacement;
    }

    private void GetSelectedFactoryPose(
        Vector3 groundPoint,
        out Vector3 position,
        out Quaternion rotation)
    {
        rotation = Quaternion.Euler(0f, BuildingRotation, 0f) *
                   Quaternion.Euler(selectedFactoryData.placementRotationOffset);

        position = groundPoint + rotation * selectedFactoryData.placementOffset;
    }
    
  
    private bool IsFactoryAreaBlocked(FactoryData data, Vector3 position, Quaternion rotation)
    {
        if (data == null || data.factoryPrefab == null || blockPlacementMask.value == 0)
            return false;

        Transform prefabRoot = data.factoryPrefab.transform;
        BoxCollider[] boundaries = data.factoryPrefab.GetComponentsInChildren<BoxCollider>(true);
        bool foundBoundary = false;

        foreach (BoxCollider boundary in boundaries)
        {
            if (!boundary.isTrigger || !IsInPlacementBlockMask(boundary.gameObject.layer))
                continue;

            foundBoundary = true;
            Vector3 localCenter = prefabRoot.InverseTransformPoint(
                boundary.transform.TransformPoint(boundary.center));
            Vector3 center = position + rotation * Vector3.Scale(localCenter, prefabRoot.localScale);
            Vector3 halfExtents = Vector3.Scale(boundary.size * 0.5f, boundary.transform.lossyScale);
            Quaternion boundaryRotation = rotation *
                Quaternion.Inverse(prefabRoot.rotation) * boundary.transform.rotation;

            if (OverlapsPlacementBlocker(center, halfExtents, boundaryRotation))
                return true;
        }

        // A prefab without a BoundaryTrigger retains the small point check until it is configured.
        return !foundBoundary && Physics.CheckSphere(position, placementProbeRadius,
            blockPlacementMask, QueryTriggerInteraction.Collide);
    }

    private bool IsConveyorPathBlocked(Vector3 start, Vector3 end, SnapTarget startSnap, SnapTarget endSnap)
    {
        if (blockPlacementMask.value == 0)
            return false;

        var candidateSpline = new Spline();
        ConfigureConveyorSpline(candidateSpline, start, end, startSnap, endSnap, conveyorStartDirection);

        float straightDistance = Vector3.Distance(start, end);
        int segments = Mathf.Clamp(Mathf.CeilToInt(straightDistance / 0.25f), 2, 128);
        float endpointPadding = Mathf.Clamp(conveyorConnectionClearance / Mathf.Max(straightDistance, 0.01f), 0f, 0.45f);
        float from = startSnap.isSnapped ? endpointPadding : 0f;
        float to = endSnap.isSnapped ? 1f - endpointPadding : 1f;
        if (to <= from)
            return false;

        Vector3 previous = candidateSpline.EvaluatePosition(from);
        float radius = Mathf.Max(0.05f, conveyorRadius * 0.8f);

        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(from, to, i / (float)segments);
            Vector3 current = candidateSpline.EvaluatePosition(t);
            if (Physics.CheckCapsule(previous, current, radius, blockPlacementMask,
                QueryTriggerInteraction.Collide))
                return true;

            previous = current;
        }

        return false;
    }

    private bool OverlapsPlacementBlocker(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        return Physics.OverlapBoxNonAlloc(center, halfExtents, placementOverlapResults, rotation,
            blockPlacementMask, QueryTriggerInteraction.Collide) > 0;
    }

    private bool IsInPlacementBlockMask(int layer) =>
        (blockPlacementMask.value & (1 << layer)) != 0;



    private GameObject BuildSelectedFactory(Vector3 groundPoint)
    {
        if (selectedFactoryData == null ||
            selectedFactoryData.factoryPrefab == null)
        {
            Quaternion rot = Quaternion.Euler(0f, BuildingRotation, 0f);
            if (SelectedType == BuildableType.Smelter)
                return BuildSmelter(groundPoint, rot).gameObject;
            if (SelectedType == BuildableType.SubmissionBin)
                return BuildSubmissionBin(groundPoint, rot).gameObject;
            if (SelectedType == BuildableType.ResourceSpot)
                return BuildResourceSpot(groundPoint, rot).gameObject;
            if (SelectedType == BuildableType.Presser)
                return BuildPresser(groundPoint, rot).gameObject;
            if (SelectedType == BuildableType.Drill)
                return BuildDrill(groundPoint, rot).gameObject;

            Debug.LogWarning("The selected FactoryData needs a prefab.");
            return null;
        }

        GetSelectedFactoryPose(groundPoint, out Vector3 position, out Quaternion rotation);

        GameObject factoryObject = Instantiate(
            selectedFactoryData.factoryPrefab,
            position,
            rotation);

        factoryObject.name = selectedFactoryData.DisplayName;

        FactoryMachine machine = factoryObject.GetComponent<FactoryMachine>();
        if (machine != null)
        {
            machine.factoryData = selectedFactoryData;
            machine.ApplyFactoryData();
            if (machine is Drill drill)
                drill.InitReferences();

            Transform outPort = (machine is Drill d) ? d.outputPort : ((machine is Presser p) ? p.outputPort : null);
            Vector3 outputPos = outPort != null ? outPort.position : position + (rotation * Vector3.forward) * 1.8f;
            foreach (var conveyor in FindObjectsByType<Conveyor>())
            {
                if (!conveyor.HasInputConnection && Vector3.Distance(conveyor.StartPosition, outputPos) < 2.5f)
                {
                    machine.outputBelt = conveyor;
                    conveyor.inputMachine = machine;
                    break;
                }
            }
        }
        Debug.Log("Built " + selectedFactoryData.DisplayName + " at " + position);
        return factoryObject;
    }
    private void HandleWidthInput()
    {
        if (SelectedType != BuildableType.ConveyorBelt)
            return;

        bool widthPressed = false;
        if (buildWidthAction != null && buildWidthAction.WasPressedThisFrame())
            widthPressed = true;
        else if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            widthPressed = true;

        if (widthPressed)
            CycleConveyorWidth();
    }

    public void CycleConveyorWidth()
    {
        // Cycles between: Slim (0.35m radius / 0.7m width) -> Standard (0.50m radius / 1.0m width) -> Wide (0.75m radius / 1.5m width)
        if (conveyorRadius <= 0.4f)
            conveyorRadius = 0.50f;
        else if (conveyorRadius <= 0.6f)
            conveyorRadius = 0.75f;
        else
            conveyorRadius = 0.35f;
    }

    private void HandlePlacement() 
    {
        bool cancelPressed = false;
        if (buildCancelAction != null && buildCancelAction.WasPressedThisFrame())
            cancelPressed = true;
        else if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            cancelPressed = true;

        if (cancelPressed && conveyorHasStart)
        {
            conveyorHasStart = false;
            activeStartMachine = null;
            conveyorStartSnap = default;
            conveyorStartDirection = Vector3.zero;
            return;
        }

        bool placePressed = false;
        if (buildPlaceAction != null && buildPlaceAction.WasPressedThisFrame())
            placePressed = true;
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            placePressed = true;

        if (!placePressed)
            return;

        if (InputManager.Instance != null && InputManager.Instance.WasRelockFrame)
            return;

        if (UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (!TryGetPlacementHit(out RaycastHit hit))
            return;

        if  (UsesTwoPointPlacement()) //testing the new function
        {
            if (!conveyorHasStart)
            {
                if (!CanAfford(BuildableType.ConveyorBelt, out _))
                    return;

                bool snapped = TryGetSnapTarget(true, hit.point, out SnapTarget snap);
                // Keep a free start marker on the ray hit so it stays under the crosshair.
                conveyorStartPoint = snapped ? snap.position : hit.point + Vector3.up * conveyorRadius;
;
                activeStartMachine = snapped ? snap.machine : null;
                conveyorStartSnap = snapped ? snap : default;
                conveyorStartDirection = GetConveyorDirection(conveyorStartSnap);
                conveyorHasStart = true;
            }
            else
            {
                if (!CanAfford(BuildableType.ConveyorBelt, out _))
                    return;

                bool snapped = TryGetSnapTarget(false, hit.point, out SnapTarget snap);
                // Free endpoints use the same ground hit as the preview marker.
                Vector3 endPoint = snapped ? snap.position : hit.point + Vector3.up * conveyorRadius;
;

                // Level height if close to flat and not snapped
                if (!snapped && Mathf.Abs(conveyorStartPoint.y - endPoint.y) < 0.25f)
                    endPoint.y = conveyorStartPoint.y;

                // A perfectly rear-facing cubic folds back over its own control points and twists the mesh.
                // Keep the first point active, but require the player to move the endpoint slightly left or right.
                if (IsEndpointBehindStart(conveyorStartPoint, endPoint, conveyorStartDirection))
                    return;

                // Reject paths that cross machine BoundaryTrigger volumes.
                if (IsConveyorPathBlocked(conveyorStartPoint, endPoint, conveyorStartSnap,
                    snapped ? snap : default))
                {
                    Debug.Log("Conveyor path is blocked.");
                    return;
                }

                if (Vector3.Distance(conveyorStartPoint, endPoint) >= minConveyorLength)
                {
                    var conveyor = BuildConveyorBelt(
                        conveyorStartPoint,
                        endPoint,
                        conveyorStartSnap,
                        snapped ? snap : default,
                        conveyorStartDirection);

                    DeductBuildCost(BuildableType.ConveyorBelt);

                    conveyorHasStart = false;
                    activeStartMachine = null;
                    conveyorStartSnap = default;
                    conveyorStartDirection = Vector3.zero;
                }
            }
        }
        else //testing new  prefab function
        {
            if (!CanAfford(selectedFactoryData, out _))
                return;

            // Check the factory prefab's BoundaryTrigger at its final rotated placement pose.
            GetSelectedFactoryPose(hit.point, out Vector3 position, out Quaternion rotation);
            if (IsFactoryAreaBlocked(selectedFactoryData, position, rotation))
            {
                Debug.Log("Machine footprint is blocked.");
                return;
            }

            BuildSelectedFactory(hit.point);
            DeductBuildCost(selectedFactoryData);
            
        }

    }




    ///// <summary>
    ///// ////////CRAFTING TABLE PREVIEW AND PLACEMENT////////
    ///// </summary>
    ///// <returns></returns>
    ///// 
    //private bool IsCraftingTableSelection()
    //{
    //    // The Crafting Table still uses the Machine category, so identify it from
    //    // the interaction component on its assigned prefab.
    //    return selectedFactoryData != null
    //        && selectedFactoryData.factoryPrefab != null
    //        && selectedFactoryData.factoryPrefab.GetComponent<CraftingTable>() != null;
    //}

    //private CraftingTable BuildCraftingTable(Vector3 groundPoint, Quaternion rotation) // build the tbale
    //{
    //    // The table prefab has its own CraftingTable component and visual elements.
    //    // No additional light, sign, or FactoryMachine component is created here.
    //    GameObject tableObject = Instantiate(selectedFactoryData.factoryPrefab, groundPoint, rotation);
    //    tableObject.name = selectedFactoryData.DisplayName;
    //    return tableObject.GetComponent<CraftingTable>();
    //}





    //PLACEMENT HIT RAY
    /// this is a function that uses raycast and check if the area is valid to place the building or conveyor belt
    public bool TryGetPlacementHit(out RaycastHit hit)
    {
        if (viewCamera == null)
        {
            hit = default;
            return false;
        }

        // Use the center of the rendered camera viewport, which is where its reticle is drawn.
        Rect cameraRect = viewCamera.pixelRect;
        // Use this camera's viewport center so the ray follows its rendered reticle position.
        Vector3 screenCenter = new Vector3(
            cameraRect.x + cameraRect.width * 0.5f,
            cameraRect.y + cameraRect.height * 0.5f,
            0f);
        Ray ray = viewCamera.ScreenPointToRay(screenCenter);

        int layerMask = ~LayerMask.GetMask("Ignore Raycast");
        if (Physics.Raycast(ray, out hit, maxBuildDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (playerTransform != null && hit.collider.transform.IsChildOf(playerTransform))
                return false;

            if ( Vector3.Angle(Vector3.up, hit.normal) > maxBuildingSlope)
                return false;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds magnetic snap points for conveyor placement:
    /// - Start snaps to Smelter output port or existing conveyor end.
    /// - End snaps to Submission Bin intake or next conveyor start.
    /// </summary>
    public bool TryGetSnapTarget(bool forStart, Vector3 hitPoint, out SnapTarget snap)
    {
        snap = default;

        if (SelectedType != BuildableType.ConveyorBelt)
            return false;

        float bestDist = forStart ? 3.0f : 3.5f;

        if (forStart)
        {
            // 1. Machine output port (Drill, Presser, Smelter)
            foreach (var machine in FindObjectsByType<FactoryMachine>())
            {
                Vector3 outputPort = (machine is Drill dr && dr.outputPort != null)
                    ? dr.outputPort.position
                    : (machine is Presser pm && pm.outputPort != null)
                    ? pm.outputPort.position
                    : machine.transform.position + machine.transform.forward * 1.8f;
                if (!(machine is Presser pm2 && pm2.outputPort != null) && !(machine is Drill dr2 && dr2.outputPort != null))
                    outputPort.y = machine.transform.position.y - 1.2f + conveyorHeightOffset;
                float dist = Vector3.Distance(hitPoint, outputPort);
                float centerDist = Vector3.Distance(hitPoint, machine.transform.position);

                // A factory output can feed only one belt.
                if (machine.outputBelt == null && (dist < bestDist || centerDist < 2.0f))
                {
                    bestDist = Mathf.Min(dist, centerDist);
                    snap.position = outputPort;
                    snap.targetName = machine is Drill ? "Drill Output"
                        : machine is Presser ? "Presser Output" : "Smelter Output";
                    snap.machine = machine;
                    snap.isSnapped = true;
                }
            }

            // 2. Existing Conveyor end point (belt-to-belt chaining)
            foreach (var conveyor in FindObjectsByType<Conveyor>())
            {
                if (!conveyor.HasOutputConnection
                    && conveyor.ConveyerSpline != null && conveyor.ConveyerSpline.Spline != null)
                {
                    Vector3 endPos = conveyor.ConveyerSpline.EvaluatePosition(1f);
                    float dist = Vector3.Distance(hitPoint, endPos);
                    if (dist < 2.0f && dist < bestDist)
                    {
                        bestDist = dist;
                        snap.position = endPos;
                        snap.targetName = "Conveyor End";
                        snap.conveyor = conveyor;
                        snap.machine = null;
                        snap.isSnapped = true;
                    }
                }
            }
        }
        else // forEnd
        {
            // 1. Receiver intake (Submission Bin, Presser, Smelter/Machine)
            foreach (var bin in FindObjectsByType<ConveyorReceiver>())
            {
                Vector3 intake = bin.transform.position + Vector3.up * 1.0f;
                string targetName = "Receiver Intake";
                if (bin is PresserReceiver pr && pr.presser != null && pr.presser.inputPort != null)
                {
                    intake = pr.presser.inputPort.position;
                    targetName = "Presser Input";
                }
                else if (bin is MachineReceiver mr && mr.machine != null)
                {
                    intake = mr.transform.position;
                    targetName = $"{mr.machine.factoryData?.DisplayName ?? "Smelter"} Input";
                }
                else if (bin is SubmissionBin)
                {
                    targetName = "Submission Bin";
                }

                float dist = Vector3.Distance(hitPoint, intake);
                float centerDist = Vector3.Distance(hitPoint, bin.transform.position);

                // A receiver intake can accept only one belt.
                if (!bin.HasInputConnection && (dist < bestDist || centerDist < 2.5f))
                {
                    bestDist = Mathf.Min(dist, centerDist);
                    snap.position = intake;
                    snap.targetName = targetName;
                    snap.receiver = bin;
                    snap.isSnapped = true;
                }
            }

            // 2. Existing Conveyor start point
            foreach (var conveyor in FindObjectsByType<Conveyor>())
            {
                if (!conveyor.HasInputConnection
                    && conveyor.ConveyerSpline != null && conveyor.ConveyerSpline.Spline != null)
                {
                    Vector3 startPos = conveyor.StartPosition;
                    float dist = Vector3.Distance(hitPoint, startPos);
                    if (dist < 2.0f && dist < bestDist)
                    {
                        bestDist = dist;
                        snap.position = startPos;
                        snap.targetName = "Conveyor Start";
                        snap.conveyor = conveyor;
                        snap.receiver = null;
                        snap.isSnapped = true;
                    }
                }
            }
        }

        return snap.isSnapped;
    }

    private void UpdatePreview() //preview view
    {
        if (previewRoot == null)
            CreatePreviewObjects();

        bool hasHit = TryGetPlacementHit(out RaycastHit hit);

        if (!hasHit)
        {
            previewRoot.SetActive(false);
            currentSnap = default;
            return;
        }

        previewRoot.SetActive(true);

        if (UsesTwoPointPlacement())
        {
            previewBuildingObj.SetActive(false);

            if (!conveyorHasStart)
            {
                bool snapped = TryGetSnapTarget(true, hit.point, out currentSnap);
                Vector3 pos = snapped ? currentSnap.position : hit.point + Vector3.up * conveyorRadius;

                previewStartMarker.SetActive(true);
                previewStartMarker.transform.position = pos;
                previewStartMarker.transform.rotation = Quaternion.identity;
                previewStartMarker.transform.localScale = new Vector3(conveyorRadius * 2f, 0.15f, conveyorRadius * 2f);
                previewSplineObj.SetActive(false);
            }
            else
            {
                previewStartMarker.SetActive(true);
                previewStartMarker.transform.position = conveyorStartPoint;
                previewStartMarker.transform.rotation = Quaternion.identity;
                previewStartMarker.transform.localScale = new Vector3(conveyorRadius * 2f, 0.15f, conveyorRadius * 2f);

                bool snapped = TryGetSnapTarget(false, hit.point, out currentSnap);
                Vector3 endPoint = snapped ? currentSnap.position : hit.point + Vector3.up * conveyorRadius;
;

                if (!snapped && Mathf.Abs(conveyorStartPoint.y - endPoint.y) < 0.25f)
                    endPoint.y = conveyorStartPoint.y;

                float dist = Vector3.Distance(conveyorStartPoint, endPoint);

                if (dist >= 0.5f && !IsEndpointBehindStart(
                    conveyorStartPoint, endPoint, conveyorStartDirection))
                {
                    previewSplineObj.SetActive(true);
                    var spline = previewSplineContainer.Spline;
                    spline.Clear();

                    ConfigureConveyorSpline(spline, conveyorStartPoint, endPoint, conveyorStartSnap,
                        snapped ? currentSnap : default, conveyorStartDirection);
                    RebuildConveyorMesh(previewSplineContainer, previewSplineMesh);
                }
                else
                {
                    previewSplineObj.SetActive(false);
                }
            }
        }
        else
        {
            currentSnap = default;
            previewStartMarker.SetActive(false);
            previewSplineObj.SetActive(false);
            previewBuildingObj.SetActive(true);

            //testing helper pose

            GetSelectedFactoryPose( hit.point,out Vector3 previewPosition,out Quaternion previewRotation);

            previewBuildingObj.transform.SetPositionAndRotation(
                previewPosition,
                previewRotation);

        }
    }

    private void UpdatePreviewMesh()
    {
        if (previewBuildingObj == null)
            return;

      
          
         if (SelectedType == BuildableType.Smelter)
            previewBuildingObj.transform.localScale = new Vector3(2f, 2.4f, 2f);
        else if (SelectedType == BuildableType.SubmissionBin)
            previewBuildingObj.transform.localScale = new Vector3(2.8f, 1.4f, 2.8f);
        else if (SelectedType == BuildableType.ResourceSpot)
            previewBuildingObj.transform.localScale = new Vector3(2f, 1.1f, 2f);
        else if (SelectedType == BuildableType.Presser)
            previewBuildingObj.transform.localScale = new Vector3(2f, 2.4f, 2f);
        else if (SelectedType == BuildableType.Drill)
            previewBuildingObj.transform.localScale = new Vector3(2f, 2.2f, 2f);
    }

    private void UpdateUI()
    {
        // The factory catalog is shown before placement; placement itself uses the world preview.
    }

    public void SetBuildModeActive(bool active)
    {
        if (IsBuildModeActive == active)
            return;

        IsBuildModeActive = active;
        conveyorHasStart = false;
        activeStartMachine = null;
        conveyorStartSnap = default;
        conveyorStartDirection = Vector3.zero;
        currentSnap = default;

        if (previewRoot != null)
            previewRoot.SetActive(false);

        if (active)
            buildTabCatalog?.SetVisible(false);
    }

    #region Building Spawning

    /// <summary>
    /// Creates a conveyor between two points. The two-argument overload is kept for tests and
    /// programmatic callers that do not have attachment information, so it creates a straight belt.
    /// </summary>
    public Conveyor BuildConveyorBelt(Vector3 start, Vector3 end)
    {
        return BuildConveyorBelt(start, end, default, default, Vector3.zero);
    }

    /// <summary>
    /// Builds a conveyor mesh and its transport component using the same Bezier spline.
    /// Flow: (1) create the object and spline container; (2) select bounded curve handles from
    /// attached endpoints; (3) extrude the flat square belt along that spline; (4) assign the
    /// collider; and (5) configure item movement to follow the spline's top-facing up direction.
    /// </summary>
    private Conveyor BuildConveyorBelt(Vector3 start, Vector3 end, SnapTarget startSnap,
        SnapTarget endSnap, Vector3 startDirectionOverride)
    {
        // 1. The container remains unrotated so spline up stays world-up on a level belt.
        var beltObj = new GameObject("Conveyor Belt");
        beltObj.transform.position = Vector3.zero;
        beltObj.transform.rotation = Quaternion.identity;

        var container = beltObj.AddComponent<SplineContainer>();
        var spline = container.Spline;
        ConfigureConveyorSpline(spline, start, end, startSnap, endSnap, startDirectionOverride);

        var filter = beltObj.AddComponent<MeshFilter>();
        var renderer = beltObj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = machineMaterial;

        var collider = beltObj.AddComponent<MeshCollider>();
        collider.convex = false;

        // 3. Generate the visual mesh from the same spline that moves objects.
        var mesh = new Mesh { name = "Square Conveyor Mesh" };
        filter.sharedMesh = mesh;

        var conveyor = beltObj.AddComponent<Conveyor>();
        conveyor.ConveyerSpline = container;
        conveyor.speed = 5f;
        conveyor.surfOffset = 0.03f;
        conveyor.SearchRes = 16;
        conveyor.keepMomentumAtEnd = true;
        conveyor.snapRotation = true;
        conveyor.rotationSpeed = 720f;
        conveyor.minimumSpacing = 0.8f;
        conveyor.meshRadius = conveyorRadius;
        conveyor.meshSegmentsPerUnit = conveyorSegmentsPerUnit;

        ConnectConveyorEndpoints(conveyor, start, end, startSnap, endSnap);

        // Do not cap linked belts: the shared endpoint remains visually seamless.
        RebuildConveyorMesh(container, mesh,
            capEnds: !conveyor.HasInputConnection && !conveyor.HasOutputConnection);

        // 4. The collider exactly matches the generated curved mesh.
        collider.sharedMesh = mesh;

        return conveyor;
    }

    /// <summary>
    /// Configures a two-knot cubic Bezier path for a conveyor.
    /// Flow: (1) start with the direct route; (2) obtain attachment travel directions; (3) validate
    /// distance, height, and turn-angle limits; (4) use a straight line if any limit fails; otherwise
    /// (5) place Bezier handles along those directions. Tangents are stored in local space because
    /// each conveyor container is intentionally unrotated.
    /// </summary>
    private void ConfigureConveyorSpline(Spline spline, Vector3 start, Vector3 end,
        SnapTarget startSnap, SnapTarget endSnap, Vector3 startDirectionOverride)
    {
        spline.Clear(); // clean up existing spline

        Vector3 directDirection = end - start;
        float directLength = directDirection.magnitude;
        if (directLength < 0.001f)
            return;

        directDirection /= directLength;
        Vector3 startDirection = startDirectionOverride.sqrMagnitude > 0.0001f
            ? startDirectionOverride.normalized
            : GetAttachmentDirection(startSnap, true, directDirection);
        Vector3 endDirection = GetAttachmentDirection(endSnap, false, directDirection);

        // Accept a curve only when it is long enough, not too steep, and both endpoint turns are safe.
        float startAngle = Vector3.Angle(directDirection, startDirection);
        float endAngle = Vector3.Angle(directDirection, endDirection);
        bool needsCurve = startAngle >= minCurveAngle || endAngle >= minCurveAngle;
        bool curveIsAcceptable = enableConveyorCurves
            && directLength >= minCurveLength
            && Mathf.Abs(end.y - start.y) <= maxCurveHeightDifference
            && startAngle <= maxCurveAngle
            && endAngle <= maxCurveAngle;

        // A free endpoint directly behind the start would otherwise retrace itself. Bias its arrival
        // direction to one side, creating a broad C-shaped turn while retaining the captured forward exit.
        bool endIsFree = endSnap.machine == null && endSnap.receiver == null && endSnap.conveyor == null;
        if (curveIsAcceptable && endIsFree && startAngle >= 135f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, startDirection).normalized;
            endDirection = (endDirection + side * 0.45f).normalized;
        }

        float handleLength = needsCurve && curveIsAcceptable
            ? directLength * curveHandleLengthRatio
            : 0f;

        // TangentOut leaves the start; TangentIn sits behind the end, hence its negative direction.
        var startKnot = new BezierKnot((float3)start, float3.zero,
            (float3)(startDirection * handleLength), quaternion.identity);
        var endKnot = new BezierKnot((float3)end,
            (float3)(-endDirection * handleLength), float3.zero, quaternion.identity);

        spline.Add(startKnot);
        spline.Add(endKnot);
        spline.Closed = false;
    }

    /// <summary>
    /// Returns the direction in which a belt should leave or enter a snapped endpoint. Machines and
    /// receivers use their forward axis; chained belts use their actual spline tangent. Free placement
    /// returns the direct route, which naturally produces a straight belt.
    /// </summary>
    private Vector3 GetAttachmentDirection(SnapTarget snap, bool isStart, Vector3 fallback)
    {
        Vector3 direction = fallback;

        if (snap.machine != null)
            direction = (snap.machine is Presser pm && pm.outputPort != null)
                ? pm.outputPort.forward
                : snap.machine.transform.forward;
        else if (snap.receiver != null)
            direction = snap.receiver.transform.forward;
        else if (snap.conveyor != null && snap.conveyor.ConveyerSpline != null)
            direction = snap.conveyor.ConveyerSpline.EvaluateTangent(isStart ? 1f : 0f);

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
    }

    /// <summary>
    /// Captures the outgoing direction at the moment the player sets the first point. Connected
    /// starts inherit their source direction; a free start uses the player's horizontal forward axis.
    /// This is what makes the second point steer the belt left, right, or into a broad back-turn.
    /// </summary>
    private Vector3 GetConveyorDirection(SnapTarget startSnap)
    {
        Vector3 playerForward = playerTransform != null ? playerTransform.forward : Vector3.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f)
            playerForward = Vector3.forward;

        return GetAttachmentDirection(startSnap, true, playerForward.normalized);
    }

    /// <summary>
    /// Rejects only the narrow cone directly behind the first point. A cubic Bezier with start and
    /// end in exactly opposite directions can overlap itself, which causes the square extrusion to
    /// flip or twist. Endpoints just outside this cone remain valid and make the intended C-bend.
    /// </summary>
    private bool IsEndpointBehindStart(Vector3 start, Vector3 end, Vector3 startDirection)
    {
        Vector3 planarDirection = startDirection;
        planarDirection.y = 0f;
        Vector3 planarToEnd = end - start;
        planarToEnd.y = 0f;

        if (planarDirection.sqrMagnitude < 0.0001f || planarToEnd.sqrMagnitude < 0.0001f)
            return false;

        float angle = Vector3.Angle(planarDirection, planarToEnd);
        return angle >= 180f - rearPlacementDeadZone;
    }

    /// <summary>
    /// Rebuilds the square-profile mesh after the spline is configured. The square template is flat
    /// relative to spline up, so Conveyor continues to place carried items above its upper face.
    /// </summary>
    private void RebuildConveyorMesh(SplineContainer container, Mesh mesh, bool capEnds = true,
        float radius = -1f, float segmentsPerUnit = -1f)
    {
        radius = radius > 0f ? radius : conveyorRadius;
        segmentsPerUnit = segmentsPerUnit > 0f ? segmentsPerUnit : conveyorSegmentsPerUnit;
        int segments = Mathf.Max(2,
            Mathf.CeilToInt(container.CalculateLength() * segmentsPerUnit));

        // Square spans -0.5 to +0.5, so multiply by two to preserve the existing belt width.
        SplineMesh.Extrude(container.Spline, mesh, radius * 2f, segments, capEnds, new Square());
    }

    public FactoryMachine BuildSmelter(Vector3 groundPoint, Quaternion rotation)
    {
        FactoryData data = GetFactoryData(BuildableType.Smelter);
        var smelterObj = data != null && data.factoryPrefab != null
            ? Instantiate(data.factoryPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        smelterObj.name = data?.DisplayName ?? "Smelter";
        Vector3 center = groundPoint + Vector3.up * 1.2f;
        smelterObj.transform.position = center;
        smelterObj.transform.rotation = rotation;
        if (data == null || data.factoryPrefab == null)
            smelterObj.transform.localScale = new Vector3(2f, 2.4f, 2f);
        if (smelterObj.TryGetComponent<Renderer>(out var smelterRenderer))
            smelterRenderer.sharedMaterial = machineMaterial;

        var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lamp.name = "Smelter Status Light";
        lamp.transform.SetParent(smelterObj.transform, false);
        lamp.transform.localPosition = new Vector3(0f, 0.7f / 2.4f, 1.02f / 2f);
        lamp.transform.localScale = new Vector3(0.7f / 2f, 0.2f / 2.4f, 0.08f / 2f);
        lamp.transform.localRotation = Quaternion.identity;
        Destroy(lamp.GetComponent<Collider>());
        lamp.GetComponent<Renderer>().sharedMaterial = yellowMaterial;

        var machine = smelterObj.GetComponent<FactoryMachine>() ?? smelterObj.AddComponent<FactoryMachine>();
        machine.factoryData = data;
        machine.idleMaterial = yellowMaterial;
        machine.workingMaterial = greenMaterial;
        machine.blockedMaterial = redMaterial;
        machine.statusLight = lamp.GetComponent<Renderer>();
        machine.ingotPrefab = ingotPrefab;
        machine.processingSeconds = data != null ? data.processingSeconds : 2f;
        machine.inputCapacity = data != null ? data.inputCapacity : 10;
        machine.beltSurfaceHeight = 0.83f;

        CreateSign("SMELTER\n1 ore > 1 ingot\nPress E to load", center + Vector3.up * 1.9f, smelterObj.transform, Color.white);

        // Auto-connect to conveyor starting in front of smelter
        Vector3 front = center + (rotation * Vector3.forward) * 1.8f;
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (!conveyor.HasInputConnection && Vector3.Distance(conveyor.StartPosition, front) < 2.5f)
            {
                machine.outputBelt = conveyor;
                conveyor.inputMachine = machine;
                break;
            }
        }

        // Auto-connect to conveyor ending near input
        Transform inputTransform = smelterObj.transform.Find("Input");
        Vector3 inputPos = inputTransform != null ? inputTransform.position : center - (rotation * Vector3.forward) * 1.8f;
        var receiver = (inputTransform != null ? inputTransform.gameObject : smelterObj).GetComponent<MachineReceiver>();
        if (receiver == null)
        {
            receiver = (inputTransform != null ? inputTransform.gameObject : smelterObj).AddComponent<MachineReceiver>();
            receiver.machine = machine;
        }
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (!conveyor.HasOutputConnection && conveyor.ConveyerSpline != null && conveyor.ConveyerSpline.Spline != null)
            {
                Vector3 endPos = conveyor.ConveyerSpline.EvaluatePosition(1f);
                if (Vector3.Distance(endPos, inputPos) < 2.5f)
                {
                    conveyor.outputReceiver = receiver;
                    receiver.inputConveyor = conveyor;
                    break;
                }
            }
        }

        return machine;
    }

    public SubmissionBin BuildSubmissionBin(Vector3 groundPoint, Quaternion rotation)
    {
        FactoryData data = GetFactoryData(BuildableType.SubmissionBin);
        var binRoot = data != null && data.factoryPrefab != null
            ? Instantiate(data.factoryPrefab)
            : new GameObject("Submission Bin");
        binRoot.name = data?.DisplayName ?? "Submission Bin";
        binRoot.transform.position = groundPoint;
        binRoot.transform.rotation = rotation;

        var bin = binRoot.GetComponent<SubmissionBin>() ?? binRoot.AddComponent<SubmissionBin>();
        bin.targetCount = 10;
        bin.acceptedItem = data != null ? data.inputItem : null;

        if (data == null || data.factoryPrefab == null)
        {
            CreateChildCube("Bin Base", binRoot.transform, new Vector3(0f, 0.15f, 0f), new Vector3(2.8f, 0.3f, 2.8f), binMaterial);
            CreateChildCube("Bin Left", binRoot.transform, new Vector3(-1.3f, 0.85f, 0f), new Vector3(0.2f, 1.4f, 2.8f), yellowMaterial);
            CreateChildCube("Bin Right", binRoot.transform, new Vector3(1.3f, 0.85f, 0f), new Vector3(0.2f, 1.4f, 2.8f), yellowMaterial);
            CreateChildCube("Bin Front", binRoot.transform, new Vector3(0f, 0.85f, 1.3f), new Vector3(2.4f, 1.4f, 0.2f), binMaterial);
            CreateChildCube("Bin Back", binRoot.transform, new Vector3(0f, 0.85f, -1.3f), new Vector3(2.4f, 1.4f, 0.2f), binMaterial);
        }

        var trigger = binRoot.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.85f, 0f);
        trigger.size = new Vector3(2.4f, 1.4f, 2.4f);

        bin.counterLabel = CreateSign("SUBMISSION BIN\n0 / 10 INGOTS", groundPoint + Vector3.up * 2.7f, binRoot.transform, yellowMaterial.color);

        // Auto-connect conveyor ending near this bin
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (conveyor.ConveyerSpline != null)
            {
                Vector3 endPos = conveyor.ConveyerSpline.EvaluatePosition(1f);
                if (!bin.HasInputConnection && !conveyor.HasOutputConnection
                    && Vector3.Distance(endPos, groundPoint + Vector3.up * 1.0f) < 3.5f)
                {
                    conveyor.outputReceiver = bin;
                    bin.inputConveyor = conveyor;
                    break;
                }
            }
        }

        return bin;
    }

    public ResourceSpot BuildResourceSpot(Vector3 groundPoint, Quaternion rotation)
    {
        FactoryData data = GetFactoryData(BuildableType.ResourceSpot);
        var spotObj = data != null && data.factoryPrefab != null
            ? Instantiate(data.factoryPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        spotObj.name = data?.DisplayName ?? "Iron Ore Spot";
        Vector3 center = groundPoint + Vector3.up * 0.55f;
        spotObj.transform.position = center;
        spotObj.transform.rotation = rotation;
        if (data == null || data.factoryPrefab == null)
            spotObj.transform.localScale = new Vector3(2f, 1.1f, 2f);
        if (spotObj.TryGetComponent<Renderer>(out var spotRenderer))
            spotRenderer.sharedMaterial = oreMaterial;

        var spot = spotObj.GetComponent<ResourceSpot>() ?? spotObj.AddComponent<ResourceSpot>();
        spot.resourceData = data != null ? data.outputItem : null;
        spot.gatherInterval = 0.4f;

        CreateSign("IRON ORE\nHold E to gather", center + Vector3.up * 1.7f, spotObj.transform, yellowMaterial.color);

        return spot;
    }

    public Presser BuildPresser(Vector3 groundPoint, Quaternion rotation)
    {
        FactoryData data = GetFactoryData(BuildableType.Presser);
        var presserObj = data != null && data.factoryPrefab != null
            ? Instantiate(data.factoryPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        presserObj.name = data?.DisplayName ?? "Presser";
        presserObj.transform.position = groundPoint;
        presserObj.transform.rotation = rotation;

        var presser = presserObj.GetComponent<Presser>() ?? presserObj.AddComponent<Presser>();
        presser.factoryData = data;
        presser.idleMaterial = yellowMaterial;
        presser.workingMaterial = greenMaterial;
        presser.blockedMaterial = redMaterial;
        presser.processingSeconds = data != null ? data.processingSeconds : 1.5f;
        presser.inputCapacity = data != null ? data.inputCapacity : 10;
        presser.InitReferences();

        if (presser.statusLight == null)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Status Light";
            lamp.transform.SetParent(presserObj.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 2.3f, 0.8f);
            lamp.transform.localScale = new Vector3(0.5f, 0.15f, 0.08f);
            Destroy(lamp.GetComponent<Collider>());
            presser.statusLight = lamp.GetComponent<Renderer>();
            lamp.GetComponent<Renderer>().sharedMaterial = yellowMaterial;
        }

        CreateSign("PRESSER\n1 ingot > 1 plate\nDrop ingot to press", groundPoint + Vector3.up * 2.8f, presserObj.transform, Color.white);

        // Auto-connect to conveyor starting in front of output port
        Vector3 outputPos = presser.outputPort != null
            ? presser.outputPort.position
            : groundPoint + (rotation * Vector3.forward) * 1.8f;
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (!conveyor.HasInputConnection && Vector3.Distance(conveyor.StartPosition, outputPos) < 2.5f)
            {
                presser.outputBelt = conveyor;
                conveyor.inputMachine = presser;
                break;
            }
        }

        // Auto-connect to conveyor ending near input port
        Vector3 inputPos = presser.inputPort != null
            ? presser.inputPort.position
            : groundPoint - (rotation * Vector3.forward) * 1.8f;
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (conveyor.ConveyerSpline != null)
            {
                Vector3 endPos = conveyor.ConveyerSpline.EvaluatePosition(1f);
                if (presser.receiver != null && !presser.receiver.HasInputConnection && !conveyor.HasOutputConnection
                    && Vector3.Distance(endPos, inputPos) < 2.5f)
                {
                    conveyor.outputReceiver = presser.receiver;
                    presser.receiver.inputConveyor = conveyor;
                    break;
                }
            }
        }

        return presser;
    }

    public Drill BuildDrill(Vector3 groundPoint, Quaternion rotation)
    {
        FactoryData data = GetFactoryData(BuildableType.Drill);
        var drillObj = data != null && data.factoryPrefab != null
            ? Instantiate(data.factoryPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        drillObj.name = data?.DisplayName ?? "Drill";
        drillObj.transform.position = groundPoint;
        drillObj.transform.rotation = rotation;

        var drill = drillObj.GetComponent<Drill>() ?? drillObj.AddComponent<Drill>();
        drill.factoryData = data;
        drill.idleMaterial = yellowMaterial;
        drill.workingMaterial = greenMaterial;
        drill.blockedMaterial = redMaterial;
        drill.processingSeconds = data != null ? data.processingSeconds : 2.0f;
        drill.inputCapacity = data != null ? data.inputCapacity : 10;
        drill.InitReferences();

        if (drill.statusLight == null)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Status Light";
            lamp.transform.SetParent(drillObj.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 2.1f, 0.9f);
            lamp.transform.localScale = new Vector3(0.5f, 0.15f, 0.08f);
            Destroy(lamp.GetComponent<Collider>());
            drill.statusLight = lamp.GetComponent<Renderer>();
            lamp.GetComponent<Renderer>().sharedMaterial = yellowMaterial;
        }

        CreateSign("DRILL\nAuto-mines ore\nPress E to collect", groundPoint + Vector3.up * 2.8f, drillObj.transform, Color.white);

        // Auto-connect to conveyor starting in front of output port
        Vector3 outputPos = drill.outputPort != null
            ? drill.outputPort.position
            : groundPoint + (rotation * Vector3.forward) * 1.8f;
        foreach (var conveyor in FindObjectsByType<Conveyor>())
        {
            if (!conveyor.HasInputConnection && Vector3.Distance(conveyor.StartPosition, outputPos) < 2.5f)
            {
                drill.outputBelt = conveyor;
                conveyor.inputMachine = drill;
                break;
            }
        }

        return drill;
    }

    /// <summary>
    /// Claims free endpoint ports for a new conveyor. Explicit placement snaps are preferred;
    /// the proximity fallback keeps programmatic construction working. Every assignment is made
    /// on both sides of the connection, which prevents later belts from reusing that port.
    /// </summary>
    private void ConnectConveyorEndpoints(Conveyor conveyor, Vector3 start, Vector3 end,
        SnapTarget startSnap, SnapTarget endSnap)
    {
        // 1. Claim the source: either a factory output or an existing belt's output endpoint.
        FactoryMachine sourceMachine = startSnap.machine;
        Conveyor sourceConveyor = startSnap.conveyor;

        if (sourceMachine == null && sourceConveyor == null)
        {
            foreach (var machine in FindObjectsByType<FactoryMachine>())
            {
                Vector3 outputPort = (machine is Presser pm && pm.outputPort != null)
                    ? pm.outputPort.position
                    : machine.transform.position + machine.transform.forward * 1.8f;
                if (machine.outputBelt == null && (Vector3.Distance(outputPort, start) < 2.5f
                    || Vector3.Distance(machine.transform.position, start) < 3.0f))
                {
                    sourceMachine = machine;
                    break;
                }
            }
        }

        if (sourceMachine != null && sourceMachine.outputBelt == null)
        {
            sourceMachine.outputBelt = conveyor;
            conveyor.inputMachine = sourceMachine;
        }
        else if (sourceConveyor != null && !sourceConveyor.HasOutputConnection)
        {
            sourceConveyor.outputConveyor = conveyor;
            conveyor.inputConveyor = sourceConveyor;
            RefreshMeshCaps(sourceConveyor);
        }

        // 2. Claim the destination: either a receiver input or an existing belt's start endpoint.
        ConveyorReceiver targetReceiver = endSnap.receiver;
        Conveyor targetConveyor = endSnap.conveyor;

        if (targetReceiver == null && targetConveyor == null)
        {
            foreach (var receiver in FindObjectsByType<ConveyorReceiver>())
            {
                Vector3 intakePos = (receiver is PresserReceiver pr && pr.presser != null && pr.presser.inputPort != null)
                    ? pr.presser.inputPort.position
                    : receiver.transform.position;
                if (!receiver.HasInputConnection && Vector3.Distance(intakePos, end) < 3.5f)
                {
                    targetReceiver = receiver;
                    break;
                }
            }
        }

        if (targetReceiver != null && !targetReceiver.HasInputConnection)
        {
            conveyor.outputReceiver = targetReceiver;
            targetReceiver.inputConveyor = conveyor;
        }
        else if (targetConveyor != null && !targetConveyor.HasInputConnection)
        {
            conveyor.outputConveyor = targetConveyor;
            targetConveyor.inputConveyor = conveyor;
            RefreshMeshCaps(targetConveyor);
        }
    }

    /// <summary>
    /// Removes the generated end caps from an already-placed conveyor after it gains a belt link.
    /// Both halves of the junction then share an open spline endpoint instead of overlapping faces.
    /// </summary>
    private void RefreshMeshCaps(Conveyor conveyor)
    {
        if (conveyor == null || conveyor.ConveyerSpline == null
            || !conveyor.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
            return;

        RebuildConveyorMesh(conveyor.ConveyerSpline, filter.sharedMesh,
            capEnds: !conveyor.HasInputConnection && !conveyor.HasOutputConnection,
            radius: conveyor.meshRadius,
            segmentsPerUnit: conveyor.meshSegmentsPerUnit);

        if (conveyor.TryGetComponent<MeshCollider>(out var collider))
            collider.sharedMesh = filter.sharedMesh;
    }

    private static GameObject CreateChildCube(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localScale = localScale;
        cube.transform.localRotation = Quaternion.identity;
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        return cube;
    }

    private static TextMesh CreateSign(string text, Vector3 position, Transform parent, Color color)
    {
        var signObj = new GameObject(text.Split('\n')[0] + " Sign");
        signObj.transform.position = position;
        signObj.transform.rotation = Quaternion.Euler(0, 180, 0);
        signObj.transform.SetParent(parent, true);

        var sign = signObj.AddComponent<TextMesh>();
        sign.text = text;
        sign.characterSize = 0.035f;
        sign.fontSize = 64;
        sign.anchor = TextAnchor.MiddleCenter;
        sign.alignment = TextAlignment.Center;
        sign.color = color;
        return sign;
    }

    #endregion

    #region Helpers & Setup

    private void CreatePreviewObjects()
    {
        if (previewRoot != null)
            return;

        previewRoot = new GameObject("[BuildMode_PreviewRoot]");
        previewRoot.hideFlags = HideFlags.DontSave;

        // Conveyor start marker
        previewStartMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        previewStartMarker.name = "ConveyorStartMarker";
        previewStartMarker.transform.SetParent(previewRoot.transform, false);
        previewStartMarker.transform.localScale = new Vector3(conveyorRadius * 2f, 0.15f, conveyorRadius * 2f);
        Destroy(previewStartMarker.GetComponent<Collider>());
        previewStartMarker.GetComponent<Renderer>().sharedMaterial = ghostMaterial;
        SetLayerRecursively(previewStartMarker, 2);

        // Conveyor spline preview
        previewSplineObj = new GameObject("ConveyorSplinePreview");
        previewSplineObj.transform.SetParent(previewRoot.transform, false);
        previewSplineContainer = previewSplineObj.AddComponent<SplineContainer>();
        var filter = previewSplineObj.AddComponent<MeshFilter>();
        var renderer = previewSplineObj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ghostMaterial;

        // The preview uses the same square mesh generator as the final conveyor.
        previewSplineMesh = new Mesh { name = "Square Conveyor Preview Mesh" };
        previewSplineMesh.hideFlags = HideFlags.DontSave;
        filter.sharedMesh = previewSplineMesh;
        SetLayerRecursively(previewSplineObj, 2);

        // Building preview (Cube)
        previewBuildingObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        previewBuildingObj.name = "BuildingPreview";
        previewBuildingObj.transform.SetParent(previewRoot.transform, false);
        Destroy(previewBuildingObj.GetComponent<Collider>());
        previewBuildingObj.GetComponent<Renderer>().sharedMaterial = ghostMaterial;
        SetLayerRecursively(previewBuildingObj, 2);

        UpdatePreviewMesh();
        previewRoot.SetActive(false);
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void ResolveMaterials()
    {
        try
        {
#if UNITY_EDITOR
            if (machineMaterial == null)
                machineMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Machine.mat");
            if (binMaterial == null)
                binMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Bin.mat");
            if (oreMaterial == null)
                oreMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Iron Ore.mat");
            if (yellowMaterial == null)
                yellowMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Safety Yellow.mat");
            if (greenMaterial == null)
                greenMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Working.mat");
            if (redMaterial == null)
                redMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Blocked.mat");

            if (ingotPrefab == null)
            {
                var ingotObj = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Resources/Iron Ingot.prefab");
                if (ingotObj != null)
                    ingotPrefab = ingotObj.GetComponent<FactoryItem>();
            }
#endif

            Shader litShader = null;
            if (machineMaterial != null)
                litShader = machineMaterial.shader;
            else if (binMaterial != null)
                litShader = binMaterial.shader;

            litShader ??= Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard");

            if (litShader == null)
            {
                var anyRenderer = FindAnyObjectByType<Renderer>();
                if (anyRenderer != null && anyRenderer.sharedMaterial != null)
                    litShader = anyRenderer.sharedMaterial.shader;
            }

            if (litShader != null)
            {
                machineMaterial ??= new Material(litShader) { color = new Color(0.12f, 0.23f, 0.29f), name = "Machine_Runtime" };
                binMaterial ??= new Material(litShader) { color = new Color(0.12f, 0.15f, 0.18f), name = "Bin_Runtime" };
                oreMaterial ??= new Material(litShader) { color = new Color(0.48f, 0.24f, 0.12f), name = "Ore_Runtime" };
                yellowMaterial ??= new Material(litShader) { color = new Color(1f, 0.65f, 0.08f), name = "Yellow_Runtime" };
                greenMaterial ??= new Material(litShader) { color = new Color(0.2f, 0.9f, 0.4f), name = "Green_Runtime" };
                redMaterial ??= new Material(litShader) { color = new Color(0.95f, 0.25f, 0.15f), name = "Red_Runtime" };
            }

            if (ingotPrefab == null)
            {
                var existingMachine = FindAnyObjectByType<FactoryMachine>();
                if (existingMachine != null && existingMachine.ingotPrefab != null)
                    ingotPrefab = existingMachine.ingotPrefab;
            }

            if (ghostMaterial == null)
            {
                var unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? litShader;

                if (unlitShader != null)
                {
                    ghostMaterial = new Material(unlitShader)
                    {
                        name = "BuildGhost_Runtime",
                        color = new Color(0.2f, 0.85f, 1f, 0.45f)
                    };
                    ghostMaterial.SetFloat("_Surface", 1);
                    ghostMaterial.SetFloat("_Blend", 0);
                    ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    ghostMaterial.SetInt("_ZWrite", 0);
                    ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                else if (machineMaterial != null)
                {
                    ghostMaterial = new Material(machineMaterial)
                    {
                        name = "BuildGhost_Runtime",
                        color = new Color(0.2f, 0.85f, 1f, 0.45f)
                    };
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[BuildController] Could not resolve all materials: {ex.Message}");
        }
    }

    private void OnDisable()
    {
        if (IsBuildModeActive)
            SetBuildModeActive(false);
        buildTabCatalog?.SetVisible(false);
        InputManager.Instance?.ExitUIMode(this);
    }

    private void OnDestroy()
    {
        if (previewRoot != null)
            Destroy(previewRoot);
    }

    #endregion
}
