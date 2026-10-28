using UnityEngine;

public class StillWater : MonoBehaviour
{
    public float Density;
    public float Viscosity;

    public float Ylvl;
    public float Depth;

    void Start()
    {
        Ylvl = this.transform.position.y + Depth / 2f;
    }
}
