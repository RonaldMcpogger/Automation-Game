using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays live storage from the FactoryMachine the player is currently using.</summary>
public class MachineInterface : MonoBehaviour
{
    public static MachineInterface Instance { get; private set; }

    [Header("Machine Data")]
    [SerializeField] private TMP_Text machineName;
    [Header("Machine Controls")]
    [Tooltip("Starts or resumes the machine currently displayed by this menu.")]
    [SerializeField] private Button startButton;
    [Tooltip("Pauses the displayed machine while preserving its items and processing progress.")]
    [SerializeField] private Button stopButton;
    [Header("Slot Lists")]
    [SerializeField] private GameObject InputContent;
    [SerializeField] private GameObject InputSlotPrefab;
    [SerializeField] private GameObject OutputContent;
    [SerializeField] private GameObject OutputSlotPrefab;

    private readonly List<GameObject> inputViews = new();
    private readonly List<GameObject> outputViews = new();
    private FactoryMachine machine;
    private CanvasGroup canvasGroup;
    private bool isOpen;

    public bool IsOpen => isOpen;
    public FactoryMachine ActiveMachine => machine;

    /// <summary>Finds the existing scene menu and adds its controller when first needed.</summary>
    public static MachineInterface EnsureInstance()
    {
        if (Instance != null) return Instance;
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate.name == "Machine Menu" && candidate.gameObject.scene.IsValid())
                return candidate.GetComponent<MachineInterface>() ?? candidate.gameObject.AddComponent<MachineInterface>();
        }
        return null;
    }

    /// <summary>Initializes the menu, finds its scene UI references, and hides it on startup.</summary>
    private void Awake()
    {
        Instance = this;
        canvasGroup = GetComponent<CanvasGroup>();
        AutoWireSceneReferences();
        if (startButton != null) startButton.onClick.AddListener(StartMachine);
        if (stopButton != null) stopButton.onClick.AddListener(StopMachine);
        SetOpen(false);
    }

    /// <summary>Clears the shared menu reference when this menu is destroyed.</summary>
    private void OnDestroy()
    {
        if (startButton != null) startButton.onClick.RemoveListener(StartMachine);
        if (stopButton != null) stopButton.onClick.RemoveListener(StopMachine);
        if (Instance == this) Instance = null;
    }

    /// <summary>Closes on cancel input and keeps the visible slot data current.</summary>
    private void Update()
    {
        if (!isOpen) return;
        if (InputManager.Instance != null && InputManager.Instance.UICancelPressed) { Close(); return; }
        if (machine == null) { Close(); return; }
        Refresh(); // Recipes may change a slot while this menu is open.
    }

    /// <summary>Opens the menu and starts displaying data from the specified machine.</summary>
    public void Open(FactoryMachine target)
    {
        if (target == null) return;
        AutoWireSceneReferences();
        if (InputContent == null || OutputContent == null)
        {
            Debug.LogWarning("Machine Menu is missing its input or output slot list.", this);
            return;
        }
        machine = target;
        SetOpen(true);
        Refresh();
    }

    /// <summary>Hides the menu, clears the selected machine, and restores gameplay input.</summary>
    public void Close()
    {
        machine = null;
        SetOpen(false);
    }

    /// <summary>Starts or resumes the selected machine from the menu's Start button.</summary>
    public void StartMachine()
    {
        if (machine != null) machine.StartMachine();
        RefreshControls();
    }

    /// <summary>Pauses the selected machine from the menu's Stop button.</summary>
    public void StopMachine()
    {
        if (machine != null) machine.StopMachine();
        RefreshControls();
    }

    /// <summary>Changes menu visibility and switches the input system between UI and gameplay modes.</summary>
    private void SetOpen(bool open)
    {
        isOpen = open;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = open ? 1f : 0f;
            canvasGroup.interactable = open;
            canvasGroup.blocksRaycasts = open;
        }
        if (open) InputManager.Instance?.EnterUIMode(this);
        else InputManager.Instance?.ExitUIMode(this);
        RefreshControls();
    }

    /// <summary>Updates the title and both slot lists using the active machine's live data.</summary>
    private void Refresh()
    {
        if (machine == null) return;
        if (machineName != null)
            machineName.text = machine.factoryData != null ? machine.factoryData.DisplayName : machine.name;

        RefreshSlots(machine.InputSlots, InputContent.transform, InputSlotPrefab, inputViews, machine.inputCapacity, "Input");
        RefreshSlots(machine.OutputSlots, OutputContent.transform, OutputSlotPrefab, outputViews, machine.outputCapacity, "Output");
        RefreshControls();
    }

    /// <summary>Enables only the action that changes the selected machine's running state.</summary>
    private void RefreshControls()
    {
        bool hasMachine = isOpen && machine != null;
        if (startButton != null) startButton.interactable = hasMachine && !machine.IsRunning;
        if (stopButton != null) stopButton.interactable = hasMachine && machine.IsRunning;
    }

    /// <summary>Creates enough slot views and updates each one with its item, amount, and capacity.</summary>
    private static void RefreshSlots(IReadOnlyList<FactorySlot> slots, Transform content, GameObject template,
        List<GameObject> views, int capacity, string slotKind)
    {
        if (content == null || template == null) return;
        while (views.Count < slots.Count)
        {
            GameObject view = Instantiate(template, content);
            view.name = $"{slotKind} Slot {views.Count + 1}";
            view.SetActive(true);
            views.Add(view);
        }

        for (int i = 0; i < views.Count; i++)
        {
            bool exists = i < slots.Count;
            views[i].SetActive(exists);
            if (!exists) continue;

            FactorySlot slot = slots[i];
            bool occupied = !slot.IsEmpty;
            string itemName = occupied ? slot.item.DisplayName : "Empty";
            TMP_Text[] labels = views[i].GetComponentsInChildren<TMP_Text>(true);
            if (labels.Length > 0)
                labels[0].text = $"{slotKind} {i + 1}\n{itemName}\n{(occupied ? slot.amount : 0)} / {Mathf.Max(1, capacity)}";
            for (int label = 1; label < labels.Length; label++) labels[label].text = string.Empty;

            Image[] images = views[i].GetComponentsInChildren<Image>(true);
            if (images.Length > 1) // Root image is the background; first child image is the item icon.
            {
                images[1].sprite = occupied ? slot.item.ItemImage : null;
                images[1].enabled = occupied && slot.item.ItemImage != null;
            }
            Button button = views[i].GetComponent<Button>();
            if (button != null) button.interactable = false; // Transfer actions are intentionally a later step.
        }
    }

    /// <summary>Finds the existing Machine Menu objects in the scene when inspector fields are unassigned.</summary>
    private void AutoWireSceneReferences()
    {
        Transform background = FindDescendant(transform, "MachineMenuBackground");
        if (background == null) return;
        if (machineName == null)
        {
            Transform title = FindDescendant(background, "Factory Name") ?? FindDescendant(background, "FactoryName");
            if (title != null) machineName = title.GetComponent<TMP_Text>();
        }
        WireSlotList(background, "Input slot list", ref InputContent, ref InputSlotPrefab);
        WireSlotList(background, "OutputSlot List", ref OutputContent, ref OutputSlotPrefab);
        if (startButton == null)
            startButton = FindDescendant(background, "Start")?.GetComponent<Button>();
        if (stopButton == null)
            stopButton = FindDescendant(background, "Stop")?.GetComponent<Button>();
    }

    /// <summary>Connects a named scroll list to its content transform and first slot template.</summary>
    private static void WireSlotList(Transform root, string listName, ref GameObject content, ref GameObject template)
    {
        Transform list = FindDescendant(root, listName);
        if (list == null) return;
        ScrollRect scroll = list.GetComponent<ScrollRect>();
        if (content == null && scroll != null) content = scroll.content.gameObject;
        if (template == null && content != null && content.transform.childCount > 0)
        {
            template = content.transform.GetChild(0).gameObject;
            template.SetActive(false);
        }
    }

    /// <summary>Returns the first child transform with the requested name, including inactive children.</summary>
    private static Transform FindDescendant(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child;
        return null;
    }
}
