using UnityEngine;



[CreateAssetMenu(fileName = "newResource", menuName = "Automation Game /Resource Data")]
public class Resource : ScriptableObject
{

    public GameObject objModel;
    public string name;
    [Range(1, 4)] public int Tier;

}
