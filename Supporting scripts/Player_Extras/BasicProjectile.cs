using System;
using System.Collections.Generic;
using UnityEngine;

public class BasicProjectiles : MonoBehaviour
{
    public event Action<Color> OnProjectileCast;

    [Header("Projectile Prefabs")]
    public GameObject normalOrbPrefab;
    public GameObject poweredOrbPrefab;
    public GameObject fullyChargedOrbPrefab;

    [Header("Explosion Prefabs")]
    public GameObject blackOutlineExplosion;
    public GameObject magentaCoreExplosion;
    public GameObject redCoreExplosion;
    public GameObject yellowCoreExplosion;

    [Header("Speed Settings")]
    public float normalSpeed = 20000f;
    public float poweredSpeed = 35000f;
    public float fullyChargedSpeed = 50000f;

    [Header("Mana Costs")]
    public float normalManaCost = 1f;
    public float poweredManaCost = 3f;
    public float fullyChargedManaCost = 10f;

    [Header("Charge Timing")]
    public float poweredChargeTime = 0.3f;
    public float fullyChargedTime = 1f;

    [Header("Lifetime Settings")]
    public float normalLifetime = 10f;
    public float poweredLifetime = 10f;
    public float fullyChargedLifetime = 10f;

    [Header("Fade Settings")]
    [Tooltip("Time in seconds after spawn when the projectile starts shrinking and fading out its particle emission, reaching zero exactly at this charge level's Lifetime. The object itself is destroyed slightly after that, once any already-emitted particles have finished their own natural lifetime.")]
    public float normalFadeTime = 8f;
    public float poweredFadeTime = 8f;
    public float fullyChargedFadeTime = 8f;

    [Header("General Settings")]
    public float castCooldown = 0.3f;
    public float spawnDistance = 3f;
    [Tooltip("Time in seconds to ignore all collisions when the projectile spawns.")]
    public float initialResistance = 0.05f;

    [Header("Recoil Settings")]
    public float normalRecoil = 500f;
    public float poweredRecoil = 900f;
    public float fullyChargedRecoil = 1500f;

    [Header("Operator Guidance")]
    public float guidanceRange = 1000f;
    [Tooltip("Smooth tracking angle adjustment when HOLDING RMB (degrees per second)")]
    public float guidanceMaxTurnRate = 70f;
    [Tooltip("Instant angle snap applied on the exact frame RMB is TAPPED (degrees per tap)")]
    public float guidanceTapTurnBoost = 20f;
    [Tooltip("Mana consumed per second while holding down RMB to guide the projectile.")]
    public float guidanceManaCostPerSec = 5f;
    [Tooltip("If the projectile is closer than this to the target, it locks on and hits in 0.1s, bypassing steering entirely.")]
    public float autoHitRange = 500f;

    [Header("References")]
    public Transform cameraTransform;
    public AdvanceManaSystem manaSystem;
    public BasicMovement_2 movementSystem;

    [Header("Debug")]
    public bool showDebug = false;

    private float lastCastTime = -999f;
    private bool canCast => Time.time >= lastCastTime + castCooldown;
    private float chargeStartTime = 0f;
    private bool isCharging = false;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (manaSystem == null)
            manaSystem = GetComponent<AdvanceManaSystem>();

