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
    }
}
