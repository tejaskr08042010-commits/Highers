using UnityEngine;

public class StillWaterReactor : MonoBehaviour
{
    public float gravity = -10f;
    public float Radius;

    private float FluidDensity;
    private float waterlvl;
    private float Y;
    private float HIW; // HeightInsideWater
    private Rigidbody rb;

    private float H;
    private float Viscosity;

    void Start()
    {
        Radius = (this.transform.localScale.x + this.transform.localScale.y + this.transform.localScale.z) / 6f; // An extra factor of 2 as scale rperesents the diameter, not radius
        rb = this.GetComponent<Rigidbody>();
    }

    void Update()
    {
        Y = this.transform.position.y;
        HIW = -((Y - Radius) - waterlvl);
    }

    void OnTriggerEnter(Collider c)
    {
        if ( c.CompareTag("WaterLevel"))
        {
            waterlvl = c.GetComponent<StillWater>().Ylvl;
            FluidDensity = c.GetComponent<StillWater>().Density;
            Viscosity = c.GetComponent<StillWater>().Viscosity;
        }
    }
    
    void OnTriggerStay(Collider c)
    {
        if (c.CompareTag("WaterLevel"))
        {
            if (HIW > 0 && HIW < 2 * Radius)
            {
                rb.AddForce((((Mathf.PI) * HIW * HIW * (3f * Radius - HIW)) / 3f) * FluidDensity * gravity * Vector3.down);
            }

            if (HIW >= 2 * Radius)
            {
                rb.AddForce((4f / 3f) * (Mathf.PI) * Radius * Radius * Radius * FluidDensity * gravity * Vector3.down);
            }

            rb.AddForce(6f * (Mathf.PI) * Viscosity * Radius * (-rb.linearVelocity));


        }

    }
    
}