        if (movementSystem == null)
            movementSystem = GetComponent<BasicMovement_2>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !isCharging && canCast)
        {
            chargeStartTime = Time.time;
            isCharging = true;
        }

        if (Input.GetMouseButtonUp(0) && isCharging)
        {
            float chargeTime = Time.time - chargeStartTime;
            TryCastSpell(chargeTime);
            isCharging = false;
        }
    }

    void TryCastSpell(float chargeTime)
    {
        if (!canCast) return;

        SpellChargeLevel chargeLevel = DetermineChargeLevel(chargeTime);
        float requiredMana = GetRequiredMana(chargeLevel);

        if (manaSystem == null || !manaSystem.HasEnoughMana(requiredMana)) return;
        if (!manaSystem.UseMana(requiredMana)) return;

        CastProjectile(chargeLevel);
        lastCastTime = Time.time;
    }

    SpellChargeLevel DetermineChargeLevel(float chargeTime)
    {
        if (chargeTime >= fullyChargedTime && manaSystem != null && manaSystem.CurrentIUMR >= fullyChargedManaCost)
            return SpellChargeLevel.FullyCharged;
        else if (chargeTime >= poweredChargeTime && manaSystem != null && manaSystem.CurrentIUMR >= poweredManaCost)
            return SpellChargeLevel.Powered;
        else
            return SpellChargeLevel.Normal;
    }

    float GetRequiredMana(SpellChargeLevel level)
    {
        switch (level)
        {
            case SpellChargeLevel.Normal: return normalManaCost;
            case SpellChargeLevel.Powered: return poweredManaCost;
            case SpellChargeLevel.FullyCharged: return fullyChargedManaCost;
            default: return normalManaCost;
        }
    }

    void CastProjectile(SpellChargeLevel chargeLevel)
    {
        GameObject orbPrefab = null;
        GameObject coreExplosion = null;
        float speed = 0f;
        float recoilMagnitude = 0f;
        float explosionScale = 1f;
        float lifetime = 10f;
        float fadeTime = 8f;
        Color vignetteColor = Color.magenta;

        switch (chargeLevel)
        {
            case SpellChargeLevel.Normal:
                orbPrefab = normalOrbPrefab;
                coreExplosion = magentaCoreExplosion;
                speed = normalSpeed;
                recoilMagnitude = normalRecoil;
                explosionScale = 1f;
                lifetime = normalLifetime;
                fadeTime = normalFadeTime;
                vignetteColor = Color.magenta;
                break;
            case SpellChargeLevel.Powered:
                orbPrefab = poweredOrbPrefab;
                coreExplosion = redCoreExplosion;
                speed = poweredSpeed;
                recoilMagnitude = poweredRecoil;
                explosionScale = 3.6f;
                lifetime = poweredLifetime;
                fadeTime = poweredFadeTime;
                vignetteColor = Color.red;
                break;
            case SpellChargeLevel.FullyCharged:
                orbPrefab = fullyChargedOrbPrefab;
                coreExplosion = yellowCoreExplosion;
                speed = fullyChargedSpeed;
                recoilMagnitude = fullyChargedRecoil;
                explosionScale = 6f;
                lifetime = fullyChargedLifetime;
                fadeTime = fullyChargedFadeTime;
                vignetteColor = Color.yellow;
                break;
        }

        if (orbPrefab == null)
        {
            Debug.LogError($"No orb prefab assigned for {chargeLevel}!");
            return;
        }

        Vector3 spawnPosition = cameraTransform.position + cameraTransform.forward * spawnDistance;
        GameObject projectile = Instantiate(orbPrefab, spawnPosition, Quaternion.identity);

        Rigidbody rb = projectile.GetComponent<Rigidbody>();
        if (rb == null) rb = projectile.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        Vector3 playerVelocity = movementSystem != null ? movementSystem.GetCurrentVelocity() : Vector3.zero;
        rb.linearVelocity = cameraTransform.forward * speed + playerVelocity;

        ApplyRecoil(recoilMagnitude);

        ProjectileCollision collisionHandler = projectile.AddComponent<ProjectileCollision>();
        collisionHandler.explosionPrefab1 = blackOutlineExplosion;
        collisionHandler.explosionPrefab2 = coreExplosion;
        collisionHandler.explosionScale = explosionScale;
        collisionHandler.initialResistance = initialResistance;
        collisionHandler.fadeTime = Mathf.Clamp(fadeTime, 0f, lifetime);
        collisionHandler.lifetime = lifetime;
        collisionHandler.hitType = chargeLevel == SpellChargeLevel.FullyCharged ? ProjectileHitType.FullyCharged :
                                   chargeLevel == SpellChargeLevel.Powered ? ProjectileHitType.Powered :
                                   ProjectileHitType.Normal;
        collisionHandler.showDebug = showDebug;

        Transform targetForThisProjectile = null;
        if (movementSystem != null && movementSystem.isLockedOn && movementSystem.currentLockedTarget != null)
        {
            targetForThisProjectile = movementSystem.currentLockedTarget;
        }

        if (targetForThisProjectile != null)
        {
            GuidedProjectile guidance = projectile.AddComponent<GuidedProjectile>();
            guidance.operatorTarget = targetForThisProjectile;
            guidance.guidanceRange = guidanceRange;
            guidance.launchSpeed = speed;
            guidance.maxTurnRate = guidanceMaxTurnRate;
            guidance.tapTurnBoost = guidanceTapTurnBoost;
            guidance.hitRange = autoHitRange;
            guidance.showDebug = showDebug;

            guidance.manaSystem = manaSystem;
            guidance.guideManaCostPerSec = guidanceManaCostPerSec;
        }

        OnProjectileCast?.Invoke(vignetteColor);

        if (showDebug)
            Debug.Log($"Projectile cast! Level: {chargeLevel}, Speed: {speed} u/s, Recoil: {recoilMagnitude} u/s");
    }

    void ApplyRecoil(float recoilMagnitude)
    {
        if (movementSystem == null) return;

        Vector3 currentVelocity = movementSystem.GetCurrentVelocity();
        Vector3 recoilVelocity = -cameraTransform.forward * recoilMagnitude;
        Vector3 newVelocity = currentVelocity + recoilVelocity;

        movementSystem.SetVelocity(newVelocity);
    }

    enum SpellChargeLevel
    {
        Normal,
        Powered,
        FullyCharged
    }
}

