using UnityEngine;

public class OperatorHitResponse : MonoBehaviour
{
    [Header("Collider")]
    [Tooltip("The child sphere collider that receives projectile hits")]
    public SphereCollider hitCollider;

    [Header("Particle System")]
    [Tooltip("The Operator's main particle system")]
    public ParticleSystem operatorParticles;

    [Header("References")]
    [Tooltip("The OperatorAI script to apply momentum hit to")]
    public OperatorAI operatorAI;

    [Header("Trail Materials")]
    [Tooltip("Default black material")]
    public Material materialBlack;
    [Tooltip("Normal (uncharged) hit - magenta")]
    public Material materialMagenta;
    [Tooltip("Powered (partially charged) hit - red")]
    public Material materialRed;
    [Tooltip("Fully charged hit - yellow")]
    public Material materialYellow;

    [Header("Knockback Speeds")]
    public float normalKnockbackSpeed = 2500f;
    public float poweredKnockbackSpeed = 3150f;
    public float fullyChargedKnockbackSpeed = 4000f;

    [Header("Settings")]
    [Tooltip("How long the hit material stays before reverting")]
    public float hitMaterialDuration = 0.25f;

    public event System.Action<ProjectileHitType, Vector3> OnAnyHit;

    private float revertTimer = 0f;
    private bool isFlashing = false;

    void Update()
    {
        if (!isFlashing) return;

        revertTimer -= Time.deltaTime;
        if (revertTimer <= 0f)
        {
            SetParticleMaterial(materialBlack);
            isFlashing = false;
        }
    }

    public void OnProjectileHit(ProjectileHitType hitType, Vector3 hitNormal)
    {
        OnAnyHit?.Invoke(hitType, hitNormal);

        if (operatorAI != null)
            operatorAI.CancelAttackOnHit();

        switch (hitType)
        {
            case ProjectileHitType.Normal:
                SetParticleMaterial(materialMagenta);
                if (operatorAI != null)
                {
                    operatorAI.ApplyMomentumHit(0.7f);
                    operatorAI.ApplyKnockback(hitNormal, normalKnockbackSpeed);
                }
                break;

            case ProjectileHitType.Powered:
                SetParticleMaterial(materialRed);
                if (operatorAI != null)
                {
                    operatorAI.ApplyMomentumHit(1.0f);
                    operatorAI.ApplyKnockback(hitNormal, poweredKnockbackSpeed);
                }
                break;

            case ProjectileHitType.FullyCharged:
                SetParticleMaterial(materialYellow);
                if (operatorAI != null)
                    operatorAI.ApplyKnockback(hitNormal, fullyChargedKnockbackSpeed);
                break;
        }

        revertTimer = hitMaterialDuration;
        isFlashing = true;
    }

    void SetParticleMaterial(Material mat)
    {
        if (mat == null || operatorParticles == null) return;

        ParticleSystemRenderer psr = operatorParticles.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
            psr.material = mat;
    }
}