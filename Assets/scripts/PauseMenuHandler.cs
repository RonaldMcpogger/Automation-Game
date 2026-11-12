using UnityEngine;

public class PauseMenuHandler : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Paus Menu Settings")]
    [SerializeField] private CanvasGroup PauseArea;
    [SerializeField] public bool IsPaused;
    private InputManager InputInstance;
    void Start()
    {
        InputInstance = InputManager.Instance;
        PauseArea.alpha = 0f;
        PauseArea.interactable = false;
        PauseArea.blocksRaycasts = false;
        setPause(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (InputInstance != null)
        {   // pause and not have other tabs conflict
            if (!IsPaused && InputInstance.GameplayEnabled && InputInstance.EscapePressed)
            {
                setPause(true);

            }
            else if (IsPaused && InputInstance.UICancelPressed)
            {
                setPause(false);
            }

        } 
    }
    private void setPause(bool pause)
    {
        IsPaused = pause;

        if(IsPaused)
        {
            Time.timeScale = 0f;
            InputInstance.EnterUIMode(this);
            PauseArea.alpha = 1f;
            PauseArea.interactable = true;
            PauseArea.blocksRaycasts = true;
        }
        else
        {
            Time.timeScale = 1f;
            InputInstance.ExitUIMode(this);

            PauseArea.alpha = 0f;
            PauseArea.interactable = false;
            PauseArea.blocksRaycasts = false;
        }
    }
    private void OnDisable()
    {
        if (InputInstance != null)
        {
            InputInstance.ExitUIMode(this);
            Time.timeScale = 1f;
        }
    }
    public void ResumeGame()
    {
        setPause(false);
    }

    public void QuitGame()
    {
        /// if Main Menu is not implemented, this will close the application
        Application.Quit();
    }
}