public class GuidedProjectile : MonoBehaviour
{
    [HideInInspector] public Transform operatorTarget;
    [HideInInspector] public float guidanceRange = 1000f;
    [HideInInspector] public float hitRange = 500f;
    [HideInInspector] public float launchSpeed = 20000f;
    [HideInInspector] public bool showDebug = false;
    [HideInInspector] public float maxTurnRate = 70f;
    [HideInInspector] public float tapTurnBoost = 20f;

    [HideInInspector] public AdvanceManaSystem manaSystem;
    [HideInInspector] public float guideManaCostPerSec = 5f;

    private const float autoHomeDuration = 0.1f;
    private const float resolveEpsilon = 0.0001f;

    private Rigidbody rb;
    private bool tapQueued = false;

    private bool isAutoHoming = false;
    private float autoHomeTimer = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (isAutoHoming) return;

        if (Input.GetMouseButtonDown(1))
            tapQueued = true;
    }

    void FixedUpdate()
    {
        if (operatorTarget == null || rb == null) return;

        if (isAutoHoming)
        {
            autoHomeTimer -= Time.fixedDeltaTime;

            if (autoHomeTimer <= resolveEpsilon)
            {
                Vector3 hitNormal = (transform.position - operatorTarget.position);
                hitNormal = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : Vector3.up;

                rb.position = operatorTarget.position;
                rb.linearVelocity = Vector3.zero;

                ProjectileCollision hitHandler = GetComponent<ProjectileCollision>();
                if (hitHandler != null)
                    hitHandler.ForceHit(operatorTarget.gameObject, hitNormal);
                else
                    Destroy(gameObject);

                return;
            }

            rb.linearVelocity = (operatorTarget.position - transform.position) / autoHomeTimer;
            return;
        }

        float distance = Vector3.Distance(transform.position, operatorTarget.position);

        if (distance <= hitRange)
        {
            isAutoHoming = true;
            autoHomeTimer = autoHomeDuration;
            tapQueued = false;
            if (showDebug) Debug.Log("[GuidedProjectile] Hit Range Reached! Auto-Homing Initiated (0.1s to impact).");
            return;
        }

        if (distance > guidanceRange)
        {
            tapQueued = false;
            return;
        }

        bool isHolding = Input.GetMouseButton(1);
        if (!tapQueued && !isHolding) return;

        Vector3 directionToOperator = (operatorTarget.position - transform.position).normalized;
        float currentSpeed = rb.linearVelocity.magnitude;

        float angleStepDegrees = 0f;

        if (tapQueued)
        {
            angleStepDegrees = tapTurnBoost;
            tapQueued = false;

            if (showDebug) Debug.Log($"[GuidedProjectile] Manual Tap Boost Applied! Slew rate: +{tapTurnBoost}°");
        }
        else if (isHolding)
        {
            float manaCostThisFrame = guideManaCostPerSec * Time.fixedDeltaTime;

            if (manaSystem != null)
            {
                if (manaSystem.HasEnoughMana(manaCostThisFrame))
                {
                    manaSystem.UseMana(manaCostThisFrame);
                    angleStepDegrees = maxTurnRate * Time.fixedDeltaTime;
                }
                else
                {
                    angleStepDegrees = 0f;
                }
            }
            else
            {
                angleStepDegrees = maxTurnRate * Time.fixedDeltaTime;
            }
        }

        float angleStepRadians = angleStepDegrees * Mathf.Deg2Rad;

        Vector3 steeredDirection = Vector3.RotateTowards(
            rb.linearVelocity.normalized,
            directionToOperator,
            angleStepRadians,
            0f
        );

        rb.linearVelocity = steeredDirection * currentSpeed;
    }
}

public class ProjectileCollision : MonoBehaviour
{
    public GameObject explosionPrefab1;
    public GameObject explosionPrefab2;
    public float explosionScale = 1f;
    public bool showDebug = false;
    public ProjectileHitType hitType = ProjectileHitType.Normal;
    public float initialResistance = 0f;

    [Tooltip("Time in seconds after spawn when scale/emission fade-out begins.")]
    public float fadeTime = 8f;
    [Tooltip("Time in seconds after spawn when scale/emission fully reach zero. The GameObject itself is destroyed a bit after this, once any already-emitted particles finish their own lifetime.")]
    public float lifetime = 10f;

    private const float baseExplosionScale = 24.10353f;
    private bool hasHit = false;
    private bool destructionScheduled = false;
    private float spawnTime;
    private Collider[] colliders;
    private Vector3 initialScale;

    private struct ChildEmission
    {
        public ParticleSystem system;
        public float initialRateOverTime;
        public float initialRateOverDistance;
    }
    private ChildEmission[] childEmissions;

