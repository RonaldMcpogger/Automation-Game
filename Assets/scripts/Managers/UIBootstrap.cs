using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Guarantees that every loaded scene in both standalone builds and the editor has:
/// 1. An active EventSystem with InputSystemUIInputModule (wired to default UI actions).
/// 2. A GraphicRaycaster on every active and inactive Canvas so mouse clicks are never lost.
/// </summary>
public static class UIBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeSceneLoad()
    {
        EnsureEventSystem();
        EnsureGraphicRaycasters();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterSceneLoad()
    {
        EnsureEventSystem();
        EnsureGraphicRaycasters();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEventSystem();
        EnsureGraphicRaycasters();
    }

    public static void EnsureEventSystem()
    {
        EventSystem es = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es = go.GetComponent<EventSystem>();
            var module = go.GetComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            Object.DontDestroyOnLoad(go);
        }
        else
        {
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                module = es.gameObject.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
        }
    }

    public static void EnsureGraphicRaycasters()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        for (int i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }
    }
}
