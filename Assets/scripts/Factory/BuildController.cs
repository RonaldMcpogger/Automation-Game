using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
//using Object = UnityEngine.Object;

using UnityEngine.Splines.ExtrusionShapes; // needed to change spline shape from circle to square for conveyor preview and construction


public enum BuildableType
{
    ConveyorBelt = 0,
    Smelter = 1,
    SubmissionBin = 2,
    ResourceSpot = 3,
    Presser = 4
}

/// <summary>
/// First-person build mode controller integrated with FPSCore's InputManager.
/// Features smart magnetic snapping to Smelter output ports, Submission Bin intakes,
/// and existing conveyor endpoints.
/// </summary>
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

    private BuildTabCatalog buildTabCatalog;
    private FactoryData selectedFactoryData;

    // FPSCore InputManager Actions
    private InputAction buildModeAction;
    private InputAction buildPlaceAction;
    private InputAction buildCancelAction;
    private InputAction buildRotateAction;
    private InputAction buildWidthAction;
    private InputAction buildScrollAction;

    private void Awake()
    {
        ResolveBuildCatalog();
        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>() ?? Camera.main;

        if (playerTransform == null)
            playerTransform = transform;

        ResolveMaterials();
        RegisterInputManagerActions();
    }

    private void ResolveBuildCatalog()
    {
        if (buildableFactories != null && buildableFactories.Length > 0)
            return;

        var loadedDefinitions = Resources.LoadAll<FactoryData>("FactoryData");
        if (loadedDefinitions == null || loadedDefinitions.Length == 0)
            return;

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
        RegisterInputManagerActions();

        buildTabCatalog = BuildTabCatalog.FindOrCreate(OnCatalogFactorySelected, BeginCatalogBuild);
        RefreshBuildCatalogUI();
        buildTabCatalog?.SetVisible(false);
    }

    private void RegisterInputManagerActions()
    {
        var im = InputManager.Instance;
        if (im == null)
            return;

        buildModeAction = im.GetExtraAction("BuildMode", "<Keyboard>/b", "<Gamepad>/dpad/up");
        buildPlaceAction = im.GetExtraAction("BuildPlace", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
        buildCancelAction = im.GetExtraAction("BuildCancel", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
        buildRotateAction = im.GetExtraAction("BuildRotate", "<Keyboard>/r", "<Gamepad>/rightShoulder");
        buildWidthAction = im.GetExtraAction("BuildWidth", "<Keyboard>/t", "<Gamepad>/leftShoulder");
        buildScrollAction = im.GetExtraVector2Action("BuildScroll", "<Mouse>/scroll");
    }

    private void Update()
    {
        HandleToggleInput();

        if (!IsBuildModeActive)
            return;

        HandleSelectionInput();
        HandleRotationInput();
        HandleWidthInput();
        HandlePlacement();
        UpdatePreview();
        UpdateUI();
    }

    private void HandleToggleInput()
    {
        bool togglePressed = false;
        if (buildModeAction != null && buildModeAction.WasPressedThisFrame())
            togglePressed = true;
        else if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            togglePressed = true;

        if (togglePressed)
        {
            if (IsBuildModeActive)
                SetBuildModeActive(false);
            else if (buildTabCatalog != null && buildTabCatalog.IsVisible)
                buildTabCatalog.SetVisible(false);
            else
                OpenBuildTab();
            return;
        }

        if (IsBuildModeActive)
        {
            bool cancelPressed = false;
            if (InputManager.Instance != null && InputManager.Instance.UICancelPressed)
                cancelPressed = true;
            else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                cancelPressed = true;

            if (cancelPressed)
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
    }

    private void HandleSelectionInput()
    {
        int hotbar = IInput.HotbarPressedIndex;
        if (buildableFactories != null && hotbar >= 0 && hotbar < buildableFactories.Length)
        {
            SetSelectedType((BuildableType)hotbar);
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) { SetSelectedType(BuildableType.ConveyorBelt); return; }
            if (Keyboard.current.digit2Key.wasPressedThisFrame) { SetSelectedType(BuildableType.Smelter); return; }
            if (Keyboard.current.digit3Key.wasPressedThisFrame) { SetSelectedType(BuildableType.SubmissionBin); return; }
            if (Keyboard.current.digit4Key.wasPressedThisFrame) { SetSelectedType(BuildableType.ResourceSpot); return; }
            if (Keyboard.current.digit5Key.wasPressedThisFrame) { SetSelectedType(BuildableType.Presser); return; }
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
            SelectCatalogIndex((GetSelectedCatalogIndex() - 1 + buildableFactories.Length) % buildableFactories.Length);
        }
        else if (scroll < -0.1f)
        {
            SelectCatalogIndex((GetSelectedCatalogIndex() + 1) % buildableFactories.Length);
        }
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

    private void OpenBuildTab()
    {
        if (buildTabCatalog == null)
            buildTabCatalog = BuildTabCatalog.FindOrCreate(OnCatalogFactorySelected, BeginCatalogBuild);

        RefreshBuildCatalogUI();
        buildTabCatalog?.SetVisible(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnCatalogFactorySelected(int catalogIndex)
    {
        SelectCatalogIndex(catalogIndex);
    }

    private void BeginCatalogBuild()
    {
        if (selectedFactoryData == null)
            return;

        buildTabCatalog?.SetVisible(false);
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

    private int GetSelectedCatalogIndex()
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

        if (SelectedType == BuildableType.ConveyorBelt)
        {
            if (!conveyorHasStart)
            {
                bool snapped = TryGetSnapTarget(true, hit.point, out SnapTarget snap);
                conveyorStartPoint = snapped ? snap.position : hit.point + Vector3.up * conveyorHeightOffset;
                activeStartMachine = snapped ? snap.machine : null;
                conveyorStartSnap = snapped ? snap : default;
                conveyorStartDirection = GetInitialConveyorDirection(conveyorStartSnap);
                conveyorHasStart = true;
            }
            else
            {
                bool snapped = TryGetSnapTarget(false, hit.point, out SnapTarget snap);
                Vector3 endPoint = snapped ? snap.position : hit.point + Vector3.up * conveyorHeightOffset;

                // Level height if close to flat and not snapped
                if (!snapped && Mathf.Abs(conveyorStartPoint.y - endPoint.y) < 0.25f)
                    endPoint.y = conveyorStartPoint.y;

                // A perfectly rear-facing cubic folds back over its own control points and twists the mesh.
                // Keep the first point active, but require the player to move the endpoint slightly left or right.
                if (IsEndpointDirectlyBehindStart(conveyorStartPoint, endPoint, conveyorStartDirection))
                    return;

                if (Vector3.Distance(conveyorStartPoint, endPoint) >= minConveyorLength)
                {
                    var conveyor = BuildConveyorBelt(
                        conveyorStartPoint,
                        endPoint,
                        conveyorStartSnap,
                        snapped ? snap : default,
                        conveyorStartDirection);

                    conveyorHasStart = false;
                    activeStartMachine = null;
                    conveyorStartSnap = default;
                    conveyorStartDirection = Vector3.zero;
                }
            }
        }
        else if (SelectedType == BuildableType.Smelter)
        {
            BuildSmelter(hit.point, Quaternion.Euler(0, BuildingRotation, 0));
        }
        else if (SelectedType == BuildableType.SubmissionBin)
        {
            BuildSubmissionBin(hit.point, Quaternion.Euler(0, BuildingRotation, 0));
        }
        else if (SelectedType == BuildableType.ResourceSpot)
        {
            BuildResourceSpot(hit.point, Quaternion.Euler(0, BuildingRotation, 0));
        }
        else if (SelectedType == BuildableType.Presser)
        {
            BuildPresser(hit.point, Quaternion.Euler(0, BuildingRotation, 0));
        }
    }

    public bool TryGetPlacementHit(out RaycastHit hit)
    {
        if (viewCamera == null)
        {
            hit = default;
            return false;
        }

        Ray ray;
        if (InputManager.Instance != null && !InputManager.Instance.CursorLocked && Mouse.current != null)
            ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        else
            ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        int layerMask = ~LayerMask.GetMask("Ignore Raycast");
        if (Physics.Raycast(ray, out hit, maxBuildDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (playerTransform != null && hit.collider.transform.IsChildOf(playerTransform))
                return false;

            if (SelectedType != BuildableType.ConveyorBelt
                && Vector3.Angle(Vector3.up, hit.normal) > maxBuildingSlope)
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
            // 1. Smelter output port (in front of smelter)
            foreach (var machine in FindObjectsByType<FactoryMachine>())
            {
                Vector3 outputPort = (machine is Presser pm && pm.outputPort != null)
                    ? pm.outputPort.position
                    : machine.transform.position + machine.transform.forward * 1.8f;
                if (!(machine is Presser pm2 && pm2.outputPort != null))
                    outputPort.y = machine.transform.position.y - 1.2f + conveyorHeightOffset;
                float dist = Vector3.Distance(hitPoint, outputPort);
                float centerDist = Vector3.Distance(hitPoint, machine.transform.position);

                // A factory output can feed only one belt.
                if (machine.outputBelt == null && (dist < bestDist || centerDist < 2.0f))
                {
                    bestDist = Mathf.Min(dist, centerDist);
                    snap.position = outputPort;
                    snap.targetName = machine is Presser ? "Presser Output" : "Smelter Output";
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
            // 1. Submission Bin intake
            foreach (var bin in FindObjectsByType<ConveyorReceiver>())
            {
                Vector3 intake = (bin is PresserReceiver pr && pr.presser != null && pr.presser.inputPort != null)
                    ? pr.presser.inputPort.position
                    : bin.transform.position + Vector3.up * 1.0f;
                float dist = Vector3.Distance(hitPoint, intake);
                float centerDist = Vector3.Distance(hitPoint, bin.transform.position);

                // A receiver intake can accept only one belt.
                if (!bin.HasInputConnection && (dist < bestDist || centerDist < 2.5f))
                {
                    bestDist = Mathf.Min(dist, centerDist);
                    snap.position = intake;
                    snap.targetName = bin is PresserReceiver ? "Presser Input" : "Submission Bin";
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

    private void UpdatePreview()
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

        if (SelectedType == BuildableType.ConveyorBelt)
        {
            previewBuildingObj.SetActive(false);

            if (!conveyorHasStart)
            {
                bool snapped = TryGetSnapTarget(true, hit.point, out currentSnap);
                Vector3 pos = snapped ? currentSnap.position : hit.point + Vector3.up * conveyorHeightOffset;

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
                Vector3 endPoint = snapped ? currentSnap.position : hit.point + Vector3.up * conveyorHeightOffset;

                if (!snapped && Mathf.Abs(conveyorStartPoint.y - endPoint.y) < 0.25f)
                    endPoint.y = conveyorStartPoint.y;

                float dist = Vector3.Distance(conveyorStartPoint, endPoint);

                if (dist >= 0.5f && !IsEndpointDirectlyBehindStart(
                    conveyorStartPoint, endPoint, conveyorStartDirection))
                {
                    previewSplineObj.SetActive(true);
                    var spline = previewSplineContainer.Spline;
                    spline.Clear();

                    ConfigureConveyorSpline(spline, conveyorStartPoint, endPoint, conveyorStartSnap,
                        snapped ? currentSnap : default, conveyorStartDirection);
                    RebuildSquareConveyorMesh(previewSplineContainer, previewSplineMesh);
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

            Quaternion rot = Quaternion.Euler(0, BuildingRotation, 0);
            previewBuildingObj.transform.rotation = rot;

            if (SelectedType == BuildableType.Smelter)
                previewBuildingObj.transform.position = hit.point + Vector3.up * 1.2f;
            else if (SelectedType == BuildableType.SubmissionBin)
                previewBuildingObj.transform.position = hit.point + Vector3.up * 0.7f;
            else if (SelectedType == BuildableType.ResourceSpot)
                previewBuildingObj.transform.position = hit.point + Vector3.up * 0.55f;
            else if (SelectedType == BuildableType.Presser)
                previewBuildingObj.transform.position = hit.point + Vector3.up * 1.2f;
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
        RebuildSquareConveyorMesh(container, mesh,
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
    private Vector3 GetInitialConveyorDirection(SnapTarget startSnap)
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
    private bool IsEndpointDirectlyBehindStart(Vector3 start, Vector3 end, Vector3 startDirection)
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
    private void RebuildSquareConveyorMesh(SplineContainer container, Mesh mesh, bool capEnds = true,
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
            RefreshConveyorMeshCaps(sourceConveyor);
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
            RefreshConveyorMeshCaps(targetConveyor);
        }
    }

    /// <summary>
    /// Removes the generated end caps from an already-placed conveyor after it gains a belt link.
    /// Both halves of the junction then share an open spline endpoint instead of overlapping faces.
    /// </summary>
    private void RefreshConveyorMeshCaps(Conveyor conveyor)
    {
        if (conveyor == null || conveyor.ConveyerSpline == null
            || !conveyor.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
            return;

        RebuildSquareConveyorMesh(conveyor.ConveyerSpline, filter.sharedMesh,
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
            var ingotObj = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FactoryPrototype/Iron Ingot.prefab");
            if (ingotObj != null)
                ingotPrefab = ingotObj.GetComponent<FactoryItem>();
        }
#endif

        var litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (machineMaterial == null)
            machineMaterial = new Material(litShader) { color = new Color(0.12f, 0.23f, 0.29f), name = "Machine_Runtime" };
        if (binMaterial == null)
            binMaterial = new Material(litShader) { color = new Color(0.12f, 0.15f, 0.18f), name = "Bin_Runtime" };
        if (oreMaterial == null)
            oreMaterial = new Material(litShader) { color = new Color(0.48f, 0.24f, 0.12f), name = "Ore_Runtime" };
        if (yellowMaterial == null)
            yellowMaterial = new Material(litShader) { color = new Color(1f, 0.65f, 0.08f), name = "Yellow_Runtime" };
        if (greenMaterial == null)
            greenMaterial = new Material(litShader) { color = new Color(0.2f, 0.9f, 0.4f), name = "Green_Runtime" };
        if (redMaterial == null)
            redMaterial = new Material(litShader) { color = new Color(0.95f, 0.25f, 0.15f), name = "Red_Runtime" };

        if (ingotPrefab == null)
        {
            var existingMachine = FindAnyObjectByType<FactoryMachine>();
            if (existingMachine != null && existingMachine.ingotPrefab != null)
                ingotPrefab = existingMachine.ingotPrefab;
        }

        if (ghostMaterial == null)
        {
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
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
    }

    private void OnDisable()
    {
        if (IsBuildModeActive)
            SetBuildModeActive(false);
        buildTabCatalog?.SetVisible(false);
    }

    private void OnDestroy()
    {
        if (previewRoot != null)
            Destroy(previewRoot);
    }

    #endregion
}
