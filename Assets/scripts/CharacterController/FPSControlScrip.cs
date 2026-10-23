using UnityEngine;

public class FPSControlScrip : MonoBehaviour
{
    [SerializeField]
    private CharacterController pControl;
   
    void Start()
    {
        pControl = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        if(!pControl.isGrounded)
        {
            // Handle falling physics or other non-grounded behavior

        }
    }
}
