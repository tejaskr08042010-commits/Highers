using UnityEngine;

public class FluidDomainCheck : MonoBehaviour
{
    public bool insideZone;
    public bool ApplyAirResistance;
    public float BernoulliConstant = 100f;
    public float Density = 1f;
    public float AirResistance;
    public float FluidAccelerationSpeed;
    public float gravity = 9.8f;
    public float Range = 0.3f;
    public float Radius = 0.1f;
    public float InterParticleForce = 3f;
    public float threshold = 1f; // Force curve threshold
    public float ForceThreshold = 1f;
    public int SpeedThreshold = 1;
   

    private float speed;
    private float speedpp;
    private GameObject[] WaterParticles;
    private float InitialHeight;
    private float Pressure;
    private float CurrentHeight;
    private Rigidbody rb;
    private GameObject[] PressurePoints;

    void Start()
    {
        InitialHeight = this.transform.position.y;
        rb = this.GetComponent<Rigidbody>();

    }

    void FixedUpdate()
    {
        WaterParticles = GameObject.FindGameObjectsWithTag("Water");
        PressurePoints = GameObject.FindGameObjectsWithTag("PressurePoint");

        CurrentHeight = this.transform.position.y;

        foreach (var w in WaterParticles)
        {

            if ((Radius * Radius) > (w.transform.position - this.transform.position).sqrMagnitude)
            {
                rb.AddForce((this.transform.position - w.transform.position).normalized * (1 - ((w.transform.position - this.transform.position).magnitude) /Radius));
                w.GetComponent<Rigidbody>().AddForce((this.transform.position - w.transform.position).normalized * (1 - ((w.transform.position - this.transform.position).magnitude) /Radius));

            }
        }

        foreach (var p in PressurePoints)
        {
            speedpp = Mathf.Sqrt(2 * (BernoulliConstant - ((p.GetComponent<FluidPressureValues>().Pressure) / ((this.transform.position - p.transform.position).magnitude * (this.transform.position - p.transform.position).magnitude * threshold)) - (Density * gravity * (CurrentHeight - InitialHeight))) / Density);
            rb.AddForce((((this.transform.position - p.transform.position).normalized * ForceThreshold)) * (speedpp - rb.linearVelocity.magnitude / SpeedThreshold));

        }

        if (!insideZone)
        {

            if (ApplyAirResistance)
            {
                rb.linearVelocity *= AirResistance;

                if (rb.linearVelocity.magnitude < 0.01)
                {
                    rb.linearVelocity = Vector3.zero;
                }

                if (rb.angularVelocity.magnitude < 0.01)
                {
                    rb.angularVelocity = Vector3.zero;
                }
            }

        }


    }

    void OnTriggerStay(Collider other)
    {
        speed = Mathf.Sqrt(2 * (BernoulliConstant - Pressure - (Density * gravity * (CurrentHeight - InitialHeight))) / Density);

        if (other.CompareTag("FluidDomain"))
        {
            insideZone = true;
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, other.transform.forward * speed, FluidAccelerationSpeed * 0.001f); //It's (0.001f) multiplied as integral values give very fast acceleration
            Pressure = other.GetComponent<FluidPressureValues>().Pressure;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("FluidDomain"))
        {
            insideZone = false;
        }
    }
}