    void Start()
    {
        spawnTime = Time.time;
        initialScale = transform.localScale;
        CacheChildParticleSystems();

        if (initialResistance > 0f)
        {
            colliders = GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                if (col != null) col.enabled = false;
            }
            Invoke(nameof(EnableColliders), initialResistance);
        }
    }

    void CacheChildParticleSystems()
    {
        List<ChildEmission> found = new List<ChildEmission>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            ParticleSystem[] systemsOnChild = child.GetComponents<ParticleSystem>();

            foreach (ParticleSystem ps in systemsOnChild)
            {
                ParticleSystem.EmissionModule emission = ps.emission;
                found.Add(new ChildEmission
                {
                    system = ps,
                    initialRateOverTime = emission.rateOverTimeMultiplier,
                    initialRateOverDistance = emission.rateOverDistanceMultiplier
                });
            }
        }

        childEmissions = found.ToArray();
    }

    void Update()
    {
        if (hasHit || destructionScheduled) return;

        float elapsed = Time.time - spawnTime;
        if (elapsed < fadeTime) return;

        float fadeDuration = lifetime - fadeTime;
        float t = fadeDuration > 0f ? Mathf.Clamp01((elapsed - fadeTime) / fadeDuration) : 1f;

        transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, t);

        for (int i = 0; i < childEmissions.Length; i++)
        {
            ParticleSystem ps = childEmissions[i].system;
            if (ps == null) continue;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTimeMultiplier = Mathf.Lerp(childEmissions[i].initialRateOverTime, 0f, t);
            emission.rateOverDistanceMultiplier = Mathf.Lerp(childEmissions[i].initialRateOverDistance, 0f, t);
        }

        if (t >= 1f)
        {
            ScheduleFinalDestruction();
        }
    }

    void ScheduleFinalDestruction()
    {
        destructionScheduled = true;

        float maxRemainingParticleLife = 0f;

        for (int i = 0; i < childEmissions.Length; i++)
        {
            ParticleSystem ps = childEmissions[i].system;
            if (ps == null) continue;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            float maxStartLifetime = GetMaxStartLifetime(ps);
            if (maxStartLifetime > maxRemainingParticleLife)
                maxRemainingParticleLife = maxStartLifetime;
        }

        Destroy(gameObject, maxRemainingParticleLife);
    }

    float GetMaxStartLifetime(ParticleSystem ps)
    {
        ParticleSystem.MinMaxCurve curve = ps.main.startLifetime;

        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Max(curve.constantMin, curve.constantMax);
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                return curve.curveMultiplier;
            default:
                return curve.constant;
        }
    }

    void EnableColliders()
    {
        if (colliders == null) return;
        foreach (Collider col in colliders)
        {
            if (col != null) col.enabled = true;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasHit || Time.time < spawnTime + initialResistance) return;
        hasHit = true;

        Vector3 impactPoint = collision.contacts[0].point;
        Vector3 hitNormal = collision.contacts[0].normal;
        NotifyOperator(collision.gameObject, hitNormal);
        SpawnExplosions(collision.gameObject, impactPoint);
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasHit || Time.time < spawnTime + initialResistance) return;
        hasHit = true;

        Vector3 hitNormal = -transform.forward;
        NotifyOperator(other.gameObject, hitNormal);
        SpawnExplosions(other.gameObject, transform.position);
        Destroy(gameObject);
    }

    public void ForceHit(GameObject target, Vector3 hitNormal)
    {
        if (hasHit || target == null) return;
        hasHit = true;

        Vector3 impactPoint = target.transform.position;
        NotifyOperator(target, hitNormal);
        SpawnExplosions(target, impactPoint);
        Destroy(gameObject);
    }

    void NotifyOperator(GameObject hit, Vector3 hitNormal)
    {
        OperatorHitResponse response = hit.GetComponent<OperatorHitResponse>();
        if (response == null)
            response = hit.GetComponentInParent<OperatorHitResponse>();

        if (response != null)
            response.OnProjectileHit(hitType, hitNormal);

        OperatorHealth health = hit.GetComponent<OperatorHealth>();
        if (health == null)
            health = hit.GetComponentInParent<OperatorHealth>();

        if (health != null)
            health.TakeDamageFromProjectile(hitType);
    }

    void SpawnExplosions(GameObject target, Vector3 impactPoint)
    {
        if (explosionPrefab1 != null)
        {
            GameObject e1 = Instantiate(explosionPrefab1, impactPoint, Quaternion.identity);
            e1.transform.localScale = Vector3.one * (baseExplosionScale * explosionScale);
        }

        if (explosionPrefab2 != null)
            Instantiate(explosionPrefab2, impactPoint, Quaternion.identity);
    }
}